import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { DRAFT_GUIDANCE_KEY } from './PromptsPage';
import { useQuery } from '@tanstack/react-query';
import { CheckCircle2, ChevronDown, FlaskConical, Play, XCircle } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, Field, Select, Textarea } from '@/components/ui';
import { PageHeader } from '@/components/ui';
import { useAction } from '@/lib/hooks';
import { cn, languageName, number } from '@/lib/utils';

type Step = Record<string, unknown> & { step: string; ms?: number };
interface Generation {
  provider: string; model?: string; ms: number; tokens?: number; inputTokens?: number | null; outputTokens?: number | null; error?: string;
  validJson?: boolean; validationPassed?: boolean; languageCorrect?: boolean; allergyGuarantee?: boolean; insufficientEvidence?: boolean;
  citations?: { title: string; url?: string; source?: string }[] | string[]; answer?: string; rawAnswer?: string | null;
}
interface RunResult { totalMs: number; steps: Step[]; generations: Generation[]; synthetic: boolean }

const Check = ({ ok, label }: { ok: boolean | undefined; label: string }) => (
  <span className={cn('inline-flex items-center gap-1 text-xs', ok ? 'text-primary' : 'text-danger')}>
    {ok ? <CheckCircle2 className="h-3.5 w-3.5" /> : <XCircle className="h-3.5 w-3.5" />}{label}
  </span>
);

function StepCard({ index, step }: { index: number; step: Step }) {
  const [open, setOpen] = useState(step.step !== 'prompt');
  const titles: Record<string, string> = { language: 'Language resolution', embedding: 'Query embedding', retrieval: 'Knowledge retrieval', prompt: 'Prompt assembly' };
  return (
    <Card className="animate-fade-up">
      <button type="button" className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left cursor-pointer" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
        <span className="flex items-center gap-3">
          <span className="flex h-6 w-6 items-center justify-center rounded-full bg-primary-soft text-xs font-semibold text-primary">{index}</span>
          <span className="text-sm font-medium text-fg">{titles[step.step] ?? step.step}</span>
          {typeof step.ms === 'number' && <span className="text-xs text-muted">{number(step.ms, 1)} ms</span>}
          {'skipped' in step && <Badge>Skipped</Badge>}
          {'error' in step && <Badge tone="warning">Unavailable</Badge>}
        </span>
        <ChevronDown className={cn('h-4 w-4 text-muted transition-transform', open && 'rotate-180')} />
      </button>
      {open && <div className="border-t border-border px-4 py-3 text-sm"><StepBody step={step} /></div>}
    </Card>
  );
}

function StepBody({ step }: { step: Step }) {
  const s = step as unknown as Record<string, never>;
  if ('skipped' in step) return <p className="text-xs text-muted">{String(step.skipped)}</p>;
  if ('error' in step) return <p className="text-xs text-warning">{String(step.error)}</p>;
  switch (step.step) {
    case 'language':
      return <p className="text-xs text-muted">Detected <strong className="text-fg">{languageName(s.detected) ?? 'unknown'}</strong>{s.ambiguous ? ' (ambiguous)' : ''} → answering in <strong className="text-fg">{languageName(s.resolved)}</strong> (source: {s.source}).</p>;
    case 'embedding': {
      const nearest = s.nearest as unknown as { chunkId: string; title: string; distance: number; withinThreshold: boolean }[];
      return (
        <div className="space-y-2 text-xs">
          <p className="text-muted">Model <code className="text-fg">{s.model}</code> · {s.dimensions} dimensions · distance threshold {s.threshold}</p>
          <p className="text-muted">Embedded text: <code className="text-fg">{s.embeddedText}</code></p>
          <ul className="space-y-1">{nearest.map((n) => <li key={n.chunkId} className="flex justify-between gap-2"><span className={n.withinThreshold ? 'text-fg' : 'text-muted line-through'}>{n.title}</span><span className="tabular-nums text-muted">{n.distance}</span></li>)}</ul>
        </div>
      );
    }
    case 'retrieval': {
      const chunks = s.chunks as unknown as { rank: number; id: string; title: string; source: string | null; url: string | null; excerpt: string }[];
      const terms = s.mappedTerms as unknown as string[];
      return (
        <div className="space-y-3 text-xs">
          <p className="text-muted">Mode <strong className="text-fg">{s.mode}</strong>, top {s.topK}{terms?.length ? <> · mapped terms: {terms.join(', ')}</> : null}</p>
          {chunks.length === 0 ? <p className="text-warning">No approved knowledge matched — the assistant must say it lacks evidence.</p> : chunks.map((c) => (
            <div key={c.id} className="rounded-lg border border-border p-3">
              <p className="font-medium text-fg">#{c.rank} {c.title}</p>
              <p className="text-muted">{c.source}{c.url && <> · <a href={c.url} target="_blank" rel="noreferrer noopener" className="text-primary hover:underline">source</a></>}</p>
              <p className="mt-1 text-muted">{c.excerpt}</p>
            </div>
          ))}
        </div>
      );
    }
    case 'prompt':
      return (
        <div className="space-y-2 text-xs">
          <p className="text-muted">System prompt {number(s.systemPromptCharacters)} characters · user prompt {number(s.userPromptCharacters)} characters</p>
          <details><summary className="cursor-pointer text-primary">System prompt</summary><pre className="mt-2 max-h-72 overflow-auto whitespace-pre-wrap rounded-lg bg-surface-2 p-3 font-mono text-[11px]">{s.systemPrompt}</pre></details>
          <details><summary className="cursor-pointer text-primary">User prompt (synthetic profile + retrieved knowledge)</summary><pre className="mt-2 max-h-72 overflow-auto whitespace-pre-wrap rounded-lg bg-surface-2 p-3 font-mono text-[11px]">{s.userPrompt}</pre></details>
        </div>
      );
    default:
      return <pre className="overflow-auto text-xs">{JSON.stringify(step, null, 2)}</pre>;
  }
}

export default function PlaygroundPage() {
  const profiles = useQuery({ queryKey: ['playground-profiles'], queryFn: () => api<{ id: string; profile: { profile: string } }[]>('/api/admin/playground/profiles'), staleTime: Infinity });
  const [question, setQuestion] = useState('');
  const [language, setLanguage] = useState('');
  const [mode, setMode] = useState('hybrid');
  const [topK, setTopK] = useState('3');
  const [profile, setProfile] = useState('vegetarian-student');
  const [providers, setProviders] = useState<string[]>(['Gemini']);
  const [generate, setGenerate] = useState(true);
  const [params] = useSearchParams();
  const [useDraft, setUseDraft] = useState(params.get('draftGuidance') === '1');
  const [draft, setDraft] = useState(() => {
    try {
      return sessionStorage.getItem(DRAFT_GUIDANCE_KEY) ?? '';
    } catch {
      return '';
    }
  });
  const run = useAction(() => api<RunResult>('/api/admin/playground/run', {
    body: { question, language: language || null, retrievalMode: mode, topK: Number(topK), profile, providers, generate, useDraftGuidance: useDraft, promptGuidance: useDraft ? draft : null },
  }));
  const result = run.data;

  return (
    <>
      <PageHeader
        title="AI playground"
        icon={<FlaskConical className="h-5 w-5" />}
        description="Runs the real assistant pipeline step by step against a fictional profile. No real user data, history or conversations are used, so free-tier providers are allowed here. Runs are audited and count against the AI rate limit."
      />
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[380px_1fr]">
        <Card className="space-y-4 self-start p-4">
          <Field label="Question" htmlFor="pg-question" hint="English, Tamil, Sinhala, Tanglish or Singlish.">
            <Textarea id="pg-question" rows={4} maxLength={1000} value={question} onChange={(e) => setQuestion(e.target.value)} placeholder="Is dhal a good protein source for me?" />
          </Field>
          <Field label="Synthetic profile" htmlFor="pg-profile">
            <Select id="pg-profile" value={profile} onChange={(e) => setProfile(e.target.value)}>
              {(profiles.data ?? [{ id: 'vegetarian-student', profile: { profile: 'Synthetic vegetarian student' } }]).map((p) => <option key={p.id} value={p.id}>{p.profile.profile}</option>)}
            </Select>
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Answer language" htmlFor="pg-lang">
              <Select id="pg-lang" value={language} onChange={(e) => setLanguage(e.target.value)}>
                <option value="">Auto-detect</option><option value="en">English</option><option value="ta">Tamil</option><option value="si">Sinhala</option>
              </Select>
            </Field>
            <Field label="Retrieval" htmlFor="pg-mode">
              <Select id="pg-mode" value={mode} onChange={(e) => setMode(e.target.value)}>
                <option value="hybrid">Hybrid</option><option value="semantic">Semantic</option><option value="keyword">Keyword</option>
              </Select>
            </Field>
            <Field label="Top K" htmlFor="pg-topk">
              <Select id="pg-topk" value={topK} onChange={(e) => setTopK(e.target.value)}>{[1, 2, 3, 4, 5, 6, 8].map((k) => <option key={k}>{k}</option>)}</Select>
            </Field>
          </div>
          <fieldset className="space-y-1.5">
            <legend className="text-sm font-medium text-fg">Generate with</legend>
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4 accent-[var(--primary)]" checked={generate} onChange={(e) => setGenerate(e.target.checked)} />Generate answers (otherwise retrieval only)</label>
            {['Gemini', 'Groq'].map((p) => (
              <label key={p} className="flex items-center gap-2 pl-6 text-sm">
                <input type="checkbox" className="h-4 w-4 accent-[var(--primary)]" disabled={!generate} checked={providers.includes(p)} onChange={(e) => setProviders(e.target.checked ? [...providers, p] : providers.filter((x) => x !== p))} />{p}
              </label>
            ))}
          </fieldset>
          <fieldset className="space-y-1.5">
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4 accent-[var(--primary)]" checked={useDraft} onChange={(e) => setUseDraft(e.target.checked)} />Use draft operator guidance (not saved)</label>
            {useDraft && <Textarea aria-label="Draft guidance" rows={3} maxLength={2000} value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Empty = no guidance" />}
          </fieldset>
          <Button className="w-full" loading={run.isPending} disabled={!question.trim() || (generate && providers.length === 0)} onClick={() => run.mutate(undefined)}>
            <Play className="h-4 w-4" /> Run pipeline
          </Button>
        </Card>

        <div className="space-y-3">
          {!result && !run.isPending && <Card className="p-8 text-center text-sm text-muted">Results appear here: language → embedding → retrieval → prompt → generation → validation.</Card>}
          {result && (
            <>
              <p className="text-xs text-muted">Total {number(result.totalMs, 0)} ms · synthetic profile only</p>
              {result.steps.map((s, i) => <StepCard key={s.step} index={i + 1} step={s} />)}
              {result.generations.length > 0 && (
                <div className={cn('grid gap-3', result.generations.length > 1 && 'lg:grid-cols-2')}>
                  {result.generations.map((g) => (
                    <Card key={g.provider} className="p-4 animate-fade-up">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <p className="text-sm font-semibold text-fg">{g.provider} {g.model && <span className="font-mono text-xs font-normal text-muted">{g.model}</span>}</p>
                        <span className="text-xs text-muted">{number(g.ms, 0)} ms{g.tokens ? ` · ${number(g.tokens)} tokens` : ''}</span>
                      </div>
                      {g.error ? <p className="mt-2 text-sm text-danger">{g.error}</p> : (
                        <>
                          <div className="mt-2 flex flex-wrap gap-3">
                            <Check ok={g.validJson} label="Valid JSON" />
                            <Check ok={g.validationPassed} label="Grounding validation" />
                            <Check ok={g.languageCorrect} label="Correct language" />
                            <Check ok={!g.allergyGuarantee} label="No allergy guarantee" />
                            {g.insufficientEvidence && <Badge tone="warning">Insufficient evidence</Badge>}
                          </div>
                          <p className="mt-3 whitespace-pre-wrap rounded-lg bg-surface-2 p-3 text-sm text-fg">{g.answer}</p>
                          {g.rawAnswer && g.rawAnswer !== g.answer && <details className="mt-2 text-xs"><summary className="cursor-pointer text-primary">Raw model answer (before validation)</summary><p className="mt-1 whitespace-pre-wrap text-muted">{g.rawAnswer}</p></details>}
                          {g.citations && g.citations.length > 0 && (
                            <ul className="mt-2 space-y-0.5 text-xs text-muted">
                              {g.citations.map((c, i) => <li key={i}>• {typeof c === 'string' ? c : c.title}</li>)}
                            </ul>
                          )}
                        </>
                      )}
                    </Card>
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </>
  );
}
