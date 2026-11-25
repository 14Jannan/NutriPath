import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Copy, MailPlus, ShieldCheck, ShieldOff, UserCog, X } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Textarea } from '@/components/ui';
import { DataTable, TableCard, type Column } from '@/components/data';
import { ConfirmDialog, Modal } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, relative } from '@/lib/utils';

export const ROLES = ['SuperAdmin', 'UserAdmin', 'AIManager', 'NutritionReviewer', 'SupportAdmin'] as const;

interface Administrator {
  id: string; email: string; fullName: string; isSuspended: boolean; lastLoginAtUtc: string | null; createdAtUtc: string;
  roles: string[]; mfaEnabled: boolean; activeSessions: number; unusedRecoveryCodes: number;
}
interface Invitation { id: string; email: string; roles: string[]; createdAtUtc: string; expiresAtUtc: string; status: 'Pending' | 'Accepted' | 'Revoked' | 'Expired' }

export function RolePicker({ value, onChange, allowSuperAdmin }: { value: string[]; onChange: (roles: string[]) => void; allowSuperAdmin: boolean }) {
  return (
    <fieldset className="space-y-2">
      <legend className="mb-1 text-sm font-medium text-fg">Roles</legend>
      {ROLES.map((role) => (
        <label key={role} className="flex items-center gap-2 text-sm text-fg">
          <input
            type="checkbox"
            className="h-4 w-4 accent-[var(--primary)]"
            checked={value.includes(role)}
            disabled={role === 'SuperAdmin' && !allowSuperAdmin}
            onChange={(e) => onChange(e.target.checked ? [...value, role] : value.filter((r) => r !== role))}
          />
          {role}
        </label>
      ))}
    </fieldset>
  );
}

export default function AdministratorsPage() {
  const { can, profile } = useAuth();
  const isSuper = profile?.roles.includes('SuperAdmin') ?? false;
  const admins = useQuery({ queryKey: ['administrators'], queryFn: () => api<Administrator[]>('/api/admin/administrators') });
  const invitations = useQuery({ queryKey: ['invitations'], queryFn: () => api<Invitation[]>('/api/admin/administrators/invitations') });
  const [editing, setEditing] = useState<Administrator | null>(null);
  const [resetting, setResetting] = useState<Administrator | null>(null);
  const [inviting, setInviting] = useState(false);
  const [revokingInvite, setRevokingInvite] = useState<Invitation | null>(null);

  const resetMfa = useAction((reason: string) => api(`/api/admin/administrators/${resetting!.id}/reset-mfa`, { method: 'POST', body: { reason } }), { success: 'MFA reset. They will enrol again at next sign-in.', invalidate: [['administrators']], silentError: true });
  const revokeInvite = useAction(() => api(`/api/admin/administrators/invitations/${revokingInvite!.id}`, { method: 'DELETE' }), { success: 'Invitation revoked.', invalidate: [['invitations']], silentError: true });

  const columns: Column<Administrator>[] = [
    { id: 'name', header: 'Administrator', cell: (a) => <div><p className="font-medium">{a.fullName || '—'} {a.id === profile?.id && <Badge className="ml-1">You</Badge>}</p><p className="text-xs text-muted">{a.email}</p></div> },
    { id: 'roles', header: 'Roles', cell: (a) => <div className="flex flex-wrap gap-1">{a.roles.map((r) => <Badge key={r} tone={r === 'SuperAdmin' ? 'warning' : 'info'}>{r}</Badge>)}</div> },
    { id: 'mfa', header: 'MFA', cell: (a) => (a.mfaEnabled ? <Badge tone="success"><ShieldCheck className="h-3 w-3" /> On</Badge> : <Badge tone="warning"><ShieldOff className="h-3 w-3" /> Not enrolled</Badge>) },
    { id: 'status', header: 'Status', cell: (a) => (a.isSuspended ? <Badge tone="danger">Suspended</Badge> : <Badge tone="success">Active</Badge>), hideOnMobile: true },
    { id: 'sessions', header: 'Sessions', cell: (a) => <span className="tabular-nums">{a.activeSessions}</span>, hideOnMobile: true },
    { id: 'codes', header: 'Recovery codes left', cell: (a) => <span className={a.mfaEnabled && a.unusedRecoveryCodes < 3 ? 'text-warning' : ''}>{a.mfaEnabled ? a.unusedRecoveryCodes : '—'}</span>, hideOnMobile: true },
    { id: 'login', header: 'Last sign-in', cell: (a) => <span className="text-xs text-muted">{relative(a.lastLoginAtUtc)}</span> },
    {
      id: 'actions', header: <span className="sr-only">Actions</span>, className: 'text-right',
      cell: (a) => a.id === profile?.id ? null : (
        <div className="flex justify-end gap-1">
          {can('admins.assign_roles') && <Button size="sm" variant="ghost" onClick={() => setEditing(a)}><UserCog className="h-3.5 w-3.5" /> Roles</Button>}
          {can('admins.manage') && a.mfaEnabled && <Button size="sm" variant="ghost" onClick={() => setResetting(a)}>Reset MFA</Button>}
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Administrators"
        icon={<ShieldCheck className="h-5 w-5" />}
        description="Accounts with portal access. Administrators use separate accounts from their personal NutriPath accounts. You can't change your own roles."
        actions={can('admins.manage') && <Button onClick={() => setInviting(true)}><MailPlus className="h-4 w-4" /> Invite administrator</Button>}
      />
      <TableCard>
        {admins.isPending ? <LoadingState /> : admins.isError ? <ErrorState error={admins.error} onRetry={() => void admins.refetch()} /> : (
          <DataTable columns={columns} rows={admins.data} rowKey={(a) => a.id} empty={<EmptyState title="No administrators" />} />
        )}
      </TableCard>

      <Card>
        <CardHeader title="Invitations" description="Single-use links valid for 3 days. Only a hash of each token is stored." />
        {invitations.isPending ? <LoadingState rows={2} /> : invitations.isError ? <ErrorState error={invitations.error} /> : invitations.data.length === 0 ? <EmptyState title="No invitations yet" /> : (
          <ul className="divide-y divide-border">
            {invitations.data.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center justify-between gap-3 px-5 py-3 text-sm">
                <div>
                  <p className="text-fg">{i.email}</p>
                  <p className="text-xs text-muted">{i.roles.join(', ')} · sent {relative(i.createdAtUtc)} · expires {formatDateTime(i.expiresAtUtc)}</p>
                </div>
                <div className="flex items-center gap-2">
                  <Badge tone={i.status === 'Pending' ? 'info' : i.status === 'Accepted' ? 'success' : 'neutral'}>{i.status}</Badge>
                  {i.status === 'Pending' && can('admins.manage') && <Button size="icon" variant="ghost" aria-label={`Revoke invitation for ${i.email}`} onClick={() => setRevokingInvite(i)}><X className="h-4 w-4" /></Button>}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      {editing && <RolesDialog admin={editing} allowSuperAdmin={isSuper} onClose={() => setEditing(null)} />}
      <InviteDialog open={inviting} onClose={() => setInviting(false)} allowSuperAdmin={isSuper} />
      <ConfirmDialog open={!!resetting} onOpenChange={(o) => !o && setResetting(null)} title={`Reset MFA for ${resetting?.email}?`} description="Removes their authenticator and recovery codes and signs them out. They set up a new authenticator at next sign-in." confirmLabel="Reset MFA" tone="danger" requireReason onConfirm={(r) => resetMfa.mutateAsync(r)} />
      <ConfirmDialog open={!!revokingInvite} onOpenChange={(o) => !o && setRevokingInvite(null)} title="Revoke this invitation?" description={`The link sent to ${revokingInvite?.email} will stop working.`} confirmLabel="Revoke" tone="danger" onConfirm={() => revokeInvite.mutateAsync(undefined)} />
    </div>
  );
}

function RolesDialog({ admin, allowSuperAdmin, onClose }: { admin: Administrator; allowSuperAdmin: boolean; onClose: () => void }) {
  const [roles, setRoles] = useState(admin.roles);
  const [reason, setReason] = useState('');
  const save = useAction(() => api(`/api/admin/administrators/${admin.id}/roles`, { method: 'PUT', body: { roles, reason } }), { success: 'Roles updated. Their sessions were refreshed with the new permissions.', invalidate: [['administrators']] });
  return (
    <Modal
      open
      onOpenChange={(o) => !o && onClose()}
      title={`Roles for ${admin.email}`}
      description="Removing every role turns this into a regular account and ends its portal access. The last active SuperAdmin can't be removed."
      footer={<><Button variant="outline" onClick={onClose}>Cancel</Button><Button loading={save.isPending} disabled={reason.trim().length < 5} onClick={() => save.mutate(undefined, { onSuccess: onClose })}>Save roles</Button></>}
    >
      <div className="space-y-4">
        <RolePicker value={roles} onChange={setRoles} allowSuperAdmin={allowSuperAdmin} />
        <Field label="Reason (audited)" htmlFor="roles-reason" hint="At least 5 characters."><Textarea id="roles-reason" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} /></Field>
      </div>
    </Modal>
  );
}

function InviteDialog({ open, onClose, allowSuperAdmin }: { open: boolean; onClose: () => void; allowSuperAdmin: boolean }) {
  const [email, setEmail] = useState('');
  const [roles, setRoles] = useState<string[]>([]);
  const [link, setLink] = useState<string | null>(null);
  const invite = useAction(() => api<{ link: string }>('/api/admin/administrators/invitations', { body: { email, roles } }), { invalidate: [['invitations']] });
  const close = () => { setEmail(''); setRoles([]); setLink(null); onClose(); };
  return (
    <Modal
      open={open}
      onOpenChange={(o) => !o && close()}
      title="Invite an administrator"
      description="The invitee creates a new, separate admin account and sets up an authenticator at first sign-in."
      footer={link ? <Button onClick={close}>Done</Button> : <><Button variant="outline" onClick={close}>Cancel</Button><Button loading={invite.isPending} disabled={!email.includes('@') || roles.length === 0} onClick={() => invite.mutate(undefined, { onSuccess: (r) => setLink(r.link) })}>Create invitation</Button></>}
    >
      {link ? (
        <div className="space-y-3">
          <p className="text-sm text-fg">Send this link to <strong>{email}</strong> through a trusted channel. It is shown only once and works once.</p>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 break-all rounded-md bg-surface-2 px-2 py-1.5 font-mono text-xs">{link}</code>
            <Button size="icon" variant="outline" aria-label="Copy link" onClick={() => void navigator.clipboard?.writeText(link)}><Copy className="h-4 w-4" /></Button>
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          <Field label="Email" htmlFor="invite-email"><Input id="invite-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
          <RolePicker value={roles} onChange={setRoles} allowSuperAdmin={allowSuperAdmin} />
        </div>
      )}
    </Modal>
  );
}
