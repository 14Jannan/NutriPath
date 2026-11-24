import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { KeyRound, LogOut, Monitor, Moon, Sun, UserCircle } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, ErrorState, KeyValue, LoadingState, PageHeader } from '@/components/ui';
import { ConfirmDialog, Modal } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { useTheme, type ThemeChoice } from '@/providers/theme';
import { cn, formatDateTime, relative } from '@/lib/utils';

interface AdminSessionRow { id: string; createdAtUtc: string; lastUsedAtUtc: string | null; expiresAtUtc: string; userAgent: string | null; current: boolean }

const describeAgent = (ua: string | null) => {
  if (!ua) return 'Unknown browser';
  const browser = /Edg\//.test(ua) ? 'Edge' : /Chrome\//.test(ua) ? 'Chrome' : /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) ? 'Safari' : 'Browser';
  const os = /Windows/.test(ua) ? 'Windows' : /Mac OS/.test(ua) ? 'macOS' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Linux/.test(ua) ? 'Linux' : '';
  return os ? `${browser} on ${os}` : browser;
};

export default function ProfilePage() {
  const { profile } = useAuth();
  const { theme, setTheme } = useTheme();
  const [confirmRevoke, setConfirmRevoke] = useState(false);
  const [confirmCodes, setConfirmCodes] = useState(false);
  const [codes, setCodes] = useState<string[] | null>(null);
  const sessions = useQuery({ queryKey: ['my-sessions'], queryFn: () => api<AdminSessionRow[]>('/api/admin/auth/sessions') });
  const revokeOthers = useAction(() => api<{ revoked: number }>('/api/admin/auth/sessions/revoke-others', { method: 'POST' }), { success: (r) => `${r.revoked} other session(s) signed out.`, invalidate: [['my-sessions']], silentError: true });
  const regenerate = useAction(() => api<{ recoveryCodes: string[] }>('/api/admin/auth/mfa/recovery-codes', { method: 'POST' }), { silentError: true });

  if (!profile) return <LoadingState />;
  const themes: [ThemeChoice, string, typeof Sun][] = [['light', 'Light', Sun], ['dark', 'Dark', Moon], ['system', 'System', Monitor]];
  return (
    <>
      <PageHeader title="My profile" icon={<UserCircle className="h-5 w-5" />} description="Your administrator account, sessions and security settings." />
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <KeyValue items={[
            ['Name', profile.fullName || '—'],
            ['Email', profile.email],
            ['Roles', <span key="r" className="flex flex-wrap gap-1">{profile.roles.map((r) => <Badge key={r} tone="info">{r}</Badge>)}</span>],
            ['Two-step verification', profile.mfaEnabled ? <Badge key="m" tone="success">Enabled</Badge> : <Badge key="m" tone="warning">Not enrolled</Badge>],
            ['Permissions', `${profile.permissions.length}`],
          ]} />
        </Card>
        <Card>
          <CardHeader title="Appearance" />
          <div className="flex flex-wrap gap-2 px-5 pb-5" role="radiogroup" aria-label="Theme">
            {themes.map(([value, label, Icon]) => (
              <button key={value} type="button" role="radio" aria-checked={theme === value} onClick={() => setTheme(value)}
                className={cn('flex items-center gap-2 rounded-lg border px-4 py-2 text-sm transition-colors cursor-pointer', theme === value ? 'border-primary bg-primary-soft text-primary' : 'border-border text-fg hover:bg-surface-2')}>
                <Icon className="h-4 w-4" /> {label}
              </button>
            ))}
          </div>
        </Card>
        <Card className="lg:col-span-2">
          <CardHeader title="Active sessions" description="Admin sessions last at most 12 hours." action={<Button size="sm" variant="outline" onClick={() => setConfirmRevoke(true)} disabled={(sessions.data?.length ?? 0) < 2}><LogOut className="h-4 w-4" /> Sign out other sessions</Button>} />
          {sessions.isPending ? <LoadingState rows={2} /> : sessions.isError ? <ErrorState error={sessions.error} /> : (
            <ul className="divide-y divide-border">
              {sessions.data.map((s) => (
                <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 px-5 py-3 text-sm">
                  <div>
                    <p className="text-fg">{describeAgent(s.userAgent)} {s.current && <Badge tone="success" className="ml-1">This session</Badge>}</p>
                    <p className="text-xs text-muted">Signed in {formatDateTime(s.createdAtUtc)} · last active {relative(s.lastUsedAtUtc)} · expires {formatDateTime(s.expiresAtUtc)}</p>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card className="p-5 lg:col-span-2">
          <p className="text-sm font-medium text-fg">Recovery codes</p>
          <p className="mt-1 text-xs text-muted">Generate a new set if you've used several or think they've been exposed. The old codes stop working immediately. You may be asked for your password and an authenticator code.</p>
          <Button className="mt-3" variant="outline" size="sm" onClick={() => setConfirmCodes(true)}><KeyRound className="h-4 w-4" /> Generate new recovery codes</Button>
        </Card>
      </div>
      <ConfirmDialog open={confirmRevoke} onOpenChange={setConfirmRevoke} title="Sign out your other sessions?" description="Every other browser signed in to the portal with your account is signed out. This one stays signed in." confirmLabel="Sign out others" onConfirm={() => revokeOthers.mutateAsync(undefined)} />
      <ConfirmDialog open={confirmCodes} onOpenChange={setConfirmCodes} title="Replace your recovery codes?" description="Your existing recovery codes stop working." confirmLabel="Generate" tone="danger" onConfirm={() => regenerate.mutateAsync(undefined).then((r) => setCodes(r.recoveryCodes))} />
      <Modal open={!!codes} onOpenChange={(o) => !o && setCodes(null)} title="Your new recovery codes" description="Shown only once. Store them somewhere safe." footer={<Button onClick={() => setCodes(null)}>I've saved them</Button>}>
        <div className="grid grid-cols-2 gap-2 rounded-xl bg-surface-2 p-4 font-mono text-sm">{codes?.map((c) => <span key={c}>{c}</span>)}</div>
        <Button className="mt-3" variant="outline" size="sm" onClick={() => void navigator.clipboard?.writeText(codes?.join('\n') ?? '')}>Copy</Button>
      </Modal>
    </>
  );
}
