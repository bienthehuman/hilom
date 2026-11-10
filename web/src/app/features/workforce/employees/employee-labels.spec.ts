import { formatDate } from '../../../core/format/date-time';
import {
  PROFESSIONAL_TYPES,
  formatFullName,
  formatListName,
  labelOf,
  requiresLicense,
} from './employee-labels';

describe('employee labels', () => {
  it('formats names for lists and headings', () => {
    const ana = { firstName: 'Ana', middleName: 'Santos', lastName: 'Reyes' };
    expect(formatListName(ana)).toBe('Reyes, Ana S.');
    expect(formatFullName(ana)).toBe('Ana Santos Reyes');
    expect(formatListName({ ...ana, middleName: null })).toBe('Reyes, Ana');
    expect(formatFullName({ ...ana, middleName: null })).toBe('Ana Reyes');
  });

  it('requires a license for physicians and nurses only', () => {
    expect(requiresLicense('physician')).toBe(true);
    expect(requiresLicense('nurse')).toBe(true);
    expect(requiresLicense('allied_health')).toBe(false);
    expect(requiresLicense(null)).toBe(false);
  });

  it('labels known values and falls back to the raw value', () => {
    expect(labelOf(PROFESSIONAL_TYPES, 'allied_health')).toBe('Allied health');
    expect(labelOf(PROFESSIONAL_TYPES, 'surgeon')).toBe('surgeon');
  });
});

describe('formatDate', () => {
  it('shows calendar dates without shifting them across time zones', () => {
    expect(formatDate('2026-01-01')).toBe('Jan 1, 2026');
    expect(formatDate(null)).toBe('—');
  });
});
