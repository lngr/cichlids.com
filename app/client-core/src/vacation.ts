const LOCAL_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

const isCalendarDate = (text: string): boolean => {
  const match = LOCAL_DATE.exec(text);
  if (!match) return false;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
};

/**
 * Checks the first and the last day a member typed for a vacation recorded afterwards, both as
 * YYYY-MM-DD: ok, invalid (not a calendar date), order (the last day lies before the first) or
 * future (a day after today). The text of the dates is trimmed.
 */
export function checkVacationDates(start: string, end: string, today: string): "ok" | "invalid" | "order" | "future" {
  const first = start.trim();
  const last = end.trim();
  if (!isCalendarDate(first) || !isCalendarDate(last)) return "invalid";
  if (first > today || last > today) return "future";
  return last < first ? "order" : "ok";
}
