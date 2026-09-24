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

// Mirrors GoalsPreviewResponse / ProfileInsightResponse in DTOs/ProfileDtos.cs.
export interface GoalsPreview {
  targetCalories: number;
  targetProteinGrams: number;
  targetCarbsGrams: number;
  targetFatGrams: number;
  targetFiberGrams: number;
  bmi: number;
  // WHO adult category; null under 18.
  bmiCategory: string | null;
  healthyWeightMinKg: number;
  healthyWeightMaxKg: number;
}

export interface ProfileInsight {
  preview: GoalsPreview;
  // null when the AI is unavailable (the numbers are still valid).
  insight: string | null;
}

// Calculates targets for unsaved values — nothing is stored.
export async function previewGoals(payload: UpdateGoalsPayload): Promise<GoalsPreview> {
  const res = await apiClient.post<GoalsPreview>('/api/profile/goals/preview', payload);
  return res.data;
}

// The AI's explanation of those targets for this person. Rate limited per user.
export async function getProfileInsight(payload: UpdateGoalsPayload): Promise<ProfileInsight> {
  const res = await apiClient.post<ProfileInsight>('/api/profile/insights', payload);
  return res.data;
}
