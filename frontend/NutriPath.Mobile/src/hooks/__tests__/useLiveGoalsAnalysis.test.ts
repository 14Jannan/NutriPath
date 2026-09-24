import { afterEach, beforeEach, describe, expect, it, jest } from '@jest/globals';
import { act, renderHook } from '@testing-library/react-native';
import { AxiosError, AxiosHeaders } from 'axios';
import * as profileApi from '@/api/profileApi';
import type { UpdateGoalsPayload } from '@/api/profileApi';
import { useLiveGoalsAnalysis } from '@/hooks/useLiveGoalsAnalysis';

jest.mock('@/api/profileApi', () => ({
  previewGoals: jest.fn(),
  getProfileInsight: jest.fn(),
}));

const api = jest.mocked(profileApi);

const values: UpdateGoalsPayload = {
  age: 23,
  sex: 'Male',
  heightCm: 172,
  weightKg: 53.5,
  activityLevel: 'Light',
  goal: 'Maintain',
  allergies: [],
  dietaryPreferences: [],
};

const preview = {
  targetCalories: 2150,
  targetProteinGrams: 86,
  targetCarbsGrams: 280,
  targetFatGrams: 60,
  targetFiberGrams: 30,
  bmi: 18.1,
  bmiCategory: 'Underweight',
  healthyWeightMinKg: 55,
  healthyWeightMaxKg: 74,
};

beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  api.previewGoals.mockResolvedValue(preview);
  api.getProfileInsight.mockResolvedValue({ preview, insight: '• Eat well.' });
});

afterEach(() => {
  jest.useRealTimers();
});

async function advance(ms: number) {
  await act(async () => {
    jest.advanceTimersByTime(ms);
  });
}

describe('useLiveGoalsAnalysis', () => {
  it('does nothing while the form is incomplete', async () => {
    const { result } = await renderHook(() => useLiveGoalsAnalysis(null));
    await advance(5000);

    expect(api.previewGoals).not.toHaveBeenCalled();
    expect(api.getProfileInsight).not.toHaveBeenCalled();
    expect(result.current.preview).toBeNull();
  });

  it('fetches the numbers first, then the AI analysis after a longer pause', async () => {
    const { result } = await renderHook(() => useLiveGoalsAnalysis(values));

    await advance(500);
    expect(api.previewGoals).toHaveBeenCalledTimes(1);
    expect(api.getProfileInsight).not.toHaveBeenCalled();
    expect(result.current.preview?.targetCalories).toBe(2150);

    await advance(1000);
    expect(api.getProfileInsight).toHaveBeenCalledTimes(1);
    expect(result.current.insight).toBe('• Eat well.');
    expect(result.current.insightLoading).toBe(false);
  });

  it('only calls the AI once the user stops changing values', async () => {
    const { rerender } = await renderHook((v: UpdateGoalsPayload) => useLiveGoalsAnalysis(v), {
      initialProps: values,
    });

    // Changing the weight three times in quick succession...
    for (const weightKg of [55, 55.5, 56]) {
      await advance(300);
      await rerender({ ...values, weightKg });
    }
    await advance(1500);

    // ...asks the AI once, for the final value only.
    expect(api.getProfileInsight).toHaveBeenCalledTimes(1);
    expect(api.getProfileInsight).toHaveBeenCalledWith({ ...values, weightKg: 56 });
  });

  it('reuses the cached analysis when values return to an earlier state', async () => {
    const { rerender, result } = await renderHook((v: UpdateGoalsPayload) => useLiveGoalsAnalysis(v), {
      initialProps: values,
    });
    await advance(1500);
    await rerender({ ...values, weightKg: 60 });
    await advance(1500);
    await rerender(values);
    await advance(1500);

    expect(api.getProfileInsight).toHaveBeenCalledTimes(2); // not 3
    expect(result.current.insight).toBe('• Eat well.');
  });

  it('explains when the AI rate limit is hit', async () => {
    const response = { status: 429, data: {}, statusText: '', headers: {}, config: { headers: new AxiosHeaders() } };
    api.getProfileInsight.mockRejectedValue(new AxiosError('Too many', '429', undefined, undefined, response));

    const { result } = await renderHook(() => useLiveGoalsAnalysis(values));
    await advance(1500);

    expect(result.current.insight).toBeNull();
    expect(result.current.insightError).toContain('Updating too often');
  });
});
