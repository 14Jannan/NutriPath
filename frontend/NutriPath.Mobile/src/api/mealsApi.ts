import { apiClient } from './client';

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

export async function logMealItem(foodId: string, quantityGrams: number, mealType: string) {
  await apiClient.post('/api/meals/items', { foodId, quantityGrams, mealType });
}

export async function getDailyMeals(): Promise<DailyMeals> {
  const response = await apiClient.get<DailyMeals>('/api/meals/daily');
  return response.data;
}

export async function deleteMealItem(id: string) {
  await apiClient.delete(`/api/meals/items/${id}`);
}
