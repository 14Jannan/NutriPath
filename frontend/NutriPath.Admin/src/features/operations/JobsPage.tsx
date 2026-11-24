import { useQuery } from '@tanstack/react-query';
import { Ban, Database, RotateCcw } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { Job, Paged } from '@/api/types';
import { Badge, Button, EmptyState, ErrorState, LoadingState, PageHeader, Select } from '@/components/ui';
import { DataTable, Pagination, TableCard, type Column } from '@/components/data';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, relative } from '@/lib/utils';

export const jobTone = { Queued: 'info', Running: 'teal', Succeeded: 'success', Failed: 'danger', Cancelled: 'neutral' } as const;
const typeLabels: Record<string, string> = { 'knowledge.index': 'Index document', 'knowledge.reindex-all': 'Re-index all knowledge', 'usda.sync': 'USDA import' };

export default function JobsPage() {
  const { can } = useAuth();
  const [filters, setFilters] = useUrlState({ type: '', status: '', page: '1' });
  const list = useQuery({
    queryKey: ['jobs', filters],
    queryFn: () => api<Paged<Job>>(`/api/admin/jobs${qs({ ...filters, pageSize: 25 })}`),
    placeholderData: (p) => p,
    refetchInterval: (q) => (q.state.data?.items.some((j) => j.status === 'Queued' || j.status === 'Running') ? 4000 : 30_000),
  });
  const cancel = useAction((id: string) => api(`/api/admin/jobs/${id}/cancel`, { method: 'POST' }), { success: 'Cancellation requested.', invalidate: [['jobs']] });
  const retry = useAction((id: string) => api(`/api/admin/jobs/${id}/retry`, { method: 'POST' }), { success: 'Job queued again.', invalidate: [['jobs']] });
  const manage = can('system.jobs.manage');

  const columns: Column<Job>[] = [
    { id: 'type', header: 'Job', cell: (j) => <div><p className="font-medium">{typeLabels[j.type] ?? j.type}</p><p className="text-xs text-muted">{j.createdBy ?? 'system'} · {relative(j.createdAtUtc)}</p></div> },
    {
      id: 'status', header: 'Status',
      cell: (j) => (
        <div className="min-w-32">
          <Badge tone={jobTone[j.status]}>{j.status}{j.cancelRequested && j.status === 'Running' ? ' (cancelling)' : ''}</Badge>
          {(j.status === 'Running' || j.status === 'Queued') && (
            <div className="mt-1.5 h-1.5 w-32 rounded-full bg-surface-2" role="progressbar" aria-valuenow={j.progress} aria-valuemin={0} aria-valuemax={100}><div className="h-1.5 rounded-full bg-primary transition-all" style={{ width: `${j.progress}%` }} /></div>
          )}
        </div>
      ),
    },
    { id: 'attempts', header: 'Attempts', cell: (j) => <span className="tabular-nums">{j.attempts}</span>, hideOnMobile: true },
    { id: 'times', header: 'Started / finished', cell: (j) => <span className="text-xs text-muted">{formatDateTime(j.startedAtUtc)}<br />{formatDateTime(j.finishedAtUtc)}</span>, hideOnMobile: true },
    { id: 'detail', header: 'Result', cell: (j) => <span className={`block max-w-xs truncate text-xs ${j.error ? 'text-danger' : 'text-muted'}`} title={j.error ?? j.result ?? ''}>{j.error ?? j.result ?? '—'}</span>, hideOnMobile: true },
    {
      id: 'actions', header: <span className="sr-only">Actions</span>, className: 'text-right',
      cell: (j) => manage && (
        <div className="flex justify-end gap-1">
          {(j.status === 'Queued' || j.status === 'Running') && !j.cancelRequested && <Button size="sm" variant="ghost" onClick={() => cancel.mutate(j.id)}><Ban className="h-3.5 w-3.5" /> Cancel</Button>}
          {(j.status === 'Failed' || j.status === 'Cancelled') && <Button size="sm" variant="ghost" onClick={() => retry.mutate(j.id)}><RotateCcw className="h-3.5 w-3.5" /> Retry</Button>}
        </div>
      ),
    },
  ];

  return (
    <>
      <PageHeader title="Background jobs" icon={<Database className="h-5 w-5" />} description="Durable jobs stored in the database: knowledge indexing and USDA imports. Failed jobs are retried up to three times automatically." />
      <TableCard
        toolbar={
          <>
            <Select aria-label="Type" value={filters.type} onChange={(e) => setFilters({ type: e.target.value })} className="w-52">
              <option value="">All types</option>
              {Object.entries(typeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </Select>
            <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-40">
              <option value="">All statuses</option>
              {Object.keys(jobTone).map((s) => <option key={s}>{s}</option>)}
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(j) => j.id} empty={<EmptyState title="No jobs" />} />
        )}
      </TableCard>
    </>
  );
}
