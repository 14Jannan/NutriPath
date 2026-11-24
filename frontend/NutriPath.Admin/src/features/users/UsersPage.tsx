import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Download, KeyRound, LogOut, Mail, Pause, Play, Users } from 'lucide-react';
import { api, download, qs } from '@/api/client';
import type { Paged, UserRow } from '@/api/types';
import { Badge, Button, Card, ErrorState, Field, Input, KeyValue, LoadingState, PageHeader, Select, EmptyState } from '@/components/ui';
import { DataTable, Pagination, SearchInput, TableCard, type Column } from '@/components/data';
import { ConfirmDialog, Drawer } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDate, formatDateTime, languageName, number, relative } from '@/lib/utils';

interface UserDetails {
  user: UserRow;
  suspendedAtUtc: string | null;
  suspensionReason: string | null;
  lockedUntilUtc: string | null;
  mobileSessionActive: boolean;
  adminSessions: number;
  aiUsageByDay: { date: string; tokens: number; requests: number }[];
  usageLimit: { tokenLimit: number; windowHours: number; overridden: boolean; reason: string | null };
  activity: { atUtc: string; action: string; outcome: string; actorEmail: string | null; reason: string | null; summary: string | null }[];
}

export const statusTone = (status: UserRow['status']) =>
  ({ Active: 'success', Suspended: 'danger', Locked: 'warning', Unverified: 'neutral' } as const)[status];

export default function UsersPage() {
  const { can } = useAuth();
  const [filters, setFilters] = useUrlState({ search: '', status: '', role: '', language: '', sort: 'newest', page: '1' });
  const [selected, setSelected] = useState<string | null>(null);
  const params = { ...filters, sort: filters.sort === 'newest' ? undefined : filters.sort, pageSize: 25 };
  const list = useQuery({ queryKey: ['users', filters], queryFn: () => api<Paged<UserRow>>(`/api/admin/users${qs(params)}`), placeholderData: (p) => p });

  const columns: Column<UserRow>[] = [
    { id: 'name', header: 'User', cell: (u) => <div className="min-w-0"><p className="truncate font-medium">{u.fullName || '—'}</p><p className="truncate text-xs text-muted">{u.email}</p></div> },
    { id: 'status', header: 'Status', cell: (u) => <Badge tone={statusTone(u.status)}>{u.status}</Badge> },
    { id: 'roles', header: 'Account', cell: (u) => (u.roles.length ? u.roles.map((r) => <Badge key={r} tone="info" className="mr-1">{r}</Badge>) : <span className="text-xs text-muted">Consumer</span>), hideOnMobile: true },
    { id: 'language', header: 'Language', cell: (u) => <span className="text-xs">{languageName(u.preferredLanguage)}</span>, hideOnMobile: true },
    { id: 'tokens', header: 'AI tokens (30d)', cell: (u) => <span className="tabular-nums">{number(u.aiTokens30d)}</span>, className: 'text-right', hideOnMobile: true },
    { id: 'created', header: 'Registered', cell: (u) => <span className="text-xs text-muted">{formatDate(u.createdAtUtc)}</span>, hideOnMobile: true },
    { id: 'login', header: 'Last sign-in', cell: (u) => <span className="text-xs text-muted">{relative(u.lastLoginAtUtc)}</span> },
  ];

  return (
    <>
      <PageHeader
        title="Users"
        icon={<Users className="h-5 w-5" />}
        description="Registered NutriPath accounts. Viewing a user's details is recorded in the audit log; meal logs and conversations are not shown here."
        actions={can('users.export') && (
          <Button variant="outline" onClick={() => void download(`/api/admin/users/export${qs({ ...params, page: undefined, pageSize: undefined })}`, 'users.csv')}>
            <Download className="h-4 w-4" /> Export CSV
          </Button>
        )}
      />
      <TableCard
        toolbar={
          <>
            <SearchInput label="Search users" placeholder="Search name or email" value={filters.search} onChange={(search) => setFilters({ search })} />
            <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-36">
              <option value="">All statuses</option>
              {['Active', 'Suspended', 'Locked', 'Unverified'].map((s) => <option key={s}>{s}</option>)}
            </Select>
            <Select aria-label="Account type" value={filters.role} onChange={(e) => setFilters({ role: e.target.value })} className="w-40">
              <option value="">All accounts</option>
              <option value="User">Consumers</option>
              {['SuperAdmin', 'UserAdmin', 'AIManager', 'NutritionReviewer', 'SupportAdmin'].map((r) => <option key={r} value={r}>{r}</option>)}
            </Select>
            <Select aria-label="Language" value={filters.language} onChange={(e) => setFilters({ language: e.target.value })} className="w-36">
              <option value="">All languages</option>
              <option value="en">English</option>
              <option value="ta">Tamil</option>
              <option value="si">Sinhala</option>
            </Select>
            <Select aria-label="Sort" value={filters.sort} onChange={(e) => setFilters({ sort: e.target.value })} className="w-40">
              <option value="newest">Newest first</option>
              <option value="oldest">Oldest first</option>
              <option value="email">Email A–Z</option>
              <option value="lastLogin">Last sign-in</option>
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(u) => u.id} onRowClick={(u) => setSelected(u.id)} empty={<EmptyState title="No users match these filters" />} />
        )}
      </TableCard>
      <UserDrawer id={selected} onClose={() => setSelected(null)} />
    </>
  );
}

type Pending = 'suspend' | 'reactivate' | 'revoke' | 'reset' | 'limit-remove' | null;

function UserDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { can, profile } = useAuth();
  const [pending, setPending] = useState<Pending>(null);
  const details = useQuery({ queryKey: ['user', id], queryFn: () => api<UserDetails>(`/api/admin/users/${id}`), enabled: !!id });
  const invalidate = [['user', id], ['users']];
  const post = (path: string, body?: unknown) => api(`/api/admin/users/${id}/${path}`, { method: 'POST', body: body ?? {} });
  const self = id === profile?.id;

  const suspend = useAction((reason: string) => post('suspend', { reason }), { success: 'User suspended and signed out.', invalidate, silentError: true });
  const reactivate = useAction((reason: string) => post('reactivate', { reason }), { success: 'User reactivated.', invalidate, silentError: true });
  const revoke = useAction((reason: string) => post('revoke-sessions', { reason }), { success: 'All sessions revoked.', invalidate, silentError: true });
  const reset = useAction(() => post('password-reset'), { success: 'Password reset email sent.', invalidate, silentError: true });
  const removeLimit = useAction(() => api(`/api/admin/users/${id}/usage-limits`, { method: 'DELETE' }), { success: 'Usage limit reset to the default.', invalidate, silentError: true });

  const d = details.data;
  return (
    <Drawer open={!!id} onOpenChange={(open) => !open && onClose()} title={d?.user.fullName || d?.user.email || 'User'}>
      {details.isPending ? <LoadingState /> : details.isError ? <ErrorState error={details.error} /> : d && (
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={statusTone(d.user.status)}>{d.user.status}</Badge>
            {d.user.roles.map((r) => <Badge key={r} tone="info">{r}</Badge>)}
            {self && <Badge>You</Badge>}
          </div>
          {d.suspensionReason && <p className="rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger">Suspended {relative(d.suspendedAtUtc)}: {d.suspensionReason}</p>}
          <Card className="p-4">
            <KeyValue
              items={[
                ['Email', d.user.email],
                ['Email verified', d.user.emailVerified ? 'Yes' : 'No'],
                ['Registered', formatDateTime(d.user.createdAtUtc)],
                ['Last sign-in', formatDateTime(d.user.lastLoginAtUtc)],
                ['Preferred language', languageName(d.user.preferredLanguage)],
                ['Mobile session', d.mobileSessionActive ? 'Signed in' : 'None'],
                ['Admin sessions', number(d.adminSessions)],
                ['Locked until', d.lockedUntilUtc ? formatDateTime(d.lockedUntilUtc) : '—'],
              ]}
            />
          </Card>

          {!self && (
            <div className="flex flex-wrap gap-2">
              {can('users.suspend') && (d.user.status === 'Suspended'
                ? <Button variant="outline" onClick={() => setPending('reactivate')}><Play className="h-4 w-4" /> Reactivate</Button>
                : <Button variant="danger" onClick={() => setPending('suspend')}><Pause className="h-4 w-4" /> Suspend</Button>)}
              {can('users.revoke_sessions') && <Button variant="outline" onClick={() => setPending('revoke')}><LogOut className="h-4 w-4" /> Revoke sessions</Button>}
              {can('users.manage') && <Button variant="outline" onClick={() => setPending('reset')}><Mail className="h-4 w-4" /> Send password reset</Button>}
            </div>
          )}

          <UsageLimitCard id={id!} limit={d.usageLimit} canManage={can('ai.usage.manage')} onRemove={() => setPending('limit-remove')} />

          <Card className="p-4">
            <p className="mb-2 text-sm font-medium text-fg">Assistant usage (last 30 days)</p>
            {d.aiUsageByDay.length === 0 ? <p className="text-xs text-muted">No assistant requests.</p> : (
              <ul className="space-y-1 text-xs">
                {d.aiUsageByDay.map((day) => (
                  <li key={day.date} className="flex justify-between"><span className="text-muted">{formatDate(day.date)}</span><span className="tabular-nums text-fg">{number(day.requests)} requests · {number(day.tokens)} tokens</span></li>
                ))}
              </ul>
            )}
          </Card>

          <Card className="p-4">
            <p className="mb-2 text-sm font-medium text-fg">Admin actions on this account</p>
            {d.activity.length === 0 ? <p className="text-xs text-muted">None recorded.</p> : (
              <ul className="space-y-2 text-xs">
                {d.activity.map((a, i) => (
                  <li key={i}>
                    <code className="text-fg">{a.action}</code> <span className="text-muted">by {a.actorEmail ?? 'system'} · {formatDateTime(a.atUtc)}</span>
                    {a.reason && <p className="text-muted">Reason: {a.reason}</p>}
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
      <ConfirmDialog open={pending === 'suspend'} onOpenChange={(o) => !o && setPending(null)} title="Suspend this user?" description="They are signed out of the app and the portal immediately and cannot sign in until reactivated." confirmLabel="Suspend" tone="danger" requireReason onConfirm={(r) => suspend.mutateAsync(r)} />
      <ConfirmDialog open={pending === 'reactivate'} onOpenChange={(o) => !o && setPending(null)} title="Reactivate this user?" description="They will be able to sign in again." confirmLabel="Reactivate" requireReason onConfirm={(r) => reactivate.mutateAsync(r)} />
      <ConfirmDialog open={pending === 'revoke'} onOpenChange={(o) => !o && setPending(null)} title="Revoke all sessions?" description="Signs the user out everywhere. Their account stays active." confirmLabel="Revoke sessions" tone="danger" requireReason onConfirm={(r) => revoke.mutateAsync(r)} />
      <ConfirmDialog open={pending === 'reset'} onOpenChange={(o) => !o && setPending(null)} title="Send a password reset email?" description="The user receives a reset code by email. Their current password keeps working until they change it." confirmLabel="Send email" onConfirm={() => reset.mutateAsync(undefined)} />
      <ConfirmDialog open={pending === 'limit-remove'} onOpenChange={(o) => !o && setPending(null)} title="Remove the custom limit?" description="The user goes back to the default assistant usage limit." confirmLabel="Remove" onConfirm={() => removeLimit.mutateAsync(undefined)} />
    </Drawer>
  );
}

function UsageLimitCard({ id, limit, canManage, onRemove }: { id: string; limit: UserDetails['usageLimit']; canManage: boolean; onRemove: () => void }) {
  const [editing, setEditing] = useState(false);
  const [tokens, setTokens] = useState(String(limit.tokenLimit));
  const [hours, setHours] = useState(String(limit.windowHours));
  const [reason, setReason] = useState('');
  const save = useAction(
    () => api(`/api/admin/users/${id}/usage-limits`, { method: 'PUT', body: { tokenLimit: Number(tokens), windowHours: Number(hours), reason } }),
    { success: 'Usage limit saved.', invalidate: [['user', id]] },
  );
  return (
    <Card className="p-4">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="text-sm font-medium text-fg">Assistant usage limit</p>
          <p className="text-xs text-muted">
            {number(limit.tokenLimit)} tokens per {limit.windowHours} h {limit.overridden ? <Badge tone="warning" className="ml-1">Custom</Badge> : <span>(default)</span>}
          </p>
          {limit.reason && <p className="mt-1 text-xs text-muted">Reason: {limit.reason}</p>}
        </div>
        {canManage && !editing && (
          <div className="flex gap-2">
            {limit.overridden && <Button size="sm" variant="ghost" onClick={onRemove}>Reset</Button>}
            <Button size="sm" variant="outline" onClick={() => setEditing(true)}><KeyRound className="h-3.5 w-3.5" /> Change</Button>
          </div>
        )}
      </div>
      {editing && (
        <form className="mt-4 grid gap-3 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); save.mutate(undefined, { onSuccess: () => setEditing(false) }); }}>
          <Field label="Token limit" htmlFor="limit-tokens"><Input id="limit-tokens" type="number" min={0} max={10_000_000} value={tokens} onChange={(e) => setTokens(e.target.value)} required /></Field>
          <Field label="Window (hours)" htmlFor="limit-hours"><Input id="limit-hours" type="number" min={1} max={720} value={hours} onChange={(e) => setHours(e.target.value)} required /></Field>
          <div className="sm:col-span-2"><Field label="Reason (audited)" htmlFor="limit-reason"><Input id="limit-reason" value={reason} onChange={(e) => setReason(e.target.value)} minLength={5} maxLength={300} required /></Field></div>
          <div className="flex gap-2 sm:col-span-2">
            <Button type="submit" size="sm" loading={save.isPending} disabled={reason.trim().length < 5}>Save limit</Button>
            <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
          </div>
        </form>
      )}
    </Card>
  );
}
