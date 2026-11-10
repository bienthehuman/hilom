/** Manila has no daylight saving, so a fixed offset turns a picked date into the hospital's midnight. */
const HOSPITAL_UTC_OFFSET = '+08:00';

/** Midnight at the hospital on the given `yyyy-mm-dd` day, as an ISO instant. */
export function startOfHospitalDay(date: string): string {
  return new Date(`${date}T00:00:00${HOSPITAL_UTC_OFFSET}`).toISOString();
}

/** Midnight at the hospital after the given day, so a "to" date includes the whole day. */
export function endOfHospitalDay(date: string): string {
  const day = new Date(`${date}T00:00:00${HOSPITAL_UTC_OFFSET}`);
  day.setUTCDate(day.getUTCDate() + 1);
  return day.toISOString();
}
