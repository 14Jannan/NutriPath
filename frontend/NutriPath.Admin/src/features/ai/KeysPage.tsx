import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { toast } from 'sonner';
import { AlertTriangle, Eye, EyeOff, KeyRound, PlugZap, RotateCcw, ShieldAlert } from 'lucide-react';
import { api, ApiError } from '@/api/client';
import type { CredentialStatus } from '@/api/types';
import { Badge, Button, Card, ErrorState, Field, Input, KeyValue, LoadingState, PageHeader } from '@/components/ui';
import { ConfirmDialog, Modal } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, relative } from '@/lib/utils';
import { testMessage, type TestResult } from './ProvidersPage';

const sourceTone = { Runtime: 'success', Environment: 'info', Missing: 'danger', Disabled: 'neutral' } as const;
const labels: Record<string, string> = { Gemini: 'Google Gemini', Groq: 'Groq', Usda: 'USDA FoodData Central' };

export default function KeysPage() {
  const { can } = useAuth();
  const data = useQuery({ queryKey: ['credentials'], queryFn: () => api<CredentialStatus[]>('/api/admin/credentials') });
  const [replacing, setReplacing] = useState<string | null>(null);
  const [rollback, setRollback] = useState<{ provider: string; id: string; version: number } | null>(null);
  const [revoke, setRevoke] = useState<{ provider: string; id: string; version: number } | null>(null);
  const test = useAction((provider: string) => api<TestResult>(`/api/admin/ai/providers/${provider}/test`, { method: 'POST' }), { success: testMessage, invalidate: [['credentials']] });
  const doRollback = useAction(() => api(`/api/admin/credentials/${rollback!.provider}/rollback/${rollback!.id}`, { method: 'POST' }), { success: 'Previous key reactivated.', invalidate: [['credentials']], silentError: true });
  const doRevoke = useAction((reason: string) => api(`/api/admin/credentials/${revoke!.provider}/revoke/${revoke!.id}`, { method: 'POST', body: { reason } }), { success: 'Key revoked. It can never be reactivated.', invalidate: [['credentials']], silentError: true });

  if (data.isPending) return <LoadingState />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const rotate = can('ai.secrets.rotate');

  return (
    <>
      <PageHeader
        title="API keys"
        icon={<KeyRound className="h-5 w-5" />}
        description="Keys are encrypted with AES-256-GCM before they're stored and are never shown again — only the last four characters. Replacing, rolling back or revoking a key needs a recent authenticator code."
      />
      {data.data.some((c) => !c.encryptionAvailable) && (
        <p className="mb-4 flex gap-2 rounded-lg bg-warning-soft px-4 py-3 text-sm text-warning"><AlertTriangle className="h-4 w-4 shrink-0" />Credential encryption keys aren't configured on the server (Credentials:EncryptionKeys), so keys can't be managed from the portal. Environment keys still work.</p>
      )}
      <div className="space-y-4">
        {data.data.map((c) => (
          <Card key={c.provider} className="p-5 animate-fade-up">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-base font-semibold text-fg">{labels[c.provider] ?? c.provider}</p>
                <p className="mt-0.5 font-mono text-sm text-muted">{c.hint ? `••••••••${c.hint}` : 'No key'}</p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge tone={sourceTone[c.source]}>{c.source === 'Runtime' ? `Portal-managed v${c.activeVersion}` : c.source === 'Environment' ? 'From server environment' : c.source}</Badge>
                {can('ai.providers.manage') && c.configured && <Button size="sm" variant="outline" loading={test.isPending && test.variables === c.provider} onClick={() => test.mutate(c.provider)}><PlugZap className="h-4 w-4" /> Test</Button>}
                {rotate && <Button size="sm" disabled={!c.encryptionAvailable} onClick={() => setReplacing(c.provider)}><KeyRound className="h-4 w-4" /> {c.configured ? 'Replace key' : 'Add key'}</Button>}
              </div>
            </div>
            <div className="mt-4">
              <KeyValue items={[
                ['Activated', c.activatedAtUtc ? formatDateTime(c.activatedAtUtc) : '—'],
                ['Last validated', c.lastValidatedAtUtc ? relative(c.lastValidatedAtUtc) : '—'],
                ['Last changed by', c.updatedBy ?? '—'],
              ]} />
            </div>
            {c.source === 'Environment' && <p className="mt-3 text-xs text-muted">Adding a key here takes over from the environment key. After that the environment key is no longer used, even if every portal key is revoked.</p>}
            {c.history.length > 0 && (
              <div className="mt-4 overflow-x-auto">
                <table className="w-full min-w-[520px] text-xs">
                  <thead><tr className="border-b border-border text-left text-muted"><th className="py-1.5 font-medium">Version</th><th className="font-medium">Key</th><th className="font-medium">Status</th><th className="font-medium">Added</th><th className="font-medium">By</th><th><span className="sr-only">Actions</span></th></tr></thead>
                  <tbody>
                    {c.history.map((h) => (
                      <tr key={h.id} className="border-b border-border last:border-0">
                        <td className="py-2 text-fg">v{h.version}</td>
                        <td className="font-mono text-muted">••••{h.hint}</td>
                        <td><Badge tone={h.status === 'Active' ? 'success' : h.status === 'Revoked' ? 'danger' : 'neutral'}>{h.status}</Badge></td>
                        <td className="text-muted">{formatDateTime(h.createdAtUtc)}</td>
                        <td className="text-muted">{h.createdBy ?? '—'}</td>
                        <td className="text-right">
                          {rotate && h.status === 'Inactive' && <Button size="sm" variant="ghost" onClick={() => setRollback({ provider: c.provider, id: h.id, version: h.version })}><RotateCcw className="h-3.5 w-3.5" /> Reactivate</Button>}
                          {rotate && h.status !== 'Revoked' && <Button size="sm" variant="ghost" className="text-danger" onClick={() => setRevoke({ provider: c.provider, id: h.id, version: h.version })}><ShieldAlert className="h-3.5 w-3.5" /> Revoke</Button>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        ))}
      </div>
      {replacing && <ReplaceKeyDialog provider={replacing} onClose={() => setReplacing(null)} />}
      <ConfirmDialog open={!!rollback} onOpenChange={(o) => !o && setRollback(null)} title={`Reactivate ${rollback?.provider} key v${rollback?.version}?`} description="It is tested first and only reactivated if it still works. The current key becomes inactive." confirmLabel="Test and reactivate" onConfirm={() => doRollback.mutateAsync(undefined)} />
      <ConfirmDialog open={!!revoke} onOpenChange={(o) => !o && setRevoke(null)} title={`Revoke ${revoke?.provider} key v${revoke?.version}?`} description="Revoked keys are permanently unusable here. If this is the active key, the assistant loses access to this provider until a new key is added. Also revoke it in the provider's console if it may have leaked." confirmLabel="Revoke key" tone="danger" requireReason onConfirm={(r) => doRevoke.mutateAsync(r)} />
    </>
  );
}

function ReplaceKeyDialog({ provider, onClose }: { provider: string; onClose: () => void }) {
  const [key, setKey] = useState('');
  const [show, setShow] = useState(false);
  const [error, setError] = useState<string>();
  const replace = useAction((apiKey: string) => api<TestResult>(`/api/admin/credentials/${provider}`, { method: 'PUT', body: { apiKey } }), { invalidate: [['credentials'], ['ai-providers']], silentError: true });
  const close = () => { setKey(''); onClose(); };
  return (
    <Modal
      open
      onOpenChange={(o) => !o && close()}
      title={`Replace the ${labels[provider] ?? provider} key`}
      description="The new key is tested with a free request first. Only a working key is encrypted and activated; otherwise the current key stays in place."
      footer={<><Button variant="outline" onClick={close}>Cancel</Button><Button
        loading={replace.isPending}
        disabled={key.trim().length < 8}
        onClick={() => {
          setError(undefined);
          replace.mutate(key.trim(), {
            onSuccess: () => { toast.success('New key tested and activated.'); close(); },
            onError: (e) => setError(e instanceof ApiError && e.detail ? `${e.message} ${e.detail}` : e.message),
          });
        }}
      >Test and activate</Button></>}
    >
      <form autoComplete="off" onSubmit={(e) => e.preventDefault()}>
        <Field label="New API key" htmlFor="new-key" error={error} hint="Pasted keys are sent once over HTTPS and never displayed again.">
          <div className="relative">
            <Input id="new-key" type={show ? 'text' : 'password'} autoComplete="new-password" spellCheck={false} value={key} onChange={(e) => setKey(e.target.value)} className="pr-10 font-mono" autoFocus />
            <button type="button" onClick={() => setShow((s) => !s)} className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-muted hover:text-fg cursor-pointer" aria-label={show ? 'Hide key' : 'Show key'}>
              {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
        </Field>
      </form>
    </Modal>
  );
}
