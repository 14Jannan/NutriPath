import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { CheckCircle2, Play, Target, XCircle } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, Field, LoadingState, PageHeader, Select } from '@/components/ui';
import { Drawer } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { languageName, number, percent, relative } from '@/lib/utils';
import { jobTone } from '@/features/operations/JobsPage';

interface Score { k: number; count: number; recall: number | null; precision: number | null; mrr: number | null }
interface Metrics { questions: number; positives: number; negatives: number; errors: number; overall: Score; byLanguage: Record<string, Score>; noEvidenceReturnRate: number | null; p50Ms: number | null; p95Ms: number | null }
interface Run { id: string; dataset: string; mode: string; topK: number; embeddingModel: string | null; status: 'Queued' | 'Running' | 'Succeeded' | 'Failed'; error: string | null; createdAtUtc: string; finishedAtUtc: string | null; metrics: Metrics | null; createdBy: string | null }
interface RunsResponse { datasets: { id: string; available: boolean }[]; modes: string[]; runs: Run[] }
interface RunDetail extends Run { results: { id: string; question: string; language: string; category: string; expected: string[]; found: string[]; pass: boolean }[] | null }

export default function EvaluationsPage() {
  const data = useQuery({
    queryKey: ['evaluations'],
    queryFn: () => api<RunsResponse>('/api/admin/evaluations'),
    refetchInterval: (q) => (q.state.data?.runs.some((r) => r.status === 'Queued' || r.status === 'Running') ? 3000 : false),
  });
  const [dataset, setDataset] = useState('nutrition');
  const [mode, setMode] = useState('hybrid');
  const [topK, setTopK] = useState('5');
  const [selected, setSelected] = useState<string | null>(null);
  const start = useAction(() => api('/api/admin/evaluations', { body: { dataset, mode, topK: Number(topK) } }), { success: 'Evaluation queued.', invalidate: [['evaluations']] });

  if (data.isPending) return <LoadingState />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const runs = data.data.runs;
  const latest = runs.find((r) => r.status === 'Succeeded');

  return (
    <>
      <PageHeader
        title="Evaluations"
        icon={<Target className="h-5 w-5" />}
        description="Retrieval benchmarks over the reviewed question sets in docs/evaluation. They measure whether the right approved documents are found (recall, precision, MRR) and whether questions that need no knowledge stay clear of it. No model is called and no user data is used."
      />
      <Card className="mb-4 p-4">
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Question set" htmlFor="ev-dataset">
            <Select id="ev-dataset" value={dataset} onChange={(e) => setDataset(e.target.value)} className="w-56">
              {data.data.datasets.map((d) => <option key={d.id} value={d.id} disabled={!d.available}>{d.id === 'nutrition' ? 'Nutrition (English)' : 'Multilingual (en/ta/si, romanised)'}{d.available ? '' : ' — missing'}</option>)}
            </Select>
          </Field>
          <Field label="Retrieval mode" htmlFor="ev-mode">
            <Select id="ev-mode" value={mode} onChange={(e) => setMode(e.target.value)} className="w-36">{data.data.modes.map((m) => <option key={m}>{m}</option>)}</Select>
          </Field>
          <Field label="Top K" htmlFor="ev-k">
            <Select id="ev-k" value={topK} onChange={(e) => setTopK(e.target.value)} className="w-24">{[1, 3, 5, 8, 10].map((k) => <option key={k}>{k}</option>)}</Select>
          </Field>
          <Button loading={start.isPending} onClick={() => start.mutate(undefined)}><Play className="h-4 w-4" /> Run evaluation</Button>
        </div>
      </Card>
      {latest?.metrics && (
        <div className="mb-4 grid grid-cols-2 gap-4 lg:grid-cols-5">
          {[
            ['Recall@' + latest.topK, percent(latest.metrics.overall.recall)],
            ['Precision@' + latest.topK, percent(latest.metrics.overall.precision)],
            ['MRR', number(latest.metrics.overall.mrr, 3)],
            ['No-evidence leakage', percent(latest.metrics.noEvidenceReturnRate)],
            ['Latency p50 / p95', `${number(latest.metrics.p50Ms)} / ${number(latest.metrics.p95Ms)} ms`],
          ].map(([label, value]) => (
            <Card key={label} className="p-4"><p className="text-xs text-muted">{label}</p><p className="mt-1 text-xl font-semibold tabular-nums text-fg">{value}</p><p className="text-[11px] text-muted">latest: {latest.dataset} · {latest.mode}</p></Card>
          ))}
        </div>
      )}
      <Card>
        <CardHeader title="Runs" />
        {runs.length === 0 ? <EmptyState title="No evaluations yet" /> : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead><tr className="border-b border-border bg-surface-2/60 text-left text-xs text-muted"><th className="px-4 py-2 font-medium">Run</th><th className="font-medium">Status</th><th className="text-right font-medium">Recall</th><th className="text-right font-medium">Precision</th><th className="text-right font-medium">MRR</th><th className="text-right font-medium">Leakage</th><th className="px-4 font-medium">When</th></tr></thead>
              <tbody>
                {runs.map((r) => (
                  <tr key={r.id} className="cursor-pointer border-b border-border last:border-0 hover:bg-surface-2/70" onClick={() => r.status === 'Succeeded' && setSelected(r.id)}>
                    <td className="px-4 py-2"><p className="font-medium text-fg">{r.dataset} · {r.mode} · top {r.topK}</p><p className="text-xs text-muted">{r.embeddingModel ?? 'keyword only'}{r.createdBy ? ` · ${r.createdBy}` : ''}</p></td>
                    <td><Badge tone={jobTone[r.status === 'Running' ? 'Running' : r.status]}>{r.status}</Badge>{r.error && <p className="text-xs text-danger">{r.error}</p>}</td>
                    <td className="text-right tabular-nums">{percent(r.metrics?.overall.recall)}</td>
                    <td className="text-right tabular-nums">{percent(r.metrics?.overall.precision)}</td>
                    <td className="text-right tabular-nums">{number(r.metrics?.overall.mrr, 3)}</td>
                    <td className="text-right tabular-nums">{percent(r.metrics?.noEvidenceReturnRate)}</td>
                    <td className="px-4 text-xs text-muted">{relative(r.createdAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
      <RunDrawer id={selected} onClose={() => setSelected(null)} />
    </>
  );
}

function RunDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const run = useQuery({ queryKey: ['evaluation', id], queryFn: () => api<RunDetail>(`/api/admin/evaluations/${id}`), enabled: !!id });
  const [onlyFailures, setOnlyFailures] = useState(true);
  const r = run.data;
  const rows = (r?.results ?? []).filter((x) => !onlyFailures || !x.pass);
  return (
    <Drawer open={!!id} onOpenChange={(o) => !o && onClose()} title={r ? `${r.dataset} · ${r.mode} · top ${r.topK}` : 'Evaluation'}>
      {run.isPending ? <LoadingState /> : run.isError ? <ErrorState error={run.error} /> : r?.metrics && (
        <div className="space-y-4">
          <Card className="p-4">
            <p className="text-sm text-fg">{r.metrics.questions} questions · {r.metrics.positives} with expected sources · {r.metrics.negatives} needing none · {r.metrics.errors} errors</p>
            <div className="mt-3 grid grid-cols-2 gap-2 text-xs sm:grid-cols-3">
              {Object.entries(r.metrics.byLanguage).map(([lang, s]) => (
                <div key={lang} className="rounded-lg bg-surface-2 p-2"><p className="font-medium text-fg">{languageName(lang)}</p><p className="text-muted">recall {percent(s.recall)} · MRR {number(s.mrr, 3)} · n={s.count}</p></div>
              ))}
            </div>
          </Card>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4 accent-[var(--primary)]" checked={onlyFailures} onChange={(e) => setOnlyFailures(e.target.checked)} /> Show only failures ({(r.results ?? []).filter((x) => !x.pass).length})</label>
          <ul className="space-y-2">
            {rows.map((x) => (
              <li key={x.id} className="rounded-lg border border-border p-3 text-xs">
                <p className="flex items-start gap-2 text-sm text-fg">{x.pass ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" /> : <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-danger" />}{x.question}</p>
                <p className="mt-1 text-muted">{languageName(x.language)} · {x.category}</p>
                <p className="mt-1 text-muted">Expected: {x.expected.length ? x.expected.join('; ') : 'no knowledge'}</p>
                <p className="text-muted">Found: {x.found.length ? x.found.join('; ') : 'nothing'}</p>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Drawer>
  );
}
