import { apiClient } from './client';

// These shapes mirror the C# responses exactly — ProfileController's
// /me payload, DailyTotals and WeeklyScoreResult.

export interface MyProfile {
  id: string;
  email: string;
  emailVerified: boolean;
  fullName: string | null;
  targetCalories: number;
  targetProteinGrams: number;
  targetCarbsGrams: number;
  targetFatGrams: number;
  targetFiberGrams: number;
}

export interface DailyTotals {
  calories: number;
  protein: number;
  carbs: number;
  fat: number;
  fiber: number;
  sugar: number;
  sodium: number;
}

// Matches the C# MealType enum names, which /api/meals/daily returns as strings.
export const MEAL_TYPES = ['Breakfast', 'Lunch', 'Dinner', 'Snack'] as const;

export interface WeeklyScore {
  overall: number;
  components: { name: string; percent: number }[];
}

export async function getMyProfile(): Promise<MyProfile> {
  const response = await apiClient.get<MyProfile>('/api/profile/me');
  return response.data;
}

export async function getDailyTotals(): Promise<DailyTotals> {
  const response = await apiClient.get<DailyTotals>('/api/nutrition/daily');
  return response.data;
}

export async function getWeeklyScore(): Promise<WeeklyScore> {
  const response = await apiClient.get<WeeklyScore>('/api/nutrition/weekly-score');
  return response.data;
}
