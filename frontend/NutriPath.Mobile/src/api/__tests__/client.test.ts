import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import axios, { AxiosError, AxiosHeaders, InternalAxiosRequestConfig } from 'axios';
import {
  ACCESS_TOKEN_KEY, apiClient, describeApiError, loginSession, logoutSession, onSessionEnded,
  REFRESH_TOKEN_KEY, restoreSession,
} from '@/api/client';

// In-memory stand-in for secure storage.
const mockStore = new Map<string, string>();
let mockIsWeb = false;
let mockLogoutPending = false;
jest.mock('@/api/tokenStorage', () => ({
  ACCESS_TOKEN_KEY: 'nutripath.accessToken',
  REFRESH_TOKEN_KEY: 'nutripath.refreshToken',
  get isWeb() { return mockIsWeb; },
  purgeLegacyWebTokens: () => {},
  isWebLogoutPending: () => mockIsWeb && mockLogoutPending,
  setWebLogoutPending: (pending: boolean) => { if (mockIsWeb) mockLogoutPending = pending; },
  getItemAsync: async (key: string) => mockStore.get(key) ?? null,
  setItemAsync: async (key: string, value: string) => {
    mockStore.set(key, value);
  },
  deleteItemAsync: async (key: string) => {
    mockStore.delete(key);
  },
}));

// A fake server: accepts only the refreshed access token, 401 otherwise.
function fakeServer(config: InternalAxiosRequestConfig) {
  const authorised = config.headers?.Authorization === 'Bearer fresh-access';
  const response = { data: { ok: true }, status: authorised ? 200 : 401, statusText: '', headers: {}, config };
  return authorised
    ? Promise.resolve(response)
    : Promise.reject(new AxiosError('Unauthorized', '401', config, undefined, response));
}

let refreshSpy: jest.SpiedFunction<typeof axios.post>;

beforeEach(async () => {
  jest.restoreAllMocks();
  mockIsWeb = false;
  mockLogoutPending = false;
  mockStore.clear();
  apiClient.defaults.adapter = fakeServer as never;
  refreshSpy = jest.spyOn(axios, 'post').mockResolvedValueOnce({
    data: { accessToken: 'expired-access', refreshToken: 'refresh-1' },
  });
  await loginSession('test@example.com', 'password');
  refreshSpy.mockClear().mockImplementation(async () => {
    await new Promise((r) => setTimeout(r, 10)); // a real refresh takes time
    return { data: { accessToken: 'fresh-access', refreshToken: 'refresh-2' } };
  });
});

describe('apiClient token refresh', () => {
  it('shares one refresh between requests that all get a 401 at once', async () => {
    // The goals screen fires several requests together; each gets a 401.
    const results = await Promise.all([apiClient.get('/a'), apiClient.post('/b'), apiClient.put('/c')]);

    expect(results.every((r) => r.status === 200)).toBe(true);
    // Refresh tokens are single-use, so refreshing three times would fail twice.
    expect(refreshSpy).toHaveBeenCalledTimes(1);
    expect(mockStore.get(ACCESS_TOKEN_KEY)).toBe('fresh-access');
    expect(mockStore.get(REFRESH_TOKEN_KEY)).toBe('refresh-2');
  });

  it('clears the session only when the refresh itself fails', async () => {
    refreshSpy.mockRejectedValue(new Error('refresh token revoked'));

    await expect(apiClient.get('/a')).rejects.toBeInstanceOf(AxiosError);
    expect(mockStore.has(ACCESS_TOKEN_KEY)).toBe(false);
    expect(mockStore.has(REFRESH_TOKEN_KEY)).toBe(false);
  });

  it('revokes the rotated token when logout starts during a refresh', async () => {
    let completeRefresh!: () => void;
    let signalRefreshStarted!: () => void;
    const refreshStarted = new Promise<void>((resolve) => { signalRefreshStarted = resolve; });
    refreshSpy.mockImplementationOnce(() => new Promise((resolve) => {
      completeRefresh = () => resolve({
        data: { accessToken: 'fresh-access', refreshToken: 'refresh-2' },
      });
      signalRefreshStarted();
    }));
    refreshSpy.mockResolvedValueOnce({ status: 204 });
    const request = expect(apiClient.get('/a')).rejects.toBeInstanceOf(AxiosError);
    await refreshStarted;
    const leaving = logoutSession();
    completeRefresh();
    await Promise.all([request, leaving]);
    expect(refreshSpy.mock.calls[1]?.[0]).toMatch(/\/api\/auth\/logout$/);
    expect(refreshSpy.mock.calls[1]?.[1]).toEqual({ refreshToken: 'refresh-2' });
    expect(mockStore.size).toBe(0);
    await expect(apiClient.get('/a')).rejects.toThrow();
  });

  it('restores a stored native session and clears it when logout is offline', async () => {
    await expect(restoreSession()).resolves.toBe(true);
    expect(refreshSpy).not.toHaveBeenCalled();
    const ended = jest.fn();
    const unsubscribe = onSessionEnded(ended);
    refreshSpy.mockRejectedValueOnce(new Error('offline'));
    await logoutSession();
    unsubscribe();
    expect(ended).toHaveBeenCalledTimes(1);
    expect(mockStore.size).toBe(0);
    expect(refreshSpy.mock.calls[0]?.[1]).toEqual({ refreshToken: 'refresh-1' });
  });

  it('restores web sessions through the cookie and leaves no readable refresh token', async () => {
    mockIsWeb = true;
    mockStore.clear();
    refreshSpy.mockResolvedValueOnce({ data: { accessToken: 'fresh-access' } });
    await expect(restoreSession()).resolves.toBe(true);
    expect(refreshSpy).toHaveBeenCalledWith(
      expect.stringMatching(/\/api\/auth\/refresh$/), {},
      expect.objectContaining({ withCredentials: true, headers: { 'X-NutriPath-Web': '1' } }),
    );
    expect(mockStore.get(ACCESS_TOKEN_KEY)).toBe('fresh-access');
    expect(mockStore.has(REFRESH_TOKEN_KEY)).toBe(false);
  });

  it('logs in and out on web without sending a refresh token in the body', async () => {
    mockIsWeb = true;
    mockStore.clear();
    refreshSpy.mockResolvedValueOnce({ data: { accessToken: 'fresh-access' } });
    await loginSession('test@example.com', 'password');
    expect(mockStore.has(REFRESH_TOKEN_KEY)).toBe(false);
    refreshSpy.mockResolvedValueOnce({ status: 204 });
    await logoutSession();
    expect(refreshSpy).toHaveBeenLastCalledWith(
      expect.stringMatching(/\/api\/auth\/logout$/), {},
      expect.objectContaining({ withCredentials: true, headers: { 'X-NutriPath-Web': '1' } }),
    );
    expect(mockStore.size).toBe(0);
  });

  it('waits for the browser session lock before sending a cookie logout', async () => {
    mockIsWeb = true;
    const originalLocks = Object.getOwnPropertyDescriptor(navigator, 'locks');
    let release!: () => void;
    const otherTab = new Promise<void>((resolve) => { release = resolve; });
    const request = jest.fn(async (_name: string, send: () => Promise<unknown>) => {
      await otherTab;
      return send();
    });
    Object.defineProperty(navigator, 'locks', { configurable: true, value: { request } });

    try {
      refreshSpy.mockResolvedValueOnce({ status: 204 });
      const logout = logoutSession();
      await expect(apiClient.get('/a')).rejects.toThrow();
      expect(request).toHaveBeenCalledWith('nutripath.auth', expect.any(Function));
      expect(refreshSpy).not.toHaveBeenCalled();
      release();
      await logout;
      expect(refreshSpy.mock.calls[0]?.[0]).toMatch(/\/logout$/);
    } finally {
      release();
      if (originalLocks) Object.defineProperty(navigator, 'locks', originalLocks);
      else Reflect.deleteProperty(navigator, 'locks');
    }
  });

  it('retries a failed web logout before trying to restore a cookie session', async () => {
    mockIsWeb = true;
    refreshSpy.mockRejectedValueOnce(new Error('offline'));
    await logoutSession();
    expect(mockLogoutPending).toBe(true);
    refreshSpy.mockResolvedValueOnce({ status: 204 });
    await expect(restoreSession()).resolves.toBe(false);
    expect(refreshSpy.mock.calls.map(([url]) => url)).toEqual([
      expect.stringMatching(/\/logout$/), expect.stringMatching(/\/logout$/),
    ]);
    expect(mockLogoutPending).toBe(false);
  });

  it('rejects requests to another destination before dispatching', async () => {
    await expect(apiClient.get('https://example.com/')).rejects.toThrow('Invalid API destination');
    await expect(apiClient.get('/a', { baseURL: 'https://example.com' })).rejects.toThrow('Invalid API destination');
  });
});

describe('describeApiError', () => {
  const withStatus = (status: number, data: unknown = {}) =>
    new AxiosError('x', String(status), undefined, undefined, {
      status,
      data,
      statusText: '',
      headers: {},
      config: { headers: new AxiosHeaders() },
    });

  it.each<[unknown, string]>([
    [withStatus(400, { message: 'Age must be between 13 and 120.' }), 'Age must be between 13 and 120.'],
    [withStatus(401), 'session has expired'],
    [withStatus(429), 'Too many attempts'],
    [withStatus(500), 'server had a problem'],
    [new AxiosError('Network Error'), "Can't reach the server"],
  ])('explains %s', (error, expected) => {
    expect(describeApiError(error)).toContain(expected);
  });
});
