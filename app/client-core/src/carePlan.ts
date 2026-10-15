import type { CareDue } from "./client.js";

/** Rhythms the plan offers as one-tap choices, in days. */
export const RHYTHM_CHOICES = [3, 5, 7, 10, 14, 21, 28];
export const MIN_RHYTHM_DAYS = 1;
export const MAX_RHYTHM_DAYS = 90;

export function isValidRhythm(days: number): boolean {
  return Number.isInteger(days) && days >= MIN_RHYTHM_DAYS && days <= MAX_RHYTHM_DAYS;
}

/** The rhythm typed by the member (whole days, 1 to 90), or null when the text is not one. */
export function parseRhythm(text: string): number | null {
  const trimmed = text.trim();
  if (!/^[0-9]+$/.test(trimmed)) return null;
  const days = Number(trimmed);
  return isValidRhythm(days) ? days : null;
}

export type DueSummary = { key: "dueIn" | "dueToday" | "overdue" | "neverLogged"; count: number };

/** The wording key of a due state with the number of days it speaks about. */
export function dueSummary(due: CareDue): DueSummary {
  switch (due.state) {
    case "due_in": return { key: "dueIn", count: due.days };
    case "due_today": return { key: "dueToday", count: 0 };
    case "overdue": return { key: "overdue", count: due.days };
    case "never_logged": return { key: "neverLogged", count: 0 };
  }
}
