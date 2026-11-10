/** Whole years, months and days between a date of birth and today (both `YYYY-MM-DD`). */
function elapsed(
  birthDate: string,
  today: string,
): { years: number; months: number; days: number } {
  const [by, bm, bd] = birthDate.split('-').map(Number) as [number, number, number];
  const [ty, tm, td] = today.split('-').map(Number) as [number, number, number];

  let months = (ty - by) * 12 + (tm - bm);
  if (td < bd) {
    months -= 1;
  }
  const birthday = Date.UTC(by, bm - 1 + months, bd);
  const days = Math.round((Date.UTC(ty, tm - 1, td) - birthday) / 86_400_000);
  return { years: Math.floor(months / 12), months, days };
}

/**
 * Age as clinicians write it: years from age 2 (`68 y`), months under 2 years (`18 mo`), days under a
 * month (`12 d`). Empty when the date of birth is after today.
 */
export function formatAge(birthDate: string, today: string): string {
  if (birthDate > today) {
    return '';
  }
  const { years, months, days } = elapsed(birthDate, today);
  if (years >= 2) {
    return `${years} y`;
  }
  return months >= 1 ? `${months} mo` : `${days} d`;
}
