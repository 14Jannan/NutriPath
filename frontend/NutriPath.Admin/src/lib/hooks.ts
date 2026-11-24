import { useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { toast } from 'sonner';
import { useSearchParams } from 'react-router';
import { useCallback } from 'react';

/**
 * A server action with a success toast and cache invalidation. Errors are shown as a toast
 * unless the caller handles them (dialogs show them inline and rethrow).
 */
export function useAction<TVars, TResult = unknown>(
  fn: (vars: TVars) => Promise<TResult>,
  { success, invalidate = [], silentError }: { success?: string | ((result: TResult) => string); invalidate?: QueryKey[]; silentError?: boolean } = {},
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async (result) => {
      await Promise.all(invalidate.map((key) => client.invalidateQueries({ queryKey: key })));
      if (success) toast.success(typeof success === 'function' ? success(result) : success);
    },
    onError: (error) => {
      if (!silentError) toast.error(error instanceof Error ? error.message : 'The action failed.');
    },
  });
}

/** Filters kept in the URL so views can be bookmarked, shared and survive reloads. */
export function useUrlState<T extends Record<string, string>>(defaults: T) {
  const [params, setParams] = useSearchParams();
  const values = Object.fromEntries(Object.entries(defaults).map(([key, value]) => [key, params.get(key) ?? value])) as T;
  const set = useCallback(
    (patch: Partial<T>) =>
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          for (const [key, value] of Object.entries(patch)) {
            if (value === undefined || value === '' || value === defaults[key]) next.delete(key);
            else next.set(key, String(value));
          }
          if (!('page' in patch)) next.delete('page');
          return next;
        },
        { replace: true },
      ),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [setParams],
  );
  return [values, set] as const;
}
