import { describe, expect, it } from "vitest";
import type { CareKind, DueCareItem } from "./client.js";
import {
  MAX_SCHEDULED_REMINDERS, REMINDER_HOUR, REMINDER_MINUTE, activeSnoozes, planReminders, reconcileReminders, reminderFromSnooze, reminderId, snoozeReminder,
  type ReminderSnooze,
} from "./careReminders.js";

type State = "due_in" | "due_today" | "overdue" | "never_logged";
const item = (tankId: number, kind: CareKind, state: State, dueDate: string | null, remindersEnabled = true): DueCareItem => ({
  tankId, tankTitle: `Tank ${tankId}`, kind, rhythmDays: 7, reason: { code: "cichlid_default", parameters: null },
  due: { state, days: 0, dueDate }, remindersEnabled,
});
/** The instant at which the clock of a member at the given offset from UTC shows the given local time. */
const at = (local: string, utcOffsetMinutes = 0) => new Date(Date.parse(`${local}:00Z`) - utcOffsetMinutes * 60_000);
const plan = (items: DueCareItem[], now: Date, utcOffsetMinutes = 0, snoozes: ReminderSnooze[] = []) =>
  planReminders({ items, snoozes, now, utcOffsetMinutes });

describe("planReminders", () => {
  it("states the reminder time in one constant", () => {
    expect([REMINDER_HOUR, REMINDER_MINUTE]).toEqual([9, 0]);
  });

  it("schedules a reminder at 09:00 local time on the due day", () => {
    const planned = plan([item(4, "water_change", "due_in", "2026-10-18")], at("2026-10-15T12:00"));
    expect(planned).toEqual([{ id: "care-reminder:4:water_change", tankId: 4, tankTitle: "Tank 4", kind: "water_change",
      dueDate: "2026-10-18", date: "2026-10-18", hour: 9, minute: 0 }]);
  });

  it("schedules care that is due today while 09:00 is still ahead and skips it afterwards", () => {
    const items = [item(4, "water_change", "due_today", "2026-10-15")];
    expect(plan(items, at("2026-10-15T07:30")).map(r => r.date)).toEqual(["2026-10-15"]);
    expect(plan(items, at("2026-10-15T09:00"))).toEqual([]);
    expect(plan(items, at("2026-10-15T18:00"))).toEqual([]);
  });

  it("does not repeat overdue care", () => {
    expect(plan([item(4, "water_change", "overdue", "2026-10-13")], at("2026-10-15T06:00"))).toEqual([]);
  });

  it("gives never logged rules no reminder", () => {
    expect(plan([item(4, "water_change", "never_logged", null), item(4, "measurement", "never_logged", null)], at("2026-10-15T06:00"))).toEqual([]);
  });

  it("gives tanks with reminders switched off no reminder", () => {
    expect(plan([item(4, "water_change", "due_in", "2026-10-18", false), item(5, "water_change", "due_in", "2026-10-18")], at("2026-10-15T06:00"))
      .map(r => r.tankId)).toEqual([5]);
  });

  it("plans one reminder per rule, ordered by day, tank and kind", () => {
    const planned = plan([
      item(7, "water_change", "due_in", "2026-10-19"), item(5, "measurement", "due_in", "2026-10-17"), item(5, "water_change", "due_in", "2026-10-17"),
      item(3, "water_change", "due_in", "2026-10-17"),
    ], at("2026-10-15T06:00"));
    expect(planned.map(r => `${r.date} ${r.tankId} ${r.kind}`)).toEqual([
      "2026-10-17 3 water_change", "2026-10-17 5 measurement", "2026-10-17 5 water_change", "2026-10-19 7 water_change",
    ]);
  });

  it("keeps the nearest reminders when the platform limit is exceeded", () => {
    const items = Array.from({ length: MAX_SCHEDULED_REMINDERS + 5 }, (_, index) =>
      item(index + 1, "water_change", "due_in", `2026-11-${String(1 + (index % 28)).padStart(2, "0")}`));
    const planned = plan(items, at("2026-10-15T06:00"));
    expect(planned).toHaveLength(MAX_SCHEDULED_REMINDERS);
    expect(planned.map(r => r.date)).toEqual([...planned.map(r => r.date)].sort());
  });

  describe("time zone", () => {
    const items = [item(4, "water_change", "due_today", "2026-10-15")];
    it("decides whether 09:00 is ahead by the clock of the member", () => {
      const now = at("2026-10-15T08:00", 0);
      expect(plan(items, now, 0).map(r => r.date)).toEqual(["2026-10-15"]);
      expect(plan(items, now, 120)).toEqual([]);
      expect(plan(items, now, -300).map(r => r.date)).toEqual(["2026-10-15"]);
    });
    it("keeps the wall-clock time of the reminder whatever the offset", () => {
      const planned = plan([item(4, "water_change", "due_in", "2026-10-20")], at("2026-10-15T08:00", 540), 540);
      expect(planned.map(r => [r.date, r.hour, r.minute])).toEqual([["2026-10-20", 9, 0]]);
    });
  });

  describe("snooze", () => {
    const snooze = (dueDate: string, until: string): ReminderSnooze => ({ tankId: 4, kind: "water_change", dueDate, until });
    it("moves the reminder to 09:00 the next day", () => {
      const now = at("2026-10-15T09:00");
      expect(snoozeReminder({ tankId: 4, kind: "water_change", dueDate: "2026-10-15" }, now, 0)).toEqual(snooze("2026-10-15", "2026-10-16"));
    });
    it("takes the next day of the member, not of UTC", () => {
      const now = at("2026-10-15T23:30", 120);
      expect(snoozeReminder({ tankId: 4, kind: "water_change", dueDate: "2026-10-15" }, now, 120).until).toBe("2026-10-16");
      expect(snoozeReminder({ tankId: 4, kind: "water_change", dueDate: "2026-10-15" }, at("2026-10-15T23:30", -300), -300).until).toBe("2026-10-16");
    });
    it("keeps a snoozed reminder after the due day although the care is overdue", () => {
      const planned = plan([item(4, "water_change", "overdue", "2026-10-15")], at("2026-10-15T10:00"), 0, [snooze("2026-10-15", "2026-10-16")]);
      expect(planned.map(r => [r.date, r.dueDate])).toEqual([["2026-10-16", "2026-10-15"]]);
    });
    it("replaces the day of the due date for care that is due today", () => {
      const planned = plan([item(4, "water_change", "due_today", "2026-10-15")], at("2026-10-15T07:00"), 0, [snooze("2026-10-15", "2026-10-16")]);
      expect(planned.map(r => r.date)).toEqual(["2026-10-16"]);
    });
    it("ignores a snooze of an earlier due date after the care was logged again", () => {
      const planned = plan([item(4, "water_change", "due_in", "2026-10-22")], at("2026-10-16T10:00"), 0, [snooze("2026-10-15", "2026-10-16")]);
      expect(planned.map(r => r.date)).toEqual(["2026-10-22"]);
    });
    it("plans nothing for a snoozed reminder whose 09:00 has passed, for a switched off tank or for never logged care", () => {
      const snoozes = [snooze("2026-10-15", "2026-10-16")];
      expect(plan([item(4, "water_change", "overdue", "2026-10-15")], at("2026-10-16T09:30"), 0, snoozes)).toEqual([]);
      expect(plan([item(4, "water_change", "overdue", "2026-10-15", false)], at("2026-10-16T06:00"), 0, snoozes)).toEqual([]);
      expect(plan([item(4, "water_change", "never_logged", null)], at("2026-10-16T06:00"), 0, snoozes)).toEqual([]);
    });
    it("turns a snooze into the reminder of its day at 09:00", () => {
      expect(reminderFromSnooze({ tankTitle: "Mbuna" }, snooze("2026-10-15", "2026-10-16"))).toEqual({ id: "care-reminder:4:water_change", tankId: 4,
        tankTitle: "Mbuna", kind: "water_change", dueDate: "2026-10-15", date: "2026-10-16", hour: 9, minute: 0 });
    });
    it("drops snoozes whose day is over", () => {
      const snoozes = [snooze("2026-10-14", "2026-10-15"), snooze("2026-10-15", "2026-10-16")];
      expect(activeSnoozes(snoozes, at("2026-10-16T08:00"), 0)).toEqual([snooze("2026-10-15", "2026-10-16")]);
      expect(activeSnoozes(snoozes, at("2026-10-17T08:00"), 0)).toEqual([]);
    });
  });
});

describe("reconcileReminders", () => {
  const planned = plan([item(4, "water_change", "due_in", "2026-10-18"), item(5, "water_change", "due_in", "2026-10-19")], at("2026-10-15T06:00"));
  it("cancels scheduled reminders that are no longer planned and schedules every planned one", () => {
    const result = reconcileReminders(planned, [reminderId(4, "water_change"), reminderId(9, "water_change"), reminderId(9, "measurement")]);
    expect(result.cancel.sort()).toEqual([reminderId(9, "measurement"), reminderId(9, "water_change")]);
    expect(result.schedule.map(r => r.tankId)).toEqual([4, 5]);
  });
  it("cancels everything when nothing is planned", () => {
    expect(reconcileReminders([], [reminderId(4, "water_change")])).toEqual({ cancel: [reminderId(4, "water_change")], schedule: [] });
  });
  it("leaves identifiers of other notifications alone", () => {
    expect(reconcileReminders([], ["other:1"]).cancel).toEqual([]);
  });
});
