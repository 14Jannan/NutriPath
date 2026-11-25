import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, Save } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, ErrorState, Field, KeyValue, LoadingState, PageHeader, Select, Textarea } from '@/components/ui';
import { Timeline } from '@/components/data';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, languageName } from '@/lib/utils';
import { CATEGORIES, SEVERITIES, categoryLabel, incidentTone, severityTone, sourceLabel } from './SafetyPage';

interface IncidentDetail {
  id: string; title: string; source: string; category: string; severity: string; status: string; description: string | null; resolution: string | null;
  language: string | null; provider: string | null; model: string | null; createdAtUtc: string; updatedAtUtc: string | null; resolvedAtUtc: string | null;
  feedbackId: string | null; assignedToUserId: string | null; assignedTo: string | null; createdBy: string | null;
  feedback: { id: string; reason: string | null; question: string; answer: string; comment: string } | null;
  history: { atUtc: string; actorEmail: string | null; action: string; summary: string | null; reason: string | null }[];
}

export default function SafetyDetailPage() {
  const { id } = useParams();
  const { can, profile } = useAuth();
  const data = useQuery({ queryKey: ['incident', id], queryFn: () => api<IncidentDetail>(`/api/admin/safety/incidents/${id}`) });
  const admins = useQuery({ queryKey: ['administrators'], queryFn: () => api<{ id: string; email: string }[]>('/api/admin/administrators'), enabled: can('admins.read') });
  const [form, setForm] = useState({ status: '', severity: '', category: '', resolution: '', assignee: '' });

  useEffect(() => {
    if (data.data) setForm({ status: data.data.status, severity: data.data.severity, category: data.data.category, resolution: data.data.resolution ?? '', assignee: data.data.assignedToUserId ?? '' });
  }, [data.data]);

  const save = useAction(() => {
    const d = data.data!;
    return api(`/api/admin/safety/incidents/${id}`, {
      method: 'PATCH',
      body: {
        status: form.status !== d.status ? form.status : null,
        severity: form.severity !== d.severity ? form.severity : null,
        category: form.category !== d.category ? form.category : null,
        resolution: form.resolution !== (d.resolution ?? '') ? form.resolution : null,
        assignedToUserId: form.assignee && form.assignee !== d.assignedToUserId ? form.assignee : null,
        unassign: !form.assignee && !!d.assignedToUserId,
      },
    });
  }, { success: 'Incident updated.', invalidate: [['incident', id], ['incidents'], ['safety-summary']] });

  if (data.isPending) return <LoadingState rows={8} />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const d = data.data;
  const editable = can('safety.manage');
  const closing = form.status === 'Resolved' || form.status === 'Dismissed';

  return (
    <>
      <Link to="/safety" className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-fg"><ArrowLeft className="h-4 w-4" /> Safety incidents</Link>
      <PageHeader
        title={d.title}
        description={<span className="flex flex-wrap items-center gap-2"><Badge tone={severityTone(d.severity)}>{d.severity}</Badge><Badge tone={incidentTone(d.status)}>{d.status}</Badge>{sourceLabel(d.source)} · {categoryLabel(d.category)}</span>}
      />
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          {d.description && <Card className="p-4"><p className="mb-1 text-xs font-medium text-muted">Description</p><p className="whitespace-pre-wrap text-sm text-fg">{d.description}</p></Card>}
          {d.feedback ? (
            <Card>
              <CardHeader title="Reported conversation" description="Shared by the user with their feedback. Viewing it is audited." />
              <div className="space-y-3 px-5 pb-5 text-sm">
                <div><p className="text-xs font-medium text-muted">Question</p><p className="whitespace-pre-wrap text-fg">{d.feedback.question}</p></div>
                <div><p className="text-xs font-medium text-muted">Answer</p><p className="whitespace-pre-wrap text-fg">{d.feedback.answer}</p></div>
                {d.feedback.comment && <div><p className="text-xs font-medium text-muted">User comment</p><p className="whitespace-pre-wrap text-fg">{d.feedback.comment}</p></div>}
              </div>
            </Card>
          ) : d.feedbackId ? (
            <Card className="p-4 text-sm text-muted">This incident links to user feedback, which needs the feedback.read permission to view.</Card>
          ) : null}
          {editable && (
            <Card>
              <CardHeader title="Update" />
              <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2">
                <Field label="Status" htmlFor="inc-status"><Select id="inc-status" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })}>{['Open', 'Investigating', 'Resolved', 'Dismissed'].map((v) => <option key={v}>{v}</option>)}</Select></Field>
                <Field label="Severity" htmlFor="inc-severity"><Select id="inc-severity" value={form.severity} onChange={(e) => setForm({ ...form, severity: e.target.value })}>{SEVERITIES.map((v) => <option key={v}>{v}</option>)}</Select></Field>
                <Field label="Category" htmlFor="inc-category"><Select id="inc-category" value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}>{CATEGORIES.map((v) => <option key={v} value={v}>{categoryLabel(v)}</option>)}</Select></Field>
                <Field label="Assigned to" htmlFor="inc-assignee">
                  <Select id="inc-assignee" value={form.assignee} onChange={(e) => setForm({ ...form, assignee: e.target.value })}>
                    <option value="">Unassigned</option>
                    {profile && !admins.data && <option value={profile.id}>Me ({profile.email})</option>}
                    {admins.data?.map((a) => <option key={a.id} value={a.id}>{a.email}</option>)}
                  </Select>
                </Field>
                <div className="sm:col-span-2">
                  <Field label={closing ? 'Resolution (required to close)' : 'Resolution / findings'} htmlFor="inc-resolution">
                    <Textarea id="inc-resolution" rows={4} maxLength={2000} value={form.resolution} onChange={(e) => setForm({ ...form, resolution: e.target.value })} />
                  </Field>
                </div>
                <div><Button loading={save.isPending} disabled={closing && !form.resolution.trim()} onClick={() => save.mutate(undefined)}><Save className="h-4 w-4" /> Save</Button></div>
              </div>
            </Card>
          )}
        </div>
        <div className="space-y-4">
          <Card className="p-4">
            <KeyValue items={[
              ['Opened', formatDateTime(d.createdAtUtc)],
              ['Opened by', d.createdBy ?? '—'],
              ['Assigned to', d.assignedTo ?? '—'],
              ['Language', d.language ? languageName(d.language) : '—'],
              ['Provider', d.provider ?? '—'],
              ['Model', d.model ?? '—'],
              ['Resolved', formatDateTime(d.resolvedAtUtc)],
            ]} />
          </Card>
          <Card>
            <CardHeader title="History" />
            <div className="px-5 pb-5">
              {d.history.length === 0 ? <p className="text-xs text-muted">No changes yet.</p> : (
                <Timeline items={d.history.map((h, i) => ({ key: `${h.atUtc}-${i}`, title: h.summary ?? h.action, meta: `${h.actorEmail ?? 'system'} · ${formatDateTime(h.atUtc)}` }))} />
              )}
            </div>
          </Card>
        </div>
      </div>
    </>
  );
}
