import axios, { InternalAxiosRequestConfig } from 'axios';
import * as tokenStorage from './tokenStorage';

export { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from './tokenStorage';
const { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } = tokenStorage;
const API_BASE_URL = (process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://localhost:5092').replace(/\/$/, '');

function requireSecureApiUrl() {
  if (!__DEV__ && !API_BASE_URL.startsWith('https://'))
    throw new Error('The production API URL must use HTTPS.');
}

export interface AuthResponse {
  accessToken: string;
  refreshToken?: string;
  accessTokenExpiresAtUtc: string;
  userId: string;
  email: string;
  fullName: string;
  // Administrator accounts manage NutriPath from the web admin portal.
  isAdmin?: boolean;
}

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' },
});

interface SessionRequestConfig extends InternalAxiosRequestConfig {
  sessionVersion?: number;
  retried?: boolean;
}

let sessionVersion = 0;
let sessionBlocked = false;
let refreshInFlight: Promise<string | null> | null = null;
let loginInFlight: Promise<AuthResponse> | null = null;
let logoutInFlight: Promise<void> | null = null;
const sessionEndedListeners = new Set<() => void>();

export function getSessionVersion() {
  return sessionVersion;
}

export function onSessionEnded(listener: () => void) {
  sessionEndedListeners.add(listener);
  return () => { sessionEndedListeners.delete(listener); };
}

function endSession() {
  sessionVersion++;
  sessionBlocked = true;
  sessionEndedListeners.forEach((listener) => listener());
}

async function clearTokens() {
  await Promise.allSettled([
    tokenStorage.deleteItemAsync(ACCESS_TOKEN_KEY),
    tokenStorage.deleteItemAsync(REFRESH_TOKEN_KEY),
  ]);
}

async function sessionPost<T>(
  action: 'login' | 'refresh' | 'logout',
  body: object,
  version?: number,
) {
  const send = () => {
    requireSecureApiUrl();
    if (version !== undefined && version !== sessionVersion) throw new axios.CanceledError();
    return axios.post<T>(`${API_BASE_URL}/api/auth/${action}`, body, {
      timeout: action === 'logout' ? 5000 : 15000,
      withCredentials: tokenStorage.isWeb,
      headers: tokenStorage.isWeb ? { 'X-NutriPath-Web': '1' } : undefined,
    });
  };

  // Same-origin browser tabs share the cookie. Wait for the previous tab
  // to receive its Set-Cookie before sending the next session request.
  if (tokenStorage.isWeb && typeof navigator !== 'undefined' && navigator.locks)
    return navigator.locks.request('nutripath.auth', send);
  return send();
}

async function storeRefreshToken(auth: AuthResponse) {
  if (tokenStorage.isWeb) return;
  if (!auth.refreshToken) throw new Error('Missing refresh credential.');
  await tokenStorage.setItemAsync(REFRESH_TOKEN_KEY, auth.refreshToken);
}

export function loginSession(email: string, password: string): Promise<AuthResponse> {
  if (logoutInFlight) return logoutInFlight.then(() => loginSession(email, password));
  if (loginInFlight) return Promise.reject(new Error('Sign-in is already in progress.'));

  const version = ++sessionVersion;
  loginInFlight = (async () => {
    await refreshInFlight;
    if (version !== sessionVersion) throw new axios.CanceledError();
    const { data } = await sessionPost<AuthResponse>('login', { email, password }, version);
    await storeRefreshToken(data);
    if (version !== sessionVersion) throw new axios.CanceledError();
    await tokenStorage.setItemAsync(ACCESS_TOKEN_KEY, data.accessToken);
    if (version !== sessionVersion) throw new axios.CanceledError();
    tokenStorage.setWebLogoutPending(false);
    sessionBlocked = false;
    return data;
  })().finally(() => { loginInFlight = null; });
  return loginInFlight;
}

async function refreshAccessToken(version: number): Promise<string | null> {
  try {
    const refreshToken = tokenStorage.isWeb ? undefined : await tokenStorage.getItemAsync(REFRESH_TOKEN_KEY);
    if (!tokenStorage.isWeb && !refreshToken) throw new Error('Missing refresh credential.');
    if (version !== sessionVersion) return null;

    const { data } = await sessionPost<AuthResponse>('refresh', refreshToken ? { refreshToken } : {}, version);
    // Logout waits for this operation and needs the replacement native token
    // even if the session was blocked while the request was in flight.
    await storeRefreshToken(data);
    if (version !== sessionVersion) return null;
    await tokenStorage.setItemAsync(ACCESS_TOKEN_KEY, data.accessToken);
    return version === sessionVersion ? data.accessToken : null;
  } catch {
    if (version === sessionVersion) {
      endSession();
      await clearTokens();
    }
    return null;
  }
}

function refreshOnce(version: number) {
  if (sessionBlocked || loginInFlight || logoutInFlight || version !== sessionVersion)
    return Promise.resolve(null);

  refreshInFlight ??= refreshAccessToken(version).finally(() => { refreshInFlight = null; });
  return refreshInFlight;
}

export async function restoreSession(): Promise<boolean> {
  tokenStorage.purgeLegacyWebTokens();
  if (tokenStorage.isWebLogoutPending()) {
    await logoutSession();
    return false;
  }

  const version = sessionVersion;
  if (sessionBlocked || loginInFlight || logoutInFlight) return false;
  if (!tokenStorage.isWeb && await tokenStorage.getItemAsync(ACCESS_TOKEN_KEY))
    return version === sessionVersion;
  return !!await refreshOnce(version);
}

export function logoutSession(): Promise<void> {
  if (logoutInFlight) return logoutInFlight;
  endSession();
  tokenStorage.setWebLogoutPending(true);

  logoutInFlight = (async () => {
    try {
      // Give outstanding responses time to store the rotated credential or
      // update the cookie before asking the server to revoke it.
      await Promise.allSettled([refreshInFlight, loginInFlight]);
      const refreshToken = tokenStorage.isWeb ? undefined : await tokenStorage.getItemAsync(REFRESH_TOKEN_KEY);
      if (tokenStorage.isWeb || refreshToken) {
        await sessionPost('logout', refreshToken ? { refreshToken } : {});
        tokenStorage.setWebLogoutPending(false);
      }
    } catch {
      // The local session ends even when remote revocation cannot be confirmed.
    } finally {
      await clearTokens();
    }
  })().finally(() => { logoutInFlight = null; });
  return logoutInFlight;
}

apiClient.interceptors.request.use(async (config: SessionRequestConfig) => {
  requireSecureApiUrl();
  // This client accepts only API-relative paths and the configured base URL.
  if (!/^\/(?![\/\\])/.test(config.url ?? '') || config.baseURL !== API_BASE_URL)
    throw new Error('Invalid API destination.');
  config.withCredentials = false;

  if (config.url?.startsWith('/api/auth/')) return config;

  const version = config.sessionVersion ?? sessionVersion;
  config.sessionVersion = version;
  if (sessionBlocked || loginInFlight || version !== sessionVersion)
    throw new axios.CanceledError();

  const token = await tokenStorage.getItemAsync(ACCESS_TOKEN_KEY);
  if (version !== sessionVersion) throw new axios.CanceledError();
  if (token) config.headers.set('Authorization', `Bearer ${token}`);
  else config.headers.delete('Authorization');
  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    const { sessionVersion: version } = response.config as SessionRequestConfig;
    if (version !== undefined && version !== sessionVersion) throw new axios.CanceledError();
    return response;
  },
  async (error) => {
    const request: SessionRequestConfig | undefined = error.config;
    const version = request?.sessionVersion;
    if (error.response?.status === 401 && request && !request.retried
        && version !== undefined && version === sessionVersion && !sessionBlocked) {
      request.retried = true;
      // A late 401 can arrive after another request has already rotated.
      const current = await tokenStorage.getItemAsync(ACCESS_TOKEN_KEY);
      const alreadyRefreshed = current && request.headers.get('Authorization') !== `Bearer ${current}`;
      const accessToken = alreadyRefreshed ? current : await refreshOnce(version);
      if (accessToken && version === sessionVersion) return apiClient(request);
    }
    return Promise.reject(error);
  },
);

export function describeApiError(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!axios.isAxiosError(error)) return fallback;
  const serverMessage = (error.response?.data as { message?: string } | undefined)?.message;
  if (serverMessage) return serverMessage;
  if (!error.response) return "Can't reach the server. Check your connection and that the backend is running.";
  switch (error.response.status) {
    case 401:
      return 'Your session has expired. Please log in again.';
    case 429:
      return 'Too many attempts. Please wait a minute and try again.';
    default:
      return error.response.status >= 500 ? 'The server had a problem. Please try again shortly.' : fallback;
  }
}
