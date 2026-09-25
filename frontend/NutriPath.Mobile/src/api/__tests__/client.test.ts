import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import axios, { AxiosError, AxiosHeaders, InternalAxiosRequestConfig } from 'axios';
import { ACCESS_TOKEN_KEY, apiClient, describeApiError, REFRESH_TOKEN_KEY } from '@/api/client';

// In-memory stand-in for secure storage.
const mockStore = new Map<string, string>();
jest.mock('@/api/tokenStorage', () => ({
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

beforeEach(() => {
  mockStore.clear();
  mockStore.set(ACCESS_TOKEN_KEY, 'expired-access');
  mockStore.set(REFRESH_TOKEN_KEY, 'refresh-1');
  apiClient.defaults.adapter = fakeServer as never;
  refreshSpy = jest.spyOn(axios, 'post').mockImplementation(async () => {
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
