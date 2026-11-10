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
