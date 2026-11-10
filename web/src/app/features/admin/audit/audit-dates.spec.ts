import { endOfHospitalDay, startOfHospitalDay } from './audit-dates';

describe('audit date range', () => {
  it('starts at midnight in Manila', () => {
    expect(startOfHospitalDay('2026-11-10')).toBe('2026-11-09T16:00:00.000Z');
  });

  it('ends at the next midnight in Manila so the whole day is included', () => {
    expect(endOfHospitalDay('2026-11-10')).toBe('2026-11-10T16:00:00.000Z');
  });

  it('rolls over month and year ends', () => {
    expect(endOfHospitalDay('2026-12-31')).toBe('2026-12-31T16:00:00.000Z');
  });
});
