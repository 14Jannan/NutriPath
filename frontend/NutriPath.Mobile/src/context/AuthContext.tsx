import React, { createContext, useContext, useEffect, useState, ReactNode } from 'react';
import * as SecureStore from 'expo-secure-store';
import { ACCESS_TOKEN_KEY } from '@/api/client';
import * as authApi from '@/api/authApi';

interface AuthContextValue {
  isLoggedIn: boolean;
  isLoading: boolean;
  loginUser: (email: string, password: string) => Promise<void>;
  logoutUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  // On app start, check whether a token already exists from a previous
  // session — this is what lets a user stay logged in across app restarts
  // instead of having to log in every single time.
  useEffect(() => {
    (async () => {
      const token = await SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
      setIsLoggedIn(!!token);
      setIsLoading(false);
    })();
  }, []);

  async function loginUser(email: string, password: string) {
    await authApi.login(email, password);
    setIsLoggedIn(true);
  }

  async function logoutUser() {
    await authApi.logout();
    setIsLoggedIn(false);
  }

  return (
    <AuthContext.Provider value={{ isLoggedIn, isLoading, loginUser, logoutUser }}>
      {children}
    </AuthContext.Provider>
  );
}

// A small custom hook so screens write `useAuth()` instead of the more
// verbose `useContext(AuthContext)` everywhere, and so we can throw a
// clear error if it's ever used outside the provider by mistake.
export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}