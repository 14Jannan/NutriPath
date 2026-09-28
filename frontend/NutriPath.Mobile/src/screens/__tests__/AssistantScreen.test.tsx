import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AxiosError, AxiosHeaders } from 'axios';
import * as aiApi from '@/api/aiApi';
import { AssistantScreen } from '@/screens/AssistantScreen';

jest.setTimeout(30000); // the first render loads the icon library

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

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };
const now = new Date().toISOString();
const usage = { tokensUsed: 2000, tokenLimit: 40000, windowHours: 5, locked: false, resetsAtUtc: null };
const lockedUntil = (resetsAt: Date) => ({ ...usage, tokensUsed: 40500, locked: true, resetsAtUtc: resetsAt.toISOString() });

// What the server sends when the allowance is used up.
function limitError(resetsAt: Date) {
  const headers = new AxiosHeaders();
  return new AxiosError('Too Many Requests', '429', undefined, undefined, {
    status: 429,
    statusText: 'Too Many Requests',
    headers,
    config: { headers },
    data: { message: 'Limit reached', usage: lockedUntil(resetsAt) },
  });
}

async function renderScreen() {
  await render(
    <SafeAreaProvider initialMetrics={metrics}>
      <AssistantScreen />
    </SafeAreaProvider>
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  api.listConversations.mockResolvedValue([
    { id: 'c1', title: 'What can I eat today', startedAtUtc: now, lastMessageAtUtc: now, messageCount: 2 },
    { id: 'c0', title: 'Is dhal good for protein?', startedAtUtc: now, lastMessageAtUtc: now, messageCount: 2 },
  ]);
  api.getConversationMessages.mockImplementation(async (id: string) =>
    id === 'c1'
      ? [
          { id: 'm1', role: 'User', content: 'What can I eat today', createdAtUtc: now },
          { id: 'm2', role: 'Assistant', content: 'Try lentils and rice.', createdAtUtc: now },
        ]
      : [
          { id: 'm3', role: 'User', content: 'Is dhal good for protein?', createdAtUtc: now },
          { id: 'm4', role: 'Assistant', content: 'Yes, dhal is a good source.', createdAtUtc: now },
        ]
  );
  api.askAssistant.mockImplementation(async (_question, conversationId) => ({
    answer: 'Have dinner: rice and fish.',
    sourcesUsed: [],
    conversationId: conversationId!,
    usage,
  }));
  api.startNewConversation.mockResolvedValue('c2');
  api.getUsage.mockResolvedValue(usage);
  api.searchChats.mockResolvedValue([
    {
      conversationId: 'c0',
      conversationTitle: 'Is dhal good for protein?',
      messageId: 'm4',
      role: 'Assistant',
      snippet: 'Yes, dhal is a good source.',
      createdAtUtc: now,
    },
  ]);
});

async function openFromHistory(title: string, reply: string) {
  await fireEvent.press(screen.getByLabelText('Chat history'));
  await fireEvent.press(await screen.findByText(title));
  await waitFor(() => expect(screen.getByText(reply)).toBeTruthy());
}

describe('AssistantScreen', () => {
  it('starts each session on a fresh chat, created with the first message', async () => {
    await renderScreen();

    expect(screen.getByText(/Ask me about your meals today/)).toBeTruthy();
    expect(api.getConversationMessages).not.toHaveBeenCalled();
    expect(api.startNewConversation).not.toHaveBeenCalled(); // no empty chats

    await fireEvent.changeText(screen.getByPlaceholderText('Ask NutriPath anything...'), 'What about dinner?');
    await fireEvent.press(screen.getByLabelText('Send'));

    await waitFor(() => expect(screen.getByText('Have dinner: rice and fish.')).toBeTruthy());
    expect(api.startNewConversation).toHaveBeenCalledTimes(1);
    expect(api.askAssistant).toHaveBeenCalledWith('What about dinner?', 'c2');
  });

  it('continues a chat reopened from history, so the AI has its earlier messages', async () => {
    await renderScreen();
    await openFromHistory('What can I eat today', 'Try lentils and rice.');
    expect(screen.getByText('Today')).toBeTruthy();

    await fireEvent.changeText(screen.getByPlaceholderText('Ask NutriPath anything...'), 'What about dinner?');
    await fireEvent.press(screen.getByLabelText('Send'));

    await waitFor(() => expect(screen.getByText('Have dinner: rice and fish.')).toBeTruthy());
    expect(api.askAssistant).toHaveBeenCalledWith('What about dinner?', 'c1');
    expect(api.startNewConversation).not.toHaveBeenCalled();
  });

  it('finds an old message through history search and opens that chat', async () => {
    await renderScreen();

    await fireEvent.press(screen.getByLabelText('Chat history'));
    await waitFor(() => expect(screen.getByText('Is dhal good for protein?')).toBeTruthy());

    await fireEvent.changeText(screen.getByLabelText('Search your messages'), 'dhal');
    await waitFor(() => expect(api.searchChats).toHaveBeenCalledWith('dhal'));
    await fireEvent.press(await screen.findByLabelText('Open message in Is dhal good for protein?'));

    await waitFor(() => expect(screen.getByText('Yes, dhal is a good source.')).toBeTruthy());
    expect(api.getConversationMessages).toHaveBeenLastCalledWith('c0');
  });

  it('shows when the assistant unlocks instead of the input while locked', async () => {
    api.getUsage.mockResolvedValue(lockedUntil(new Date(Date.now() + 90 * 60_000)));
    await renderScreen();

    await waitFor(() => expect(screen.getByText('Assistant limit reached')).toBeTruthy());
    expect(screen.getByText(/You can chat again at .* \(in 1h 30m\)/)).toBeTruthy();
    expect(screen.queryByPlaceholderText('Ask NutriPath anything...')).toBeNull();
    // Past chats can still be read.
    await openFromHistory('What can I eat today', 'Try lentils and rice.');
  });

  it('locks when the limit is hit mid-send, and gives the question back', async () => {
    api.askAssistant.mockRejectedValue(limitError(new Date(Date.now() + 20 * 60_000)));
    await renderScreen();

    await fireEvent.changeText(screen.getByPlaceholderText('Ask NutriPath anything...'), 'Plan my lunch');
    await fireEvent.press(screen.getByLabelText('Send'));

    await waitFor(() => expect(screen.getByText('Assistant limit reached')).toBeTruthy());
    expect(screen.getByText(/\(in 20m\)/)).toBeTruthy();
    expect(screen.queryByText('Plan my lunch')).toBeNull();
    expect(screen.queryByText(/Sorry, I couldn't answer/)).toBeNull();
  });
});
