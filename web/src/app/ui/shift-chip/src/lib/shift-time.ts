/** "06:00:00" (API time) → "06:00". */
export function formatClock(time: string): string {
  return time.slice(0, 5);
}

/** "06:00–14:00", or "22:00–06:00 next day" for shifts that cross midnight. */
export function formatShiftHours(
  startsAt: string,
  endsAt: string,
  crossesMidnight: boolean,
): string {
  const range = `${formatClock(startsAt)}–${formatClock(endsAt)}`;
  return crossesMidnight ? `${range} next day` : range;
}

/** 480 → "8 h", 450 → "7 h 30 min", 45 → "45 min". */
export function formatShiftLength(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) {
    return `${rest} min`;
  }
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}
