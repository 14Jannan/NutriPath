import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Eraser, Save, Settings2 } from 'lucide-react';
import { api } from '@/api/client';
import { Button, Card, CardHeader, ErrorState, Field, Input, LoadingState, PageHeader, Textarea } from '@/components/ui';
import { ConfirmDialog } from '@/components/overlays';
import { VersionHistory } from '@/components/versions';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime } from '@/lib/utils';

interface SystemSettings {
  maintenanceMessage: string | null; supportEmail: string | null; metricsRetentionDays: number; feedbackRetentionDays: number;
  auditRetentionDays: number; notificationRetentionDays: number; exportExpiryDays: number;
}
interface SettingsResponse { version: number; settings: SystemSettings; lastRetention: { status: string; finishedAtUtc: string | null; resultJson: string | null } | null }

const retentionFields: { key: keyof SystemSettings; label: string; min: number; max: number; hint: string }[] = [
  { key: 'metricsRetentionDays', label: 'AI request metrics (days)', min: 30, max: 730, hint: 'Per-request metadata used for usage charts.' },
  { key: 'feedbackRetentionDays', label: 'Answer feedback (days)', min: 30, max: 730, hint: 'Feedback linked to an open safety incident is kept until it closes.' },
  { key: 'auditRetentionDays', label: 'Audit logs (days)', min: 365, max: 3650, hint: 'At least one year.' },
  { key: 'notificationRetentionDays', label: 'Portal notifications (days)', min: 7, max: 365, hint: '' },
  { key: 'exportExpiryDays', label: 'Data export download window (days)', min: 1, max: 30, hint: 'Exports are deleted after this.' },
];

export default function SystemSettingsPage() {
  const { can } = useAuth();
  const data = useQuery({ queryKey: ['system-settings'], queryFn: () => api<SettingsResponse>('/api/admin/settings') });
  const [form, setForm] = useState<SystemSettings | null>(null);
  const [note, setNote] = useState('');
  const [confirmRun, setConfirmRun] = useState(false);
  const editable = can('system.settings.manage');

  useEffect(() => {
    if (data.data) setForm({ ...data.data.settings });
  }, [data.data]);

  const save = useAction(
    () => api<{ version: number }>('/api/admin/settings', { method: 'PUT', body: { document: form, expectedVersion: data.data!.version, note: note || null } }),
    { success: (r) => `Saved as version ${r.version}.`, invalidate: [['system-settings'], ['platform-history', 'system'], ['app-status']] },
  );
  const run = useAction(() => api('/api/admin/settings/retention/run', { method: 'POST' }), { success: 'Retention clean-up queued. Track it under Background jobs.', invalidate: [['system-settings'], ['jobs']] });

  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  if (data.isPending || !form) return <LoadingState />;
  const dirty = JSON.stringify(form) !== JSON.stringify(data.data.settings);
  const set = (patch: Partial<SystemSettings>) => setForm((f) => ({ ...f!, ...patch }));

  return (
    <>
      <PageHeader
        title="System settings"
        icon={<Settings2 className="h-5 w-5" />}
        description="Platform-wide settings stored in the database, versioned and audited. Secrets and privacy routing stay in deployment configuration."
        actions={editable && <Button disabled={!dirty} loading={save.isPending} onClick={() => save.mutate(undefined, { onSuccess: () => setNote('') })}><Save className="h-4 w-4" /> Save changes</Button>}
      />
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          <Card>
            <CardHeader title="Announcements" description="The maintenance message appears in the mobile app and at the top of this portal." />
            <div className="grid gap-4 px-5 pb-5">
              <Field label="Maintenance message (blank = none)" htmlFor="maintenance" hint={`${form.maintenanceMessage?.length ?? 0}/300`}>
                <Textarea id="maintenance" rows={2} maxLength={300} disabled={!editable} value={form.maintenanceMessage ?? ''} onChange={(e) => set({ maintenanceMessage: e.target.value || null })} />
              </Field>
              <Field label="Support email shown to users" htmlFor="support-email">
                <Input id="support-email" type="email" disabled={!editable} value={form.supportEmail ?? ''} onChange={(e) => set({ supportEmail: e.target.value || null })} />
              </Field>
            </div>
          </Card>
          <Card>
            <CardHeader
              title="Data retention"
              description="A daily job deletes data older than these periods."
              action={editable && <Button size="sm" variant="outline" onClick={() => setConfirmRun(true)}><Eraser className="h-4 w-4" /> Run now</Button>}
            />
            <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2">
              {retentionFields.map((f) => (
                <Field key={f.key} label={f.label} htmlFor={f.key} hint={`${f.min}–${f.max}. ${f.hint}`}>
                  <Input id={f.key} type="number" min={f.min} max={f.max} disabled={!editable} value={String(form[f.key] ?? '')} onChange={(e) => set({ [f.key]: Number(e.target.value) } as Partial<SystemSettings>)} />
                </Field>
              ))}
            </div>
            <p className="px-5 pb-5 text-xs text-muted">
              Last clean-up: {data.data.lastRetention ? `${data.data.lastRetention.status}${data.data.lastRetention.finishedAtUtc ? ` · ${formatDateTime(data.data.lastRetention.finishedAtUtc)}` : ''}${data.data.lastRetention.resultJson ? ` · deleted ${data.data.lastRetention.resultJson}` : ''}` : 'never'}
            </p>
          </Card>
          {editable && dirty && (
            <Card className="p-4"><Field label="Change note (optional)" htmlFor="settings-note"><Input id="settings-note" maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} /></Field></Card>
          )}
        </div>
        <VersionHistory docKey="system" current={data.data.version} canRollback={editable} invalidate={[['system-settings'], ['app-status']]} />
      </div>
      <ConfirmDialog open={confirmRun} onOpenChange={setConfirmRun} title="Run the retention clean-up now?" description="Permanently deletes data older than the saved retention periods." confirmLabel="Run clean-up" tone="danger" onConfirm={() => run.mutateAsync(undefined)} />
    </>
  );
}
