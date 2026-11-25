import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { History, RotateCcw } from 'lucide-react';
import { api } from '@/api/client';
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState } from './ui';
import { ConfirmDialog } from './overlays';
import { useAction } from '@/lib/hooks';
import { formatDateTime } from '@/lib/utils';

export interface VersionEntry { version: number; createdAtUtc: string; note: string | null; createdBy: string | null; document: unknown }

/** History and rollback for a versioned platform document (flags, system, prompt). */
export function VersionHistory({ docKey, current, canRollback, invalidate }: { docKey: 'flags' | 'system' | 'prompt'; current: number; canRollback: boolean; invalidate: unknown[][] }) {
  const history = useQuery({ queryKey: ['platform-history', docKey], queryFn: () => api<VersionEntry[]>(`/api/admin/platform/${docKey}/history`) });
  const [target, setTarget] = useState<number | null>(null);
  const rollback = useAction(
    () => api<{ version: number }>(`/api/admin/platform/${docKey}/rollback/${target}`, { method: 'POST', body: { expectedVersion: current } }),
    { success: (r) => `Restored as version ${r.version}.`, invalidate: [...invalidate, ['platform-history', docKey]], silentError: true },
  );
  return (
    <Card className="self-start">
      <CardHeader title="Version history" icon={<History className="h-4 w-4" />} />
      {history.isPending ? <LoadingState rows={3} /> : history.isError ? <ErrorState error={history.error} /> : history.data.length === 0 ? (
        <EmptyState title="No saved versions yet" description="Defaults are in use." />
      ) : (
        <ul className="max-h-[520px] divide-y divide-border overflow-y-auto">
          {history.data.map((h) => (
            <li key={h.version} className="flex items-start justify-between gap-2 px-5 py-3">
              <div className="min-w-0">
                <p className="text-sm text-fg">Version {h.version} {h.version === current && <Badge tone="success" className="ml-1">Current</Badge>}</p>
                <p className="text-xs text-muted">{formatDateTime(h.createdAtUtc)} · {h.createdBy ?? 'system'}</p>
                {h.note && <p className="mt-0.5 text-xs text-muted">{h.note}</p>}
              </div>
              {canRollback && h.version !== current && (
                <Button size="sm" variant="ghost" onClick={() => setTarget(h.version)}><RotateCcw className="h-3.5 w-3.5" /> Restore</Button>
              )}
            </li>
          ))}
        </ul>
      )}
      <ConfirmDialog open={target !== null} onOpenChange={(o) => !o && setTarget(null)} title={`Restore version ${target}?`} description="Saves a new version with that version's content. Nothing is lost from history." confirmLabel="Restore" onConfirm={() => rollback.mutateAsync(undefined)} />
    </Card>
  );
}
