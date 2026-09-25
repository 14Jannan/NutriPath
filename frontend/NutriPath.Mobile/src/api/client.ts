import axios from 'axios';
import * as SecureStore from './tokenStorage';

const API_BASE_URL = process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://localhost:5087';

export const ACCESS_TOKEN_KEY = 'nutripath.accessToken';
export const REFRESH_TOKEN_KEY = 'nutripath.refreshToken';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' },
});

// Runs before EVERY request this client sends. Reads the saved access
// token and attaches it as a Bearer header automatically — no screen
// ever has to remember to do this itself.
apiClient.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// The refresh currently in progress, shared by every request that hits a
// 401 at the same time. Refresh tokens are single-use (the server rotates
// them), so if several requests each refreshed on their own, all but the
// first would fail — and their failure would wipe the new tokens, logging
// the user out mid-action. One shared refresh avoids that.
let refreshInFlight: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = await SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
  if (!refreshToken) return null;
  try {
    const response = await axios.post(`${API_BASE_URL}/api/auth/refresh`, { refreshToken });
    const { accessToken, refreshToken: newRefreshToken } = response.data;
    await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, accessToken);
    await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, newRefreshToken);
    return accessToken;
  } catch {
    // Only clear the session if nobody has stored a newer token meanwhile.
    if ((await SecureStore.getItemAsync(REFRESH_TOKEN_KEY)) === refreshToken) {
      await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
      await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
    }
    return null;
  }
}

// Runs after EVERY response. If the server says 401 (token expired/
// invalid), refresh once (shared, see above) and retry the original
// request. If the refresh fails, the session has genuinely ended.
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    if (error.response?.status === 401 && originalRequest && !originalRequest._retry) {
      originalRequest._retry = true; // prevent an infinite retry loop

      refreshInFlight ??= refreshAccessToken().finally(() => {
        refreshInFlight = null;
      });
      const accessToken = await refreshInFlight;

      if (accessToken) {
        originalRequest.headers.Authorization = `Bearer ${accessToken}`;
        return apiClient(originalRequest); // retry the original call
      }
    }

    return Promise.reject(error);
  }
);

/**
 * A plain-language reason for a failed request: the server's own message
 * when it sent one, otherwise what kind of failure it was — instead of a
 * vague "please try again" that hides the real cause.
 */
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