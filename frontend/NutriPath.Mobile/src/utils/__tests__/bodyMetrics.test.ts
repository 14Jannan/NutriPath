import { describe, expect, it } from '@jest/globals';
import { bmi, bmiCategory, healthyWeightRange, heightHint, validateBody } from '@/utils/bodyMetrics';

describe('bodyMetrics', () => {
  it('calculates BMI and its category', () => {
    const value = bmi(172, 53.5);
    expect(value).toBeCloseTo(18.08, 2);
    expect(bmiCategory(value)).toBe('Underweight');
    expect(bmiCategory(22)).toBe('Healthy');
    expect(bmiCategory(27)).toBe('Overweight');
    expect(bmiCategory(31)).toBe('Obese');
  });

  it('gives the healthy weight range for a height', () => {
    expect(healthyWeightRange(172)).toEqual({ min: 55, max: 74 });
  });

  it('accepts realistic values', () => {
    expect(validateBody(23, 172, 53.5)).toBeNull();
  });

  it.each<[number, number, number, string]>([
    [5, 172, 60, 'Age'],
    [23, 60, 60, 'Height'],
    [23, 172, 500, 'Weight'],
    [23, 172, 30, "doesn't look right"], // BMI ~10: a typo, not a real body
    [23, 150, 200, "doesn't look right"], // BMI ~89
    [23, Number.NaN, 60, 'Height'], // empty field
  ])('rejects age %d, %d cm, %d kg', (age, height, weight, expected) => {
    expect(validateBody(age, height, weight)).toContain(expected);
  });

  it('gives teens a different height hint', () => {
    expect(heightHint(15)).toContain('Teen');
    expect(heightHint(30)).toContain('adults');
  });
});
