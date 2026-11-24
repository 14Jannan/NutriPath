import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api, logoutRequest, publicPost, refreshSession, session } from '@/api/client';
import type { AdminProfile, LoginStep, SessionResponse } from '@/api/types';
import { Button, Field, Input } from '@/components/ui';
import { Modal } from '@/components/overlays';

type Status = 'loading' | 'signed-out' | 'signed-in' | 'expired';

interface AuthContextValue {
  status: Status;
  profile: AdminProfile | null;
  can: (permission: string) => boolean;
  login: (email: string, password: string) => Promise<LoginStep>;
  verifyMfa: (ticket: string, code?: string, recoveryCode?: string) => Promise<SessionResponse>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Admin session. On load it tries the HttpOnly refresh cookie (so a page reload keeps the
 * session without ever storing a token in the browser). Sessions end on logout, on expiry, or
 * when the server rejects the token (suspension, role change, revocation).
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading');
  const [profile, setProfile] = useState<AdminProfile | null>(null);
  const [reauthOpen, setReauthOpen] = useState(false);
  const reauthResolver = useRef<((ok: boolean) => void) | null>(null);

  const loadProfile = useCallback(async () => {
    const me = await api<AdminProfile>('/api/admin/auth/me');
    setProfile(me);
  }, []);

  useEffect(() => {
    session.onExpired(() => {
      setProfile(null);
      setStatus((current) => (current === 'signed-in' ? 'expired' : 'signed-out'));
    });
    session.onReauth(
      () =>
        new Promise<boolean>((resolve) => {
          reauthResolver.current = resolve;
          setReauthOpen(true);
        }),
    );
    let cancelled = false;
    (async () => {
      if (await refreshSession()) {
        try {
          await loadProfile();
          if (!cancelled) setStatus('signed-in');
          return;
        } catch {
          /* fall through to signed out */
        }
      }
      if (!cancelled) setStatus('signed-out');
    })();
    return () => {
      cancelled = true;
    };
  }, [loadProfile]);

  // Keep the short-lived access token fresh while the tab is open.
  useEffect(() => {
    if (status !== 'signed-in') return;
    const timer = setInterval(() => void refreshSession(), 8 * 60 * 1000);
    return () => clearInterval(timer);
  }, [status]);

  const login = useCallback((email: string, password: string) => publicPost<LoginStep>('/api/admin/auth/login', { email, password }), []);

  const verifyMfa = useCallback(async (ticket: string, code?: string, recoveryCode?: string) => {
    const result = await publicPost<SessionResponse>('/api/admin/auth/mfa/verify', { ticket, code, recoveryCode });
    session.setToken(result.accessToken);
    setProfile(result.profile);
    setStatus('signed-in');
    return result;
  }, []);

  const logout = useCallback(async () => {
    await logoutRequest();
    setProfile(null);
    setStatus('signed-out');
  }, []);

  const can = useCallback((permission: string) => profile?.permissions.includes(permission) ?? false, [profile]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, profile, can, login, verifyMfa, logout, refreshProfile: loadProfile }),
    [status, profile, can, login, verifyMfa, logout, loadProfile],
  );

  return (
    <AuthContext.Provider value={value}>
      {children}
      <ReauthDialog
        open={reauthOpen}
        onDone={(ok) => {
          setReauthOpen(false);
          reauthResolver.current?.(ok);
          reauthResolver.current = null;
        }}
      />
    </AuthContext.Provider>
  );
}

/** Sensitive actions need password + TOTP within the last 15 minutes. */
function ReauthDialog({ open, onDone }: { open: boolean; onDone: (ok: boolean) => void }) {
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const reset = () => {
    setPassword('');
    setCode('');
    setError(undefined);
  };
  return (
    <Modal
      open={open}
      onOpenChange={(value) => {
        if (!value) {
          reset();
          onDone(false);
        }
      }}
      title="Confirm it's you"
      description="This action changes security settings. Enter your password and a code from your authenticator app."
    >
      <form
        className="space-y-4"
        onSubmit={async (event) => {
          event.preventDefault();
          setBusy(true);
          setError(undefined);
          try {
            const result = await api<{ accessToken: string }>('/api/admin/auth/reauth', { body: { password, code } });
            session.setToken(result.accessToken);
            reset();
            onDone(true);
          } catch (e) {
            setError(e instanceof Error ? e.message : 'That did not work.');
          } finally {
            setBusy(false);
          }
        }}
      >
        <Field label="Password" htmlFor="reauth-password">
          <Input id="reauth-password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </Field>
        <Field label="Authenticator code" htmlFor="reauth-code" error={error}>
          <Input id="reauth-code" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} required />
        </Field>
        <div className="flex justify-end gap-2">
          <Button
            variant="outline"
            onClick={() => {
              reset();
              onDone(false);
            }}
          >
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!password || code.length !== 6}>
            Confirm
          </Button>
        </div>
      </form>
    </Modal>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
}
