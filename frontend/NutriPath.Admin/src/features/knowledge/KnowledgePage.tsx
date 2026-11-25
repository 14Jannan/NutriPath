import { useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { BookOpenCheck, FilePlus2, Upload } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { KnowledgeRow } from '@/api/types';
import { Badge, Button, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select, Textarea } from '@/components/ui';
import { DataTable, Pagination, SearchInput, TableCard, type Column } from '@/components/data';
import { Modal } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDate, relative } from '@/lib/utils';

export const knowledgeTone = { Draft: 'neutral', PendingReview: 'warning', Approved: 'success', Rejected: 'danger', Archived: 'neutral' } as const;
export const embeddingTone = { Indexed: 'success', Partial: 'warning', Missing: 'danger', NotApplicable: 'neutral', Unavailable: 'neutral' } as const;
export const statusLabel = (s: string) => (s === 'PendingReview' ? 'Pending review' : s);

export interface KnowledgeInput { title: string; category: string; organization: string; sourceUrl: string; publicationDate: string | null; content: string }
export const emptyInput: KnowledgeInput = { title: '', category: '', organization: '', sourceUrl: '', publicationDate: null, content: '' };
interface Preview { chunks: { index: number; content: string; words: number }[]; duplicate: { id: string; title: string; status: string } | null }

export default function KnowledgePage() {
  const { can } = useAuth();
  const [filters, setFilters] = useUrlState({ search: '', status: '', category: '', page: '1' });
  const [params, setParams] = useSearchParams();
  const list = useQuery({
    queryKey: ['knowledge', filters],
    queryFn: () => api<{ total: number; page: number; pageSize: number; categories: string[]; items: KnowledgeRow[] }>(`/api/admin/knowledge/documents${qs({ ...filters, pageSize: 25 })}`),
    placeholderData: (p) => p,
  });
  const navigate = useNavigate();
  const creating = params.get('new') === '1';
  const setCreating = (open: boolean) => setParams((p) => { const n = new URLSearchParams(p); if (open) n.set('new', '1'); else n.delete('new'); return n; }, { replace: true });

  const columns: Column<KnowledgeRow>[] = [
    { id: 'title', header: 'Document', cell: (d) => <div className="min-w-0"><p className="truncate font-medium">{d.title}</p><p className="truncate text-xs text-muted">{d.organization} · {d.category}</p></div> },
    { id: 'status', header: 'Status', cell: (d) => <Badge tone={knowledgeTone[d.status]}>{statusLabel(d.status)}</Badge> },
    { id: 'version', header: 'Version', cell: (d) => <span className="tabular-nums">v{d.version}</span>, hideOnMobile: true },
    { id: 'chunks', header: 'Chunks', cell: (d) => <span className="tabular-nums">{d.chunks}</span>, hideOnMobile: true },
    { id: 'embedding', header: 'Embeddings', cell: (d) => <Badge tone={embeddingTone[d.embedding]}>{d.embedding === 'NotApplicable' ? 'n/a' : d.embedding}</Badge>, hideOnMobile: true },
    { id: 'author', header: 'Author / reviewer', cell: (d) => <span className="text-xs text-muted">{d.author ?? '—'}{d.reviewer ? ` / ${d.reviewer}` : ''}</span>, hideOnMobile: true },
    { id: 'updated', header: 'Updated', cell: (d) => <span className="text-xs text-muted">{relative(d.updatedAtUtc ?? d.reviewedAtUtc)}</span> },
  ];

  return (
    <>
      <PageHeader
        title="Knowledge base"
        icon={<BookOpenCheck className="h-5 w-5" />}
        description="Evidence the assistant cites. Only approved documents are retrieved. Drafts are reviewed by someone other than their author; editing an approved document creates a new version that is reviewed again."
        actions={can('knowledge.create') && <Button onClick={() => setCreating(true)}><FilePlus2 className="h-4 w-4" /> New draft</Button>}
      />
      <TableCard
        toolbar={
          <>
            <SearchInput label="Search documents" placeholder="Search titles" value={filters.search} onChange={(search) => setFilters({ search })} />
            <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-44">
              <option value="">All statuses</option>
              {Object.keys(knowledgeTone).map((s) => <option key={s} value={s}>{statusLabel(s)}</option>)}
            </Select>
            <Select aria-label="Category" value={filters.category} onChange={(e) => setFilters({ category: e.target.value })} className="w-44">
              <option value="">All categories</option>
              {list.data?.categories.map((c) => <option key={c}>{c}</option>)}
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(d) => d.id} onRowClick={(d) => navigate(`/knowledge/${d.id}`)} empty={<EmptyState title="No documents match" />} />
        )}
      </TableCard>
      {creating && <KnowledgeEditor categories={list.data?.categories ?? []} onClose={() => setCreating(false)} onSaved={(id) => navigate(`/knowledge/${id}`)} />}
    </>
  );
}

/** Create or edit a draft, with a server-side chunk preview and duplicate check before saving. */
export function KnowledgeEditor({ initial, documentId, categories, onClose, onSaved }: {
  initial?: KnowledgeInput; documentId?: string; categories: string[]; onClose: () => void; onSaved: (id: string) => void;
}) {
  const [input, setInput] = useState<KnowledgeInput>(initial ?? emptyInput);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState<string>();
  const fileRef = useRef<HTMLInputElement>(null);
  const set = (patch: Partial<KnowledgeInput>) => { setInput((i) => ({ ...i, ...patch })); setPreview(null); setError(undefined); };

  const body = { ...input, publicationDate: input.publicationDate || null };
  const runPreview = useAction(() => api<Preview>('/api/admin/knowledge/documents/preview', { body }), { silentError: true });
  const save = useAction(
    () => documentId
      ? api<{ id: string; newVersion: boolean }>(`/api/admin/knowledge/documents/${documentId}`, { method: 'PUT', body })
      : api<{ id: string }>('/api/admin/knowledge/documents', { body }),
    { success: documentId ? 'Draft saved.' : 'Draft created.', invalidate: [['knowledge'], ['knowledge-doc']], silentError: true },
  );
  const importFile = useAction(async (file: File) => {
    const form = new FormData();
    form.append('file', file);
    for (const key of ['title', 'category', 'organization', 'sourceUrl'] as const) if (input[key]) form.append(key, input[key]);
    return api<Partial<KnowledgeInput>>('/api/admin/knowledge/documents/import', { form, method: 'POST' });
  }, { silentError: true, success: 'File parsed. Review the fields before saving.' });

  const fail = (e: Error) => setError(e.message);
  return (
    <Modal
      open
      wide
      onOpenChange={(o) => !o && onClose()}
      title={documentId ? 'Edit document' : 'New knowledge draft'}
      description="Use authoritative public sources (WHO, ministries of health, peer-reviewed guidance). Personal or patient data must never be added."
      footer={
        <>
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button variant="secondary" loading={runPreview.isPending} onClick={() => runPreview.mutate(undefined, { onSuccess: setPreview, onError: fail })}>Preview chunks</Button>
          <Button loading={save.isPending} disabled={!preview} onClick={() => save.mutate(undefined, { onSuccess: (r) => onSaved(r.id), onError: fail })}>Save draft</Button>
        </>
      }
    >
      <div className="space-y-4">
        {!documentId && (
          <div className="flex flex-wrap items-center gap-2 rounded-lg border border-dashed border-border p-3 text-xs text-muted">
            <Upload className="h-4 w-4" /> Import from a PDF (text layer, max 5 MB) or a .txt, .md or .json file (max 256 KB). Check the extracted text before saving.
            <input ref={fileRef} type="file" accept=".pdf,.txt,.md,.json,application/pdf,text/plain,text/markdown,application/json" className="hidden" onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) importFile.mutate(file, { onSuccess: (parsed) => set({ ...Object.fromEntries(Object.entries(parsed).filter(([, v]) => v !== null && v !== undefined)) }), onError: fail });
              e.target.value = '';
            }} />
            <Button size="sm" variant="outline" loading={importFile.isPending} onClick={() => fileRef.current?.click()}>Choose file</Button>
          </div>
        )}
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Title" htmlFor="k-title"><Input id="k-title" maxLength={300} value={input.title} onChange={(e) => set({ title: e.target.value })} /></Field>
          <Field label="Category" htmlFor="k-category">
            <Input id="k-category" list="k-categories" maxLength={100} value={input.category} onChange={(e) => set({ category: e.target.value })} />
            <datalist id="k-categories">{categories.map((c) => <option key={c} value={c} />)}</datalist>
          </Field>
          <Field label="Organization" htmlFor="k-org"><Input id="k-org" maxLength={200} value={input.organization} onChange={(e) => set({ organization: e.target.value })} /></Field>
          <Field label="Publication date" htmlFor="k-date"><Input id="k-date" type="date" value={input.publicationDate ?? ''} onChange={(e) => set({ publicationDate: e.target.value || null })} /></Field>
          <div className="sm:col-span-2"><Field label="Source URL (https)" htmlFor="k-url"><Input id="k-url" type="url" maxLength={2000} value={input.sourceUrl} onChange={(e) => set({ sourceUrl: e.target.value })} placeholder="https://www.who.int/…" /></Field></div>
        </div>
        <Field label="Content (English; paragraphs become retrieval chunks)" htmlFor="k-content">
          <Textarea id="k-content" rows={12} value={input.content} onChange={(e) => set({ content: e.target.value })} className="font-mono text-xs" />
        </Field>
        {error && <p className="rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger" role="alert">{error}</p>}
        {preview && (
          <div className="space-y-2">
            {preview.duplicate && <p className="rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning">Identical content already exists: “{preview.duplicate.title}” ({statusLabel(preview.duplicate.status)}).</p>}
            <p className="text-sm font-medium text-fg">{preview.chunks.length} chunk{preview.chunks.length === 1 ? '' : 's'}</p>
            <ol className="max-h-64 space-y-2 overflow-y-auto">
              {preview.chunks.map((c) => (
                <li key={c.index} className="rounded-lg border border-border p-2 text-xs">
                  <p className="mb-1 text-muted">Chunk {c.index} · {c.words} words</p>
                  <p className="text-fg">{c.content}</p>
                </li>
              ))}
            </ol>
          </div>
        )}
        {!preview && <p className="text-xs text-muted">Preview the chunks before saving.{input.publicationDate ? ` Published ${formatDate(input.publicationDate)}.` : ''}</p>}
      </div>
    </Modal>
  );
}
