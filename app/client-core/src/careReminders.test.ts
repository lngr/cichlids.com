import { describe, expect, it } from "vitest";
import type { CareKind, DueCareItem, DueStreak } from "./client.js";
import {
  MAX_SCHEDULED_REMINDERS, REMINDER_HOUR, REMINDER_MINUTE, activeSnoozes, planReminders, reconcileReminders, reminderFromSnooze, reminderId, snoozeReminder,
  type ReminderSnooze,
} from "./careReminders.js";

type State = "due_in" | "due_today" | "overdue" | "never_logged";
const item = (tankId: number, kind: CareKind, state: State, dueDate: string | null, remindersEnabled = true, streak: DueStreak | null = null): DueCareItem => ({
  tankId, tankTitle: `Tank ${tankId}`, kind, rhythmDays: 7, reason: { code: "cichlid_default", parameters: null },
  due: { state, days: 0, dueDate }, remindersEnabled, streak,
});
const running = (reminderFrom: string, deadline: string): DueStreak => ({ state: "running", reminderFrom, deadline });
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
    expect(planned).toEqual([{ id: "care-reminder:4:water_change:2026-10-18", tankId: 4, tankTitle: "Tank 4", kind: "water_change",
      dueDate: "2026-10-18", date: "2026-10-18", hour: 9, minute: 0, daysLeft: null }]);
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

  describe("water change streak", () => {
    const weekly = (state: State = "due_today", streak: DueStreak | null = running("2026-10-15", "2026-10-18"), remindersEnabled = true) =>
      item(4, "water_change", state, "2026-10-15", remindersEnabled, streak);

    it("plans one reminder per day from the first reminder day through the deadline, each with the days left", () => {
      const planned = plan([weekly("due_in")], at("2026-10-12T12:00"));
      expect(planned.map(r => [r.date, r.daysLeft, r.dueDate])).toEqual([
        ["2026-10-15", 3, "2026-10-15"], ["2026-10-16", 2, "2026-10-15"], ["2026-10-17", 1, "2026-10-15"], ["2026-10-18", 0, "2026-10-15"],
      ]);
      expect(planned.every(r => r.hour === 9 && r.minute === 0)).toBe(true);
    });
    it("gives every day of the period its own notification identifier", () => {
      const ids = plan([weekly("due_in")], at("2026-10-12T12:00")).map(r => r.id);
      expect(new Set(ids).size).toBe(4);
      expect(ids[0]).toBe("care-reminder:4:water_change:2026-10-15");
    });
    it("continues on the grace days although the care is overdue and leaves out days that have passed", () => {
      const planned = plan([weekly("overdue")], at("2026-10-16T09:30"));
      expect(planned.map(r => [r.date, r.daysLeft])).toEqual([["2026-10-17", 1], ["2026-10-18", 0]]);
    });
    it("plans nothing after the deadline day has passed", () => {
      expect(plan([weekly("overdue")], at("2026-10-18T09:00"))).toEqual([]);
    });
    it("plans nothing for a streak paused by a vacation, and nothing for a tank with reminders off", () => {
      expect(plan([weekly("due_today", { state: "paused", reminderFrom: null, deadline: null })], at("2026-10-15T06:00"))).toEqual([]);
      expect(plan([weekly("due_today", running("2026-10-15", "2026-10-18"), false)], at("2026-10-15T06:00"))).toEqual([]);
    });
    it("ignores a snooze of the rule", () => {
      const snooze: ReminderSnooze = { tankId: 4, kind: "water_change", dueDate: "2026-10-15", until: "2026-10-16" };
      expect(plan([weekly("overdue")], at("2026-10-16T10:00"), 0, [snooze]).map(r => r.date)).toEqual(["2026-10-17", "2026-10-18"]);
    });
    it("leaves other care kinds with the single reminder on the due day", () => {
      const planned = plan([item(4, "measurement", "due_in", "2026-10-18", true, running("2026-10-15", "2026-10-18"))], at("2026-10-12T12:00"));
      expect(planned.map(r => [r.date, r.daysLeft])).toEqual([["2026-10-18", null]]);
    });
    it("keeps the nearest reminders of all tanks within the platform limit", () => {
      const items = Array.from({ length: 20 }, (_, index) => item(index + 1, "water_change", "due_in", "2026-10-20", true, running("2026-10-20", "2026-10-30")));
      const planned = plan(items, at("2026-10-12T12:00"));
      expect(planned).toHaveLength(MAX_SCHEDULED_REMINDERS);
      expect(planned.map(r => r.date)).toEqual([...planned.map(r => r.date)].sort());
    });
    it("counts the days by the calendar of the member", () => {
      const planned = plan([weekly("due_in")], at("2026-10-14T20:00", 120), 120);
      expect(planned.map(r => r.daysLeft)).toEqual([3, 2, 1, 0]);
    });
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
      expect(reminderFromSnooze({ tankTitle: "Mbuna" }, snooze("2026-10-15", "2026-10-16"))).toEqual({ id: "care-reminder:4:water_change:2026-10-16", tankId: 4,
        tankTitle: "Mbuna", kind: "water_change", dueDate: "2026-10-15", date: "2026-10-16", hour: 9, minute: 0, daysLeft: null });
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
  const day = "2026-10-18";
  it("cancels scheduled reminders that are no longer planned and schedules every planned one", () => {
    const result = reconcileReminders(planned, [reminderId(4, "water_change", day), reminderId(9, "water_change", day), reminderId(9, "measurement", day)]);
    expect(result.cancel.sort()).toEqual([reminderId(9, "measurement", day), reminderId(9, "water_change", day)]);
    expect(result.schedule.map(r => r.tankId)).toEqual([4, 5]);
  });
  it("cancels the reminders of the days of a period that the streak no longer plans", () => {
    const scheduled = ["2026-10-15", "2026-10-16", "2026-10-17", "2026-10-18"].map(date => reminderId(4, "water_change", date));
    const streak = plan([item(4, "water_change", "overdue", "2026-10-15", true, running("2026-10-15", "2026-10-18"))], at("2026-10-12T12:00"));
    expect(reconcileReminders(streak.slice(2), scheduled).cancel).toEqual(scheduled.slice(0, 2));
    expect(reconcileReminders([], scheduled).cancel).toEqual(scheduled);
  });
  it("cancels everything when nothing is planned", () => {
    expect(reconcileReminders([], [reminderId(4, "water_change", day)])).toEqual({ cancel: [reminderId(4, "water_change", day)], schedule: [] });
  });
  it("leaves identifiers of other notifications alone", () => {
    expect(reconcileReminders([], ["other:1"]).cancel).toEqual([]);
  });
});
