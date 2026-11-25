import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { MessageCircleWarning, ThumbsDown, ThumbsUp } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { Paged } from '@/api/types';
import { Badge, Button, Card, EmptyState, ErrorState, Field, LoadingState, PageHeader, Select, Textarea } from '@/components/ui';
import { DataTable, Pagination, StatCard, TableCard, type Column } from '@/components/data';
import { Drawer } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, languageName, number, percent, relative } from '@/lib/utils';

interface FeedbackRow { id: string; helpful: boolean; reason: string | null; language: string; provider: string | null; status: string; createdAtUtc: string; reviewedAtUtc: string | null; hasComment: boolean }
interface Summary {
  total: number; helpful: number; notHelpful: number; unreviewed: number;
  byReason: { reason: string; count: number }[];
  byLanguage: { language: string; helpful: number; notHelpful: number }[];
}
interface FeedbackDetail extends FeedbackRow {
  question: string; answer: string; comment: string; reviewNote: string | null; reviewer: string | null;
  incident: { id: string; status: string; severity: string } | null;
}

export const reasonLabel = (r: string | null) =>
  ({ Incorrect: 'Incorrect', Unsafe: 'Unsafe', WrongLanguage: 'Wrong language', NotRelevant: 'Not relevant', MissingSources: 'Missing sources', Other: 'Other' } as Record<string, string>)[r ?? ''] ?? '—';
const statusTone = (s: string) => (s === 'New' ? 'info' : s === 'Actioned' ? 'success' : 'neutral');

export default function FeedbackPage() {
  const [filters, setFilters] = useUrlState({ status: 'New', helpful: '', reason: '', language: '', page: '1' });
  const [selected, setSelected] = useState<string | null>(null);
  const summary = useQuery({ queryKey: ['feedback-summary'], queryFn: () => api<Summary>('/api/admin/feedback/summary') });
  const list = useQuery({ queryKey: ['feedback', filters], queryFn: () => api<Paged<FeedbackRow>>(`/api/admin/feedback${qs({ ...filters, pageSize: 25 })}`), placeholderData: (p) => p });

  const columns: Column<FeedbackRow>[] = [
    { id: 'rating', header: 'Rating', cell: (f) => (f.helpful ? <Badge tone="success"><ThumbsUp className="h-3 w-3" /> Helpful</Badge> : <Badge tone="danger"><ThumbsDown className="h-3 w-3" /> {reasonLabel(f.reason)}</Badge>) },
    { id: 'language', header: 'Language', cell: (f) => <span className="text-xs">{languageName(f.language)}</span> },
    { id: 'provider', header: 'Provider', cell: (f) => <span className="text-xs">{f.provider ?? '—'}</span>, hideOnMobile: true },
    { id: 'comment', header: 'Comment', cell: (f) => <span className="text-xs text-muted">{f.hasComment ? 'Yes' : '—'}</span>, hideOnMobile: true },
    { id: 'status', header: 'Status', cell: (f) => <Badge tone={statusTone(f.status)}>{f.status}</Badge> },
    { id: 'when', header: 'Received', cell: (f) => <span className="text-xs text-muted">{relative(f.createdAtUtc)}</span> },
  ];

  const s = summary.data;
  return (
    <>
      <PageHeader
        title="Answer feedback"
        icon={<ThumbsUp className="h-5 w-5" />}
        description="Ratings users give assistant answers in the app. Users agree to share the rated question and answer; opening one shows it and is recorded in the audit log."
      />
      {s && (
        <div className="mb-4 grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Ratings (30 days)" value={number(s.total)} icon={<MessageCircleWarning className="h-4 w-4" />} hint={`${number(s.unreviewed)} not reviewed yet`} />
          <StatCard label="Helpful" value={s.total ? percent(s.helpful / s.total, 0) : '—'} tone="teal" icon={<ThumbsUp className="h-4 w-4" />} hint={`${number(s.helpful)} ratings`} />
          <StatCard label="Not helpful" value={number(s.notHelpful)} tone="danger" icon={<ThumbsDown className="h-4 w-4" />} hint={s.byReason.map((r) => `${reasonLabel(r.reason)} ${r.count}`).join(' · ') || 'none'} />
          <StatCard label="By language" value={s.byLanguage.length} tone="info" icon={<ThumbsUp className="h-4 w-4" />} hint={s.byLanguage.map((l) => `${l.language}: ${l.helpful}/${l.helpful + l.notHelpful}`).join(' · ') || '—'} />
        </div>
      )}
      <TableCard
        toolbar={
          <>
            <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-40">
              <option value="">All statuses</option>
              {['New', 'Reviewed', 'Actioned', 'Dismissed'].map((v) => <option key={v}>{v}</option>)}
            </Select>
            <Select aria-label="Rating" value={filters.helpful} onChange={(e) => setFilters({ helpful: e.target.value })} className="w-40">
              <option value="">All ratings</option><option value="true">Helpful</option><option value="false">Not helpful</option>
            </Select>
            <Select aria-label="Reason" value={filters.reason} onChange={(e) => setFilters({ reason: e.target.value })} className="w-44">
              <option value="">All reasons</option>
              {['Incorrect', 'Unsafe', 'WrongLanguage', 'NotRelevant', 'MissingSources', 'Other'].map((r) => <option key={r} value={r}>{reasonLabel(r)}</option>)}
            </Select>
            <Select aria-label="Language" value={filters.language} onChange={(e) => setFilters({ language: e.target.value })} className="w-36">
              <option value="">All languages</option><option value="en">English</option><option value="ta">Tamil</option><option value="si">Sinhala</option>
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(f) => f.id} onRowClick={(f) => setSelected(f.id)} empty={<EmptyState title="No feedback matches" description="Feedback appears here when users rate answers in the app." />} />
        )}
      </TableCard>
      <FeedbackDrawer id={selected} onClose={() => setSelected(null)} />
    </>
  );
}

function FeedbackDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { can } = useAuth();
  const detail = useQuery({ queryKey: ['feedback-detail', id], queryFn: () => api<FeedbackDetail>(`/api/admin/feedback/${id}`), enabled: !!id });
  const [status, setStatus] = useState('Reviewed');
  const [note, setNote] = useState('');
  const review = useAction(() => api(`/api/admin/feedback/${id}/review`, { body: { status, note: note || null } }), {
    success: 'Review saved.', invalidate: [['feedback'], ['feedback-summary'], ['feedback-detail', id]],
  });
  const d = detail.data;
  return (
    <Drawer open={!!id} onOpenChange={(o) => !o && onClose()} title="Feedback">
      {detail.isPending ? <LoadingState /> : detail.isError ? <ErrorState error={detail.error} /> : d && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            {d.helpful ? <Badge tone="success">Helpful</Badge> : <Badge tone="danger">{reasonLabel(d.reason)}</Badge>}
            <Badge>{languageName(d.language)}</Badge>
            {d.provider && <Badge>{d.provider}</Badge>}
            <Badge tone={statusTone(d.status)}>{d.status}</Badge>
            <span className="text-xs text-muted">{formatDateTime(d.createdAtUtc)}</span>
          </div>
          {d.incident && <p className="rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger">Linked safety incident ({d.incident.severity}, {d.incident.status}). <Link className="underline" to={`/safety/${d.incident.id}`}>Open</Link></p>}
          <Card className="p-4"><p className="mb-1 text-xs font-medium text-muted">Question</p><p className="whitespace-pre-wrap text-sm text-fg">{d.question || '—'}</p></Card>
          <Card className="p-4"><p className="mb-1 text-xs font-medium text-muted">Answer shown to the user</p><p className="whitespace-pre-wrap text-sm text-fg">{d.answer}</p></Card>
          {d.comment && <Card className="p-4"><p className="mb-1 text-xs font-medium text-muted">User comment</p><p className="whitespace-pre-wrap text-sm text-fg">{d.comment}</p></Card>}
          {d.reviewNote && <p className="text-xs text-muted">Review note{d.reviewer ? ` by ${d.reviewer}` : ''}: {d.reviewNote}</p>}
          {can('feedback.manage') && (
            <Card className="space-y-3 p-4">
              <Field label="Mark as" htmlFor="fb-status">
                <Select id="fb-status" value={status} onChange={(e) => setStatus(e.target.value)}>
                  <option value="Reviewed">Reviewed (no action needed)</option><option value="Actioned">Actioned (knowledge or prompt changed)</option><option value="Dismissed">Dismissed</option>
                </Select>
              </Field>
              <Field label="Note (optional)" htmlFor="fb-note"><Textarea id="fb-note" rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
              <div className="flex flex-wrap gap-2">
                <Button size="sm" loading={review.isPending} onClick={() => review.mutate(undefined)}>Save review</Button>
                {!d.incident && !d.helpful && can('safety.manage') && <Link to={`/safety?new=1&feedback=${d.id}`}><Button size="sm" variant="outline">Open safety incident</Button></Link>}
              </div>
            </Card>
          )}
        </div>
      )}
    </Drawer>
  );
}
