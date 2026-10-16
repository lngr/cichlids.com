import type { CareKind } from "@cichlids/client-core";

/** What a reminder notification carries so the app can open the care view and snooze it without the server. */
export type ReminderData = { tankId: number; tankTitle: string; kind: CareKind; dueDate: string; daysLeft: number | null };

export function reminderData({ tankId, tankTitle, kind, dueDate, daysLeft }: ReminderData): Record<string, unknown> {
  return { tankId, tankTitle, kind, dueDate, daysLeft };
}

/** The reminder in the data of a notification; null for any other notification. */
export function parseReminderData(data: Record<string, unknown> | undefined): ReminderData | null {
  if (!data) return null;
  const { tankId, tankTitle, kind, dueDate, daysLeft } = data;
  if (typeof tankId !== "number" || !Number.isInteger(tankId) || typeof tankTitle !== "string" || typeof dueDate !== "string") return null;
  return kind === "water_change" || kind === "measurement" ? { tankId, tankTitle, kind, dueDate, daysLeft: typeof daysLeft === "number" ? daysLeft : null } : null;
}

/** The path of the care view of a tank. */
export function careViewPath(tankId: number): string {
  return `/care?tank=${tankId}`;
}
