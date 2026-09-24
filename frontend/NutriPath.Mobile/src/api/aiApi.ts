import { apiClient } from './client';

// Mirrors AiChatResponse in DTOs/AiDtos.cs.
export async function askAssistant(message: string): Promise<string> {
  const response = await apiClient.post<{ reply: string }>('/api/ai/chat', { message });
  return response.data.reply;
}
