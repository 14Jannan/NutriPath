import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Boxes, RefreshCw } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, PageHeader } from '@/components/ui';
import { ConfirmDialog } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { number, percent, relative } from '@/lib/utils';
import { jobTone } from '@/features/operations/JobsPage';

interface Coverage {
  activeModel: string; approvedChunks: number; worker: 'Ready' | 'NotReady' | 'Unreachable' | 'NotConfigured'; storage: string | null;
  models: { model: string; dimensions: number; active: boolean; indexed: number | null; missing: number | null; coverage: number | null }[];
  recentJobs: { id: string; type: string; status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled'; progress: number; createdAtUtc: string; finishedAtUtc: string | null; error: string | null }[];
}

const workerTone = { Ready: 'success', NotReady: 'warning', Unreachable: 'danger', NotConfigured: 'neutral' } as const;

export default function EmbeddingsPage() {
  const { can } = useAuth();
  const [confirm, setConfirm] = useState(false);
  const data = useQuery({ queryKey: ['coverage'], queryFn: () => api<Coverage>('/api/admin/knowledge/coverage'), refetchInterval: (q) => (q.state.data?.recentJobs.some((j) => j.status === 'Queued' || j.status === 'Running') ? 5000 : false) });
  const reindex = useAction(() => api('/api/admin/knowledge/reindex', { body: { documentId: null } }), { success: 'Re-indexing all approved documents has been queued.', invalidate: [['coverage']], silentError: true });

  if (data.isPending) return <LoadingState />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const c = data.data;
  return (
    <>
      <PageHeader
        title="Embeddings"
        icon={<Boxes className="h-5 w-5" />}
        description="Vector coverage of approved English knowledge chunks for semantic retrieval. Tamil, Sinhala and romanised questions are matched to these English chunks by the multilingual model."
        actions={can('knowledge.reindex') && <Button variant="outline" disabled={c.worker !== 'Ready'} onClick={() => setConfirm(true)}><RefreshCw className="h-4 w-4" /> Re-index all</Button>}
      />
      <div className="mb-4 flex flex-wrap items-center gap-3 text-sm">
        <span className="text-muted">Embedding worker</span><Badge tone={workerTone[c.worker]}>{c.worker}</Badge>
        <span className="text-muted">· Approved chunks</span><strong className="text-fg">{number(c.approvedChunks)}</strong>
      </div>
      {c.storage && <p className="mb-4 rounded-lg bg-warning-soft px-4 py-2 text-sm text-warning">{c.storage}</p>}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {c.models.map((m) => (
          <Card key={m.model} className="p-5">
            <div className="flex items-start justify-between gap-2">
              <div><p className="font-mono text-sm text-fg">{m.model}</p><p className="text-xs text-muted">{m.dimensions} dimensions</p></div>
              {m.active ? <Badge tone="success">Active</Badge> : <Badge>Inactive</Badge>}
            </div>
            <p className="mt-4 text-3xl font-semibold tabular-nums text-fg">{percent(m.coverage)}</p>
            <div className="mt-2 h-2 rounded-full bg-surface-2"><div className="h-2 rounded-full bg-primary transition-all" style={{ width: `${(m.coverage ?? 0) * 100}%` }} /></div>
            <p className="mt-2 text-xs text-muted">{m.indexed === null ? 'Coverage unavailable here.' : `${number(m.indexed)} indexed · ${number(m.missing)} missing`}</p>
          </Card>
        ))}
      </div>
      <Card className="mt-4">
        <CardHeader title="Recent indexing jobs" action={<Link to="/jobs" className="text-xs text-primary hover:underline">All jobs</Link>} />
        {c.recentJobs.length === 0 ? <EmptyState title="No indexing jobs yet" /> : (
          <ul className="divide-y divide-border">
            {c.recentJobs.map((j) => (
              <li key={j.id} className="flex flex-wrap items-center justify-between gap-2 px-5 py-2.5 text-sm">
                <span className="font-mono text-xs text-fg">{j.type}</span>
                <span className="flex items-center gap-2 text-xs text-muted">{j.status === 'Running' && `${j.progress}%`}<Badge tone={jobTone[j.status]}>{j.status}</Badge>{relative(j.createdAtUtc)}</span>
                {j.error && <p className="w-full text-xs text-danger">{j.error}</p>}
              </li>
            ))}
          </ul>
        )}
      </Card>
      <ConfirmDialog open={confirm} onOpenChange={setConfirm} title="Re-index all approved knowledge?" description={`Recomputes embeddings for every approved chunk with ${c.activeModel}. It runs in the background; retrieval keeps working meanwhile.`} confirmLabel="Queue re-index" onConfirm={() => reindex.mutateAsync(undefined)} />
    </>
  );
}
