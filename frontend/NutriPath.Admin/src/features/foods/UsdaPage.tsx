import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Download, Search, Wheat } from 'lucide-react';
import { api, qs } from '@/api/client';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select } from '@/components/ui';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, number } from '@/lib/utils';

interface UsdaResult { fdcId: number; description: string; imported: boolean; nutrients: { name: string; value: number }[] }
interface SyncRun { id: string; startedAtUtc: string; completedAtUtc: string | null; status: string; recordsFetched: number; recordsInserted: number; recordsUpdated: number; recordsFailed: number }

export default function UsdaPage() {
  const { can } = useAuth();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [pageSize, setPageSize] = useState('10');
  const history = useQuery({ queryKey: ['usda-history'], queryFn: () => api<SyncRun[]>('/api/admin/foods/usda/history') });
  const search = useQuery({ queryKey: ['usda-search', submitted], queryFn: () => api<UsdaResult[]>(`/api/admin/foods/usda/search${qs({ query: submitted })}`), enabled: !!submitted && can('foods.sync'), staleTime: 300_000 });
  const sync = useAction(() => api<{ jobId: string }>('/api/admin/foods/usda/sync', { body: { query: submitted, pageSize: Number(pageSize) } }), { success: 'Import queued as a background job. Verified foods are never overwritten.', invalidate: [['usda-history'], ['jobs']] });

  return (
    <>
      <PageHeader title="USDA sync" icon={<Wheat className="h-5 w-5" />} description="Preview foods from USDA FoodData Central and import them into the catalogue. Imports run as background jobs; foods marked verified are skipped." />
      {can('foods.sync') && (
        <Card className="mb-4 p-4">
          <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); setSubmitted(query.trim()); }}>
            <div className="min-w-0 flex-1"><Field label="Search USDA" htmlFor="usda-q"><Input id="usda-q" maxLength={100} value={query} onChange={(e) => setQuery(e.target.value)} placeholder="lentils, raw" /></Field></div>
            <Button type="submit" variant="outline" disabled={!query.trim()}><Search className="h-4 w-4" /> Preview</Button>
          </form>
          {submitted && (
            <div className="mt-4">
              {search.isPending ? <LoadingState rows={3} /> : search.isError ? <ErrorState error={search.error} /> : search.data.length === 0 ? <EmptyState title="USDA returned no foods" /> : (
                <>
                  <ul className="divide-y divide-border rounded-lg border border-border">
                    {search.data.map((f) => (
                      <li key={f.fdcId} className="px-3 py-2 text-sm">
                        <div className="flex flex-wrap items-center justify-between gap-2"><span className="text-fg">{f.description}</span>{f.imported ? <Badge tone="success">In catalogue</Badge> : <Badge>New</Badge>}</div>
                        <p className="text-xs text-muted">FDC {f.fdcId} · {f.nutrients.map((n) => `${n.name} ${number(n.value, 1)}`).join(' · ')}</p>
                      </li>
                    ))}
                  </ul>
                  <div className="mt-3 flex flex-wrap items-end gap-2">
                    <Field label="Foods to import" htmlFor="usda-size"><Select id="usda-size" value={pageSize} onChange={(e) => setPageSize(e.target.value)} className="w-28">{[5, 10, 25, 50].map((n) => <option key={n}>{n}</option>)}</Select></Field>
                    <Button loading={sync.isPending} onClick={() => sync.mutate(undefined)}><Download className="h-4 w-4" /> Import “{submitted}”</Button>
                  </div>
                </>
              )}
            </div>
          )}
        </Card>
      )}
      <Card>
        <CardHeader title="Sync history" />
        {history.isPending ? <LoadingState rows={3} /> : history.isError ? <ErrorState error={history.error} /> : history.data.length === 0 ? <EmptyState title="No USDA syncs yet" /> : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-sm">
              <thead><tr className="border-b border-border bg-surface-2/60 text-left text-xs text-muted"><th className="px-4 py-2 font-medium">Started</th><th className="font-medium">Status</th><th className="text-right font-medium">Fetched</th><th className="text-right font-medium">Inserted</th><th className="text-right font-medium">Updated</th><th className="px-4 text-right font-medium">Failed</th></tr></thead>
              <tbody>
                {history.data.map((s) => (
                  <tr key={s.id} className="border-b border-border last:border-0">
                    <td className="px-4 py-2 text-xs text-muted">{formatDateTime(s.startedAtUtc)}</td>
                    <td><Badge tone={s.status === 'Completed' ? 'success' : s.status === 'Failed' ? 'danger' : 'info'}>{s.status}</Badge></td>
                    <td className="text-right tabular-nums">{number(s.recordsFetched)}</td>
                    <td className="text-right tabular-nums">{number(s.recordsInserted)}</td>
                    <td className="text-right tabular-nums">{number(s.recordsUpdated)}</td>
                    <td className="px-4 text-right tabular-nums">{number(s.recordsFailed)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}
