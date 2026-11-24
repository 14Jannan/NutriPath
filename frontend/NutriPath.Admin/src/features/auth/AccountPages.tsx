import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { Clock, Compass, ShieldOff } from 'lucide-react';
import { publicPost } from '@/api/client';
import { Button, EmptyState, Field, Input } from '@/components/ui';
import { AuthShell } from './LoginPage';

/** Invitation links carry the token in the URL fragment (#...), which is never sent to servers or logs. */
export function AcceptInvitationPage() {
  const navigate = useNavigate();
  const token = typeof window !== 'undefined' ? window.location.hash.slice(1) : '';
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [email, setEmail] = useState<string | null>(null);
  if (!token) return <AuthShell title="Invitation link incomplete"><p className="text-sm text-muted">Open the full link you were sent, or ask an administrator for a new invitation.</p></AuthShell>;
  if (email) {
    return (
      <AuthShell title="Account created" subtitle={`Sign in as ${email}. You'll set up your authenticator app at first sign-in.`}>
        <Button className="w-full" onClick={() => navigate('/login')}>Go to sign in</Button>
      </AuthShell>
    );
  }
  const mismatch = confirm.length > 0 && confirm !== password;
  return (
    <AuthShell title="Accept your administrator invitation" subtitle="Create a password for your new admin account. Use a separate consumer account for personal nutrition tracking.">
      <form
        className="space-y-4"
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setError(undefined);
          try {
            const result = await publicPost<{ email: string }>('/api/admin/auth/invitations/accept', { token, fullName, password });
            window.history.replaceState(null, '', window.location.pathname); // drop the token from the address bar
            setEmail(result.email);
          } catch (err) {
            setError(err instanceof Error ? err.message : 'The invitation could not be accepted.');
          } finally {
            setBusy(false);
          }
        }}
      >
        <Field label="Full name" htmlFor="name"><Input id="name" value={fullName} onChange={(e) => setFullName(e.target.value)} required maxLength={100} autoComplete="name" /></Field>
        <Field label="Password" htmlFor="new-password" hint="At least 12 characters."><Input id="new-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={12} autoComplete="new-password" /></Field>
        <Field label="Confirm password" htmlFor="confirm" error={mismatch ? 'Passwords do not match.' : error}><Input id="confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required autoComplete="new-password" /></Field>
        <Button type="submit" className="w-full" loading={busy} disabled={!fullName.trim() || password.length < 12 || mismatch}>Create admin account</Button>
      </form>
    </AuthShell>
  );
}

/** Uses the standard emailed reset code; the new password works for both the app and the portal. */
export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  return (
    <AuthShell title="Reset your password" subtitle={sent ? 'Enter the 6-digit code we emailed and choose a new password.' : "We'll email a reset code if the account exists."}>
      {message ? (
        <div className="space-y-4">
          <p className="rounded-lg bg-primary-soft px-3 py-2 text-sm text-primary">{message}</p>
          <Link to="/login" className="block text-center text-sm text-primary hover:underline">Back to sign in</Link>
        </div>
      ) : (
        <form
          className="space-y-4"
          onSubmit={async (e) => {
            e.preventDefault();
            setBusy(true);
            setError(undefined);
            try {
              if (!sent) {
                await publicPost('/api/auth/forgot-password', { email });
                setSent(true);
              } else {
                await publicPost('/api/auth/reset-password', { email, code, newPassword: password });
                setMessage('Password updated. All sessions were signed out; sign in again.');
              }
            } catch (err) {
              setError(err instanceof Error ? err.message : 'That did not work.');
            } finally {
              setBusy(false);
            }
          }}
        >
          <Field label="Email" htmlFor="reset-email"><Input id="reset-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required disabled={sent} autoComplete="username" /></Field>
          {sent && (
            <>
              <Field label="Reset code" htmlFor="reset-code"><Input id="reset-code" inputMode="numeric" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} required /></Field>
              <Field label="New password" htmlFor="reset-password" hint="At least 12 characters for administrator accounts."><Input id="reset-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={12} autoComplete="new-password" /></Field>
            </>
          )}
          {error && <p className="text-sm text-danger" role="alert">{error}</p>}
          <Button type="submit" className="w-full" loading={busy}>{sent ? 'Set new password' : 'Send reset code'}</Button>
          <Link to="/login" className="block text-center text-sm text-muted hover:text-fg">Back to sign in</Link>
        </form>
      )}
    </AuthShell>
  );
}

export function SessionExpiredPage() {
  return (
    <AuthShell title="Your session has ended" subtitle="For security, admin sessions end after inactivity, when your access changes, or when you sign out elsewhere.">
      <div className="flex items-center gap-3 rounded-lg bg-surface-2 p-3 text-sm text-muted"><Clock className="h-5 w-5 text-primary" /> Sign in again to continue.</div>
      <Link to="/login" className="mt-4 block"><Button className="w-full">Sign in</Button></Link>
    </AuthShell>
  );
}

export function ForbiddenPage() {
  return <EmptyState icon={<ShieldOff className="h-6 w-6" />} title="403 — You don't have access to this page" description="Your role doesn't include the permission this page needs. Ask a SuperAdmin if you need it." action={<Link to="/"><Button variant="outline">Back to dashboard</Button></Link>} />;
}

export function NotFoundPage() {
  return <EmptyState icon={<Compass className="h-6 w-6" />} title="404 — Page not found" description="The page you're looking for doesn't exist." action={<Link to="/"><Button variant="outline">Back to dashboard</Button></Link>} />;
}
