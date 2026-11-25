import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Save, ToggleRight } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, PageHeader, Switch } from '@/components/ui';
import { VersionHistory } from '@/components/versions';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';

interface FlagsResponse {
  version: number;
  flags: { key: string; name: string; description: string; default: boolean; clientVisible: boolean; enabled: boolean; overridden: boolean }[];
}

export default function FlagsPage() {
  const { can } = useAuth();
  const data = useQuery({ queryKey: ['feature-flags'], queryFn: () => api<FlagsResponse>('/api/admin/feature-flags') });
  const [values, setValues] = useState<Record<string, boolean>>({});
  const [note, setNote] = useState('');
  const editable = can('system.flags.manage');

  useEffect(() => {
    if (data.data) setValues(Object.fromEntries(data.data.flags.map((f) => [f.key, f.enabled])));
  }, [data.data]);

  const save = useAction(
    () => api<{ version: number }>('/api/admin/feature-flags', { method: 'PUT', body: { document: { flags: values }, expectedVersion: data.data!.version, note: note || null } }),
    { success: (r) => `Saved as version ${r.version}. Takes effect within 10 seconds.`, invalidate: [['feature-flags'], ['platform-history', 'flags'], ['app-status']] },
  );

  if (data.isPending) return <LoadingState />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const dirty = data.data.flags.some((f) => values[f.key] !== f.enabled);

  return (
    <>
      <PageHeader
        title="Feature flags"
        icon={<ToggleRight className="h-5 w-5" />}
        description="Switches the API checks on every request. Use them to pause a feature during an incident without a deployment. Every change is versioned and audited."
        actions={editable && <Button disabled={!dirty} loading={save.isPending} onClick={() => save.mutate(undefined, { onSuccess: () => setNote('') })}><Save className="h-4 w-4" /> Save changes</Button>}
      />
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="space-y-3 xl:col-span-2">
          {data.data.flags.map((f) => (
            <Card key={f.key} className="flex flex-wrap items-start justify-between gap-4 p-4 animate-fade-up">
              <div className="min-w-0 max-w-xl">
                <p className="flex flex-wrap items-center gap-2 font-medium text-fg">
                  {f.name}
                  <code className="text-[11px] font-normal text-muted">{f.key}</code>
                  {f.clientVisible && <Badge tone="info">Affects the app</Badge>}
                  {values[f.key] !== f.default && <Badge tone="warning">Not default</Badge>}
                </p>
                <p className="mt-1 text-sm text-muted">{f.description}</p>
              </div>
              <Switch label={values[f.key] ? 'On' : 'Off'} checked={values[f.key] ?? f.enabled} disabled={!editable} onChange={(v) => setValues((s) => ({ ...s, [f.key]: v }))} />
            </Card>
          ))}
          {editable && dirty && (
            <Card className="p-4">
              <Field label="Why are you changing this? (optional, kept in history)" htmlFor="flag-note"><Input id="flag-note" maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
            </Card>
          )}
        </div>
        <VersionHistory docKey="flags" current={data.data.version} canRollback={editable} invalidate={[['feature-flags'], ['app-status']]} />
      </div>
    </>
  );
}
