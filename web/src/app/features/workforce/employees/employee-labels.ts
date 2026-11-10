import { type EmploymentType } from '../../../api/models/employment-type';
import { type ProfessionalType } from '../../../api/models/professional-type';
import { type Sex } from '../../../api/models/sex';

import { type Option } from '../../../core/format/options';

export { type Option, labelOf } from '../../../core/format/options';

export const PROFESSIONAL_TYPES: readonly Option<ProfessionalType>[] = [
  { value: 'physician', label: 'Physician' },
  { value: 'nurse', label: 'Nurse' },
  { value: 'allied_health', label: 'Allied health' },
  { value: 'administrative', label: 'Administrative' },
];

export const EMPLOYMENT_TYPES: readonly Option<EmploymentType>[] = [
  { value: 'full_time', label: 'Full time' },
  { value: 'part_time', label: 'Part time' },
  { value: 'contract', label: 'Contract' },
];

export const SEXES: readonly Option<Sex>[] = [
  { value: 'female', label: 'Female' },
  { value: 'male', label: 'Male' },
];

/** Same rule as the API: physicians and nurses need a license number. */
export function requiresLicense(type: ProfessionalType | null | undefined): boolean {
  return type === 'physician' || type === 'nurse';
}

interface PersonName {
  firstName: string;
  middleName?: string | null;
  lastName: string;
}

/** "Reyes, Ana S." as lists show it: last name first, middle initial. */
export function formatListName(person: PersonName): string {
  const initial = person.middleName ? ` ${person.middleName.trim().charAt(0)}.` : '';
  return `${person.lastName}, ${person.firstName}${initial}`;
}

/** "Ana Santos Reyes" for headings. */
export function formatFullName(person: PersonName): string {
  return [person.firstName, person.middleName, person.lastName].filter(Boolean).join(' ');
}
