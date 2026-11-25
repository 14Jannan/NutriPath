import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Archive, Check, Languages, Pencil, Plus, Search } from 'lucide-react';
import { api, qs } from '@/api/client';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select, Textarea } from '@/components/ui';
import { ConfirmDialog, Modal } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { languageName, relative } from '@/lib/utils';

interface Term {
  id: string; language: 'ta' | 'si'; nativeTerms: string[]; romanisedTerms: string[]; englishTerms: string; notes: string | null;
  status: 'Draft' | 'Approved' | 'Archived'; createdAtUtc: string; approvedAtUtc: string | null; author: string | null; approver: string | null;
}

export default function TerminologyPage() {
  const { can, profile } = useAuth();
  const [filters, setFilters] = useUrlState({ status: '', language: '', search: '' });
  const data = useQuery({ queryKey: ['terms', filters], queryFn: () => api<{ builtInEntries: number; items: Term[] }>(`/api/admin/terminology${qs(filters)}`) });
  const [editing, setEditing] = useState<Term | 'new' | null>(null);
  const [archiving, setArchiving] = useState<Term | null>(null);
  const approve = useAction((id: string) => api(`/api/admin/terminology/${id}/approve`, { method: 'POST' }), { success: 'Approved. Keyword search uses it now.', invalidate: [['terms']] });
  const archive = useAction(() => api(`/api/admin/terminology/${archiving!.id}/archive`, { method: 'POST' }), { success: 'Archived.', invalidate: [['terms']], silentError: true });
  const manage = can('terminology.manage');

  return (
    <>
      <PageHeader
        title="Terminology"
        icon={<Languages className="h-5 w-5" />}
        description="Tamil, Sinhala and romanised (Tanglish/Singlish) nutrition terms mapped to the English words in the knowledge base, so keyword search can find evidence for non-English questions. Terms are added by fluent speakers and approved by a second reviewer."
        actions={manage && <Button onClick={() => setEditing('new')}><Plus className="h-4 w-4" /> Add term</Button>}
      />
      <TermTester />
      <Card>
        <CardHeader title={`Portal-managed terms (${data.data?.items.length ?? 0})`} description={`Plus ${data.data?.builtInEntries ?? '…'} built-in entries reviewed in code (NutritionTermLexicon.cs).`} />
        <div className="flex flex-wrap gap-2 px-5 pb-4">
          <Input aria-label="Search terms" placeholder="Search" value={filters.search} onChange={(e) => setFilters({ search: e.target.value })} className="w-56" />
          <Select aria-label="Status" value={filters.status} onChange={(e) => setFilters({ status: e.target.value })} className="w-36">
            <option value="">All statuses</option><option>Draft</option><option>Approved</option><option>Archived</option>
          </Select>
          <Select aria-label="Language" value={filters.language} onChange={(e) => setFilters({ language: e.target.value })} className="w-36">
            <option value="">Both languages</option><option value="ta">Tamil</option><option value="si">Sinhala</option>
          </Select>
        </div>
        {data.isPending ? <LoadingState /> : data.isError ? <ErrorState error={data.error} /> : data.data.items.length === 0 ? <EmptyState title="No terms yet" description="Add a term when a common word in questions isn't understood." /> : (
          <ul className="divide-y divide-border border-t border-border">
            {data.data.items.map((t) => (
              <li key={t.id} className="flex flex-wrap items-start justify-between gap-3 px-5 py-3">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 text-sm text-fg">
                    <span lang={t.language}>{t.nativeTerms.join(', ') || '—'}</span>
                    {t.romanisedTerms.length > 0 && <span className="text-muted">({t.romanisedTerms.join(', ')})</span>}
                    <span className="text-muted">→</span> <strong>{t.englishTerms}</strong>
                  </p>
                  <p className="mt-0.5 text-xs text-muted">
                    {languageName(t.language)} · by {t.author ?? '—'} {relative(t.createdAtUtc)}{t.approver ? ` · approved by ${t.approver}` : ''}{t.notes ? ` · ${t.notes}` : ''}
                  </p>
                </div>
                <div className="flex items-center gap-1">
                  <Badge tone={t.status === 'Approved' ? 'success' : t.status === 'Draft' ? 'info' : 'neutral'}>{t.status}</Badge>
                  {manage && t.status === 'Draft' && (
                    <Button size="sm" variant="ghost" disabled={t.author === profile?.email} title={t.author === profile?.email ? 'Another reviewer must approve your terms' : undefined} loading={approve.isPending && approve.variables === t.id} onClick={() => approve.mutate(t.id)}>
                      <Check className="h-3.5 w-3.5" /> Approve
                    </Button>
                  )}
                  {manage && t.status !== 'Archived' && <Button size="icon" variant="ghost" aria-label="Edit term" onClick={() => setEditing(t)}><Pencil className="h-4 w-4" /></Button>}
                  {manage && t.status !== 'Archived' && <Button size="icon" variant="ghost" aria-label="Archive term" onClick={() => setArchiving(t)}><Archive className="h-4 w-4" /></Button>}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
      {editing && <TermDialog term={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog open={!!archiving} onOpenChange={(o) => !o && setArchiving(null)} title="Archive this term?" description="Keyword search stops using it immediately." confirmLabel="Archive" tone="danger" onConfirm={() => archive.mutateAsync(undefined)} />
    </>
  );
}

function TermTester() {
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const result = useQuery({ queryKey: ['term-test', query], queryFn: () => api<{ detected: string | null; englishTerms: string[] }>(`/api/admin/terminology/test${qs({ text: query })}`), enabled: !!query });
  return (
    <Card className="mb-4 p-4">
      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); setQuery(text.trim()); }}>
        <div className="min-w-0 flex-1"><Field label="Test a question" htmlFor="term-test" hint="Shows the English search terms a question maps to (built-in and approved terms)."><Input id="term-test" value={text} onChange={(e) => setText(e.target.value)} placeholder="paruppu la protein irukka?" /></Field></div>
        <Button type="submit" variant="outline" disabled={!text.trim()}><Search className="h-4 w-4" /> Test</Button>
      </form>
      {result.data && (
        <p className="mt-3 text-sm text-fg">
          Detected: <strong>{result.data.detected ? languageName(result.data.detected) : 'unknown'}</strong> · English terms:{' '}
          {result.data.englishTerms.length ? result.data.englishTerms.map((t) => <Badge key={t} tone="info" className="mr-1">{t}</Badge>) : <span className="text-muted">none (no keyword evidence)</span>}
        </p>
      )}
    </Card>
  );
}

function TermDialog({ term, onClose }: { term: Term | null; onClose: () => void }) {
  const [language, setLanguage] = useState<string>(term?.language ?? 'ta');
  const [native, setNative] = useState(term?.nativeTerms.join('\n') ?? '');
  const [romanised, setRomanised] = useState(term?.romanisedTerms.join('\n') ?? '');
  const [english, setEnglish] = useState(term?.englishTerms ?? '');
  const [notes, setNotes] = useState(term?.notes ?? '');
  const [error, setError] = useState<string>();
  const save = useAction(
    () => {
      const body = { language, nativeTerms: native, romanisedTerms: romanised, englishTerms: english, notes: notes || null };
      return term ? api(`/api/admin/terminology/${term.id}`, { method: 'PUT', body }) : api('/api/admin/terminology', { body });
    },
    { success: 'Saved as a draft for review.', invalidate: [['terms']], silentError: true },
  );
  return (
    <Modal
      open
      onOpenChange={(o) => !o && onClose()}
      title={term ? 'Edit term' : 'Add a term'}
      description="Edits go back to draft and need approval again."
      footer={<><Button variant="outline" onClick={onClose}>Cancel</Button><Button loading={save.isPending} disabled={!english.trim() || (!native.trim() && !romanised.trim())} onClick={() => save.mutate(undefined, { onSuccess: onClose, onError: (e) => setError(e.message) })}>Save draft</Button></>}
    >
      <div className="space-y-4">
        <Field label="Language" htmlFor="term-lang"><Select id="term-lang" value={language} onChange={(e) => setLanguage(e.target.value)}><option value="ta">Tamil</option><option value="si">Sinhala</option></Select></Field>
        <Field label="Native-script terms (one per line; matched as word prefixes)" htmlFor="term-native"><Textarea id="term-native" rows={3} lang={language} value={native} onChange={(e) => setNative(e.target.value)} /></Field>
        <Field label="Romanised spellings (one per line, at least 3 letters)" htmlFor="term-roman"><Textarea id="term-roman" rows={3} value={romanised} onChange={(e) => setRomanised(e.target.value)} /></Field>
        <Field label="English search terms" htmlFor="term-english" hint="Words that appear in the approved English knowledge, e.g. “finger millet whole grains”."><Input id="term-english" maxLength={200} value={english} onChange={(e) => setEnglish(e.target.value)} /></Field>
        <Field label="Notes (optional)" htmlFor="term-notes"><Input id="term-notes" maxLength={300} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
        {error && <p className="text-sm text-danger" role="alert">{error}</p>}
      </div>
    </Modal>
  );
}
