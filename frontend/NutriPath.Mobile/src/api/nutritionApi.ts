import { apiClient } from './client';
import { todayIso } from '@/utils/date';

// These shapes mirror the C# responses exactly — DailyNutritionResponse
// (DTOs/NutritionDtos.cs) and WeeklyScoreResponse (DTOs/WeeklyScoreDtos.cs).

// Matches the C# MealType enum names, which /api/meals/daily returns as strings.
export const MEAL_TYPES = ['Breakfast', 'Lunch', 'Dinner', 'Snack'] as const;

export interface MacroProgress {
  label: string;
  current: number;
  target: number;
}

export interface DailyNutrition {
  targetCalories: number;
  eatenCalories: number;
  remainingCalories: number;
  macros: MacroProgress[];
}

export interface ScoreComponent {
  key: string;
  label: string;
  status: string;
  percent: number;
  note: string;
  tone: 'good' | 'neutral' | 'warn';
}

export interface WeeklyScore {
  overall: number;
  // Oldest first, ending with today.
  dailyScores: number[];
  components: ScoreComponent[];
}

export async function getDailyNutrition(date: string = todayIso()): Promise<DailyNutrition> {
  const res = await apiClient.get<DailyNutrition>('/api/nutrition/daily', { params: { date } });
  return res.data;
}

// The 7 days ending on `date`.
export async function getWeeklyScore(date: string = todayIso()): Promise<WeeklyScore> {
  const res = await apiClient.get<WeeklyScore>('/api/nutrition/weekly-score', { params: { date } });
  return res.data;
}
