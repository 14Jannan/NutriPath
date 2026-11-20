import { isAxiosError } from 'axios';
import React, { createContext, useCallback, useContext, useEffect, useState, ReactNode } from 'react';
import { getSessionVersion, onSessionEnded, restoreSession } from '@/api/client';
import * as authApi from '@/api/authApi';
import { getMyProfile } from '@/api/profileApi';
import { cancelMealReminders } from '@/notifications/mealReminders';

// Whether the logged-in user has finished the required profile setup.
// 'checking' while we ask the server; 'error' if we couldn't find out
// (e.g. offline) — never guessed, so nobody is wrongly sent to setup
// or wrongly let past it.
export type ProfileStatus = 'checking' | 'incomplete' | 'complete' | 'error';

interface AuthContextValue {
  isLoggedIn: boolean;
  isLoading: boolean;
  loginRequired: boolean;
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
  const [loginRequired, setLoginRequired] = useState(false);
  const [profileStatus, setProfileStatus] = useState<ProfileStatus>('checking');
  const [fullName, setFullName] = useState('');

  const resetSession = useCallback(() => {
    setIsLoggedIn(false);
    setProfileStatus('checking');
    setFullName('');
    void cancelMealReminders().catch(() => {});
  }, []);

  const refreshProfileStatus = useCallback(async () => {
    const version = getSessionVersion();
    setProfileStatus('checking');
    try {
      const profile = await getMyProfile();
      if (version !== getSessionVersion()) return;
      setFullName(profile.fullName ?? '');
      // Targets are only calculated once the goals form has been saved.
      setProfileStatus(profile.targetCalories > 0 ? 'complete' : 'incomplete');
    } catch (error) {
      if (version !== getSessionVersion()) return;
      // 404: the account no longer exists (e.g. deleted), so the saved
      // login is useless. 401 here means the session couldn't be renewed.
      // Both are a logout, not a connection problem.
      const status = isAxiosError(error) ? error.response?.status : undefined;
      if (status === 404 || status === 401) {
        resetSession();
        await authApi.logout();
        return;
      }
      setProfileStatus('error');
    }
  }, [resetSession]);

  useEffect(() => {
    let active = true;
    const version = getSessionVersion();
    const unsubscribe = onSessionEnded(resetSession);
    void restoreSession().then((restored) => {
      if (!active || !restored || version !== getSessionVersion()) return;
      setLoginRequired(true);
      setIsLoggedIn(true);
      void refreshProfileStatus();
    }).catch(() => {
      if (active && version === getSessionVersion()) resetSession();
    }).finally(() => {
      if (active) setIsLoading(false);
    });
    return () => {
      active = false;
      unsubscribe();
    };
  }, [refreshProfileStatus, resetSession]);

  async function loginUser(email: string, password: string) {
    const auth = await authApi.login(email, password);
    setLoginRequired(true);
    setFullName(auth.fullName);
    setIsLoggedIn(true);
    await refreshProfileStatus();
  }

  async function logoutUser() {
    setLoginRequired(true);
    await authApi.logout();
  }

  const markProfileComplete = useCallback(() => setProfileStatus('complete'), []);

  return (
    <AuthContext.Provider
      value={{
        isLoggedIn,
        isLoading,
        loginRequired,
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
