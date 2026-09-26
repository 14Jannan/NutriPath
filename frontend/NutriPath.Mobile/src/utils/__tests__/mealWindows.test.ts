import { describe, expect, it } from '@jest/globals';
import { mealStatus, mealWindow, windowEnd } from '@/utils/mealWindows';

const lunch = mealWindow('Lunch')!; // 12:00 - 15:00
const at = (h: number, m = 0) => new Date(2026, 8, 26, h, m);

describe('mealWindows', () => {
  it("follows the day: upcoming, then now, then missed if it wasn't logged", () => {
    expect(mealStatus(lunch, '2026-09-26', false, at(9))).toBe('upcoming');
    expect(mealStatus(lunch, '2026-09-26', false, at(12))).toBe('open');
    expect(mealStatus(lunch, '2026-09-26', false, at(14, 59))).toBe('open');
    expect(mealStatus(lunch, '2026-09-26', false, at(15))).toBe('missed');
  });

  it('counts a meal logged ahead of time or late as logged', () => {
    expect(mealStatus(lunch, '2026-09-26', true, at(9))).toBe('logged');
    expect(mealStatus(lunch, '2026-09-26', true, at(20))).toBe('logged');
  });

  it("treats a past day's unlogged meals as missed", () => {
    expect(mealStatus(lunch, '2026-09-25', false, at(9))).toBe('missed');
  });

  it('closes the window at local time on the given day', () => {
    expect(windowEnd(mealWindow('Snack')!, '2026-09-26')).toEqual(new Date(2026, 8, 26, 17, 30));
  });
});
