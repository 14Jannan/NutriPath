import type { MaterialCommunityIcons } from '@expo/vector-icons';
import { NONE_OPTION, OTHER_OPTION } from '@/components/MultiSelectDropdown';

// Shared by the first-login setup wizard and the Edit Goals screen, so the
// two can never offer different choices. Values match the backend enums.

type IconName = keyof typeof MaterialCommunityIcons.glyphMap;

export interface ChoiceOption {
  value: string;
  label: string;
  icon: IconName;
  description?: string;
}

export const SEX_OPTIONS: ChoiceOption[] = [
  { value: 'Male', label: 'Male', icon: 'gender-male' },
  { value: 'Female', label: 'Female', icon: 'gender-female' },
  { value: 'Other', label: 'Other', icon: 'gender-non-binary' },
];

export const ACTIVITY_OPTIONS: ChoiceOption[] = [
  { value: 'Sedentary', label: 'Sedentary', icon: 'sofa-outline', description: 'Mostly sitting, little or no exercise' },
  { value: 'Light', label: 'Lightly active', icon: 'walk', description: 'Light exercise or walking 1-3 days a week' },
  { value: 'Moderate', label: 'Moderately active', icon: 'run', description: 'Exercise or sport 3-5 days a week' },
  { value: 'VeryActive', label: 'Very active', icon: 'weight-lifter', description: 'Hard exercise or a physical job 6-7 days a week' },
];

export const GOAL_OPTIONS: ChoiceOption[] = [
  { value: 'Lose', label: 'Lose weight', icon: 'trending-down', description: 'A gentle deficit, about 0.5 kg a week' },
  { value: 'Maintain', label: 'Maintain', icon: 'scale-balance', description: 'Stay where you are and eat well' },
  { value: 'Gain', label: 'Gain weight', icon: 'trending-up', description: 'A steady surplus, about 0.5 kg a week' },
];

// The most common food allergens. Names are kept close to how foods are
// named, since the assistant filters suggestions by matching them.
export const ALLERGY_OPTIONS = [
  NONE_OPTION, 'Peanuts', 'Tree nuts', 'Milk', 'Eggs', 'Fish', 'Shellfish',
  'Wheat', 'Gluten', 'Soy', 'Sesame', OTHER_OPTION,
] as const;

export const DIET_OPTIONS = [
  NONE_OPTION, 'Vegetarian', 'Vegan', 'Pescatarian', 'Halal', 'No beef', 'No pork',
  'Lactose-free', 'Gluten-free', 'Low sugar', OTHER_OPTION,
] as const;
