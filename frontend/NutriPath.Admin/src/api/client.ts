/**
 * Admin API client.
 * - The access token lives only in memory (never localStorage/sessionStorage).
 * - The refresh token is an HttpOnly, SameSite=Strict cookie the browser sends to /api/admin/auth.
 * - Refresh and logout send the X-NutriPath-Admin header, which a cross-site form cannot add.
 * - A 401 triggers one shared refresh attempt; "reauth_required" asks for password + TOTP, then retries.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly detail?: string;
  readonly data: unknown;

  constructor(status: number, message: string, data?: unknown, code?: string, detail?: string) {
    super(message);
    this.status = status;
    this.data = data;
    this.code = code;
    this.detail = detail;
  }
}

let accessToken: string | null = null;
let refreshing: Promise<boolean> | null = null;
let onSessionExpired: (() => void) | null = null;
let onReauthRequired: (() => Promise<boolean>) | null = null;

export const session = {
  setToken(token: string | null) {
    accessToken = token;
  },
  hasToken: () => accessToken !== null,
  onExpired(handler: () => void) {
    onSessionExpired = handler;
  },
  onReauth(handler: () => Promise<boolean>) {
    onReauthRequired = handler;
  },
};

const CSRF_HEADER = 'X-NutriPath-Admin';

async function parseError(response: Response): Promise<ApiError> {
  let data: unknown = null;
  try {
    data = await response.json();
  } catch {
    /* empty or non-JSON body */
  }
  const problem = (data ?? {}) as { title?: string; detail?: string; message?: string; code?: string };
  const message = problem.title ?? problem.detail ?? problem.message ?? defaultMessage(response.status);
  return new ApiError(response.status, message, data, problem.code, problem.detail);
}

function defaultMessage(status: number) {
  if (status === 403) return "You don't have permission to do that.";
  if (status === 404) return 'Not found.';
  if (status === 429) return 'Too many requests. Please wait a moment.';
  if (status >= 500) return 'The server had a problem. Please try again.';
  return 'Request failed.';
}

/** Exchanges the refresh cookie for a new access token. Concurrent callers share one request. */
export function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      const response = await fetch('/api/admin/auth/refresh', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { [CSRF_HEADER]: '1' },
      });
      if (!response.ok) {
        accessToken = null;
        return false;
      }
      const body = (await response.json()) as { accessToken: string };
      accessToken = body.accessToken;
      return true;
    } catch {
      return false;
    } finally {
      queueMicrotask(() => (refreshing = null));
    }
  })();
  return refreshing;
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  form?: FormData;
  signal?: AbortSignal;
  /** Return the raw Response (downloads). */
  raw?: boolean;
  /** Internal: prevents infinite retry loops. */
  retried?: boolean;
}

export async function api<T = unknown>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  let body: BodyInit | undefined;
  if (options.form) body = options.form;
  else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }
  const response = await fetch(path, {
    method: options.method ?? (body ? 'POST' : 'GET'),
    headers,
    body,
    credentials: 'same-origin',
    signal: options.signal,
  });

  if (response.status === 401 && !options.retried) {
    const error = await parseError(response.clone());
    if (error.code === 'reauth_required' && onReauthRequired) {
      if (await onReauthRequired()) return api<T>(path, { ...options, retried: true });
      throw error;
    }
    if (await refreshSession()) return api<T>(path, { ...options, retried: true });
    onSessionExpired?.();
    throw error;
  }
  if (!response.ok) throw await parseError(response);
  if (options.raw) return response as unknown as T;
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** Downloads an authenticated file (CSV exports) without exposing the token in a URL. */
export async function download(path: string, fallbackName: string) {
  const response = await api<Response>(path, { raw: true });
  const blob = await response.blob();
  const name = /filename="?([^";]+)"?/.exec(response.headers.get('content-disposition') ?? '')?.[1] ?? fallbackName;
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Unauthenticated calls in the sign-in flow (login, MFA, invitations). */
export async function publicPost<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', [CSRF_HEADER]: '1' },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw await parseError(response);
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function logoutRequest() {
  try {
    await fetch('/api/admin/auth/logout', { method: 'POST', credentials: 'same-origin', headers: { [CSRF_HEADER]: '1' } });
  } finally {
    accessToken = null;
  }
}

export const qs = (params: Record<string, string | number | boolean | null | undefined>) => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params))
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value));
  const text = search.toString();
  return text ? `?${text}` : '';
};
