import { apiClient, loginSession, logoutSession } from './client';

export async function register(fullName: string, email: string, password: string) {
  await apiClient.post('/api/auth/register', { fullName, email, password });
}

export async function verifyOtp(email: string, code: string) {
  await apiClient.post('/api/auth/verify-otp', { email, code });
}

export const login = loginSession;

export async function forgotPassword(email: string) {
  await apiClient.post('/api/auth/forgot-password', { email });
}

export async function resetPassword(email: string, code: string, newPassword: string) {
  await apiClient.post('/api/auth/reset-password', { email, code, newPassword });
}

export const logout = logoutSession;
