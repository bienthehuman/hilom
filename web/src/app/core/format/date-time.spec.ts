import { dayBefore, formatDateSpan, hospitalToday } from './date-time';

describe('hospital dates', () => {
  it('uses the hospital time zone for today', () => {
    // 20:30 UTC on Nov 9 is already Nov 10 in Manila (UTC+8).
    expect(hospitalToday(new Date('2026-11-09T20:30:00Z'))).toBe('2026-11-10');
    expect(hospitalToday(new Date('2026-11-09T10:00:00Z'))).toBe('2026-11-09');
  });

  it('steps back a day across month and year ends', () => {
    expect(dayBefore('2026-03-01')).toBe('2026-02-28');
    expect(dayBefore('2027-01-01')).toBe('2026-12-31');
  });

  it('formats date spans', () => {
    expect(formatDateSpan('2026-11-01', '2026-11-30')).toBe('Nov 1, 2026 – Nov 30, 2026');
    expect(formatDateSpan('2026-11-01', null)).toBe('From Nov 1, 2026');
  });
});
