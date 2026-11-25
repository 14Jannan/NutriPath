import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Plus, ShieldAlert, ShieldCheck } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { Paged } from '@/api/types';
import { Badge, Button, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select, Textarea } from '@/components/ui';
import { DataTable, Pagination, StatCard, TableCard, type Column } from '@/components/data';
import { Modal } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { languageName, number, relative } from '@/lib/utils';

export interface IncidentRow {
  id: string; title: string; source: string; category: string; severity: 'Low' | 'Medium' | 'High' | 'Critical'; status: string;
  language: string | null; provider: string | null; model: string | null; createdAtUtc: string; updatedAtUtc: string | null; assignedTo: string | null; hasFeedback: boolean;
}
export const CATEGORIES = ['AllergySafety', 'MedicalAdvice', 'Hallucination', 'HarmfulAdvice', 'Privacy', 'Other'];
export const SEVERITIES = ['Low', 'Medium', 'High', 'Critical'];
export const severityTone = (s: string) => (s === 'Critical' || s === 'High' ? 'danger' : s === 'Medium' ? 'warning' : 'neutral');
export const incidentTone = (s: string) => (s === 'Open' ? 'danger' : s === 'Investigating' ? 'warning' : s === 'Resolved' ? 'success' : 'neutral');
export const categoryLabel = (c: string) => c.replace(/([a-z])([A-Z])/g, '$1 $2');
export const sourceLabel = (s: string) => ({ AutomaticValidation: 'Automatic validation', UserReport: 'User report', Reviewer: 'Reviewer' } as Record<string, string>)[s] ?? s;

export default function SafetyPage() {
  const { can } = useAuth();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [filters, setFilters] = useUrlState({ status: 'Active', severity: '', category: '', source: '', page: '1' });
  const summary = useQuery({ queryKey: ['safety-summary'], queryFn: () => api<{ open: number; investigating: number; highOrCriticalActive: number; blockedAllergyGuarantees30d: number }>('/api/admin/safety/summary') });
  const list = useQuery({ queryKey: ['incidents', filters], queryFn: () => api<Paged<IncidentRow>>(`/api/admin/safety/incidents${qs({ ...filters, pageSize: 25 })}`), placeholderData: (p) => p });
  const creating = params.get('new') === '1';

  const columns: Column<IncidentRow>[] = [
    { id: 'title', header: 'Incident', cell: (i) => <div className="min-w-0"><p className="truncate font-medium">{i.title}</p><p className="text-xs text-muted">{sourceLabel(i.source)} · {categoryLabel(i.category)}</p></div> },
    { id: 'severity', header: 'Severity', cell: (i) => <Badge tone={severityTone(i.severity)}>{i.severity}</Badge> },
    { id: 'status', header: 'Status', cell: (i) => <Badge tone={incidentTone(i.status)}>{i.status}</Badge> },
    { id: 'context', header: 'Language / provider', cell: (i) => <span className="text-xs text-muted">{i.language ? languageName(i.language) : '—'} · {i.provider ?? '—'}</span>, hideOnMobile: true },
    { id: 'assigned', header: 'Assigned', cell: (i) => <span className="text-xs">{i.assignedTo ?? '—'}</span>, hideOnMobile: true },
    { id: 'when', header: 'Opened', cell: (i) => <span className="text-xs text-muted">{relative(i.createdAtUtc)}</span> },
  ];

  const s = summary.data;
  return (
    <>
      <PageHeader
        title="Safety incidents"
        icon={<ShieldAlert className="h-5 w-5" />}
        description="Raised automatically when validation blocks an unsafe answer, when a user reports an answer as unsafe, or by a reviewer. Incidents need a written resolution before they close."
        actions={can('safety.manage') && <Button onClick={() => setParams({ new: '1' })}><Plus className="h-4 w-4" /> New incident</Button>}
      />
      {s && (
        <div className="mb-4 grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Open" value={number(s.open)} tone="danger" icon={<ShieldAlert className="h-4 w-4" />} />
          <StatCard label="Investigating" value={number(s.investigating)} tone="warning" icon={<ShieldAlert className="h-4 w-4" />} />
          <StatCard label="High or critical (active)" value={number(s.highOrCriticalActive)} tone="danger" icon={<ShieldAlert className="h-4 w-4" />} />
          <StatCard label="Blocked allergy guarantees (30 days)" value={number(s.blockedAllergyGuarantees30d)} tone="teal" icon={<ShieldCheck className="h-4 w-4" />} hint="Users got the safe fallback" />
        </div>
      )}
      <TableCard
        toolbar={
          <>
            <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-52">
              <option value="Active">Open or investigating</option><option value="">All</option>
              {['Open', 'Investigating', 'Resolved', 'Dismissed'].map((v) => <option key={v}>{v}</option>)}
            </Select>
            <Select aria-label="Severity" value={filters.severity} onChange={(e) => setFilters({ severity: e.target.value })} className="w-36">
              <option value="">All severities</option>{SEVERITIES.map((v) => <option key={v}>{v}</option>)}
            </Select>
            <Select aria-label="Category" value={filters.category} onChange={(e) => setFilters({ category: e.target.value })} className="w-44">
              <option value="">All categories</option>{CATEGORIES.map((v) => <option key={v} value={v}>{categoryLabel(v)}</option>)}
            </Select>
            <Select aria-label="Source" value={filters.source} onChange={(e) => setFilters({ source: e.target.value })} className="w-48">
              <option value="">All sources</option>{['AutomaticValidation', 'UserReport', 'Reviewer'].map((v) => <option key={v} value={v}>{sourceLabel(v)}</option>)}
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(i) => i.id} onRowClick={(i) => navigate(`/safety/${i.id}`)} empty={<EmptyState icon={<ShieldCheck className="h-6 w-6" />} title="No incidents match" />} />
        )}
      </TableCard>
      {creating && <NewIncidentDialog feedbackId={params.get('feedback')} onClose={() => setParams({})} onCreated={(id) => navigate(`/safety/${id}`)} />}
    </>
  );
}

function NewIncidentDialog({ feedbackId, onClose, onCreated }: { feedbackId: string | null; onClose: () => void; onCreated: (id: string) => void }) {
  const [title, setTitle] = useState('');
  const [category, setCategory] = useState('Other');
  const [severity, setSeverity] = useState('Medium');
  const [description, setDescription] = useState('');
  const create = useAction(() => api<{ id: string }>('/api/admin/safety/incidents', { body: { title, category, severity, description: description || null, feedbackId } }), {
    success: 'Incident opened.', invalidate: [['incidents'], ['safety-summary'], ['notifications']],
  });
  return (
    <Modal
      open
      onOpenChange={(o) => !o && onClose()}
      title="Open a safety incident"
      description={feedbackId ? 'Linked to the selected feedback.' : "Describe the problem. Don't paste personal health data; link feedback instead."}
      footer={<><Button variant="outline" onClick={onClose}>Cancel</Button><Button loading={create.isPending} disabled={title.trim().length < 5} onClick={() => create.mutate(undefined, { onSuccess: (r) => onCreated(r.id) })}>Open incident</Button></>}
    >
      <div className="space-y-4">
        <Field label="Title" htmlFor="inc-title"><Input id="inc-title" maxLength={300} value={title} onChange={(e) => setTitle(e.target.value)} /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Category" htmlFor="inc-cat"><Select id="inc-cat" value={category} onChange={(e) => setCategory(e.target.value)}>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</Select></Field>
          <Field label="Severity" htmlFor="inc-sev"><Select id="inc-sev" value={severity} onChange={(e) => setSeverity(e.target.value)}>{SEVERITIES.map((c) => <option key={c}>{c}</option>)}</Select></Field>
        </div>
        <Field label="Description" htmlFor="inc-desc"><Textarea id="inc-desc" rows={4} maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
      </div>
    </Modal>
  );
}
