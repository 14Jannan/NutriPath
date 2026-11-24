import { useState, type ReactNode } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { QRCodeSVG } from 'qrcode.react';
import { Copy, Download, Eye, EyeOff, KeyRound, ShieldCheck, Smartphone } from 'lucide-react';
import { useAuth } from '@/providers/auth';
import type { LoginStep } from '@/api/types';
import { Button, Card, Field, Input } from '@/components/ui';
import { Logo } from '@/layouts/AdminLayout';

const credentialsSchema = z.object({
  email: z.string().trim().email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
});

export function AuthShell({ children, title, subtitle }: { children: ReactNode; title: string; subtitle?: ReactNode }) {
  return (
    <div className="flex min-h-full items-center justify-center bg-[radial-gradient(ellipse_at_top,var(--primary-soft),transparent_60%)] px-4 py-10">
      <div className="w-full max-w-md animate-fade-up">
        <div className="mb-6 flex justify-center">
          <Logo />
        </div>
        <Card className="p-6 sm:p-8">
          <h1 className="text-xl font-semibold tracking-tight text-fg">{title}</h1>
          {subtitle && <p className="mt-1.5 text-sm text-muted">{subtitle}</p>}
          <div className="mt-6">{children}</div>
        </Card>
        <p className="mt-6 text-center text-xs text-muted">Authorized NutriPath administrators only. Activity is audited.</p>
      </div>
    </div>
  );
}

export function LoginPage() {
  const { status, login } = useAuth();
  const location = useLocation();
  const [step, setStep] = useState<LoginStep | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [serverError, setServerError] = useState<string>();
  const form = useForm<z.infer<typeof credentialsSchema>>({ resolver: zodResolver(credentialsSchema), defaultValues: { email: '', password: '' } });
  const from = (location.state as { from?: string } | null)?.from ?? '/';

  // The MFA step goes first: after enrolment it must show the one-time recovery codes even
  // though the session is already signed in.
  if (step) return <MfaStep step={step} onRestart={() => setStep(null)} redirectTo={from} />;
  if (status === 'signed-in') return <Navigate to={from} replace />;

  return (
    <AuthShell title="Sign in to the admin portal" subtitle="Use your administrator account. You'll confirm with your authenticator app next.">
      <form
        noValidate
        className="space-y-4"
        onSubmit={form.handleSubmit(async (values) => {
          setServerError(undefined);
          try {
            setStep(await login(values.email, values.password));
            form.reset({ email: values.email, password: '' });
          } catch (e) {
            setServerError(e instanceof Error ? e.message : 'Sign-in failed.');
          }
        })}
      >
        <Field label="Email" htmlFor="email" error={form.formState.errors.email?.message}>
          <Input id="email" type="email" autoComplete="username" autoFocus {...form.register('email')} />
        </Field>
        <Field label="Password" htmlFor="password" error={form.formState.errors.password?.message}>
          <div className="relative">
            <Input id="password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" className="pr-10" {...form.register('password')} />
            <button
              type="button"
              onClick={() => setShowPassword((s) => !s)}
              className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-muted hover:text-fg cursor-pointer"
              aria-label={showPassword ? 'Hide password' : 'Show password'}
            >
              {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
        </Field>
        {serverError && <p className="rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger" role="alert">{serverError}</p>}
        <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>
          Continue
        </Button>
        <div className="text-center">
          <Link to="/forgot-password" className="text-sm text-primary hover:underline">Forgot password?</Link>
        </div>
      </form>
    </AuthShell>
  );
}

function MfaStep({ step, onRestart, redirectTo }: { step: LoginStep; onRestart: () => void; redirectTo: string }) {
  const { verifyMfa } = useAuth();
  const navigate = useNavigate();
  const [code, setCode] = useState('');
  const [useRecovery, setUseRecovery] = useState(false);
  const [recovery, setRecovery] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const enrolling = step.step === 'mfa-enroll';

  if (recoveryCodes) return <RecoveryCodes codes={recoveryCodes} onDone={() => navigate(redirectTo, { replace: true })} />;

  const submit = async () => {
    setBusy(true);
    setError(undefined);
    try {
      const result = await verifyMfa(step.ticket, useRecovery ? undefined : code, useRecovery ? recovery : undefined);
      if (result.recoveryCodes?.length) setRecoveryCodes(result.recoveryCodes);
      else navigate(redirectTo, { replace: true });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Verification failed.');
      setCode('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthShell
      title={enrolling ? 'Set up two-step verification' : 'Two-step verification'}
      subtitle={enrolling ? 'Administrator accounts need an authenticator app. Scan the code, then enter the 6-digit code it shows.' : 'Enter the 6-digit code from your authenticator app.'}
    >
      <form className="space-y-5" onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        {enrolling && step.otpAuthUri && step.enrollmentSecret && (
          <div className="space-y-3 rounded-xl border border-border bg-surface-2 p-4">
            <div className="flex justify-center rounded-lg bg-white p-3">
              {/* Rendered locally: the secret never goes to a third-party QR service. */}
              <QRCodeSVG value={step.otpAuthUri} size={168} level="M" aria-label="Authenticator QR code" />
            </div>
            <div className="flex items-start gap-2 text-xs text-muted">
              <Smartphone className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
              <span>Can't scan? Enter this key manually (time-based, 6 digits):</span>
            </div>
            <div className="flex items-center gap-2">
              <code className="min-w-0 flex-1 break-all rounded-md bg-surface px-2 py-1.5 font-mono text-xs text-fg">{step.enrollmentSecret.match(/.{1,4}/g)?.join(' ')}</code>
              <Button variant="outline" size="icon" aria-label="Copy key" onClick={() => void navigator.clipboard?.writeText(step.enrollmentSecret ?? '')}>
                <Copy className="h-4 w-4" />
              </Button>
            </div>
          </div>
        )}
        {!useRecovery ? (
          <Field label="Authenticator code" htmlFor="code" error={error}>
            <Input id="code" inputMode="numeric" autoComplete="one-time-code" autoFocus maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} className="text-center font-mono text-lg tracking-[0.5em]" />
          </Field>
        ) : (
          <Field label="Recovery code" htmlFor="recovery" error={error} hint="Each recovery code works once.">
            <Input id="recovery" autoComplete="off" autoFocus value={recovery} onChange={(e) => setRecovery(e.target.value)} placeholder="XXXXX-XXXXX" className="font-mono" />
          </Field>
        )}
        <Button type="submit" className="w-full" loading={busy} disabled={useRecovery ? recovery.trim().length < 10 : code.length !== 6}>
          <ShieldCheck className="h-4 w-4" /> {enrolling ? 'Verify and finish setup' : 'Verify'}
        </Button>
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          {!enrolling && (
            <button type="button" className="text-primary hover:underline cursor-pointer" onClick={() => { setUseRecovery((u) => !u); setError(undefined); }}>
              {useRecovery ? 'Use authenticator code' : 'Use a recovery code'}
            </button>
          )}
          <button type="button" className="text-muted hover:text-fg cursor-pointer" onClick={onRestart}>Start over</button>
        </div>
      </form>
    </AuthShell>
  );
}

export function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [saved, setSaved] = useState(false);
  const text = `NutriPath admin recovery codes\nEach code works once. Keep them somewhere safe.\n\n${codes.join('\n')}\n`;
  return (
    <AuthShell title="Save your recovery codes" subtitle="Use one if you lose your authenticator. They are shown only once.">
      <div className="grid grid-cols-2 gap-2 rounded-xl border border-border bg-surface-2 p-4 font-mono text-sm text-fg">
        {codes.map((code) => <span key={code}>{code}</span>)}
      </div>
      <div className="mt-4 flex flex-wrap gap-2">
        <Button variant="outline" size="sm" onClick={() => { void navigator.clipboard?.writeText(text); setSaved(true); }}>
          <Copy className="h-4 w-4" /> Copy
        </Button>
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
            const link = document.createElement('a');
            link.href = url;
            link.download = 'nutripath-admin-recovery-codes.txt';
            link.click();
            URL.revokeObjectURL(url);
            setSaved(true);
          }}
        >
          <Download className="h-4 w-4" /> Download
        </Button>
      </div>
      <label className="mt-5 flex items-center gap-2 text-sm text-fg">
        <input type="checkbox" checked={saved} onChange={(e) => setSaved(e.target.checked)} className="h-4 w-4 accent-[var(--primary)]" />
        I've saved these codes somewhere safe
      </label>
      <Button className="mt-4 w-full" disabled={!saved} onClick={onDone}>
        <KeyRound className="h-4 w-4" /> Continue to the portal
      </Button>
    </AuthShell>
  );
}
