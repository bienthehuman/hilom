import { type EmploymentType } from '../../../api/models/employment-type';
import { type ProfessionalType } from '../../../api/models/professional-type';
import { type Sex } from '../../../api/models/sex';

export interface Option<T extends string> {
  value: T;
  label: string;
}

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

/** The label for a value, or the value itself if it's unknown (e.g. a new server value). */
export function labelOf<T extends string>(options: readonly Option<T>[], value: string): string {
  return options.find((option) => option.value === value)?.label ?? value;
}

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
