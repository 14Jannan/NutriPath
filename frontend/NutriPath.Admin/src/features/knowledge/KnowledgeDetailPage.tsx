import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Archive, ArrowLeft, Check, ExternalLink, Pencil, RefreshCw, RotateCcw, Send, X } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, ErrorState, KeyValue, LoadingState, PageHeader } from '@/components/ui';
import { ConfirmDialog } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDate, formatDateTime } from '@/lib/utils';
import { KnowledgeEditor, knowledgeTone, statusLabel } from './KnowledgePage';

interface DocumentDetails {
  id: string; title: string; category: string; organization: string; sourceUrl: string; language: string; publicationDate: string | null;
  version: number; status: keyof typeof knowledgeTone; content: string; contentHash: string; rejectionReason: string | null;
  createdAtUtc: string; updatedAtUtc: string | null; reviewedAtUtc: string | null; author: string | null; reviewer: string | null;
  chunks: { id: string; title: string; content: string; words: number }[];
  previousVersions: { id: string; version: number; status: string; updatedAtUtc: string | null }[];
  revisions: { id: string; version: number; status: string; updatedAtUtc: string | null }[];
}

type Action = 'submit' | 'approve' | 'reject' | 'archive' | 'restore' | 'reindex' | null;

export default function KnowledgeDetailPage() {
  const { id } = useParams();
  const { can, profile } = useAuth();
  const navigate = useNavigate();
  const [action, setAction] = useState<Action>(null);
  const [editing, setEditing] = useState(false);
  const doc = useQuery({ queryKey: ['knowledge-doc', id], queryFn: () => api<DocumentDetails>(`/api/admin/knowledge/documents/${id}`) });
  const invalidate = [['knowledge-doc', id], ['knowledge'], ['dashboard-overview']];
  const post = (path: string, body: unknown = {}) => api(`/api/admin/knowledge/documents/${id}/${path}`, { method: 'POST', body });

  const submit = useAction(() => post('submit'), { success: 'Submitted for review.', invalidate, silentError: true });
  const approve = useAction(() => post('approve'), { success: 'Approved. Indexing has been queued.', invalidate, silentError: true });
  const reject = useAction((reason: string) => post('reject', { reason }), { success: 'Rejected with feedback.', invalidate, silentError: true });
  const archive = useAction((reason: string) => post('archive', { reason }), { success: 'Archived. It is no longer retrieved.', invalidate, silentError: true });
  const restore = useAction(() => api<{ id: string }>(`/api/admin/knowledge/documents/${id}/restore`, { method: 'POST' }), { success: 'Restored as a new draft.', invalidate, silentError: true });
  const reindex = useAction(() => api('/api/admin/knowledge/reindex', { body: { documentId: id } }), { success: 'Re-indexing queued. Track it under Background jobs.', silentError: true });

  if (doc.isPending) return <LoadingState rows={8} />;
  if (doc.isError) return <ErrorState error={doc.error} onRetry={() => void doc.refetch()} />;
  const d = doc.data;
  const isAuthor = d.author === profile?.email;
  const editable = d.status === 'Draft' || d.status === 'Rejected' || d.status === 'Approved';

  return (
    <>
      <Link to="/knowledge" className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-fg"><ArrowLeft className="h-4 w-4" /> Knowledge base</Link>
      <PageHeader
        title={d.title}
        description={<span className="flex flex-wrap items-center gap-2"><Badge tone={knowledgeTone[d.status]}>{statusLabel(d.status)}</Badge> Version {d.version} · {d.organization} · {d.category}</span>}
        actions={
          <>
            {can('knowledge.edit') && editable && <Button variant="outline" onClick={() => setEditing(true)}><Pencil className="h-4 w-4" /> {d.status === 'Approved' ? 'Revise (new version)' : 'Edit'}</Button>}
            {can('knowledge.edit') && (d.status === 'Draft' || d.status === 'Rejected') && <Button onClick={() => setAction('submit')}><Send className="h-4 w-4" /> Submit for review</Button>}
            {d.status === 'PendingReview' && can('knowledge.approve') && <Button disabled={isAuthor} title={isAuthor ? 'Authors cannot approve their own documents.' : undefined} onClick={() => setAction('approve')}><Check className="h-4 w-4" /> Approve</Button>}
            {d.status === 'PendingReview' && can('knowledge.review') && <Button variant="danger" disabled={isAuthor} onClick={() => setAction('reject')}><X className="h-4 w-4" /> Reject</Button>}
            {d.status === 'Approved' && can('knowledge.reindex') && <Button variant="outline" onClick={() => setAction('reindex')}><RefreshCw className="h-4 w-4" /> Re-index</Button>}
            {d.status !== 'Archived' && can('knowledge.archive') && <Button variant="ghost" onClick={() => setAction('archive')}><Archive className="h-4 w-4" /> Archive</Button>}
            {d.status === 'Archived' && can('knowledge.edit') && <Button variant="outline" onClick={() => setAction('restore')}><RotateCcw className="h-4 w-4" /> Restore as draft</Button>}
          </>
        }
      />
      {d.status === 'PendingReview' && isAuthor && <p className="mb-4 rounded-lg bg-info-soft px-4 py-2 text-sm text-info">You wrote this version, so another reviewer must approve it.</p>}
      {d.rejectionReason && <p className="mb-4 rounded-lg bg-danger-soft px-4 py-2 text-sm text-danger">Reviewer feedback: {d.rejectionReason}</p>}

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          <Card>
            <CardHeader title="Content" description={`${d.chunks.length} retrieval chunks`} />
            <div className="max-h-[480px] overflow-y-auto whitespace-pre-wrap px-5 pb-5 text-sm leading-relaxed text-fg">{d.content}</div>
          </Card>
          <Card>
            <CardHeader title="Chunks" />
            <ol className="space-y-2 px-5 pb-5">
              {d.chunks.map((c, i) => (
                <li key={c.id} className="rounded-lg border border-border p-3 text-xs">
                  <p className="mb-1 text-muted">{i + 1}. {c.title} · {c.words} words</p>
                  <p className="text-fg">{c.content}</p>
                </li>
              ))}
            </ol>
          </Card>
        </div>
        <div className="space-y-4">
          <Card className="p-4">
            <KeyValue items={[
              ['Source', <a key="s" href={d.sourceUrl} target="_blank" rel="noreferrer noopener" className="inline-flex items-center gap-1 break-all text-primary hover:underline">{new URL(d.sourceUrl).hostname}<ExternalLink className="h-3 w-3" /></a>],
              ['Published', formatDate(d.publicationDate)],
              ['Language', d.language],
              ['Author', d.author ?? '—'],
              ['Reviewer', d.reviewer ?? '—'],
              ['Created', formatDateTime(d.createdAtUtc)],
              ['Updated', formatDateTime(d.updatedAtUtc)],
              ['Reviewed', formatDateTime(d.reviewedAtUtc)],
            ]} />
            <p className="mt-3 break-all font-mono text-[10px] text-muted">SHA-256 {d.contentHash}</p>
          </Card>
          {(d.previousVersions.length > 0 || d.revisions.length > 0) && (
            <Card>
              <CardHeader title="Versions" />
              <ul className="divide-y divide-border">
                {[...d.revisions.map((v) => ({ ...v, label: 'Newer revision' })), { id: d.id, version: d.version, status: d.status, updatedAtUtc: d.updatedAtUtc, label: 'This version' }, ...d.previousVersions.map((v) => ({ ...v, label: 'Earlier version' }))].map((v) => (
                  <li key={v.id} className="flex items-center justify-between px-5 py-2.5 text-sm">
                    {v.id === d.id ? <span className="font-medium text-fg">v{v.version} · {v.label}</span> : <Link to={`/knowledge/${v.id}`} className="text-primary hover:underline">v{v.version} · {v.label}</Link>}
                    <Badge tone={knowledgeTone[v.status as keyof typeof knowledgeTone] ?? 'neutral'}>{statusLabel(v.status)}</Badge>
                  </li>
                ))}
              </ul>
            </Card>
          )}
        </div>
      </div>

      {editing && (
        <KnowledgeEditor
          documentId={d.id}
          categories={[d.category]}
          initial={{ title: d.title, category: d.category, organization: d.organization, sourceUrl: d.sourceUrl, publicationDate: d.publicationDate, content: d.content }}
          onClose={() => setEditing(false)}
          onSaved={(newId) => { setEditing(false); if (newId !== d.id) navigate(`/knowledge/${newId}`); }}
        />
      )}
      <ConfirmDialog open={action === 'submit'} onOpenChange={(o) => !o && setAction(null)} title="Submit for review?" description="A reviewer other than the author must approve it before the assistant can use it." confirmLabel="Submit" onConfirm={() => submit.mutateAsync(undefined)} />
      <ConfirmDialog open={action === 'approve'} onOpenChange={(o) => !o && setAction(null)} title="Approve this document?" description={d.previousVersions.length ? 'It replaces the earlier approved version (which is archived) and is queued for embedding.' : 'The assistant can cite it once approved. Embedding is queued automatically.'} confirmLabel="Approve" onConfirm={() => approve.mutateAsync(undefined)} />
      <ConfirmDialog open={action === 'reject'} onOpenChange={(o) => !o && setAction(null)} title="Reject this document?" description="The author sees your feedback and can revise and resubmit." confirmLabel="Reject" tone="danger" requireReason onConfirm={(r) => reject.mutateAsync(r)} />
      <ConfirmDialog open={action === 'archive'} onOpenChange={(o) => !o && setAction(null)} title="Archive this document?" description="It stops being retrieved immediately. It can be restored later as a new draft." confirmLabel="Archive" tone="danger" requireReason onConfirm={(r) => archive.mutateAsync(r)} />
      <ConfirmDialog open={action === 'restore'} onOpenChange={(o) => !o && setAction(null)} title="Restore as a new draft?" description="Creates a draft copy that must be reviewed and approved again." confirmLabel="Restore" onConfirm={() => restore.mutateAsync(undefined).then((r) => navigate(`/knowledge/${r.id}`))} />
      <ConfirmDialog open={action === 'reindex'} onOpenChange={(o) => !o && setAction(null)} title="Re-index this document?" description="Recomputes its embeddings with the active model in a background job." confirmLabel="Queue re-index" onConfirm={() => reindex.mutateAsync(undefined)} />
    </>
  );
}
