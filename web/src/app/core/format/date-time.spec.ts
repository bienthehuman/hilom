import {
  addDays,
  dayBefore,
  formatDateSpan,
  formatTimeRange,
  formatWeekday,
  hospitalDateOf,
  hospitalToday,
  weekDays,
  weekStart,
} from './date-time';

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

describe('weeks', () => {
  it('starts weeks on Monday', () => {
    expect(weekStart('2026-11-10')).toBe('2026-11-09'); // Tuesday → Monday
    expect(weekStart('2026-11-09')).toBe('2026-11-09');
    expect(weekStart('2026-11-15')).toBe('2026-11-09'); // Sunday belongs to the week before
  });

  it('lists the seven days, across month ends', () => {
    expect(weekDays('2026-11-30')).toEqual([
      '2026-11-30',
      '2026-12-01',
      '2026-12-02',
      '2026-12-03',
      '2026-12-04',
      '2026-12-05',
      '2026-12-06',
    ]);
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(formatWeekday('2026-11-09')).toBe('Mon');
  });
});

describe('times', () => {
  it('shows instants in hospital time', () => {
    // 01:00 UTC is 09:00 in Manila (UTC+8).
    expect(formatTimeRange('2026-11-16T01:00:00Z', '2026-11-16T01:30:00Z')).toBe('09:00–09:30');
  });

  it('finds the hospital date of an instant', () => {
    // 17:00 UTC on the 15th is 01:00 on the 16th in Manila.
    expect(hospitalDateOf('2026-11-15T17:00:00Z')).toBe('2026-11-16');
  });
});
