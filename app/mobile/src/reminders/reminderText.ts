import type { CareKind } from "@cichlids/client-core";

/** Translates a catalog key; count selects the plural form. */
export type Translate = (key: string, options?: { count: number }) => string;

/**
 * The body of a care reminder: the day of the care for a single reminder, and for a reminder of a
 * running water change streak the days until the streak breaks, with the hint to enter a vacation.
 */
export function reminderBody(reminder: { kind: CareKind; daysLeft: number | null }, t: Translate): string {
  if (reminder.daysLeft === null) return t(`care.reminders.notification.${reminder.kind}`);
  return reminder.daysLeft === 0
    ? t("care.reminders.notification.streakToday")
    : t("care.reminders.notification.streakBreaks", { count: reminder.daysLeft });
}
