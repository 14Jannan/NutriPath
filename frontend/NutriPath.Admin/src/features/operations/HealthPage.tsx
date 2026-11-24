import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Activity, RefreshCw } from 'lucide-react';
import { api } from '@/api/client';
import type { ComponentHealth } from '@/api/types';
import { Badge, Button, Card, ErrorState, LoadingState, PageHeader, StatusDot } from '@/components/ui';
import { healthTone, relative } from '@/lib/utils';

export default function HealthPage() {
  const client = useQueryClient();
  const health = useQuery({ queryKey: ['health'], queryFn: () => api<{ components: ComponentHealth[]; note: string }>('/api/admin/health'), refetchInterval: 60_000 });
  const refresh = async () => client.setQueryData(['health'], await api('/api/admin/health?refresh=true'));
  return (
    <>
      <PageHeader
        title="System health"
        icon={<Activity className="h-5 w-5" />}
        description={health.data?.note}
        actions={<Button variant="outline" onClick={() => void refresh()} loading={health.isFetching}><RefreshCw className="h-4 w-4" /> Check now</Button>}
      />
      {health.isPending ? <LoadingState /> : health.isError ? <ErrorState error={health.error} onRetry={() => void health.refetch()} /> : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {health.data.components.map((c) => (
            <Card key={c.name} className="p-5 animate-fade-up">
              <div className="flex items-center justify-between gap-2">
                <p className="flex items-center gap-2 font-medium text-fg"><StatusDot tone={healthTone(c.status)} />{c.name}</p>
                <Badge tone={healthTone(c.status) === 'danger' ? 'danger' : healthTone(c.status) === 'warning' ? 'warning' : healthTone(c.status) === 'success' ? 'success' : 'neutral'}>{c.status}</Badge>
              </div>
              {c.detail && <p className="mt-2 text-sm text-muted">{c.detail}</p>}
              <p className="mt-3 text-xs text-muted">{c.latencyMs !== null && `${c.latencyMs} ms · `}checked {relative(c.checkedAtUtc)}</p>
            </Card>
          ))}
        </div>
      )}
    </>
  );
}
