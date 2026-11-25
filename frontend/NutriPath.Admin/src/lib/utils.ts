import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';
import { format, formatDistanceToNowStrict, parseISO } from 'date-fns';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** The API sends UTC; values without a zone marker are UTC too. */
export function toDate(value: string | null | undefined) {
  if (!value) return null;
  return parseISO(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
}

export function formatDateTime(value: string | null | undefined) {
  const date = toDate(value);
  return date ? format(date, 'd MMM yyyy, HH:mm') : '—';
}

export function formatDate(value: string | null | undefined) {
  const date = toDate(value);
  return date ? format(date, 'd MMM yyyy') : '—';
}

export function relative(value: string | null | undefined) {
  const date = toDate(value);
  return date ? `${formatDistanceToNowStrict(date)} ago` : 'never';
}

export const number = (value: number | null | undefined, digits = 0) =>
  value === null || value === undefined || Number.isNaN(value)
    ? '—'
    : value.toLocaleString(undefined, { maximumFractionDigits: digits, minimumFractionDigits: 0 });

export const percent = (value: number | null | undefined, digits = 1) =>
  value === null || value === undefined ? '—' : `${(value * 100).toFixed(digits)}%`;

export const languageName = (code: string | null | undefined) =>
  ({ en: 'English', ta: 'Tamil (தமிழ்)', si: 'Sinhala (සිංහල)' })[code ?? ''] ?? code ?? '—';

export const healthTone = (status: string) =>
  status === 'Healthy' ? 'success' : status === 'Degraded' || status === 'RateLimited' ? 'warning' : status === 'Unavailable' ? 'danger' : 'neutral';
