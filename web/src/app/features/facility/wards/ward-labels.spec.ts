import { labelOf } from '../../../core/format/options';
import { WARD_TYPES, bedCount } from './ward-labels';

describe('ward labels', () => {
  it('counts beds in words', () => {
    expect(bedCount(0)).toBe('No beds');
    expect(bedCount(1)).toBe('1 bed');
    expect(bedCount(24)).toBe('24 beds');
  });

  it('labels every ward type', () => {
    expect(labelOf(WARD_TYPES, 'icu')).toBe('ICU');
    expect(WARD_TYPES.map((t) => t.value)).toEqual([
      'general',
      'pediatric',
      'icu',
      'maternity',
      'surgical',
      'isolation',
    ]);
  });
});
