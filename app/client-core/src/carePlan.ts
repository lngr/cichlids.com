import type { CareDue, DueCareItem } from "./client.js";

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

/** Days ahead that care still counts as due on Today. */
export const TODAY_DUE_HORIZON_DAYS = 2;

const dueRank = (due: CareDue) => due.state === "overdue" ? 0 : due.state === "due_today" ? 1 : 2;

/**
 * The care Today shows: overdue care (longest first), care due today, then care due within the
 * next two days (nearest first). Care due later and rules without an entry yet are left out.
 * Items that rank equally keep their order.
 */
export function dueCareForToday(items: DueCareItem[]): DueCareItem[] {
  return items
    .filter(({ due }) => due.state === "overdue" || due.state === "due_today" || (due.state === "due_in" && due.days <= TODAY_DUE_HORIZON_DAYS))
    .map((item, index) => ({ item, index }))
    .sort((a, b) => dueRank(a.item.due) - dueRank(b.item.due)
      || (a.item.due.state === "overdue" ? b.item.due.days - a.item.due.days : a.item.due.days - b.item.due.days)
      || a.index - b.index)
    .map(({ item }) => item);
}
