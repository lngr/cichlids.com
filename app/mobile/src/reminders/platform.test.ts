import { beforeEach, describe, expect, it, vi } from "vitest";
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
vi.mock("i18next", () => ({ default: { t: (key: string) => key } }));
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

const item = (tankId: number, dueDate: string, remindersEnabled = true): DueCareItem => ({
  tankId, tankTitle: `Tank ${tankId}`, kind: "water_change", rhythmDays: 7, reason: { code: "cichlid_default", parameters: null },
  due: { state: "due_in", days: 3, dueDate }, remindersEnabled,
});
const NOW = new Date(2026, 9, 15, 12, 0, 0);

beforeEach(() => {
  mocks.store.clear();
  mocks.permission = { status: "granted" };
  mocks.scheduled = [];
  mocks.lastResponse = null;
  mocks.scheduleNotificationAsync.mockClear();
  mocks.cancelScheduledNotificationAsync.mockClear();
});

describe("syncReminders", () => {
  it("schedules one notification per planned reminder at 09:00 local time with the tank and the kind", async () => {
    await platform.syncReminders([item(4, "2026-10-18")], NOW);
    expect(mocks.scheduleNotificationAsync).toHaveBeenCalledTimes(1);
    expect(mocks.scheduleNotificationAsync).toHaveBeenCalledWith(expect.objectContaining({
      identifier: "care-reminder:4:water_change",
      content: expect.objectContaining({ title: "Tank 4", body: "care.reminders.notification.water_change",
        data: { tankId: 4, tankTitle: "Tank 4", kind: "water_change", dueDate: "2026-10-18" }, categoryIdentifier: "care-reminder" }),
      trigger: expect.objectContaining({ type: "date", date: new Date(2026, 9, 18, 9, 0) }),
    }));
  });

  it("cancels reminders of tanks that are switched off or gone and leaves other notifications alone", async () => {
    mocks.scheduled = [{ identifier: "care-reminder:4:water_change" }, { identifier: "care-reminder:9:water_change" }, { identifier: "other" }];
    await platform.syncReminders([item(4, "2026-10-18", false)], NOW);
    expect(mocks.cancelScheduledNotificationAsync.mock.calls.map(call => call[0]).sort())
      .toEqual(["care-reminder:4:water_change", "care-reminder:9:water_change"]);
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
  });

  it("keeps no reminder scheduled without the permission", async () => {
    mocks.permission = { status: "denied" };
    mocks.scheduled = [{ identifier: "care-reminder:4:water_change" }];
    await platform.syncReminders([item(4, "2026-10-18")], NOW);
    expect(mocks.cancelScheduledNotificationAsync).toHaveBeenCalledWith("care-reminder:4:water_change");
    expect(mocks.scheduleNotificationAsync).not.toHaveBeenCalled();
  });
});

describe("reminder responses", () => {
  const response = (actionIdentifier: string) => ({
    actionIdentifier,
    notification: { request: { content: { data: { tankId: 4, tankTitle: "Tank 4", kind: "water_change", dueDate: "2026-10-15" } } } },
  });
  const flush = () => new Promise(resolve => setTimeout(resolve, 0));

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
    const open = vi.fn();
    platform.startReminderListeners(open);
    mocks.listener.current?.(response("snooze"));
    await flush();
    expect(open).not.toHaveBeenCalled();
    const call = mocks.scheduleNotificationAsync.mock.calls.at(-1) as unknown as [{ identifier: string; trigger: { date: Date } }];
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    expect(call[0].identifier).toBe("care-reminder:4:water_change");
    expect(call[0].trigger.date).toEqual(new Date(tomorrow.getFullYear(), tomorrow.getMonth(), tomorrow.getDate(), 9, 0));
    expect(JSON.parse(mocks.store.get("cichlids.careReminderSnoozes")!)).toHaveLength(1);
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
    mocks.scheduled = [{ identifier: "care-reminder:4:water_change" }, { identifier: "other" }];
    await platform.clearReminders();
    expect(mocks.cancelScheduledNotificationAsync).toHaveBeenCalledTimes(1);
    expect(mocks.store.has("cichlids.careReminderSnoozes")).toBe(false);
  });
});
