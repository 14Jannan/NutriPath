import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Download, ScrollText } from 'lucide-react';
import { api, download, qs } from '@/api/client';
import type { AuditEntry, Paged } from '@/api/types';
import { Badge, Button, Card, EmptyState, ErrorState, Field, Input, KeyValue, LoadingState, PageHeader, Select } from '@/components/ui';
import { DataTable, Pagination, SearchInput, TableCard, type Column } from '@/components/data';
import { Drawer } from '@/components/overlays';
import { useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime } from '@/lib/utils';

const outcomeTone = (o: string) => (o === 'Success' ? 'success' : o === 'Denied' ? 'warning' : 'danger');

export default function AuditPage() {
  const { can } = useAuth();
  const [filters, setFilters] = useUrlState({ search: '', action: '', outcome: '', from: '', to: '', page: '1' });
  const [selected, setSelected] = useState<AuditEntry | null>(null);
  const actions = useQuery({ queryKey: ['audit-actions'], queryFn: () => api<string[]>('/api/admin/audit-logs/actions'), staleTime: 300_000 });
  const list = useQuery({ queryKey: ['audit', filters], queryFn: () => api<Paged<AuditEntry>>(`/api/admin/audit-logs${qs({ ...filters, pageSize: 50 })}`), placeholderData: (p) => p });

  const columns: Column<AuditEntry>[] = [
    { id: 'at', header: 'Time', cell: (a) => <span className="whitespace-nowrap text-xs text-muted">{formatDateTime(a.atUtc)}</span> },
    { id: 'actor', header: 'Actor', cell: (a) => <span className="text-xs">{a.actorEmail ?? 'system'}</span> },
    { id: 'action', header: 'Action', cell: (a) => <code className="text-xs text-fg">{a.action}</code> },
    { id: 'outcome', header: 'Outcome', cell: (a) => <Badge tone={outcomeTone(a.outcome)}>{a.outcome}</Badge> },
    { id: 'summary', header: 'Details', cell: (a) => <span className="block max-w-sm truncate text-xs text-muted">{a.summary ?? a.resourceType ?? '—'}</span>, hideOnMobile: true },
  ];

  return (
    <>
      <PageHeader
        title="Audit logs"
        icon={<ScrollText className="h-5 w-5" />}
        description="Append-only record of administrator actions, sign-ins and denied attempts. Secrets, passwords and health data are never written to it."
        actions={can('audit.export') && <Button variant="outline" onClick={() => void download(`/api/admin/audit-logs/export${qs({ ...filters, page: undefined })}`, 'audit-log.csv')}><Download className="h-4 w-4" /> Export CSV</Button>}
      />
      <TableCard
        toolbar={
          <>
            <SearchInput label="Search audit log" placeholder="Search actor, summary or resource" value={filters.search} onChange={(search) => setFilters({ search })} />
            <Select aria-label="Action" value={filters.action} onChange={(e) => setFilters({ action: e.target.value })} className="w-52">
              <option value="">All actions</option>
              {actions.data?.map((a) => <option key={a}>{a}</option>)}
            </Select>
            <Select aria-label="Outcome" value={filters.outcome} onChange={(e) => setFilters({ outcome: e.target.value })} className="w-36">
              <option value="">All outcomes</option><option>Success</option><option>Failure</option><option>Denied</option>
            </Select>
            <Field label="From" htmlFor="audit-from"><Input id="audit-from" type="date" value={filters.from} onChange={(e) => setFilters({ from: e.target.value })} className="w-40" /></Field>
            <Field label="To" htmlFor="audit-to"><Input id="audit-to" type="date" value={filters.to} onChange={(e) => setFilters({ to: e.target.value })} className="w-40" /></Field>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(a) => String(a.id)} onRowClick={setSelected} empty={<EmptyState title="No audit entries match" />} />
        )}
      </TableCard>
      <Drawer open={!!selected} onOpenChange={(o) => !o && setSelected(null)} title={selected?.action ?? ''}>
        {selected && (
          <Card className="p-4">
            <KeyValue items={[
              ['Time', formatDateTime(selected.atUtc)],
              ['Outcome', <Badge key="o" tone={outcomeTone(selected.outcome)}>{selected.outcome}</Badge>],
              ['Actor', selected.actorEmail ?? 'system'],
              ['Actor roles', selected.actorRoles ?? '—'],
              ['Resource', selected.resourceType ? `${selected.resourceType} ${selected.resourceId ?? ''}` : '—'],
              ['Correlation ID', <code key="c" className="break-all text-xs">{selected.correlationId ?? '—'}</code>],
              ['Summary', selected.summary ?? '—'],
              ['Reason', selected.reason ?? '—'],
            ]} />
          </Card>
        )}
      </Drawer>
    </>
  );
}
