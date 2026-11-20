import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

export const ACCESS_TOKEN_KEY = 'nutripath.accessToken';
export const REFRESH_TOKEN_KEY = 'nutripath.refreshToken';
export const isWeb = Platform.OS === 'web';

const logoutPendingKey = 'nutripath.logoutPending';
let webAccessToken: string | null = null;
let logoutPending = false;

export function purgeLegacyWebTokens() {
  if (!isWeb) return;
  try {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    logoutPending ||= localStorage.getItem(logoutPendingKey) === '1';
  } catch {
    // Browser storage can be disabled. The current page still signs out locally.
  }
}

export function isWebLogoutPending() {
  return isWeb && logoutPending;
}

export function setWebLogoutPending(pending: boolean) {
  if (!isWeb) return;
  logoutPending = pending;
  try {
    // Only a sign-out marker is persisted. It prevents a failed remote logout
    // from silently restoring the cookie session on the next page load.
    if (pending) localStorage.setItem(logoutPendingKey, '1');
    else localStorage.removeItem(logoutPendingKey);
  } catch {
    // In-memory sign-out still works.
  }
}

export async function getItemAsync(key: string): Promise<string | null> {
  if (isWeb) return key === ACCESS_TOKEN_KEY ? webAccessToken : null;
  try {
    return await SecureStore.getItemAsync(key);
  } catch {
    return null;
  }
}

export async function setItemAsync(key: string, value: string): Promise<void> {
  if (isWeb) {
    if (key === ACCESS_TOKEN_KEY) webAccessToken = value;
    return;
  }
  await SecureStore.setItemAsync(key, value);
}

export async function deleteItemAsync(key: string): Promise<void> {
  if (isWeb) {
    if (key === ACCESS_TOKEN_KEY) webAccessToken = null;
    return;
  }
  await SecureStore.deleteItemAsync(key);
}
