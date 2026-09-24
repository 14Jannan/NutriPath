import { apiClient, ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from './client';
import * as SecureStore from './tokenStorage';

// These shapes mirror the C# records in DTOs/AuthDtos.cs exactly.
// Keeping them in sync by hand is a real maintenance cost worth naming
// honestly — Phase 19 (API docs) or a generated-client approach is the
// long-term fix; for now, matching them carefully is the discipline.

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
  userId: string;
  email: string;
  fullName: string;
}

export async function register(fullName: string, email: string, password: string) {
  await apiClient.post('/api/auth/register', { fullName, email, password });
}

export async function verifyOtp(email: string, code: string) {
  await apiClient.post('/api/auth/verify-otp', { email, code });
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  const response = await apiClient.post<AuthResponse>('/api/auth/login', { email, password });
  await persistTokens(response.data);
  return response.data;
}

export async function forgotPassword(email: string) {
  await apiClient.post('/api/auth/forgot-password', { email });
}

export async function resetPassword(email: string, code: string, newPassword: string) {
  await apiClient.post('/api/auth/reset-password', { email, code, newPassword });
}

export async function logout() {
  await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
  await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
}

async function persistTokens(auth: AuthResponse) {
  await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, auth.accessToken);
  await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, auth.refreshToken);
}