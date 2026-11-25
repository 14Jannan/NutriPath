import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Activity, Bot, BookOpenCheck, Clock, Cpu, LayoutDashboard, ListTodo, ShieldCheck, Users, UtensilsCrossed } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { ComponentHealth } from '@/api/types';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, PageHeader, Select, StatusDot } from '@/components/ui';
import { StatCard, Timeline } from '@/components/data';
import { useAuth } from '@/providers/auth';
import { formatDate, formatDateTime, healthTone, languageName, number, percent, relative } from '@/lib/utils';

export interface Overview {
  generatedAtUtc: string;
  definitions: { activeUser: string; aiMetrics: string };
  users: { total: number; verified: number; suspended: number; administrators: number; newToday: number; new7d: number; new30d: number; dailyActive: number; weeklyActive: number };
  ai: { today: number; thisMonth: number; last30Days: number; successRate: number | null; averageLatencyMs: number | null; medianLatencyMs: number | null; tokens30d: number; inputTokens30d: number; outputTokens30d: number; recordingSince: string | null };
  knowledge: { approvedDocuments: number; pendingReview: number; drafts: number; approvedChunks: number; embeddingModel: string; indexedChunks: number | null; coverage: number | null; coverageNote: string | null };
  foods: { total: number; userCreated: number };
  jobs: { queued: number; failed7d: number };
  health: ComponentHealth[];
  recentActivity: { atUtc: string; actorEmail: string | null; action: string; outcome: string; resourceType: string | null; summary: string | null }[];
  pendingApprovals: { id: string; title: string; category: string; updatedAtUtc: string | null }[];
}

interface Trends {
  from: string;
  to: string;
  series: { date: string; registrations: number; activeUsers: number; aiRequests: number; aiFailures: number; tokens: number; p50LatencyMs: number | null; p95LatencyMs: number | null; approvedKnowledge: number; catalogueFoods: number }[];
  providers: { provider: string; requests: number }[];
  languages: { language: string; requests: number }[];
}


const axis = { stroke: 'var(--muted)', fontSize: 11, tickLine: false, axisLine: false } as const;
const tooltipStyle = { contentStyle: { background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8, fontSize: 12, color: 'var(--fg)' } };
const shortDate = (value: string) => formatDate(value).replace(/,? \d{4}$/, '');

function ChartCard({ title, description, children, empty }: { title: string; description?: string; children: React.ReactElement; empty?: boolean }) {
  return (
    <Card className="p-4 animate-fade-up">
      <p className="text-sm font-medium text-fg">{title}</p>
      {description && <p className="text-xs text-muted">{description}</p>}
      <div className="mt-3 h-56">
        {empty ? <EmptyState title="No data recorded in this range" /> : <ResponsiveContainer width="100%" height="100%">{children}</ResponsiveContainer>}
      </div>
    </Card>
  );
}

export default function DashboardPage() {
  const { can } = useAuth();
  const [range, setRange] = useState('30');
  const overview = useQuery({ queryKey: ['dashboard-overview'], queryFn: () => api<Overview>('/api/admin/dashboard/overview'), refetchInterval: 60_000 });
  const trends = useQuery({ queryKey: ['dashboard-trends', range], queryFn: () => api<Trends>(`/api/admin/dashboard/trends${qs({ range })}`) });

  if (overview.isPending) return <LoadingState rows={8} />;
  if (overview.isError) return <ErrorState error={overview.error} onRetry={() => void overview.refetch()} />;
  const o = overview.data;
  const series = trends.data?.series ?? [];
  const noAi = series.every((d) => d.aiRequests === 0);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Dashboard"
        icon={<LayoutDashboard className="h-5 w-5" />}
        description={<>Live figures from the NutriPath database. Updated {relative(o.generatedAtUtc)}.</>}
        actions={
          <Select aria-label="Chart range" value={range} onChange={(e) => setRange(e.target.value)} className="w-40">
            <option value="today">Today</option>
            <option value="7">Last 7 days</option>
            <option value="30">Last 30 days</option>
            <option value="90">Last 90 days</option>
          </Select>
        }
      />

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Registered users" value={number(o.users.total)} icon={<Users className="h-4 w-4" />} hint={`${number(o.users.new7d)} new in 7 days · ${number(o.users.newToday)} today`} />
        <StatCard label="Active users today" value={number(o.users.dailyActive)} tone="teal" icon={<Activity className="h-4 w-4" />} hint={`${number(o.users.weeklyActive)} active in 7 days`} />
        <StatCard label="Assistant requests today" value={number(o.ai.today)} tone="info" icon={<Bot className="h-4 w-4" />} hint={`${number(o.ai.thisMonth)} this month · ${percent(o.ai.successRate)} success (30d)`} />
        <StatCard label="Median AI latency" value={o.ai.medianLatencyMs === null ? '—' : `${number(o.ai.medianLatencyMs)} ms`} tone="warning" icon={<Clock className="h-4 w-4" />} hint={`${number(o.ai.tokens30d)} tokens in 30 days`} />
        <StatCard label="Approved knowledge" value={number(o.knowledge.approvedDocuments)} icon={<BookOpenCheck className="h-4 w-4" />} hint={`${number(o.knowledge.pendingReview)} pending · ${number(o.knowledge.drafts)} drafts`} />
        <StatCard label="Embedding coverage" value={percent(o.knowledge.coverage)} tone="teal" icon={<Cpu className="h-4 w-4" />} hint={o.knowledge.coverageNote ?? `${number(o.knowledge.indexedChunks)} of ${number(o.knowledge.approvedChunks)} chunks · ${o.knowledge.embeddingModel}`} />
        <StatCard label="Catalogue foods" value={number(o.foods.total)} tone="info" icon={<UtensilsCrossed className="h-4 w-4" />} hint={`${number(o.foods.userCreated)} user-created foods`} />
        <StatCard label="Background jobs" value={number(o.jobs.queued)} tone={o.jobs.failed7d ? 'danger' : 'primary'} icon={<ListTodo className="h-4 w-4" />} hint={`queued or running · ${number(o.jobs.failed7d)} failed in 7 days`} />
      </div>

      {o.ai.recordingSince === null && (
        <p className="rounded-lg border border-border bg-surface-2 px-4 py-3 text-xs text-muted">
          AI metrics are recorded per request from the moment this portal's backend is deployed. None have been recorded yet, so AI figures show no data instead of estimates.
        </p>
      )}

      {trends.isError ? (
        <ErrorState error={trends.error} onRetry={() => void trends.refetch()} />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <ChartCard title="Registrations and active users" description={o.definitions.activeUser}>
            <LineChart data={series}>
              <CartesianGrid stroke="var(--border)" vertical={false} />
              <XAxis dataKey="date" tickFormatter={shortDate} {...axis} minTickGap={24} />
              <YAxis {...axis} allowDecimals={false} width={32} />
              <Tooltip {...tooltipStyle} labelFormatter={(v) => formatDate(String(v))} />
              <Line type="monotone" dataKey="activeUsers" name="Active users" stroke="var(--chart-1)" strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="registrations" name="Registrations" stroke="var(--chart-2)" strokeWidth={2} dot={false} />
            </LineChart>
          </ChartCard>
          <ChartCard title="Assistant requests" description={o.definitions.aiMetrics} empty={noAi}>
            <BarChart data={series}>
              <CartesianGrid stroke="var(--border)" vertical={false} />
              <XAxis dataKey="date" tickFormatter={shortDate} {...axis} minTickGap={24} />
              <YAxis {...axis} allowDecimals={false} width={32} />
              <Tooltip {...tooltipStyle} labelFormatter={(v) => formatDate(String(v))} />
              <Bar dataKey="aiRequests" name="Requests" fill="var(--chart-1)" radius={[3, 3, 0, 0]} stackId="a" />
              <Bar dataKey="aiFailures" name="Failures" fill="var(--danger)" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ChartCard>
          <ChartCard title="AI latency (successful requests)" description="Median and 95th percentile, milliseconds." empty={noAi}>
            <LineChart data={series}>
              <CartesianGrid stroke="var(--border)" vertical={false} />
              <XAxis dataKey="date" tickFormatter={shortDate} {...axis} minTickGap={24} />
              <YAxis {...axis} width={44} />
              <Tooltip {...tooltipStyle} labelFormatter={(v) => formatDate(String(v))} />
              <Line type="monotone" dataKey="p50LatencyMs" name="p50" stroke="var(--chart-3)" strokeWidth={2} dot={false} connectNulls />
              <Line type="monotone" dataKey="p95LatencyMs" name="p95" stroke="var(--chart-4)" strokeWidth={2} dot={false} connectNulls />
            </LineChart>
          </ChartCard>
          <ChartCard title="Knowledge and food catalogue growth" description="Cumulative approved documents and catalogue foods.">
            <AreaChart data={series}>
              <CartesianGrid stroke="var(--border)" vertical={false} />
              <XAxis dataKey="date" tickFormatter={shortDate} {...axis} minTickGap={24} />
              <YAxis yAxisId="k" {...axis} width={32} allowDecimals={false} />
              <YAxis yAxisId="f" orientation="right" {...axis} width={44} allowDecimals={false} />
              <Tooltip {...tooltipStyle} labelFormatter={(v) => formatDate(String(v))} />
              <Area yAxisId="k" type="monotone" dataKey="approvedKnowledge" name="Approved knowledge" stroke="var(--chart-1)" fill="var(--chart-1)" fillOpacity={0.15} />
              <Area yAxisId="f" type="monotone" dataKey="catalogueFoods" name="Catalogue foods" stroke="var(--chart-2)" fill="var(--chart-2)" fillOpacity={0.1} />
            </AreaChart>
          </ChartCard>
          {(trends.data?.languages.length ?? 0) > 0 && (
            <ChartCard title="Answers by language" description="Successful assistant answers in this range.">
              <BarChart data={trends.data!.languages.map((l) => ({ ...l, name: languageName(l.language) }))} layout="vertical">
                <XAxis type="number" {...axis} allowDecimals={false} />
                <YAxis type="category" dataKey="name" {...axis} width={110} />
                <Tooltip {...tooltipStyle} />
                <Bar dataKey="requests" name="Answers" fill="var(--chart-2)" radius={[0, 3, 3, 0]} />
              </BarChart>
            </ChartCard>
          )}
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader title="System health" icon={<ShieldCheck className="h-4 w-4" />} action={can('system.monitor') ? <Link to="/health" className="text-xs text-primary hover:underline">Details</Link> : undefined} />
          <ul className="divide-y divide-border">
            {o.health.map((c) => (
              <li key={c.name} className="flex items-center justify-between gap-3 px-5 py-2.5 text-sm">
                <span className="flex items-center gap-2 text-fg"><StatusDot tone={healthTone(c.status)} />{c.name}</span>
                <span className="text-xs text-muted">{c.status}{c.latencyMs !== null ? ` · ${c.latencyMs} ms` : ''}</span>
              </li>
            ))}
          </ul>
        </Card>
        <Card>
          <CardHeader title="Pending knowledge approvals" icon={<BookOpenCheck className="h-4 w-4" />} action={<Link to="/knowledge?status=PendingReview" className="text-xs text-primary hover:underline">Review queue</Link>} />
          {o.pendingApprovals.length === 0 ? (
            <EmptyState title="Nothing waiting for review" />
          ) : (
            <ul className="divide-y divide-border">
              {o.pendingApprovals.map((d) => (
                <li key={d.id} className="px-5 py-2.5">
                  <Link to={`/knowledge/${d.id}`} className="text-sm text-fg hover:text-primary">{d.title}</Link>
                  <p className="text-xs text-muted">{d.category} · submitted {relative(d.updatedAtUtc)}</p>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card>
          <CardHeader title="Recent admin activity" action={can('audit.read') ? <Link to="/audit" className="text-xs text-primary hover:underline">Audit log</Link> : undefined} />
          <div className="px-5 pb-5">
            {o.recentActivity.length === 0 ? (
              <EmptyState title="No admin activity yet" />
            ) : (
              <Timeline
                items={o.recentActivity.map((a, i) => ({
                  key: `${a.atUtc}-${i}`,
                  title: <><code className="text-xs">{a.action}</code>{a.outcome !== 'Success' && <Badge tone="danger" className="ml-2">{a.outcome}</Badge>}</>,
                  meta: `${a.actorEmail ?? 'system'} · ${formatDateTime(a.atUtc)}`,
                  detail: a.summary,
                  tone: a.outcome === 'Success' ? 'success' : 'danger',
                }))}
              />
            )}
          </div>
        </Card>
      </div>

      <Card className="p-4">
        <p className="mb-3 text-sm font-medium text-fg">Quick actions</p>
        <div className="flex flex-wrap gap-2">
          {can('knowledge.create') && <Link to="/knowledge?new=1"><Button variant="outline" size="sm">New knowledge draft</Button></Link>}
          {can('ai.secrets.rotate') && <Link to="/ai/keys"><Button variant="outline" size="sm">Rotate an API key</Button></Link>}
          {can('ai.evaluate') && <Link to="/ai/playground"><Button variant="outline" size="sm">Open AI playground</Button></Link>}
          {can('users.read') && <Link to="/users"><Button variant="outline" size="sm">Find a user</Button></Link>}
          {can('foods.sync') && <Link to="/foods/usda"><Button variant="outline" size="sm">USDA sync</Button></Link>}
        </div>
      </Card>
    </div>
  );
}
