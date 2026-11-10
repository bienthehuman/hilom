import { type WardType } from '../../../api/models/ward-type';
import { type Option } from '../../../core/format/options';

export const WARD_TYPES: readonly Option<WardType>[] = [
  { value: 'general', label: 'General' },
  { value: 'pediatric', label: 'Pediatric' },
  { value: 'icu', label: 'ICU' },
  { value: 'maternity', label: 'Maternity' },
  { value: 'surgical', label: 'Surgical' },
  { value: 'isolation', label: 'Isolation' },
];

/** "12 beds", "1 bed", "No beds". */
export function bedCount(count: number): string {
  return count === 0 ? 'No beds' : `${count} ${count === 1 ? 'bed' : 'beds'}`;
}
