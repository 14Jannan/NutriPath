import { apiClient } from './client';
import { todayIso } from '@/utils/date';

export interface MealItemResponse {
  id: string;
  foodName: string;
  quantityGrams: number;
  calories: number;
  proteinGrams: number;
  carbsGrams: number;
  fatGrams: number;
  fiberGrams: number;
  sugarGrams: number;
  sodiumMilligrams: number;
}

export interface MealGroup {
  mealType: string;
  items: MealItemResponse[];
  totalCalories: number;
}

export interface DailyMeals {
  date: string;
  meals: MealGroup[];
  totalCalories: number;
  totalProteinGrams: number;
  totalCarbsGrams: number;
  totalFatGrams: number;
  totalFiberGrams: number;
}

// Dates are the user's local yyyy-MM-dd (see utils/date.ts).
export async function logMealItem(foodId: string, quantityGrams: number, mealType: string, date: string = todayIso()) {
  await apiClient.post('/api/meals/items', { foodId, quantityGrams, mealType, date });
}

export async function getDailyMeals(date: string = todayIso()): Promise<DailyMeals> {
  const response = await apiClient.get<DailyMeals>('/api/meals/daily', { params: { date } });
  return response.data;
}

export async function deleteMealItem(id: string) {
  await apiClient.delete(`/api/meals/items/${id}`);
}
