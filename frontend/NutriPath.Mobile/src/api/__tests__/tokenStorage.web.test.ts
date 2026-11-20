import { expect, it, jest } from '@jest/globals';
import * as SecureStore from 'expo-secure-store';
import {
  ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY, deleteItemAsync, getItemAsync,
  purgeLegacyWebTokens, setItemAsync, setWebLogoutPending,
} from '@/api/tokenStorage';

jest.mock('react-native', () => {
  const native = jest.requireActual<typeof import('react-native')>('react-native');
  return Object.create(native, { Platform: { value: { ...native.Platform, OS: 'web' } } });
});
jest.mock('expo-secure-store', () => ({
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
  deleteItemAsync: jest.fn(),
}));

it('removes legacy browser credentials and keeps new access tokens in memory', async () => {
  const stored = new Map([[ACCESS_TOKEN_KEY, 'legacy-access'], [REFRESH_TOKEN_KEY, 'legacy-refresh']]);
  const originalStorage = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: {
      getItem: (key: string) => stored.get(key),
      setItem: (key: string, value: string) => stored.set(key, value),
      removeItem: (key: string) => stored.delete(key),
    },
  });

  try {
    purgeLegacyWebTokens();
    expect(stored.size).toBe(0);
    await setItemAsync(ACCESS_TOKEN_KEY, 'access');
    await setItemAsync(REFRESH_TOKEN_KEY, 'refresh');
    expect(stored.size).toBe(0);
    expect(await getItemAsync(ACCESS_TOKEN_KEY)).toBe('access');
    expect(await getItemAsync(REFRESH_TOKEN_KEY)).toBeNull();
    expect(SecureStore.setItemAsync).not.toHaveBeenCalled();

    setWebLogoutPending(true);
    await deleteItemAsync(ACCESS_TOKEN_KEY);
    expect(await getItemAsync(ACCESS_TOKEN_KEY)).toBeNull();
    expect([...stored.values()]).toEqual(['1']);
  } finally {
    setWebLogoutPending(false);
    if (originalStorage) Object.defineProperty(globalThis, 'localStorage', originalStorage);
    else Reflect.deleteProperty(globalThis, 'localStorage');
  }
});
