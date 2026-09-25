import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
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
}));
const api = jest.mocked(aiApi);

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };
const now = new Date().toISOString();

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
  api.askAssistant.mockResolvedValue({ answer: 'Have dinner: rice and fish.', sourcesUsed: [], conversationId: 'c1' });
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

describe('AssistantScreen', () => {
  it('reopens the latest chat, with a day divider', async () => {
    await renderScreen();

    await waitFor(() => expect(screen.getByText('Try lentils and rice.')).toBeTruthy());
    expect(api.getConversationMessages).toHaveBeenCalledWith('c1');
    expect(screen.getByText('Today')).toBeTruthy();
  });

  it('continues the open chat, so the AI has its earlier messages', async () => {
    await renderScreen();
    await waitFor(() => expect(screen.getByText('Try lentils and rice.')).toBeTruthy());

    await fireEvent.changeText(screen.getByPlaceholderText('Ask NutriPath anything...'), 'What about dinner?');
    await fireEvent.press(screen.getByLabelText('Send'));

    await waitFor(() => expect(screen.getByText('Have dinner: rice and fish.')).toBeTruthy());
    expect(api.askAssistant).toHaveBeenCalledWith('What about dinner?', 'c1');
  });

  it('finds an old message through history search and opens that chat', async () => {
    await renderScreen();
    await waitFor(() => expect(screen.getByText('Try lentils and rice.')).toBeTruthy());

    await fireEvent.press(screen.getByLabelText('Chat history'));
    await waitFor(() => expect(screen.getByText('Is dhal good for protein?')).toBeTruthy());

    await fireEvent.changeText(screen.getByLabelText('Search your messages'), 'dhal');
    await waitFor(() => expect(api.searchChats).toHaveBeenCalledWith('dhal'));
    await fireEvent.press(await screen.findByLabelText('Open message in Is dhal good for protein?'));

    await waitFor(() => expect(screen.getByText('Yes, dhal is a good source.')).toBeTruthy());
    expect(api.getConversationMessages).toHaveBeenLastCalledWith('c0');
  });
});
