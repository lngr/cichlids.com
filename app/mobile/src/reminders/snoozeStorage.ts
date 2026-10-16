import type { ReminderSnooze } from "@cichlids/client-core";

const DATE = /^\d{4}-\d{2}-\d{2}$/;

function isSnooze(value: unknown): value is ReminderSnooze {
  if (typeof value !== "object" || value === null) return false;
  const { tankId, kind, dueDate, until } = value as Record<string, unknown>;
  return typeof tankId === "number" && Number.isInteger(tankId) && (kind === "water_change" || kind === "measurement")
    && typeof dueDate === "string" && DATE.test(dueDate) && typeof until === "string" && DATE.test(until);
}

export function serializeSnoozes(snoozes: ReminderSnooze[]): string {
  return JSON.stringify(snoozes);
}

/** The snoozes in stored text; text that is missing or damaged yields none, and entries of another shape are skipped. */
export function parseSnoozes(text: string | null): ReminderSnooze[] {
  if (!text) return [];
  try {
    const parsed: unknown = JSON.parse(text);
    return Array.isArray(parsed) ? parsed.filter(isSnooze) : [];
  } catch {
    return [];
  }
}
