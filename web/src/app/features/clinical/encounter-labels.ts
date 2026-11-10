import { type EncounterPhysicianRef } from '../../api/models/encounter-physician-ref';
import { type EncounterStatus } from '../../api/models/encounter-status';
import { type EncounterType } from '../../api/models/encounter-type';
import { type Option } from '../../core/format/options';

export { labelOf } from '../../core/format/options';

export const ENCOUNTER_TYPES: readonly Option<EncounterType>[] = [
  { value: 'outpatient', label: 'Clinic visit' },
  { value: 'inpatient', label: 'Ward stay' },
];

/** Status filters for encounter lists. `any` means no filter. */
export const ENCOUNTER_STATUS_FILTERS: readonly Option<EncounterStatus | 'any'>[] = [
  { value: 'open', label: 'Open' },
  { value: 'closed', label: 'Closed' },
  { value: 'any', label: 'All' },
];

/** "Dr. Jose Santos" */
export function formatPhysician(
  physician: Pick<EncounterPhysicianRef, 'firstName' | 'lastName'>,
): string {
  return `Dr. ${physician.firstName} ${physician.lastName}`;
}
