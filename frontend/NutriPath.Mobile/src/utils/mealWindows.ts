// When each meal is expected, in the user's local time. Logging is allowed
// at any time — ahead of the window when the user already knows what
// they'll eat, or after it when they log late — but a meal still unlogged
// when its window closes gets a reminder (see notifications/mealReminders).

export type MealType = 'Breakfast' | 'Lunch' | 'Snack' | 'Dinner';

export interface MealWindow {
  mealType: MealType;
  // Minutes after local midnight.
  start: number;
  end: number;
  emoji: string;
}

const at = (hours: number, minutes = 0) => hours * 60 + minutes;

export const MEAL_WINDOWS: MealWindow[] = [
  { mealType: 'Breakfast', start: at(6), end: at(10), emoji: '🍳' },
  { mealType: 'Lunch', start: at(12), end: at(15), emoji: '🍛' },
  { mealType: 'Snack', start: at(15, 30), end: at(17, 30), emoji: '🍌' },
  { mealType: 'Dinner', start: at(19), end: at(22), emoji: '🍲' },
];

export function mealWindow(mealType: string): MealWindow | undefined {
  return MEAL_WINDOWS.find((w) => w.mealType === mealType);
}

/** "6:00 am" in the device's own 12/24-hour style. */
function formatMinutes(minutes: number): string {
  const d = new Date(2000, 0, 1, Math.floor(minutes / 60), minutes % 60);
  return d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

/** e.g. "6:00 am – 10:00 am". */
export function describeWindow(window: MealWindow): string {
  return `${formatMinutes(window.start)} – ${formatMinutes(window.end)}`;
}

/** The moment a meal's window closes on a given local yyyy-MM-dd day. */
export function windowEnd(window: MealWindow, isoDate: string): Date {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Date(y, m - 1, d, 0, window.end);
}

export type MealStatus = 'logged' | 'upcoming' | 'open' | 'missed';

/**
 * Where a meal stands on a day: logged, not due yet, due now, or its
 * window has passed without a log. Past days have only logged/missed.
 */
export function mealStatus(window: MealWindow, isoDate: string, logged: boolean, now: Date = new Date()): MealStatus {
  if (logged) return 'logged';
  const [y, m, d] = isoDate.split('-').map(Number);
  const dayStart = new Date(y, m - 1, d).getTime();
  const minutesIn = (now.getTime() - dayStart) / 60_000;
  if (minutesIn >= window.end) return 'missed';
  return minutesIn >= window.start ? 'open' : 'upcoming';
}
