import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import ta from './locales/ta.json';
import si from './locales/si.json';

/**
 * Localization for the AI chat (assistant screen and chat history). The rest of the app is
 * still English-only; see docs/multilingual-ai.md for the screens that remain untranslated.
 */
export const UI_LANGUAGES = ['en', 'ta', 'si'] as const;
export type UiLanguage = (typeof UI_LANGUAGES)[number];

export const resources = { en: { translation: en }, ta: { translation: ta }, si: { translation: si } } as const;

if (!i18n.isInitialized) {
  void i18n.use(initReactI18next).init({
    resources,
    lng: 'en',
    fallbackLng: 'en',
    interpolation: { escapeValue: false }, // React already escapes text
    initAsync: false,
  });
}

export default i18n;
