import React from 'react';
import { StyleSheet } from 'react-native';
import { afterEach, beforeEach, describe, expect, it, jest } from '@jest/globals';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AxiosError, AxiosHeaders } from 'axios';
import * as SecureStore from 'expo-secure-store';
import * as aiApi from '@/api/aiApi';
import i18n from '@/i18n';
import en from '@/i18n/locales/en.json';
import ta from '@/i18n/locales/ta.json';
import si from '@/i18n/locales/si.json';
import { CHAT_LANGUAGE_KEY, loadChatLanguage, saveChatLanguage } from '@/i18n/chatLanguage';
import { hasIndicScript, scriptAwareStyle } from '@/utils/script';
import { AssistantScreen } from '@/screens/AssistantScreen';

jest.setTimeout(30000);

jest.mock('expo-secure-store', () => ({
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
}));
jest.mock('@/api/aiApi', () => ({
  askAssistant: jest.fn(),
  listConversations: jest.fn(),
  getConversationMessages: jest.fn(),
  searchChats: jest.fn(),
  startNewConversation: jest.fn(),
  deleteConversation: jest.fn(),
  getUsage: jest.fn(),
}));
const api = jest.mocked(aiApi);
const store = jest.mocked(SecureStore);

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };
const usage = { tokensUsed: 100, tokenLimit: 40000, windowHours: 5, locked: false, resetsAtUtc: null };

function httpError(status: number, data: unknown, headers: Record<string, string> = {}) {
  const axiosHeaders = new AxiosHeaders(headers);
  return new AxiosError('Request failed', String(status), undefined, undefined, {
    status, statusText: '', headers: axiosHeaders, config: { headers: axiosHeaders }, data,
  });
}

async function renderScreen() {
  await render(
    <SafeAreaProvider initialMetrics={metrics}>
      <AssistantScreen />
    </SafeAreaProvider>
  );
}

async function ask(placeholder: string, question: string, send: string) {
  await fireEvent.changeText(await screen.findByPlaceholderText(placeholder), question);
  await fireEvent.press(screen.getByLabelText(send));
}

beforeEach(() => {
  jest.clearAllMocks();
  store.getItemAsync.mockResolvedValue(null);
  store.setItemAsync.mockResolvedValue(undefined);
  api.getUsage.mockResolvedValue(usage);
  api.listConversations.mockResolvedValue([]);
  api.startNewConversation.mockResolvedValue('c1');
  api.askAssistant.mockResolvedValue({ answer: 'ok', sourcesUsed: [], conversationId: 'c1', usage });
});

afterEach(async () => {
  await act(async () => {
    await i18n.changeLanguage('en');
  });
});

describe('Assistant language selector', () => {
  it('offers Automatic, English, Tamil and Sinhala, each labelled in its own language', async () => {
    await renderScreen();
    for (const name of ['Automatic', 'English', 'தமிழ்', 'සිංහල']) expect(screen.getByText(name)).toBeTruthy();
    expect(screen.getByTestId('chat-language-auto').props.accessibilityState).toEqual({ selected: true });
  });

  it('switches the chat to Tamil, saves the choice and sends it with each question', async () => {
    await renderScreen();
    await fireEvent.press(screen.getByTestId('chat-language-ta'));

    expect(await screen.findByText(ta.chat.title)).toBeTruthy();
    expect(screen.getByText(ta.chat.welcome)).toBeTruthy();
    expect(store.setItemAsync).toHaveBeenCalledWith(CHAT_LANGUAGE_KEY, 'ta');

    await ask(ta.chat.placeholder, 'எனக்கு எவ்வளவு புரதம் தேவை?', ta.chat.send);
    await waitFor(() => expect(api.askAssistant).toHaveBeenCalledWith('எனக்கு எவ்வளவு புரதம் தேவை?', 'c1', 'ta'));
  });

  it('restores the saved Sinhala preference on launch', async () => {
    store.getItemAsync.mockResolvedValue('si');
    await renderScreen();

    expect(await screen.findByText(si.chat.title)).toBeTruthy();
    expect(screen.getByTestId('chat-language-si').props.accessibilityState).toEqual({ selected: true });
    await ask(si.chat.placeholder, 'මට ප්‍රෝටීන් කොපමණ ඕනද?', si.chat.send);
    await waitFor(() => expect(api.askAssistant).toHaveBeenCalledWith('මට ප්‍රෝටීන් කොපමණ ඕනද?', 'c1', 'si'));
  });

  it('sends localized quick prompts so the reply comes back in that language', async () => {
    store.getItemAsync.mockResolvedValue('ta');
    await renderScreen();
    await fireEvent.press(await screen.findByText(ta.chat.prompts.eatNow));
    await waitFor(() => expect(api.askAssistant).toHaveBeenCalledWith(ta.chat.prompts.eatNow, 'c1', 'ta'));
  });

  it('ignores corrupted stored values', async () => {
    store.getItemAsync.mockResolvedValue('fr');
    expect(await loadChatLanguage()).toBe('auto');
    store.getItemAsync.mockRejectedValue(new Error('keystore unavailable'));
    expect(await loadChatLanguage()).toBe('auto');
    store.setItemAsync.mockRejectedValue(new Error('keystore unavailable'));
    await expect(saveChatLanguage('si')).resolves.toBeUndefined();
  });
});

describe('Localized assistant errors', () => {
  it.each([
    ['ta', httpError(503, { message: 'x', code: 'provider_not_permitted' }), ta.chat.errors.notPermitted],
    ['si', httpError(409, { message: 'busy' }), si.chat.errors.busy],
    ['en', httpError(503, { message: 'x' }), en.chat.errors.unavailable],
  ] as const)('shows the %s message for a server refusal', async (language, error, expected) => {
    store.getItemAsync.mockResolvedValue(language);
    api.askAssistant.mockRejectedValueOnce(error);
    await renderScreen();
    const strings = { en, ta, si }[language];
    await ask(strings.chat.placeholder, 'question', strings.chat.send);
    expect(await screen.findByText(expected)).toBeTruthy();
  });

  it('tells the user when to retry after a provider rate limit', async () => {
    api.askAssistant.mockRejectedValueOnce(httpError(503, { message: 'x' }, { 'retry-after': '120' }));
    await renderScreen();
    await ask(en.chat.placeholder, 'question', en.chat.send);
    expect(await screen.findByText('The assistant is busy right now. Please try again in 2m.')).toBeTruthy();
  });

  it('reports a lost connection in the chat language', async () => {
    store.getItemAsync.mockResolvedValue('si');
    api.askAssistant.mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'));
    await renderScreen();
    await ask(si.chat.placeholder, 'question', si.chat.send);
    expect(await screen.findByText(si.chat.errors.network)).toBeTruthy();
  });
});

describe('Tamil and Sinhala rendering', () => {
  it('renders Tamil and Sinhala replies with taller lines so vowel signs are not clipped', async () => {
    api.askAssistant
      .mockResolvedValueOnce({ answer: 'உங்கள் புரத இலக்கு **113 g**.', sourcesUsed: [], conversationId: 'c1', usage })
      .mockResolvedValueOnce({ answer: 'ඔබේ ප්‍රෝටීන් ඉලක්කය 113 g.', sourcesUsed: [], conversationId: 'c1', usage });
    await renderScreen();
    await ask(en.chat.placeholder, 'one', en.chat.send);
    const tamil = await screen.findByText(/உங்கள் புரத இலக்கு/);
    await ask(en.chat.placeholder, 'two', en.chat.send);
    const sinhala = await screen.findByText(/ඔබේ ප්‍රෝටීන් ඉලක්කය/);
    for (const node of [tamil, sinhala]) expect(StyleSheet.flatten(node.props.style).lineHeight).toBe(23);
    expect(screen.getByText('113 g')).toBeTruthy(); // bold numbers survive in Tamil text
  });

  it('only changes line height for Tamil and Sinhala text', () => {
    expect(hasIndicScript('Protein')).toBe(false);
    expect(hasIndicScript('புரதம்')).toBe(true);
    expect(hasIndicScript('ප්‍රෝටීන්')).toBe(true);
    expect(scriptAwareStyle('Protein', { fontSize: 14 })).toBeUndefined();
    expect(scriptAwareStyle('புரதம்', { fontSize: 14 })).toEqual({ lineHeight: 23 });
  });
});

describe('Locale files', () => {
  const keys = (value: unknown, prefix = ''): string[] =>
    typeof value === 'object' && value !== null
      ? Object.entries(value).flatMap(([k, v]) => keys(v, prefix ? `${prefix}.${k}` : k))
      : [prefix];
  const placeholders = (text: string) => (text.match(/\{\{\w+\}\}/g) ?? []).sort();
  const lookup = (obj: unknown, path: string) => path.split('.').reduce<any>((o, k) => o?.[k], obj);

  it('Tamil and Sinhala translate every English string with the same placeholders', () => {
    const english = keys(en).sort();
    for (const locale of [ta, si]) {
      expect(keys(locale).sort()).toEqual(english);
      for (const key of english) expect(placeholders(lookup(locale, key))).toEqual(placeholders(lookup(en, key)));
    }
  });

  it('Tamil and Sinhala strings are written in their own scripts', () => {
    const scriptKeys = keys(en).filter((k) => !k.startsWith('chat.language.') || k.endsWith('label') || k.endsWith('auto'));
    for (const key of scriptKeys) {
      expect(lookup(ta, key)).toMatch(/[஀-௿]/);
      expect(lookup(si, key)).toMatch(/[඀-෿]/);
    }
  });
});
