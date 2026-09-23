import axios from 'axios';
import * as SecureStore from 'expo-secure-store';

const API_BASE_URL = process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://localhost:5087';

export const ACCESS_TOKEN_KEY = 'nutripath.accessToken';
export const REFRESH_TOKEN_KEY = 'nutripath.refreshToken';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' },
});

// Runs before EVERY request this client sends. Reads the saved access
// token and attaches it as a Bearer header automatically — no screen
// ever has to remember to do this itself.
apiClient.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Runs after EVERY response. If the server says 401 (token expired/
// invalid), try exactly once to use the refresh token to get a new
// access token, then retry the original request. If that also fails,
// give up and clear everything — the app will treat this as "logged out."
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true; // prevent an infinite retry loop

      const refreshToken = await SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
      if (refreshToken) {
        try {
          const response = await axios.post(`${API_BASE_URL}/api/auth/refresh`, { refreshToken });
          const { accessToken, refreshToken: newRefreshToken } = response.data;

          await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, accessToken);
          await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, newRefreshToken);

          originalRequest.headers.Authorization = `Bearer ${accessToken}`;
          return apiClient(originalRequest); // retry the original call
        } catch {
          await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
          await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
        }
      }
    }

    return Promise.reject(error);
  }
);