import { type DischargeDisposition } from '../../api/models/discharge-disposition';
import { type EncounterPhysicianRef } from '../../api/models/encounter-physician-ref';
import { type EncounterStatus } from '../../api/models/encounter-status';
import { type EncounterType } from '../../api/models/encounter-type';
import { type NoteType } from '../../api/models/note-type';
import { type MedicationOutcome } from '../../api/models/medication-outcome';
import { type OrderType } from '../../api/models/order-type';
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

/** Kinds of note staff write from the Notes tab. Discharge summaries come from discharging. */
export const NOTE_TYPES: readonly Option<NoteType>[] = [
  { value: 'assessment', label: 'Assessment' },
  { value: 'progress', label: 'Progress' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'diet', label: 'Diet' },
];

export const ALL_NOTE_TYPES: readonly Option<NoteType>[] = [
  ...NOTE_TYPES,
  { value: 'discharge_summary', label: 'Discharge summary' },
];

/** How a ward stay ended, chosen when discharging. */
export const DISCHARGE_DISPOSITIONS: readonly Option<NonNullable<DischargeDisposition>>[] = [
  { value: 'home', label: 'Home' },
  { value: 'transferred_out', label: 'Transferred to another facility' },
  { value: 'against_advice', label: 'Left against medical advice' },
  { value: 'expired', label: 'Died' },
];

/** Kinds of physician order, in the order the form offers them. */
export const ORDER_TYPES: readonly Option<OrderType>[] = [
  { value: 'medication', label: 'Medication' },
  { value: 'laboratory', label: 'Laboratory' },
  { value: 'diet', label: 'Diet' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'other', label: 'Other' },
];

/** What happened when a dose was due, in the order the MAR form offers them. */
export const MEDICATION_OUTCOMES: readonly Option<MedicationOutcome>[] = [
  { value: 'given', label: 'Given' },
  { value: 'held', label: 'Held' },
  { value: 'refused', label: 'Refused' },
  { value: 'not_available', label: 'Not available' },
];
