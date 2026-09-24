import { apiClient } from './client';

// Mirrors ChatResponse in DTOs/AiDtos.cs.
export interface ChatResponse {
  answer: string;
  sourcesUsed: string[];
}

export async function askAssistant(question: string): Promise<ChatResponse> {
  const response = await apiClient.post<ChatResponse>('/api/ai/chat', { question });
  return response.data;
}
