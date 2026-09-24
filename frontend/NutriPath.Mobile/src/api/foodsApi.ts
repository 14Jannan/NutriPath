import { apiClient } from './client';

export interface FoodSearchResult {
  id: string;
  name: string;
  servingSizeGrams: number;
  calories: number;
  proteinGrams: number;
  carbsGrams: number;
  fatGrams: number;
  fiberGrams: number;
  sugarGrams: number;
  sodiumMilligrams: number;
  sourceName: string;
}

export async function searchFoods(query: string): Promise<FoodSearchResult[]> {
  const response = await apiClient.get<FoodSearchResult[]>('/api/foods/search', { params: { query } });
  return response.data;
}

export async function getFoodById(id: string): Promise<FoodSearchResult> {
  const response = await apiClient.get<FoodSearchResult>(`/api/foods/${id}`);
  return response.data;
}
