import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Check, FileLock2, X } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { Paged } from '@/api/types';
import { Badge, Button, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select } from '@/components/ui';
import { DataTable, Pagination, TableCard, type Column } from '@/components/data';
import { ConfirmDialog, Modal } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, relative } from '@/lib/utils';

interface DataRequestRow {
  id: string; userId: string; type: 'Export' | 'Deletion'; status: 'Pending' | 'Completed' | 'Rejected' | 'Cancelled'; email: string;
  userNote: string | null; adminNote: string | null; createdAtUtc: string; processedAtUtc: string | null; exportExpiresAtUtc: string | null;
  exportReady: boolean; accountExists: boolean; isAdministrator: boolean; processedBy: string | null;
}

const statusTone = (s: string) => (s === 'Pending' ? 'warning' : s === 'Completed' ? 'success' : s === 'Rejected' ? 'danger' : 'neutral');

export default function PrivacyPage() {
  const { can } = useAuth();
  const [filters, setFilters] = useUrlState({ status: 'Pending', type: '', page: '1' });
  const list = useQuery({ queryKey: ['data-requests', filters], queryFn: () => api<Paged<DataRequestRow>>(`/api/admin/privacy/requests${qs({ ...filters, pageSize: 25 })}`), placeholderData: (p) => p });
  const [approving, setApproving] = useState<DataRequestRow | null>(null);
  const [rejecting, setRejecting] = useState<DataRequestRow | null>(null);
  const reject = useAction((reason: string) => api(`/api/admin/privacy/requests/${rejecting!.id}/reject`, { body: { reason } }), { success: 'Request rejected; the user sees your reason in the app.', invalidate: [['data-requests']], silentError: true });
  const manage = can('privacy.requests.manage');

  const columns: Column<DataRequestRow>[] = [
    { id: 'user', header: 'Account', cell: (r) => <div><p className="font-medium">{r.email}</p>{r.isAdministrator && <Badge tone="warning">Administrator</Badge>}</div> },
    { id: 'type', header: 'Request', cell: (r) => <Badge tone={r.type === 'Deletion' ? 'danger' : 'info'}>{r.type === 'Deletion' ? 'Delete account' : 'Data export'}</Badge> },
    { id: 'status', header: 'Status', cell: (r) => <Badge tone={statusTone(r.status)}>{r.status}</Badge> },
    { id: 'note', header: 'Notes', cell: (r) => <span className="block max-w-xs truncate text-xs text-muted">{r.adminNote ?? r.userNote ?? '—'}</span>, hideOnMobile: true },
    { id: 'when', header: 'Requested', cell: (r) => <span className="text-xs text-muted">{relative(r.createdAtUtc)}</span> },
    {
      id: 'actions', header: <span className="sr-only">Actions</span>, className: 'text-right',
      cell: (r) => manage && r.status === 'Pending' && (
        <div className="flex justify-end gap-1">
          <Button size="sm" variant={r.type === 'Deletion' ? 'danger' : 'primary'} disabled={r.isAdministrator && r.type === 'Deletion'} onClick={() => setApproving(r)}><Check className="h-3.5 w-3.5" /> {r.type === 'Deletion' ? 'Delete' : 'Approve'}</Button>
          <Button size="sm" variant="ghost" onClick={() => setRejecting(r)}><X className="h-3.5 w-3.5" /> Reject</Button>
        </div>
      ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Privacy requests"
        icon={<FileLock2 className="h-5 w-5" />}
        description="Requests users make in the app for a copy of their data or deletion of their account. Exports are built in the background, encrypted, and downloadable by the user in the app until they expire. Deletion is immediate and permanent."
      />
      <TableCard
        toolbar={
          <>
            <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-40">
              <option value="">All statuses</option>{['Pending', 'Completed', 'Rejected', 'Cancelled'].map((s) => <option key={s}>{s}</option>)}
            </Select>
            <Select aria-label="Type" value={filters.type} onChange={(e) => setFilters({ type: e.target.value })} className="w-40">
              <option value="">All types</option><option value="Export">Data export</option><option value="Deletion">Account deletion</option>
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(r) => r.id} empty={<EmptyState title="No requests match" />} />
        )}
      </TableCard>
      {approving && <ApproveDialog request={approving} onClose={() => setApproving(null)} />}
      <ConfirmDialog open={!!rejecting} onOpenChange={(o) => !o && setRejecting(null)} title="Reject this request?" description="The user sees your reason in the app. Reject only when the request can't be honoured (e.g. legal hold)." confirmLabel="Reject" tone="danger" requireReason onConfirm={(r) => reject.mutateAsync(r)} />
    </>
  );
}

function ApproveDialog({ request, onClose }: { request: DataRequestRow; onClose: () => void }) {
  const deletion = request.type === 'Deletion';
  const [note, setNote] = useState('');
  const [confirmEmail, setConfirmEmail] = useState('');
  const approve = useAction(() => api(`/api/admin/privacy/requests/${request.id}/approve`, { body: { note: note || null } }), {
    success: deletion ? 'Account and all its data deleted.' : 'Export queued. The user can download it in the app once it is ready.',
    invalidate: [['data-requests'], ['users']],
  });
  return (
    <Modal
      open
      onOpenChange={(o) => !o && onClose()}
      title={deletion ? 'Permanently delete this account?' : 'Approve data export'}
      description={deletion
        ? `Deletes ${request.email}: profile, meal logs, custom foods, conversations, feedback and usage records. This can't be undone. A record without personal data is kept.`
        : `Builds a JSON copy of ${request.email}'s data. It is encrypted at rest and expires automatically.`}
      footer={<><Button variant="outline" onClick={onClose}>Cancel</Button><Button variant={deletion ? 'danger' : 'primary'} loading={approve.isPending} disabled={deletion && confirmEmail.trim().toLowerCase() !== request.email.toLowerCase()} onClick={() => approve.mutate(undefined, { onSuccess: onClose })}>{deletion ? 'Delete account' : 'Approve export'}</Button></>}
    >
      <div className="space-y-4">
        <p className="text-xs text-muted">Requested {formatDateTime(request.createdAtUtc)}{request.userNote ? ` · “${request.userNote}”` : ''}</p>
        <Field label="Note (audited, optional)" htmlFor="dr-note"><Input id="dr-note" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. identity confirmed by email reply" /></Field>
        {deletion && <Field label={`Type ${request.email} to confirm`} htmlFor="dr-confirm"><Input id="dr-confirm" value={confirmEmail} onChange={(e) => setConfirmEmail(e.target.value)} autoComplete="off" /></Field>}
      </div>
    </Modal>
  );
}
