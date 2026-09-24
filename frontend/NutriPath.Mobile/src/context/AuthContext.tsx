import React, { createContext, useCallback, useContext, useEffect, useState, ReactNode } from 'react';
import * as SecureStore from '@/api/tokenStorage';
import { ACCESS_TOKEN_KEY } from '@/api/client';
import * as authApi from '@/api/authApi';
import { getMyProfile } from '@/api/profileApi';

// Whether the logged-in user has finished the required profile setup.
// 'checking' while we ask the server; 'error' if we couldn't find out
// (e.g. offline) — never guessed, so nobody is wrongly sent to setup
// or wrongly let past it.
export type ProfileStatus = 'checking' | 'incomplete' | 'complete' | 'error';

interface AuthContextValue {
  isLoggedIn: boolean;
  isLoading: boolean;
  profileStatus: ProfileStatus;
  // The name from sign-up, for a personal greeting during setup.
  fullName: string;
  loginUser: (email: string, password: string) => Promise<void>;
  logoutUser: () => Promise<void>;
  // Called once setup is saved, to let the user into the app.
  markProfileComplete: () => void;
  // Re-checks after an 'error' (the retry button).
  refreshProfileStatus: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [profileStatus, setProfileStatus] = useState<ProfileStatus>('checking');
  const [fullName, setFullName] = useState('');

  const refreshProfileStatus = useCallback(async () => {
    setProfileStatus('checking');
    try {
      const profile = await getMyProfile();
      setFullName(profile.fullName ?? '');
      // Targets are only calculated once the goals form has been saved.
      setProfileStatus(profile.targetCalories > 0 ? 'complete' : 'incomplete');
    } catch {
      // If the session couldn't be renewed, the client has cleared the
      // tokens: that's a logout, not a connection problem.
      if (!(await SecureStore.getItemAsync(ACCESS_TOKEN_KEY))) {
        setIsLoggedIn(false);
        return;
      }
      setProfileStatus('error');
    }
  }, []);

  // On app start, check whether a token already exists from a previous
  // session — this is what lets a user stay logged in across app restarts
  // instead of having to log in every single time.
  useEffect(() => {
    (async () => {
      const token = await SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
      setIsLoggedIn(!!token);
      setIsLoading(false);
      if (token) await refreshProfileStatus();
    })();
  }, [refreshProfileStatus]);

  async function loginUser(email: string, password: string) {
    const auth = await authApi.login(email, password);
    setFullName(auth.fullName);
    setIsLoggedIn(true);
    await refreshProfileStatus();
  }

  async function logoutUser() {
    await authApi.logout();
    setIsLoggedIn(false);
    setProfileStatus('checking');
    setFullName('');
  }

  const markProfileComplete = useCallback(() => setProfileStatus('complete'), []);

  return (
    <AuthContext.Provider
      value={{
        isLoggedIn,
        isLoading,
        profileStatus,
        fullName,
        loginUser,
        logoutUser,
        markProfileComplete,
        refreshProfileStatus,
      }}
    >
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
