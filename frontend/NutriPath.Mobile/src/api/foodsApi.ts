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
  // True for a food this user added themselves (private to them).
  isCustom: boolean;
}

// Nutrients per 100 g, as on most package labels. Mirrors CreateFoodRequest.
export interface NewFood {
  name: string;
  calories: number;
  proteinGrams: number;
  carbsGrams: number;
  fatGrams: number;
  fiberGrams?: number;
  sugarGrams?: number;
  sodiumMilligrams?: number;
}

export async function searchFoods(query: string): Promise<FoodSearchResult[]> {
  const response = await apiClient.get<FoodSearchResult[]>('/api/foods/search', { params: { query } });
  return response.data;
}

export async function getFoodById(id: string): Promise<FoodSearchResult> {
  const response = await apiClient.get<FoodSearchResult>(`/api/foods/${id}`);
  return response.data;
}

// Adds a private food for something the catalog doesn't have.
export async function createFood(food: NewFood): Promise<FoodSearchResult> {
  const response = await apiClient.post<FoodSearchResult>('/api/foods', food);
  return response.data;
}

// Edits one of the user's own foods. Meals already logged keep their values.
export async function updateFood(id: string, food: NewFood): Promise<FoodSearchResult> {
  const response = await apiClient.put<FoodSearchResult>(`/api/foods/${id}`, food);
  return response.data;
}

// Deletes one of the user's own foods (not allowed if it's in their log).
export async function deleteFood(id: string): Promise<void> {
  await apiClient.delete(`/api/foods/${id}`);
}
