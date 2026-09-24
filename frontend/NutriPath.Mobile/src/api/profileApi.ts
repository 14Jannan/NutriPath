import { apiClient } from './client';

// Mirrors ProfileResponse / UpdateGoalsRequest in DTOs/ProfileDtos.cs.
export interface ProfileResponse {
  email: string;
  fullName: string;
  emailVerified: boolean;
  age: number;
  sex: string;
  heightCm: number;
  weightKg: number;
  activityLevel: string;
  goal: string;
  targetCalories: number;
  targetProteinGrams: number;
  targetCarbsGrams: number;
  targetFatGrams: number;
  targetFiberGrams: number;
  allergies: string[];
  dietaryPreferences: string[];
}

export interface UpdateGoalsPayload {
  age: number;
  sex: string;
  heightCm: number;
  weightKg: number;
  activityLevel: string;
  goal: string;
  allergies: string[];
  dietaryPreferences: string[];
}

export async function getMyProfile(): Promise<ProfileResponse> {
  const res = await apiClient.get<ProfileResponse>('/api/profile/me');
  return res.data;
}

export async function updateGoals(payload: UpdateGoalsPayload): Promise<ProfileResponse> {
  const res = await apiClient.put<ProfileResponse>('/api/profile/goals', payload);
  return res.data;
}
