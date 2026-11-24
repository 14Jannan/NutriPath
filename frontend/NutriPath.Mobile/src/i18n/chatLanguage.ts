import { useCallback, useEffect, useState } from 'react';
import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import i18n, { type UiLanguage } from './index';

/** The reply language picked in the chat. "auto" lets the server detect it from each question. */
export type ChatLanguage = 'auto' | UiLanguage;
export const CHAT_LANGUAGES: ChatLanguage[] = ['auto', 'en', 'ta', 'si'];

export const CHAT_LANGUAGE_KEY = 'nutripath.chatLanguage';

const isChatLanguage = (value: unknown): value is ChatLanguage =>
  typeof value === 'string' && (CHAT_LANGUAGES as string[]).includes(value);

/** The chat UI follows an explicit choice; "Automatic" keeps it in English. */
export const uiLanguageFor = (language: ChatLanguage): UiLanguage => (language === 'auto' ? 'en' : language);

// Not a secret, but SecureStore is the storage this app already ships on iOS and Android.
// Storage can be unavailable (private browsing, disabled storage); the choice then lasts
// for the session only.
export async function loadChatLanguage(): Promise<ChatLanguage> {
  try {
    const stored =
      Platform.OS === 'web' ? globalThis.localStorage?.getItem(CHAT_LANGUAGE_KEY) : await SecureStore.getItemAsync(CHAT_LANGUAGE_KEY);
    return isChatLanguage(stored) ? stored : 'auto';
  } catch {
    return 'auto';
  }
}

export async function saveChatLanguage(language: ChatLanguage): Promise<void> {
  try {
    if (Platform.OS === 'web') globalThis.localStorage?.setItem(CHAT_LANGUAGE_KEY, language);
    else await SecureStore.setItemAsync(CHAT_LANGUAGE_KEY, language);
  } catch {
    // Keep working with the in-memory choice.
  }
}

/** The persisted chat language, applied to the chat UI strings. */
export function useChatLanguage() {
  const [language, setLanguageState] = useState<ChatLanguage>('auto');

  useEffect(() => {
    let active = true;
    void loadChatLanguage().then((stored) => {
      if (!active) return;
      setLanguageState(stored);
      void i18n.changeLanguage(uiLanguageFor(stored));
    });
    return () => {
      active = false;
    };
  }, []);

  const setLanguage = useCallback((next: ChatLanguage) => {
    setLanguageState(next);
    void i18n.changeLanguage(uiLanguageFor(next));
    void saveChatLanguage(next);
  }, []);

  return { language, setLanguage };
}
