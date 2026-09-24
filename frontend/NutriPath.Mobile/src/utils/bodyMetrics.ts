// Plausibility checks and guidance for the goals form. Fixed rules, not
// AI: they must be instant, free, and give the same answer every time.
// The hard limits mirror ProfileService on the backend.

export const LIMITS = {
  age: { min: 13, max: 120 },
  heightCm: { min: 100, max: 250 },
  weightKg: { min: 25, max: 300 },
  // Outside this BMI range the height/weight pair is almost certainly a
  // typo (e.g. 172 cm and 17 kg), not a real body.
  bmi: { min: 12, max: 70 },
} as const;

export function bmi(heightCm: number, weightKg: number): number {
  const m = heightCm / 100;
  return weightKg / (m * m);
}

/** WHO adult "healthy weight" range (BMI 18.5–24.9) for a height. */
export function healthyWeightRange(heightCm: number): { min: number; max: number } {
  const m2 = (heightCm / 100) ** 2;
  return { min: Math.round(18.5 * m2), max: Math.round(24.9 * m2) };
}

/** WHO adult BMI category. Not valid under 18, where BMI is judged by age percentiles. */
export function bmiCategory(value: number): 'Underweight' | 'Healthy' | 'Overweight' | 'Obese' {
  if (value < 18.5) return 'Underweight';
  if (value < 25) return 'Healthy';
  if (value < 30) return 'Overweight';
  return 'Obese';
}

/** A hint shown under the height field, so people enter a real measurement. */
export function heightHint(age: number | null): string {
  if (age !== null && age >= LIMITS.age.min && age < 18) {
    return 'Teen heights vary a lot. Enter your measured height in cm (usually 140–195).';
  }
  return 'Most adults are between 145 and 200 cm. Enter your measured height.';
}

/**
 * The first problem with the entered values, or null if they're plausible.
 * Empty fields are reported too, since every value is needed for targets.
 */
export function validateBody(age: number, heightCm: number, weightKg: number): string | null {
  if (!Number.isFinite(age) || age < LIMITS.age.min || age > LIMITS.age.max) {
    return `Age must be between ${LIMITS.age.min} and ${LIMITS.age.max}.`;
  }
  if (!Number.isFinite(heightCm) || heightCm < LIMITS.heightCm.min || heightCm > LIMITS.heightCm.max) {
    return `Height must be between ${LIMITS.heightCm.min} and ${LIMITS.heightCm.max} cm.`;
  }
  if (!Number.isFinite(weightKg) || weightKg < LIMITS.weightKg.min || weightKg > LIMITS.weightKg.max) {
    return `Weight must be between ${LIMITS.weightKg.min} and ${LIMITS.weightKg.max} kg.`;
  }
  const value = bmi(heightCm, weightKg);
  if (value < LIMITS.bmi.min || value > LIMITS.bmi.max) {
    return `${weightKg} kg at ${heightCm} cm doesn't look right. Please check both values.`;
  }
  return null;
}
