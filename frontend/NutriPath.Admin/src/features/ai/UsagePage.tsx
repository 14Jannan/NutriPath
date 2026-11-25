import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { format, subDays } from 'date-fns';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { AlertTriangle, BarChart3, Clock, Coins, Hash, Users } from 'lucide-react';
import { api, qs } from '@/api/client';
import { Card, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader } from '@/components/ui';
import { StatCard } from '@/components/data';
import { formatDate, languageName, number, percent } from '@/lib/utils';

interface Usage {
  from: string; to: string;
  totals: { requests: number; succeeded: number; failed: number; rateLimited: number; notPermitted: number; validationFallbacks: number; insufficientEvidence: number; tokens: number; inputTokens: number; outputTokens: number; latencyP50Ms: number | null; latencyP95Ms: number | null; retrievalP50Ms: number | null; estimatedCostUsd: number; distinctUsers: number };
  byDay: { date: string; requests: number; failures: number; tokens: number; p50LatencyMs: number }[];
  byProvider: { provider: string; requests: number; tokens: number; failures: number }[];
  byModel: { model: string; requests: number; tokens: number }[];
  byLanguage: { language: string; requests: number; validationFallbacks: number }[];
  byOutcome: { outcome: string; requests: number }[];
  costNote: string;
}

const axis = { stroke: 'var(--muted)', fontSize: 11, tickLine: false, axisLine: false } as const;
const tooltip = { contentStyle: { background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8, fontSize: 12, color: 'var(--fg)' } };

function Breakdown({ title, rows }: { title: string; rows: { label: string; value: number; extra?: string }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.value));
  return (
    <Card className="p-4">
      <p className="mb-3 text-sm font-medium text-fg">{title}</p>
      {rows.length === 0 ? <p className="text-xs text-muted">No data.</p> : (
        <ul className="space-y-2.5">
          {rows.map((r) => (
            <li key={r.label}>
              <div className="flex justify-between text-xs"><span className="text-fg">{r.label}</span><span className="tabular-nums text-muted">{number(r.value)}{r.extra ? ` · ${r.extra}` : ''}</span></div>
              <div className="mt-1 h-1.5 rounded-full bg-surface-2"><div className="h-1.5 rounded-full bg-primary" style={{ width: `${(r.value / max) * 100}%` }} /></div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

export default function UsagePage() {
  const [from, setFrom] = useState(() => format(subDays(new Date(), 29), 'yyyy-MM-dd'));
  const [to, setTo] = useState(() => format(new Date(), 'yyyy-MM-dd'));
  const usage = useQuery({ queryKey: ['ai-usage', from, to], queryFn: () => api<Usage>(`/api/admin/ai/usage${qs({ from, to })}`), placeholderData: (p) => p });
  const u = usage.data;
  return (
    <>
      <PageHeader
        title="AI usage"
        icon={<BarChart3 className="h-5 w-5" />}
        description="Recorded per assistant request: provider, model, language, tokens, latency and outcome. No prompts, answers or health data are stored in these metrics."
        actions={
          <div className="flex gap-2">
            <Field label="From" htmlFor="from"><Input id="from" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} /></Field>
            <Field label="To" htmlFor="to"><Input id="to" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></Field>
          </div>
        }
      />
      {usage.isPending ? <LoadingState rows={8} /> : usage.isError ? <ErrorState error={usage.error} onRetry={() => void usage.refetch()} /> : u && (
        u.totals.requests === 0 ? <Card><EmptyState title="No assistant requests recorded in this range" description="Metrics are recorded from when the admin portal backend was deployed." /></Card> : (
          <div className="space-y-4">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard label="Requests" value={number(u.totals.requests)} icon={<Hash className="h-4 w-4" />} hint={`${percent(u.totals.succeeded / u.totals.requests)} succeeded · ${number(u.totals.rateLimited)} rate limited`} />
              <StatCard label="Tokens" value={number(u.totals.tokens)} tone="info" icon={<Coins className="h-4 w-4" />} hint={`${number(u.totals.inputTokens)} in · ${number(u.totals.outputTokens)} out`} />
              <StatCard label="Latency p50 / p95" value={`${number(u.totals.latencyP50Ms)} / ${number(u.totals.latencyP95Ms)} ms`} tone="warning" icon={<Clock className="h-4 w-4" />} hint={`Retrieval p50 ${number(u.totals.retrievalP50Ms)} ms`} />
              <StatCard label="Distinct users" value={number(u.totals.distinctUsers)} tone="teal" icon={<Users className="h-4 w-4" />} hint={`Estimated cost $${u.totals.estimatedCostUsd.toFixed(4)}`} />
            </div>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <StatCard label="Validation fallbacks" value={number(u.totals.validationFallbacks)} tone="danger" icon={<AlertTriangle className="h-4 w-4" />} hint="Answers replaced by the safe fallback" />
              <StatCard label="Insufficient evidence" value={number(u.totals.insufficientEvidence)} tone="warning" icon={<AlertTriangle className="h-4 w-4" />} hint="No approved source covered the question" />
              <StatCard label="Failed requests" value={number(u.totals.failed)} tone="danger" icon={<AlertTriangle className="h-4 w-4" />} hint={`${number(u.totals.notPermitted)} blocked by privacy routing`} />
            </div>
            <Card className="p-4">
              <p className="text-sm font-medium text-fg">Requests per day</p>
              <div className="mt-3 h-64">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={u.byDay}>
                    <CartesianGrid stroke="var(--border)" vertical={false} />
                    <XAxis dataKey="date" tickFormatter={(v) => formatDate(String(v))} {...axis} minTickGap={24} />
                    <YAxis {...axis} allowDecimals={false} width={36} />
                    <Tooltip {...tooltip} labelFormatter={(v) => formatDate(String(v))} />
                    <Bar dataKey="requests" name="Requests" fill="var(--chart-1)" radius={[3, 3, 0, 0]} />
                    <Bar dataKey="failures" name="Failures" fill="var(--danger)" radius={[3, 3, 0, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </Card>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
              <Breakdown title="By provider" rows={u.byProvider.map((p) => ({ label: p.provider, value: p.requests, extra: `${number(p.failures)} failed` }))} />
              <Breakdown title="By model" rows={u.byModel.map((m) => ({ label: m.model, value: m.requests }))} />
              <Breakdown title="By language" rows={u.byLanguage.map((l) => ({ label: languageName(l.language), value: l.requests, extra: `${number(l.validationFallbacks)} fallbacks` }))} />
              <Breakdown title="By outcome" rows={u.byOutcome.map((o) => ({ label: o.outcome, value: o.requests }))} />
            </div>
            <p className="text-xs text-muted">{u.costNote}</p>
          </div>
        )
      )}
    </>
  );
}
