import { type AllergyCategory } from '../../api/models/allergy-category';
import { type AllergySeverity } from '../../api/models/allergy-severity';
import { type CivilStatus } from '../../api/models/civil-status';
import { type Sex } from '../../api/models/sex';
import { type Option } from '../../core/format/options';

export { labelOf } from '../../core/format/options';

export const SEXES: readonly Option<Sex>[] = [
  { value: 'female', label: 'Female' },
  { value: 'male', label: 'Male' },
];

export const CIVIL_STATUSES: readonly Option<NonNullable<CivilStatus>>[] = [
  { value: 'single', label: 'Single' },
  { value: 'married', label: 'Married' },
  { value: 'widowed', label: 'Widowed' },
  { value: 'separated', label: 'Separated' },
  { value: 'annulled', label: 'Annulled' },
];

export const ALLERGY_CATEGORIES: readonly Option<AllergyCategory>[] = [
  { value: 'drug', label: 'Drug' },
  { value: 'food', label: 'Food' },
  { value: 'environmental', label: 'Environmental' },
  { value: 'other', label: 'Other' },
];

export const ALLERGY_SEVERITIES: readonly Option<AllergySeverity>[] = [
  { value: 'mild', label: 'Mild' },
  { value: 'moderate', label: 'Moderate' },
  { value: 'severe', label: 'Severe' },
];

interface PersonName {
  firstName: string;
  middleName?: string | null;
  lastName: string;
}

/** "Dela Cruz, Juan Santos": last name first, as on hospital records. Same as the patient header. */
export function formatPatientName(person: PersonName): string {
  return `${person.lastName}, ${[person.firstName, person.middleName].filter(Boolean).join(' ')}`;
}
