import { useQuery } from '@tanstack/react-query';
import { Cpu, Lock, PlugZap } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, ErrorState, KeyValue, LoadingState, PageHeader } from '@/components/ui';
import { useAction } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { formatDateTime, number, relative } from '@/lib/utils';

interface ProvidersResponse {
  providers: {
    name: string; model: string; enabled: boolean; active: boolean; lastSuccessAtUtc: string | null; privacy: string;
    lastTest: { atUtc: string; outcome: string; category: string | null } | null;
    last24h: { requests: number; failures: number; rateLimited: number; tokens: number; avgLatencyMs: number | null } | null;
  }[];
  privacy: { geminiFreeTier: boolean; realUserDataApproved: boolean; syntheticDomains: string[]; approvedAccounts: number; unapprovedUserProvider: string; note: string };
}

export interface TestResult { ok: boolean; category: string; httpStatus?: number | null }

export const testMessage = (r: TestResult) =>
  r.ok ? 'Connection works.' : ({ InvalidCredential: 'The provider rejected the key.', RateLimited: 'The key works but is rate limited right now.', Unreachable: 'The provider could not be reached.', Missing: 'No key is configured.' } as Record<string, string>)[r.category] ?? `The provider returned an error (${r.category}).`;

export default function ProvidersPage() {
  const { can } = useAuth();
  const data = useQuery({ queryKey: ['ai-providers'], queryFn: () => api<ProvidersResponse>('/api/admin/ai/providers') });
  const test = useAction((provider: string) => api<TestResult>(`/api/admin/ai/providers/${provider}/test`, { method: 'POST' }), { success: testMessage, invalidate: [['ai-providers']] });

  if (data.isPending) return <LoadingState />;
  if (data.isError) return <ErrorState error={data.error} onRetry={() => void data.refetch()} />;
  const { providers, privacy } = data.data;
  return (
    <>
      <PageHeader title="AI providers" icon={<Cpu className="h-5 w-5" />} description="Status of the language-model providers behind the NutriPath assistant. Usage figures come from recorded request metadata; no prompts or answers are stored." />
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {providers.map((p) => (
          <Card key={p.name} className="p-5 animate-fade-up">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-lg font-semibold text-fg">{p.name}</p>
                <p className="font-mono text-xs text-muted">{p.model}</p>
              </div>
              <div className="flex flex-wrap gap-1.5">
                {p.active && <Badge tone="success">Primary</Badge>}
                <Badge tone={p.enabled ? 'teal' : 'neutral'}>{p.enabled ? 'Enabled' : 'Disabled'}</Badge>
              </div>
            </div>
            <div className="mt-4">
              <KeyValue
                items={[
                  ['Requests (24 h)', number(p.last24h?.requests ?? 0)],
                  ['Failures (24 h)', `${number(p.last24h?.failures ?? 0)} (${number(p.last24h?.rateLimited ?? 0)} rate limited)`],
                  ['Tokens (24 h)', number(p.last24h?.tokens ?? 0)],
                  ['Average latency', p.last24h?.avgLatencyMs ? `${number(p.last24h.avgLatencyMs)} ms` : '—'],
                  ['Last successful answer', p.lastSuccessAtUtc ? relative(p.lastSuccessAtUtc) : 'none recorded'],
                  ['Last connection test', p.lastTest ? `${p.lastTest.outcome}${p.lastTest.category ? ` (${p.lastTest.category})` : ''} · ${formatDateTime(p.lastTest.atUtc)}` : 'never'],
                ]}
              />
            </div>
            <p className="mt-4 flex gap-2 rounded-lg bg-surface-2 px-3 py-2 text-xs text-muted"><Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />{p.privacy}</p>
            {can('ai.providers.manage') && (
              <Button className="mt-4" variant="outline" size="sm" loading={test.isPending && test.variables === p.name} onClick={() => test.mutate(p.name)}>
                <PlugZap className="h-4 w-4" /> Test connection
              </Button>
            )}
          </Card>
        ))}
      </div>
      <Card className="mt-6 p-5">
        <p className="text-sm font-semibold text-fg">Privacy routing</p>
        <p className="mt-1 text-xs text-muted">{privacy.note}</p>
        <div className="mt-4">
          <KeyValue
            items={[
              ['Gemini tier', privacy.geminiFreeTier ? 'Free tier' : 'Paid tier'],
              ['Real-user data on Gemini', privacy.realUserDataApproved ? 'Approved in deployment configuration' : 'Not approved'],
              ['Synthetic test domains', privacy.syntheticDomains.join(', ') || '—'],
              ['Allow-listed accounts', number(privacy.approvedAccounts)],
              ['Provider for other users', privacy.unapprovedUserProvider],
            ]}
          />
        </div>
      </Card>
    </>
  );
}
