export function localCalendarDate(date = new Date()): string {
  return `${String(date.getFullYear()).padStart(4, "0")}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

/** The member's local calendar date and the offset of the clock from UTC in minutes, as the care plan endpoints expect them. */
export function localTime(date = new Date()): { date: string; utcOffsetMinutes: number } {
  return { date: localCalendarDate(date), utcOffsetMinutes: -date.getTimezoneOffset() || 0 };
}
