import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DueCareItem } from "@cichlids/client-core";

const mocks = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    store,
    permission: { status: "granted" },
    scheduled: [] as { identifier: string }[],
    scheduleNotificationAsync: vi.fn(async (_request: unknown) => "id"),
    cancelScheduledNotificationAsync: vi.fn(async (_id: string) => {}),
    listener: { current: null as null | ((response: unknown) => void) },
    lastResponse: null as unknown,
  };
});

vi.mock("react-native", () => ({ Linking: { openSettings: vi.fn() } }));
vi.mock("i18next", () => ({ default: { t: (key: string, options?: { count: number }) => options ? `${key}:${options.count}` : key } }));
vi.mock("../lib/localStore", () => ({
  localStore: {
    getItem: async (key: string) => mocks.store.get(key) ?? null,
    setItem: async (key: string, value: string) => void mocks.store.set(key, value),
    removeItem: async (key: string) => void mocks.store.delete(key),
  },
}));
vi.mock("expo-notifications", () => ({
  setNotificationHandler: vi.fn(),
  setNotificationChannelAsync: vi.fn(async () => null),
  setNotificationCategoryAsync: vi.fn(async () => ({})),
  AndroidImportance: { DEFAULT: 3 },
  SchedulableTriggerInputTypes: { DATE: "date" },
  DEFAULT_ACTION_IDENTIFIER: "expo.modules.notifications.actions.DEFAULT",
  getPermissionsAsync: vi.fn(async () => mocks.permission),
  requestPermissionsAsync: vi.fn(async () => mocks.permission),
  getAllScheduledNotificationsAsync: vi.fn(async () => mocks.scheduled),
  scheduleNotificationAsync: mocks.scheduleNotificationAsync,
  cancelScheduledNotificationAsync: mocks.cancelScheduledNotificationAsync,
  addNotificationResponseReceivedListener: vi.fn((listener: (response: unknown) => void) => {
    mocks.listener.current = listener;
    return { remove: vi.fn() };
  }),
  getLastNotificationResponseAsync: vi.fn(async () => mocks.lastResponse),
  clearLastNotificationResponseAsync: vi.fn(async () => {}),
}));

const platform = await import("./platform");
const Notifications = await import("expo-notifications");

const item = (tankId: number, dueDate: string, remindersEnabled = true, streak: DueCareItem["streak"] = null): DueCareItem => ({
  tankId, tankTitle: `Tank ${tankId}`, kind: "water_change", rhythmDays: 7, reason: { code: "cichlid_default", parameters: null },
  due: { state: "due_in", days: 3, dueDate }, remindersEnabled, streak,
});
const NOW = new Date(2026, 9, 15, 12, 0, 0);

beforeEach(() => {
  mocks.store.clear();
  mocks.permission = { status: "granted" };
  mocks.scheduled = [];
  mocks.lastResponse = null;
  mocks.scheduleNotificationAsync.mockClear();
  mocks.cancelScheduledNotificationAsync.mockClear();
  vi.mocked(Notifications.clearLastNotificationResponseAsync).mockClear();
});

describe("syncReminders", () => {
  it("schedules one notification per planned reminder at 09:00 local time with the tank and the kind", async () => {
    await platform.syncReminders([item(4, "2026-10-18")], NOW);
    expect(mocks.scheduleNotificationAsync).toHaveBeenCalledTimes(1);
    expect(mocks.scheduleNotificationAsync).toHaveBeenCalledWith(expect.objectContaining({
      identifier: "care-reminder:4:water_change:2026-10-18",
      content: expect.objectContaining({ title: "Tank 4", body: "care.reminders.notification.water_change",
        data: { tankId: 4, tankTitle: "Tank 4", kind: "water_change", dueDate: "2026-10-18", daysLeft: null }, categoryIdentifier: "care-reminder" }),
      trigger: expect.objectContaining({ type: "date", date: new Date(2026, 9, 18, 9, 0) }),
    }));
  });

  it("schedules one notification per day of a running streak with the days left and the snooze action except on the last day", async () => {
    await platform.syncReminders([item(4, "2026-10-17", true, { state: "running", reminderFrom: "2026-10-17", deadline: "2026-10-20" })], NOW);
    const calls = mocks.scheduleNotificationAsync.mock.calls as unknown as [{ identifier: string; content: { body: string; categoryIdentifier?: string }; trigger: { date: Date } }][];
    expect(calls.map(([request]) => request.identifier)).toEqual([17, 18, 19, 20].map(day => `care-reminder:4:water_change:2026-10-${day}`));
    expect(calls.map(([request]) => request.content.body)).toEqual([
      "care.reminders.notification.streakBreaks:3", "care.reminders.notification.streakBreaks:2", "care.reminders.notification.streakBreaks:1",
      "care.reminders.notification.streakToday",
    ]);
    expect(calls.map(([request]) => request.content.categoryIdentifier)).toEqual(["care-reminder", "care-reminder", "care-reminder", undefined]);
    expect(calls.map(([request]) => request.trigger.date)).toEqual([17, 18, 19, 20].map(day => new Date(2026, 9, day, 9, 0)));
  });

  it("cancels the reminders that remain of a period once the streak plans none", async () => {
    mocks.scheduled = [17, 18, 19, 20].map(day => ({ identifier: `care-reminder:4:water_change:2026-10-${day}` }));
    await platform.syncReminders([item(4, "2026-10-17", true, { state: "paused", reminderFrom: null, deadline: null })], NOW);
    expect(mocks.cancelScheduledNotificationAsync).toHaveBeenCalledTimes(4);
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
  });

  it("cancels reminders of tanks that are switched off or gone and leaves other notifications alone", async () => {
    mocks.scheduled = [{ identifier: "care-reminder:4:water_change:2026-10-18" }, { identifier: "care-reminder:9:water_change:2026-10-18" }, { identifier: "other" }];
    await platform.syncReminders([item(4, "2026-10-18", false)], NOW);
    expect(mocks.cancelScheduledNotificationAsync.mock.calls.map(call => call[0]).sort())
      .toEqual(["care-reminder:4:water_change:2026-10-18", "care-reminder:9:water_change:2026-10-18"]);
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
  });

  it("keeps no reminder scheduled without the permission", async () => {
    mocks.permission = { status: "denied" };
    mocks.scheduled = [{ identifier: "care-reminder:4:water_change:2026-10-18" }];
    await platform.syncReminders([item(4, "2026-10-18")], NOW);
    expect(mocks.cancelScheduledNotificationAsync).toHaveBeenCalledWith("care-reminder:4:water_change:2026-10-18");
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
  });
});

describe("reminder responses", () => {
  afterEach(() => { vi.useRealTimers(); });
  const response = (actionIdentifier: string) => ({
    actionIdentifier,
    notification: { request: { content: { data: { tankId: 4, tankTitle: "Tank 4", kind: "water_change", dueDate: "2026-10-15" } } } },
  });
  // Every handled response ends with clearing the last notification response.
  const flush = () => vi.waitFor(() => expect(Notifications.clearLastNotificationResponseAsync).toHaveBeenCalledTimes(1));

  it("opens the care view of the tank when the notification is tapped", async () => {
    const open = vi.fn();
    platform.startReminderListeners(open);
    mocks.listener.current?.(response("expo.modules.notifications.actions.DEFAULT"));
    await flush();
    expect(open).toHaveBeenCalledWith("/care?tank=4");
  });

  it("opens the care view for the notification that started the app", async () => {
    const open = vi.fn();
    mocks.lastResponse = response("expo.modules.notifications.actions.DEFAULT");
    platform.startReminderListeners(open);
    await flush();
    expect(open).toHaveBeenCalledWith("/care?tank=4");
  });

  it("moves a snoozed reminder to 09:00 the next day and keeps it for the next rescheduling", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 9, 15, 23, 59, 59));
    const open = vi.fn();
    platform.startReminderListeners(open);
    mocks.listener.current?.(response("snooze"));
    await flush();
    vi.useRealTimers();
    expect(open).not.toHaveBeenCalled();
    const call = mocks.scheduleNotificationAsync.mock.calls.at(-1) as unknown as [{ identifier: string; trigger: { date: Date } }];
    expect(call[0].identifier).toBe("care-reminder:4:water_change:2026-10-16");
    expect(call[0].trigger.date).toEqual(new Date(2026, 9, 16, 9, 0));
    expect(JSON.parse(mocks.store.get("cichlids.careReminderSnoozes")!)).toHaveLength(1);
  });

  it("dismisses a streak reminder on snooze and leaves the planned reminder of the next day alone", async () => {
    const open = vi.fn();
    platform.startReminderListeners(open);
    mocks.listener.current?.({ actionIdentifier: "snooze", notification: { request: { content: { data: { tankId: 4, tankTitle: "Tank 4", kind: "water_change", dueDate: "2026-10-15", daysLeft: 2 } } } } });
    await flush();
    expect(open).not.toHaveBeenCalled();
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
    expect(mocks.store.has("cichlids.careReminderSnoozes")).toBe(false);
  });

  it("ignores notifications that are not care reminders", async () => {
    const open = vi.fn();
    platform.startReminderListeners(open);
    mocks.listener.current?.({ actionIdentifier: "snooze", notification: { request: { content: { data: {} } } } });
    await flush();
    expect(open).not.toHaveBeenCalled();
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
  });
});

describe("clearReminders", () => {
  it("cancels every care reminder and forgets the snoozes", async () => {
    mocks.store.set("cichlids.careReminderSnoozes", "[]");
    mocks.scheduled = [{ identifier: "care-reminder:4:water_change:2026-10-18" }, { identifier: "other" }];
    await platform.clearReminders();
    expect(mocks.cancelScheduledNotificationAsync).toHaveBeenCalledTimes(1);
    expect(mocks.store.has("cichlids.careReminderSnoozes")).toBe(false);
  });
});
