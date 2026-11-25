import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { FlaskConical, MessageSquareText, Save } from 'lucide-react';
import { api } from '@/api/client';
import { Button, Card, CardHeader, ErrorState, Field, Input, LoadingState, PageHeader, Select, Textarea } from '@/components/ui';
import { VersionHistory } from '@/components/versions';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { languageName } from '@/lib/utils';

interface PromptsResponse { version: number; guidance: string | null; basePrompts: { language: string; prompt: string }[]; preview: string }

export const DRAFT_GUIDANCE_KEY = 'nutripath-admin-draft-guidance';

export default function PromptsPage() {
  const { can } = useAuth();
  const data = useQuery({ queryKey: ['prompts'], queryFn: () => api<PromptsResponse>('/api/admin/prompts') });
  const [guidance, setGuidance] = useState('');
  const [note, setNote] = useState('');
  const [language, setLanguage] = useState('en');
  const editable = can('ai.prompts.manage');

  useEffect(() => {
    if (data.data) setGuidance(data.data.guidance ?? '');
  }, [data.data]);

  const save = useAction(
    () => api<{ version: number }>('/api/admin/prompts', { method: 'PUT', body: { document: { guidance }, expectedVersion: data.data!.version, note: note || null } }),
    { success: (r) => `Guidance saved as version ${r.version}. New answers use it within 10 seconds.`, invalidate: [['prompts'], ['platform-history', 'prompt']] },
  );

  if (data.isPending) return <LoadingState rows={8} />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const dirty = guidance.trim() !== (data.data.guidance ?? '');
  const base = data.data.basePrompts.find((p) => p.language === language)?.prompt ?? '';

  return (
    <>
      <PageHeader
        title="Prompt guidance"
        icon={<MessageSquareText className="h-5 w-5" />}
        description="The assistant's safety and grounding rules live in code and can't be edited here. You can add operator guidance that is appended after the rules and is always subordinate to them. Changes need a recent authenticator code."
        actions={editable && <Button disabled={!dirty} loading={save.isPending} onClick={() => save.mutate(undefined, { onSuccess: () => setNote('') })}><Save className="h-4 w-4" /> Save guidance</Button>}
      />
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          <Card>
            <CardHeader
              title="Operator guidance"
              description="For tone and emphasis, e.g. “Prefer Sri Lankan food examples.” Instructions that try to override the rules are rejected."
              action={can('ai.evaluate') && (
                <Link to="/ai/playground?draftGuidance=1" onClick={() => { try { sessionStorage.setItem(DRAFT_GUIDANCE_KEY, guidance); } catch { /* storage unavailable */ } }}>
                  <Button size="sm" variant="outline"><FlaskConical className="h-4 w-4" /> Try in playground</Button>
                </Link>
              )}
            />
            <div className="space-y-3 px-5 pb-5">
              <Field label="Guidance" htmlFor="guidance" hint={`${guidance.length}/2000 characters`}>
                <Textarea id="guidance" rows={8} maxLength={2000} disabled={!editable} value={guidance} onChange={(e) => setGuidance(e.target.value)} placeholder="No extra guidance. The built-in rules apply." />
              </Field>
              {editable && dirty && <Field label="Change note (optional)" htmlFor="prompt-note"><Input id="prompt-note" maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} /></Field>}
            </div>
          </Card>
          <Card>
            <CardHeader
              title="Built-in system prompt (read-only)"
              description="Version-controlled in AiPrompt.cs. Shown per answer language."
              action={
                <Select aria-label="Language" value={language} onChange={(e) => setLanguage(e.target.value)} className="w-44">
                  {data.data.basePrompts.map((p) => <option key={p.language} value={p.language}>{languageName(p.language)}</option>)}
                </Select>
              }
            />
            <pre className="mx-5 mb-5 max-h-[480px] overflow-auto whitespace-pre-wrap rounded-lg bg-surface-2 p-4 font-mono text-[11px] leading-relaxed text-fg">{base}</pre>
          </Card>
        </div>
        <VersionHistory docKey="prompt" current={data.data.version} canRollback={editable} invalidate={[['prompts']]} />
      </div>
    </>
  );
}
