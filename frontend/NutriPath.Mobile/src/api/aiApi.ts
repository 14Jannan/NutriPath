import { apiClient } from './client';
import { todayIso } from '@/utils/date';

// Mirrors ChatResponse in DTOs/AiDtos.cs.
export interface ChatResponse {
  answer: string;
  sourcesUsed: string[];
}

// Mirrors ChatHistoryMessage in DTOs/AiDtos.cs.
export interface ChatHistoryMessage {
  id: string;
  role: string;
  content: string;
  createdAtUtc: string;
}

export async function getChatHistory(): Promise<ChatHistoryMessage[]> {
  const response = await apiClient.get<ChatHistoryMessage[]>('/api/ai/history');
  return response.data;
}

export async function askAssistant(question: string): Promise<ChatResponse> {
  // localDate makes "today" in the AI's answers match the user's own day.
  const response = await apiClient.post<ChatResponse>('/api/ai/chat', { question, localDate: todayIso() });
  return response.data;
}

// Starts a fresh conversation; earlier ones are kept on the server but no longer shown.
export async function startNewConversation(): Promise<void> {
  await apiClient.post('/api/ai/conversations');
}
