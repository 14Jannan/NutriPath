import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

// expo-secure-store wraps the iOS Keychain / Android Keystore, which have
// no browser equivalent — on web its native module is an empty stub, so
// every call throws. localStorage is the standard fallback there; it's
// less secure than a hardware enclave, but browsers have no better option.
const isWeb = Platform.OS === 'web';

export async function getItemAsync(key: string): Promise<string | null> {
  if (isWeb) {
    return localStorage.getItem(key);
  }
  return SecureStore.getItemAsync(key);
}

export async function setItemAsync(key: string, value: string): Promise<void> {
  if (isWeb) {
    localStorage.setItem(key, value);
    return;
  }
  await SecureStore.setItemAsync(key, value);
}

export async function deleteItemAsync(key: string): Promise<void> {
  if (isWeb) {
    localStorage.removeItem(key);
    return;
  }
  await SecureStore.deleteItemAsync(key);
}
