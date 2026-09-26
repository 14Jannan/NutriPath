import { apiClient } from './client';
import { toLocalIsoDateTime } from '@/utils/date';

// These shapes mirror DTOs/AiDtos.cs.

// The user's assistant allowance: a token limit over a rolling window.
export interface AiUsageStatus {
  tokensUsed: number;
  tokenLimit: number;
  windowHours: number;
  locked: boolean;
  // When chatting is possible again; only set while locked.
  resetsAtUtc: string | null;
}

export interface ChatResponse {
  answer: string;
  sourcesUsed: string[];
  // The conversation this answer was saved to.
  conversationId: string;
  // Usage after this answer.
  usage?: AiUsageStatus | null;
}

export interface ChatHistoryMessage {
  id: string;
  role: string; // "User" or "Assistant"
  content: string;
  createdAtUtc: string;
}

export interface ConversationSummary {
  id: string;
  // The conversation's first question, shortened.
  title: string;
  startedAtUtc: string;
  lastMessageAtUtc: string;
  messageCount: number;
}

export interface ChatSearchResult {
  conversationId: string;
  conversationTitle: string;
  messageId: string;
  role: string;
  // Text around the match.
  snippet: string;
  createdAtUtc: string;
}

/**
 * Asks the assistant. Sends the device's local date, time and timezone so
 * answers about "today" or "tonight" match the user's own clock, and the
 * conversation to continue (its recent messages are the AI's memory).
 */
export async function askAssistant(question: string, conversationId?: string | null): Promise<ChatResponse> {
  const response = await apiClient.post<ChatResponse>('/api/ai/chat', {
    question,
    localDateTime: toLocalIsoDateTime(),
    conversationId: conversationId ?? undefined,
  });
  return response.data;
}

export async function getUsage(): Promise<AiUsageStatus> {
  const response = await apiClient.get<AiUsageStatus>('/api/ai/usage');
  return response.data;
}

// The most recent conversation's messages, oldest first.
export async function getChatHistory(): Promise<ChatHistoryMessage[]> {
  const response = await apiClient.get<ChatHistoryMessage[]>('/api/ai/history');
  return response.data;
}

// Past conversations, most recently active first (empty ones are left out).
export async function listConversations(): Promise<ConversationSummary[]> {
  const response = await apiClient.get<ConversationSummary[]>('/api/ai/conversations');
  return response.data;
}

export async function getConversationMessages(conversationId: string): Promise<ChatHistoryMessage[]> {
  const response = await apiClient.get<ChatHistoryMessage[]>(`/api/ai/conversations/${conversationId}/messages`);
  return response.data;
}

// Case-insensitive search across all of the user's messages, newest first.
export async function searchChats(query: string): Promise<ChatSearchResult[]> {
  const response = await apiClient.get<ChatSearchResult[]>('/api/ai/search', { params: { q: query } });
  return response.data;
}

// Starts a fresh conversation and returns its id; earlier ones stay in the history.
export async function startNewConversation(): Promise<string> {
  const response = await apiClient.post<{ id: string }>('/api/ai/conversations');
  return response.data.id;
}

export async function deleteConversation(conversationId: string): Promise<void> {
  await apiClient.delete(`/api/ai/conversations/${conversationId}`);
}
