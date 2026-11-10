/** Hospital time zone. Data is stored in UTC; everything is shown in this zone (docs/DESIGN_SYSTEM.md). */
export const HOSPITAL_TIME_ZONE = 'Asia/Manila';

const dateTime = new Intl.DateTimeFormat('en-PH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  hourCycle: 'h23',
  timeZone: HOSPITAL_TIME_ZONE,
});

/** e.g. "Nov 10, 2026, 08:30". Returns the fallback for missing values. */
export function formatDateTime(iso: string | null | undefined, fallback = '—'): string {
  return iso ? dateTime.format(new Date(iso)) : fallback;
}

const dateOnly = new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeZone: 'UTC' });

/**
 * A calendar date from the API (`2026-11-09`), e.g. "Nov 9, 2026". Pure dates have no time zone,
 * so they're formatted as-is and never shifted.
 */
export function formatDate(date: string | null | undefined, fallback = '—'): string {
  return date ? dateOnly.format(new Date(`${date}T00:00:00Z`)) : fallback;
}
