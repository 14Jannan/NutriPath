import { describe, expect, it } from '@jest/globals';
import { addDaysIso, describeDay, toLocalIsoDate } from '@/utils/date';

describe('toLocalIsoDate', () => {
  it('uses the local calendar day, zero-padded', () => {
    expect(toLocalIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('stays on the local day just after local midnight', () => {
    // toISOString() would report the UTC day here in any timezone ahead
    // of UTC (e.g. Sri Lanka), which is the bug this helper avoids.
    expect(toLocalIsoDate(new Date(2026, 8, 24, 0, 30))).toBe('2026-09-24');
  });
});

describe('addDaysIso', () => {
  it.each<[string, number, string]>([
    ['2026-09-24', -1, '2026-09-23'],
    ['2026-03-01', -1, '2026-02-28'], // month rollover
    ['2026-12-31', 1, '2027-01-01'],  // year rollover
    ['2028-02-28', 1, '2028-02-29'],  // leap year
  ])('%s %+d day(s) is %s', (start, days, expected) => {
    expect(addDaysIso(start, days)).toBe(expected);
  });
});

describe('describeDay', () => {
  const today = '2026-09-24';

  it('names today and yesterday', () => {
    expect(describeDay('2026-09-24', today)).toBe('Today');
    expect(describeDay('2026-09-23', today)).toBe('Yesterday');
  });

  it('formats older days as a date', () => {
    const label = describeDay('2026-09-21', today);
    expect(label).not.toBe('Today');
    expect(label).toContain('21');
  });
});
