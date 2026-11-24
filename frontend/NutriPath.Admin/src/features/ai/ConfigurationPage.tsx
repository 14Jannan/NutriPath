import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { History, RotateCcw, Save, SlidersHorizontal } from 'lucide-react';
import { api } from '@/api/client';
import type { AiRuntimeDocument } from '@/api/types';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select, Switch } from '@/components/ui';
import { ConfirmDialog } from '@/components/overlays';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, number } from '@/lib/utils';

interface ConfigResponse {
  version: number;
  configuration: AiRuntimeDocument;
  effective: {
    provider: string; disabledProviders: string[]; geminiModel: string; geminiTemperature: number; geminiMaxOutputTokens: number;
    geminiThinkingLevel: string | null; geminiTimeoutSeconds: number; geminiMaxAttempts: number; geminiMaxRetryDelaySeconds: number;
    groqModel: string; multilingual: { enabled: boolean; supportedLanguages: string[]; defaultLanguage: string };
    usage: { tokenLimit: number; windowHours: number };
  };
}
interface HistoryEntry { version: number; createdAtUtc: string; note: string | null; configuration: AiRuntimeDocument; createdBy: string | null }
interface ModelList { provider: string; available: boolean; models: string[] }

/** Blank numeric fields mean "use the deployment default" (sent as null). */
const num = (value: string) => (value.trim() === '' ? null : Number(value));
const str = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));

export default function ConfigurationPage() {
  const { can } = useAuth();
  const config = useQuery({ queryKey: ['ai-config'], queryFn: () => api<ConfigResponse>('/api/admin/ai/configuration') });
  const history = useQuery({ queryKey: ['ai-config-history'], queryFn: () => api<HistoryEntry[]>('/api/admin/ai/configuration/history') });
  const geminiModels = useQuery({ queryKey: ['models', 'Gemini'], queryFn: () => api<ModelList>('/api/admin/ai/models?provider=Gemini'), staleTime: 600_000 });
  const groqModels = useQuery({ queryKey: ['models', 'Groq'], queryFn: () => api<ModelList>('/api/admin/ai/models?provider=Groq'), staleTime: 600_000 });
  const [doc, setDoc] = useState<AiRuntimeDocument | null>(null);
  const [note, setNote] = useState('');
  const [rollbackTo, setRollbackTo] = useState<number | null>(null);
  const editable = can('ai.models.manage');
  const canSwitchProvider = can('ai.providers.manage');

  useEffect(() => {
    if (config.data) setDoc(structuredClone(config.data.configuration));
  }, [config.data]);

  const invalidate = [['ai-config'], ['ai-config-history'], ['ai-providers']];
  const save = useAction(() => api<{ version: number }>('/api/admin/ai/configuration', { method: 'PUT', body: { configuration: doc, expectedVersion: config.data!.version, note: note || null } }), { success: (r) => `Saved as version ${r.version}. Applies to new requests within seconds.`, invalidate });
  const rollback = useAction(() => api<{ version: number }>(`/api/admin/ai/configuration/rollback/${rollbackTo}`, { method: 'POST', body: { expectedVersion: config.data!.version } }), { success: (r) => `Rolled back; now version ${r.version}.`, invalidate, silentError: true });

  if (config.isError) return <ErrorState error={config.error} onRetry={() => void config.refetch()} />;
  if (config.isPending || !doc) return <LoadingState rows={8} />;
  const current = config.data;
  const e = current.effective;
  const set = (patch: Partial<AiRuntimeDocument>) => setDoc((d) => ({ ...d!, ...patch }));
  const dirty = JSON.stringify(doc) !== JSON.stringify(current.configuration);
  const languages = doc.supportedLanguages ?? e.multilingual.supportedLanguages;

  const modelField = (id: string, label: string, value: string | null | undefined, effective: string, list: ModelList | undefined, onChange: (v: string | null) => void) => (
    <Field label={label} htmlFor={id} hint={list && !list.available ? 'Model list unavailable (no working key); type a model name.' : `Currently ${effective}. Blank uses the deployment default.`}>
      <Input id={id} list={`${id}-options`} value={value ?? ''} placeholder={effective} disabled={!editable} onChange={(ev) => onChange(ev.target.value.trim() || null)} className="font-mono" />
      <datalist id={`${id}-options`}>{list?.models.map((m) => <option key={m} value={m} />)}</datalist>
    </Field>
  );

  return (
    <>
      <PageHeader
        title="AI configuration"
        icon={<SlidersHorizontal className="h-5 w-5" />}
        description={<>Runtime settings for the assistant, versioned with history and rollback. Version <strong>{current.version}</strong>. Privacy routing (which users may reach free-tier Gemini) is deployment-only and not editable here.</>}
        actions={editable && <Button disabled={!dirty} loading={save.isPending} onClick={() => save.mutate(undefined, { onSuccess: () => setNote('') })}><Save className="h-4 w-4" /> Save changes</Button>}
      />
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          <Card>
            <CardHeader title="Providers" description="The primary provider answers first; the other is the fallback when enabled." />
            <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2">
              <Field label="Primary provider" htmlFor="provider" hint={!canSwitchProvider ? 'Switching providers needs ai.providers.manage.' : undefined}>
                <Select id="provider" value={doc.provider ?? ''} disabled={!editable || !canSwitchProvider} onChange={(ev) => set({ provider: ev.target.value || null })}>
                  <option value="">Deployment default ({e.provider})</option>
                  <option value="Gemini">Gemini</option>
                  <option value="Groq">Groq</option>
                </Select>
              </Field>
              <div className="space-y-3 pt-6">
                <Switch label="Gemini enabled" checked={doc.geminiEnabled} disabled={!editable} onChange={(v) => set({ geminiEnabled: v })} />
                <Switch label="Groq enabled" checked={doc.groqEnabled} disabled={!editable} onChange={(v) => set({ groqEnabled: v })} />
              </div>
            </div>
          </Card>
          <Card>
            <CardHeader title="Gemini" />
            <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2">
              {modelField('gemini-model', 'Model', doc.geminiModel, e.geminiModel, geminiModels.data, (v) => set({ geminiModel: v }))}
              <Field label="Thinking level" htmlFor="thinking" hint={`Currently ${e.geminiThinkingLevel || 'model default'}.`}>
                <Select id="thinking" value={doc.geminiThinkingLevel ?? ''} disabled={!editable} onChange={(ev) => set({ geminiThinkingLevel: ev.target.value || null })}>
                  <option value="">Deployment default</option>
                  {['minimal', 'low', 'medium', 'high'].map((l) => <option key={l}>{l}</option>)}
                </Select>
              </Field>
              <Field label="Temperature (0–1)" htmlFor="temp"><Input id="temp" type="number" step="0.05" min={0} max={1} placeholder={String(e.geminiTemperature)} disabled={!editable} value={str(doc.geminiTemperature)} onChange={(ev) => set({ geminiTemperature: num(ev.target.value) })} /></Field>
              <Field label="Max output tokens (256–4096)" htmlFor="maxout"><Input id="maxout" type="number" min={256} max={4096} placeholder={String(e.geminiMaxOutputTokens)} disabled={!editable} value={str(doc.geminiMaxOutputTokens)} onChange={(ev) => set({ geminiMaxOutputTokens: num(ev.target.value) })} /></Field>
              <Field label="Timeout (5–60 s)" htmlFor="timeout"><Input id="timeout" type="number" min={5} max={60} placeholder={String(e.geminiTimeoutSeconds)} disabled={!editable} value={str(doc.geminiTimeoutSeconds)} onChange={(ev) => set({ geminiTimeoutSeconds: num(ev.target.value) })} /></Field>
              <Field label="Retry attempts (1–5)" htmlFor="attempts"><Input id="attempts" type="number" min={1} max={5} placeholder={String(e.geminiMaxAttempts)} disabled={!editable} value={str(doc.geminiMaxAttempts)} onChange={(ev) => set({ geminiMaxAttempts: num(ev.target.value) })} /></Field>
              <Field label="Max retry delay (0–30 s)" htmlFor="delay"><Input id="delay" type="number" min={0} max={30} placeholder={String(e.geminiMaxRetryDelaySeconds)} disabled={!editable} value={str(doc.geminiMaxRetryDelaySeconds)} onChange={(ev) => set({ geminiMaxRetryDelaySeconds: num(ev.target.value) })} /></Field>
            </div>
          </Card>
          <Card>
            <CardHeader title="Groq" />
            <div className="px-5 pb-5">{modelField('groq-model', 'Model', doc.groqModel, e.groqModel, groqModels.data, (v) => set({ groqModel: v }))}</div>
          </Card>
          <Card>
            <CardHeader title="Languages and limits" />
            <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2">
              <div className="space-y-3">
                <Switch label="Multilingual answers" checked={doc.multilingualEnabled ?? e.multilingual.enabled} disabled={!editable} onChange={(v) => set({ multilingualEnabled: v })} />
                <fieldset className="space-y-1.5">
                  <legend className="text-sm font-medium text-fg">Supported languages</legend>
                  {[['en', 'English (required)'], ['ta', 'Tamil'], ['si', 'Sinhala']].map(([code, label]) => (
                    <label key={code} className="flex items-center gap-2 text-sm text-fg">
                      <input type="checkbox" className="h-4 w-4 accent-[var(--primary)]" checked={languages.includes(code)} disabled={!editable || code === 'en'}
                        onChange={(ev) => set({ supportedLanguages: ev.target.checked ? [...languages, code] : languages.filter((l) => l !== code) })} />
                      {label}
                    </label>
                  ))}
                </fieldset>
              </div>
              <div className="space-y-4">
                <Field label="Default language" htmlFor="default-lang">
                  <Select id="default-lang" value={doc.defaultLanguage ?? ''} disabled={!editable} onChange={(ev) => set({ defaultLanguage: ev.target.value || null })}>
                    <option value="">Deployment default ({e.multilingual.defaultLanguage})</option>
                    {languages.map((l) => <option key={l} value={l}>{l}</option>)}
                  </Select>
                </Field>
                <Field label="Default token limit per user (1,000–2,000,000)" htmlFor="limit"><Input id="limit" type="number" min={1000} max={2_000_000} placeholder={number(e.usage.tokenLimit)} disabled={!editable} value={str(doc.tokenLimit)} onChange={(ev) => set({ tokenLimit: num(ev.target.value) })} /></Field>
                <Field label="Usage window (1–168 h)" htmlFor="window"><Input id="window" type="number" min={1} max={168} placeholder={String(e.usage.windowHours)} disabled={!editable} value={str(doc.windowHours)} onChange={(ev) => set({ windowHours: num(ev.target.value) })} /></Field>
              </div>
            </div>
          </Card>
          {editable && dirty && (
            <Card className="p-4">
              <Field label="Change note (optional, shown in history)" htmlFor="note"><Input id="note" maxLength={200} value={note} onChange={(ev) => setNote(ev.target.value)} /></Field>
              <div className="mt-3 flex gap-2">
                <Button loading={save.isPending} onClick={() => save.mutate(undefined, { onSuccess: () => setNote('') })}><Save className="h-4 w-4" /> Save changes</Button>
                <Button variant="ghost" onClick={() => setDoc(structuredClone(current.configuration))}>Discard</Button>
              </div>
            </Card>
          )}
        </div>
        <Card className="self-start">
          <CardHeader title="Version history" icon={<History className="h-4 w-4" />} />
          {history.isPending ? <LoadingState rows={3} /> : history.isError ? <ErrorState error={history.error} /> : history.data.length === 0 ? <EmptyState title="No saved versions yet" description="Deployment defaults are in use." /> : (
            <ul className="divide-y divide-border">
              {history.data.map((h) => (
                <li key={h.version} className="flex items-start justify-between gap-2 px-5 py-3">
                  <div className="min-w-0">
                    <p className="text-sm text-fg">Version {h.version} {h.version === current.version && <Badge tone="success" className="ml-1">Current</Badge>}</p>
                    <p className="text-xs text-muted">{formatDateTime(h.createdAtUtc)}</p>
                    {h.note && <p className="mt-0.5 text-xs text-muted">{h.note}</p>}
                  </div>
                  {editable && h.version !== current.version && <Button size="sm" variant="ghost" onClick={() => setRollbackTo(h.version)}><RotateCcw className="h-3.5 w-3.5" /> Restore</Button>}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
      <ConfirmDialog open={rollbackTo !== null} onOpenChange={(o) => !o && setRollbackTo(null)} title={`Restore version ${rollbackTo}?`} description="This saves a new version with that version's settings. The current settings stay in history." confirmLabel="Restore" onConfirm={() => rollback.mutateAsync(undefined)} />
    </>
  );
}
