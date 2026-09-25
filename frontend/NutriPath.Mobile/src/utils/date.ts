// The backend's "today" is UTC, which is still yesterday in Sri Lanka
// until 05:30, so the app always sends its own local calendar date.

/** A Date's LOCAL calendar day as yyyy-MM-dd (toISOString would use UTC). */
export function toLocalIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function todayIso(): string {
  return toLocalIsoDate(new Date());
}

/** Shifts a yyyy-MM-dd date by whole days, handling month/year rollover. */
export function addDaysIso(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  return toLocalIsoDate(new Date(y, m - 1, d + days));
}

/** "Today", "Yesterday", or e.g. "Mon, 21 Sep". */
export function describeDay(isoDate: string, today: string = todayIso()): string {
  if (isoDate === today) return 'Today';
  if (isoDate === addDaysIso(today, -1)) return 'Yesterday';
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' });
}

/**
 * The device's local date-time WITH its UTC offset, e.g.
 * "2026-09-25T19:30:00+05:30", so the server knows both the user's local
 * time and their timezone (toISOString would convert to UTC and lose it).
 */
export function toLocalIsoDateTime(date: Date = new Date()): string {
  const pad = (n: number) => String(Math.floor(Math.abs(n))).padStart(2, '0');
  const offsetMinutes = -date.getTimezoneOffset();
  const sign = offsetMinutes >= 0 ? '+' : '-';
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
  return `${toLocalIsoDate(date)}T${time}${sign}${pad(offsetMinutes / 60)}:${pad(offsetMinutes % 60)}`;
}
