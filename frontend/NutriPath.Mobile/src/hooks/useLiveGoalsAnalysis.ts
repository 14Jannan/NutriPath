import { useEffect, useRef, useState } from 'react';
import { isAxiosError } from 'axios';
import { getProfileInsight, previewGoals, GoalsPreview, UpdateGoalsPayload } from '@/api/profileApi';

// Numbers are cheap and exact, so they update soon after typing stops.
// The AI waits longer, so it isn't called for every half-typed value.
const PREVIEW_DELAY_MS = 500;
const INSIGHT_DELAY_MS = 1500;

/**
 * Live analysis for the goals form: backend-calculated targets, plus the
 * AI's explanation of them, both refreshed as the values change.
 * Pass null while the form is incomplete or invalid.
 */
export function useLiveGoalsAnalysis(payload: UpdateGoalsPayload | null) {
  const [preview, setPreview] = useState<GoalsPreview | null>(null);
  const [insight, setInsight] = useState<string | null>(null);
  const [insightLoading, setInsightLoading] = useState(false);
  const [insightError, setInsightError] = useState<string | null>(null);

  // The same values always get the same answer, so going back to earlier
  // values (e.g. undoing a typo) shows it instantly without a new AI call.
  const insightCache = useRef(new Map<string, string | null>());

  const key = payload ? JSON.stringify(payload) : null;

  useEffect(() => {
    if (!key) {
      setPreview(null);
      setInsight(null);
      setInsightLoading(false);
      setInsightError(null);
      return;
    }
    const values = JSON.parse(key) as UpdateGoalsPayload;

    // Set by the cleanup, so responses for values the user has since
    // changed are ignored rather than overwriting newer results.
    let stale = false;

    const previewTimer = setTimeout(() => {
      previewGoals(values)
        .then((p) => !stale && setPreview(p))
        .catch(() => !stale && setPreview(null));
    }, PREVIEW_DELAY_MS);

    let insightTimer: ReturnType<typeof setTimeout> | undefined;
    if (insightCache.current.has(key)) {
      setInsight(insightCache.current.get(key) ?? null);
      setInsightLoading(false);
      setInsightError(null);
    } else {
      setInsightLoading(true);
      setInsightError(null);
      insightTimer = setTimeout(() => {
        getProfileInsight(values)
          .then((result) => {
            insightCache.current.set(key, result.insight);
            if (stale) return;
            setInsight(result.insight);
            if (!result.insight) setInsightError('The AI assistant is unavailable right now. Your targets above are still accurate.');
          })
          .catch((error) => {
            if (stale) return;
            setInsight(null);
            setInsightError(
              isAxiosError(error) && error.response?.status === 429
                ? 'Updating too often. Pause for a minute and the analysis will refresh.'
                : "Couldn't get the AI analysis right now."
            );
          })
          .finally(() => !stale && setInsightLoading(false));
      }, INSIGHT_DELAY_MS);
    }

    return () => {
      stale = true;
      clearTimeout(previewTimer);
      if (insightTimer) clearTimeout(insightTimer);
    };
  }, [key]);

  return { preview, insight, insightLoading, insightError };
}
