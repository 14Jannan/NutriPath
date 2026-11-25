import { useEffect, useState } from 'react';
import { AppStatus, getAppStatus } from '@/api/accountApi';

// One shared request per minute for the whole app.
let cached: { status: AppStatus; at: number } | null = null;
let inFlight: Promise<AppStatus> | null = null;

export function loadAppStatus(): Promise<AppStatus> {
  if (cached && Date.now() - cached.at < 60_000) return Promise.resolve(cached.status);
  inFlight ??= getAppStatus()
    .then((status) => {
      cached = { status, at: Date.now() };
      return status;
    })
    .finally(() => {
      inFlight = null;
    });
  return inFlight;
}

/** For tests. */
export function resetAppStatusCache() {
  cached = null;
  inFlight = null;
}

/**
 * Server-controlled feature switches and the maintenance notice. Until the server answers (or if
 * it can't be reached) features count as on: the server still enforces every switch.
 */
export function useAppStatus() {
  const [status, setStatus] = useState<AppStatus | null>(cached?.status ?? null);
  useEffect(() => {
    let active = true;
    loadAppStatus().then((s) => active && setStatus(s)).catch(() => {});
    return () => {
      active = false;
    };
  }, []);
  return {
    status,
    isEnabled: (flag: string) => status?.features[flag] ?? true,
  };
}
