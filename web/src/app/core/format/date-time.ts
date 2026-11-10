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

const isoDay = new Intl.DateTimeFormat('en-CA', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  timeZone: HOSPITAL_TIME_ZONE,
});

/** Today's date in the hospital (`2026-11-09`), whatever time zone the browser is in. */
export function hospitalToday(now: Date = new Date()): string {
  return isoDay.format(now);
}

/** The calendar day before a date (`2026-11-09` → `2026-11-08`). */
export function dayBefore(date: string): string {
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() - 1);
  return day.toISOString().slice(0, 10);
}

/** "Nov 1, 2026 – Nov 30, 2026", or "From Nov 1, 2026" when open-ended. */
export function formatDateSpan(start: string, lastDay: string | null | undefined): string {
  return lastDay ? `${formatDate(start)} – ${formatDate(lastDay)}` : `From ${formatDate(start)}`;
}

/** `date` plus `days` (negative to go back), as `YYYY-MM-DD`. */
export function addDays(date: string, days: number): string {
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() + days);
  return day.toISOString().slice(0, 10);
}

/** The Monday of the week containing `date`. Hospital weeks run Monday to Sunday. */
export function weekStart(date: string): string {
  const weekday = new Date(`${date}T00:00:00Z`).getUTCDay();
  return addDays(date, -((weekday + 6) % 7));
}

/** The seven days from `monday`. */
export function weekDays(monday: string): string[] {
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}

const weekdayShort = new Intl.DateTimeFormat('en-PH', { weekday: 'short', timeZone: 'UTC' });
const weekdayLong = new Intl.DateTimeFormat('en-PH', {
  weekday: 'long',
  month: 'long',
  day: 'numeric',
  timeZone: 'UTC',
});
const monthDay = new Intl.DateTimeFormat('en-PH', {
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
});

/** "Mon" */
export function formatWeekday(date: string): string {
  return weekdayShort.format(new Date(`${date}T00:00:00Z`));
}

/** "Monday, November 9" */
export function formatLongDay(date: string): string {
  return weekdayLong.format(new Date(`${date}T00:00:00Z`));
}

/** "Nov 9" */
export function formatMonthDay(date: string): string {
  return monthDay.format(new Date(`${date}T00:00:00Z`));
}
