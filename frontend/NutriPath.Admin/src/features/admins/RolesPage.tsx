import { Fragment } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Check, ListChecks, Minus } from 'lucide-react';
import { api } from '@/api/client';
import { Card, ErrorState, LoadingState, PageHeader } from '@/components/ui';

interface RoleMatrix { roles: { role: string; permissions: string[] }[]; permissions: string[] }

const roleDescriptions: Record<string, string> = {
  SuperAdmin: 'Everything, including administrator management and API keys.',
  UserAdmin: 'User accounts, suspensions, sessions, usage limits and exports.',
  AIManager: 'AI providers, models, configuration, playground, knowledge drafting and indexing.',
  NutritionReviewer: 'Reviews and approves knowledge; manages the food catalogue and USDA sync.',
  SupportAdmin: 'Read-only dashboard and user lookup for support.',
};

export default function RolesPage() {
  const matrix = useQuery({ queryKey: ['roles'], queryFn: () => api<RoleMatrix>('/api/admin/roles'), staleTime: Infinity });
  if (matrix.isPending) return <LoadingState />;
  if (matrix.isError) return <ErrorState error={matrix.error} />;
  const { roles, permissions } = matrix.data;
  const groups = [...new Set(permissions.map((p) => p.split('.')[0]))];
  return (
    <>
      <PageHeader
        title="Roles & permissions"
        icon={<ListChecks className="h-5 w-5" />}
        description="The role-to-permission matrix is defined in the backend code and enforced by every API endpoint, so it can't drift from what this page shows. Change it through a reviewed code change."
      />
      <div className="mb-6 grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-5">
        {roles.map((r) => (
          <Card key={r.role} className="p-4">
            <p className="text-sm font-semibold text-fg">{r.role}</p>
            <p className="mt-1 text-xs text-muted">{roleDescriptions[r.role]}</p>
            <p className="mt-2 text-xs text-primary">{r.permissions.length} permissions</p>
          </Card>
        ))}
      </div>
      <Card className="overflow-x-auto">
        <table className="w-full min-w-[720px] text-sm">
          <caption className="sr-only">Permissions by role</caption>
          <thead>
            <tr className="border-b border-border bg-surface-2/60 text-xs text-muted">
              <th scope="col" className="px-4 py-2.5 text-left font-medium">Permission</th>
              {roles.map((r) => <th key={r.role} scope="col" className="px-3 py-2.5 text-center font-medium">{r.role}</th>)}
            </tr>
          </thead>
          <tbody>
            {groups.map((group) => (
              <Fragment key={group}>
                <tr className="bg-surface-2/30"><th colSpan={roles.length + 1} scope="colgroup" className="px-4 py-1.5 text-left text-[11px] font-semibold uppercase tracking-wide text-muted">{group}</th></tr>
                {permissions.filter((p) => p.split('.')[0] === group).map((p) => (
                  <tr key={p} className="border-b border-border last:border-0">
                    <th scope="row" className="px-4 py-2 text-left font-mono text-xs font-normal text-fg">{p}</th>
                    {roles.map((r) => (
                      <td key={r.role} className="px-3 py-2 text-center">
                        {r.permissions.includes(p) ? <Check className="mx-auto h-4 w-4 text-primary" aria-label="Granted" /> : <Minus className="mx-auto h-4 w-4 text-border" aria-label="Not granted" />}
                      </td>
                    ))}
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
