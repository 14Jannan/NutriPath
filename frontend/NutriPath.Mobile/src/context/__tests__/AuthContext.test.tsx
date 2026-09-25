import React, { ReactNode } from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { act, renderHook, waitFor } from '@testing-library/react-native';
import { AxiosError, AxiosHeaders } from 'axios';
import * as profileApi from '@/api/profileApi';
import * as authApi from '@/api/authApi';
import { AuthProvider, useAuth } from '@/context/AuthContext';

const mockTokens = new Map<string, string>();
jest.mock('@/api/tokenStorage', () => ({
  getItemAsync: async (key: string) => mockTokens.get(key) ?? null,
  setItemAsync: async (key: string, value: string) => {
    mockTokens.set(key, value);
  },
  deleteItemAsync: async (key: string) => {
    mockTokens.delete(key);
  },
}));
jest.mock('@/api/profileApi', () => ({ getMyProfile: jest.fn() }));
jest.mock('@/api/authApi', () => ({ login: jest.fn(), logout: jest.fn() }));

const api = jest.mocked(profileApi);
const auth = jest.mocked(authApi);

const wrapper = ({ children }: { children: ReactNode }) => <AuthProvider>{children}</AuthProvider>;
const profile = (targetCalories: number) => ({ fullName: 'Jannan', targetCalories }) as never;

beforeEach(() => {
  jest.clearAllMocks();
  mockTokens.clear();
  auth.login.mockImplementation(async () => {
    mockTokens.set('nutripath.accessToken', 'token');
    return { fullName: 'Jannan' } as never;
  });
});

describe('AuthContext profile gate', () => {
  it('sends a user without goals to setup after login', async () => {
    api.getMyProfile.mockResolvedValue(profile(0));
    const { result } = await renderHook(() => useAuth(), { wrapper });

    await act(() => result.current.loginUser('a@b.com', 'pw'));

    expect(result.current.isLoggedIn).toBe(true);
    expect(result.current.profileStatus).toBe('incomplete');
    expect(result.current.fullName).toBe('Jannan');
  });

  it('lets a user with goals straight in', async () => {
    api.getMyProfile.mockResolvedValue(profile(2062));
    const { result } = await renderHook(() => useAuth(), { wrapper });

    await act(() => result.current.loginUser('a@b.com', 'pw'));

    expect(result.current.profileStatus).toBe('complete');
  });

  it('checks a saved session on app start', async () => {
    mockTokens.set('nutripath.accessToken', 'saved');
    api.getMyProfile.mockResolvedValue(profile(0));

    const { result } = await renderHook(() => useAuth(), { wrapper });

    await waitFor(() => expect(result.current.profileStatus).toBe('incomplete'));
    expect(result.current.isLoggedIn).toBe(true);
  });

  it("reports an error instead of guessing when the server can't be reached", async () => {
    api.getMyProfile.mockRejectedValue(new Error('Network Error'));
    const { result } = await renderHook(() => useAuth(), { wrapper });

    await act(() => result.current.loginUser('a@b.com', 'pw'));

    expect(result.current.profileStatus).toBe('error');
  });

  it('logs out a saved session whose account no longer exists', async () => {
    mockTokens.set('nutripath.accessToken', 'saved');
    api.getMyProfile.mockRejectedValue(
      new AxiosError('Not Found', '404', undefined, undefined, {
        status: 404,
        data: {},
        statusText: '',
        headers: {},
        config: { headers: new AxiosHeaders() },
      })
    );

    const { result } = await renderHook(() => useAuth(), { wrapper });

    await waitFor(() => expect(result.current.isLoggedIn).toBe(false));
    expect(auth.logout).toHaveBeenCalled();
  });

  it('unlocks the app once setup is saved', async () => {
    api.getMyProfile.mockResolvedValue(profile(0));
    const { result } = await renderHook(() => useAuth(), { wrapper });
    await act(() => result.current.loginUser('a@b.com', 'pw'));

    await act(async () => result.current.markProfileComplete());

    expect(result.current.profileStatus).toBe('complete');
  });
});
