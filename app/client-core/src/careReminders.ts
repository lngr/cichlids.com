import type { CareKind, DueCareItem } from "./client.js";

/** The local wall-clock time of a care reminder on its day. */
export const REMINDER_HOUR = 9;
export const REMINDER_MINUTE = 0;

/** Upper bound of reminders scheduled at once; iOS keeps at most 64 pending local notifications. */
export const MAX_SCHEDULED_REMINDERS = 60;

const ID_PREFIX = "care-reminder:";
const DAY_MS = 86_400_000;

/** A reminder moved to a later local day; it belongs to one due date of one rule. */
export type ReminderSnooze = { tankId: number; kind: CareKind; dueDate: string; until: string };

/** One local notification: the local calendar date and the wall-clock time at which it fires. */
export type PlannedReminder = {
  id: string; tankId: number; tankTitle: string; kind: CareKind; dueDate: string; date: string; hour: number; minute: number;
};

export type ReminderInput = {
  items: DueCareItem[];
  snoozes: ReminderSnooze[];
  now: Date | number;
  /** Offset of the clock of the member from UTC in minutes. */
  utcOffsetMinutes: number;
};

/** The identifier of the notification of a rule; a rule has at most one scheduled reminder. */
export function reminderId(tankId: number, kind: CareKind): string {
  return `${ID_PREFIX}${tankId}:${kind}`;
}

export function isReminderId(id: string): boolean {
  return id.startsWith(ID_PREFIX);
}

const instant = (now: Date | number) => typeof now === "number" ? now : now.getTime();

/** The wall-clock reading of an instant for a clock the given minutes ahead of UTC, as milliseconds of a UTC date. */
const wallClock = (now: Date | number, utcOffsetMinutes: number) => instant(now) + utcOffsetMinutes * 60_000;

const dateText = (wallMs: number) => new Date(wallMs).toISOString().slice(0, 10);

const reminderWallMs = (date: string) => Date.parse(`${date}T00:00:00Z`) + (REMINDER_HOUR * 60 + REMINDER_MINUTE) * 60_000;

/** The local calendar date of an instant. */
export function localDateOf(now: Date | number, utcOffsetMinutes: number): string {
  return dateText(wallClock(now, utcOffsetMinutes));
}

/**
 * The notifications to schedule: one at 09:00 local time on the due day of each rule whose tank
 * has reminders on and whose care has been logged before. Care that is overdue gets no reminder
 * unless the member snoozed it, and a reminder whose time has passed is left out. At most
 * MAX_SCHEDULED_REMINDERS are kept, the nearest first.
 */
export function planReminders({ items, snoozes, now, utcOffsetMinutes }: ReminderInput): PlannedReminder[] {
  const localNow = wallClock(now, utcOffsetMinutes);
  const planned: PlannedReminder[] = [];
  for (const item of items) {
    const dueDate = item.due.dueDate;
    if (!item.remindersEnabled || item.due.state === "never_logged" || dueDate === null) continue;
    const snooze = snoozes.find(s => s.tankId === item.tankId && s.kind === item.kind && s.dueDate === dueDate);
    const date = snooze?.until ?? (item.due.state === "overdue" ? null : dueDate);
    if (date === null || reminderWallMs(date) <= localNow) continue;
    planned.push({ id: reminderId(item.tankId, item.kind), tankId: item.tankId, tankTitle: item.tankTitle, kind: item.kind,
      dueDate, date, hour: REMINDER_HOUR, minute: REMINDER_MINUTE });
  }
  return planned
    .sort((a, b) => a.date.localeCompare(b.date) || a.tankId - b.tankId || a.kind.localeCompare(b.kind))
    .slice(0, MAX_SCHEDULED_REMINDERS);
}

/** Moves the reminder of a rule to 09:00 on the day after the local day of the instant. */
export function snoozeReminder(reminder: { tankId: number; kind: CareKind; dueDate: string }, now: Date | number, utcOffsetMinutes: number): ReminderSnooze {
  return { tankId: reminder.tankId, kind: reminder.kind, dueDate: reminder.dueDate, until: dateText(wallClock(now, utcOffsetMinutes) + DAY_MS) };
}

/** The reminder a snooze plans: the rule of the snooze at 09:00 on its day. */
export function reminderFromSnooze(tank: { tankTitle: string }, snooze: ReminderSnooze): PlannedReminder {
  return { id: reminderId(snooze.tankId, snooze.kind), tankId: snooze.tankId, tankTitle: tank.tankTitle, kind: snooze.kind,
    dueDate: snooze.dueDate, date: snooze.until, hour: REMINDER_HOUR, minute: REMINDER_MINUTE };
}

/** The snoozes whose day has not passed yet. */
export function activeSnoozes(snoozes: ReminderSnooze[], now: Date | number, utcOffsetMinutes: number): ReminderSnooze[] {
  const today = localDateOf(now, utcOffsetMinutes);
  return snoozes.filter(s => s.until >= today);
}

/**
 * The difference between the planned reminders and the scheduled notifications: the identifiers
 * of reminders to cancel and the reminders to schedule. A notification that is not a care
 * reminder is never cancelled.
 */
export function reconcileReminders(planned: PlannedReminder[], scheduledIds: string[]): { cancel: string[]; schedule: PlannedReminder[] } {
  const wanted = new Set(planned.map(r => r.id));
  return { cancel: scheduledIds.filter(id => isReminderId(id) && !wanted.has(id)), schedule: planned };
}
