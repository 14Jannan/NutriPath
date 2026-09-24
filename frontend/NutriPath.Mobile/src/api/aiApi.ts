import { apiClient } from './client';

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
  const response = await apiClient.post<ChatResponse>('/api/ai/chat', { question });
  return response.data;
}
