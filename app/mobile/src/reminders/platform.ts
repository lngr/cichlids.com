import { Linking } from "react-native";
import i18next from "i18next";
import * as Notifications from "expo-notifications";
import {
  activeSnoozes, planReminders, reconcileReminders, reminderFromSnooze, snoozeReminder,
  type DueCareItem, type PlannedReminder, type ReminderSnooze,
} from "@cichlids/client-core";
import { localStore } from "../lib/localStore";
import { localTime } from "../today/localDate";
import { parseReminderData, reminderData, careViewPath } from "./notificationData";
import { reminderBody } from "./reminderText";
import type { NotificationPermission } from "./permissionPrompt";
import { parseSnoozes, serializeSnoozes } from "./snoozeStorage";

/** True where the platform schedules local notifications. */
export const remindersSupported = true;

const SNOOZE_STORAGE_KEY = "cichlids.careReminderSnoozes";
const CHANNEL_ID = "care-reminders";
const CATEGORY_ID = "care-reminder";
const SNOOZE_ACTION_ID = "snooze";

Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldShowBanner: true, shouldShowList: true, shouldPlaySound: false, shouldSetBadge: false }),
});

async function prepareDelivery(): Promise<void> {
  await Notifications.setNotificationChannelAsync(CHANNEL_ID, {
    name: i18next.t("care.reminders.channel"),
    importance: Notifications.AndroidImportance.DEFAULT,
  });
  await Notifications.setNotificationCategoryAsync(CATEGORY_ID, [{
    identifier: SNOOZE_ACTION_ID,
    buttonTitle: i18next.t("care.reminders.snooze"),
    options: { opensAppToForeground: false },
  }]);
}

export async function readPermission(): Promise<NotificationPermission> {
  return (await Notifications.getPermissionsAsync()).status;
}

/** Asks the system for the permission; the caller has explained the reminders before. */
export async function requestPermission(): Promise<NotificationPermission> {
  await prepareDelivery();
  return (await Notifications.requestPermissionsAsync()).status;
}

export function openSystemSettings(): void {
  void Linking.openSettings();
}

async function readSnoozes(now: Date): Promise<ReminderSnooze[]> {
  const stored = parseSnoozes(await localStore.getItem(SNOOZE_STORAGE_KEY));
  const active = activeSnoozes(stored, now, localTime(now).utcOffsetMinutes);
  if (active.length !== stored.length) await writeSnoozes(active);
  return active;
}

async function writeSnoozes(snoozes: ReminderSnooze[]): Promise<void> {
  if (snoozes.length === 0) await localStore.removeItem(SNOOZE_STORAGE_KEY);
  else await localStore.setItem(SNOOZE_STORAGE_KEY, serializeSnoozes(snoozes));
}

async function schedule(reminder: PlannedReminder): Promise<void> {
  const [year, month, day] = reminder.date.split("-").map(Number);
  await Notifications.scheduleNotificationAsync({
    identifier: reminder.id,
    content: {
      title: reminder.tankTitle,
      body: reminderBody(reminder, (key, options) => i18next.t(key, options)),
      data: reminderData(reminder),
      // The snooze dismisses the reminder of today; on the last day of a running streak no reminder follows.
      ...(reminder.daysLeft === 0 ? {} : { categoryIdentifier: CATEGORY_ID }),
    },
    trigger: { type: Notifications.SchedulableTriggerInputTypes.DATE, date: new Date(year, month - 1, day, reminder.hour, reminder.minute), channelId: CHANNEL_ID },
  });
}

/**
 * Makes the scheduled notifications match the due care: cancels the reminders that are no longer
 * planned and schedules every planned one. Without the permission no reminder stays scheduled.
 */
export async function syncReminders(items: DueCareItem[], now = new Date()): Promise<void> {
  const scheduled = (await Notifications.getAllScheduledNotificationsAsync()).map(request => request.identifier);
  const granted = (await readPermission()) === "granted";
  const planned = granted ? planReminders({ items, snoozes: await readSnoozes(now), now, utcOffsetMinutes: localTime(now).utcOffsetMinutes }) : [];
  const { cancel, schedule: toSchedule } = reconcileReminders(planned, scheduled);
  for (const id of cancel) await Notifications.cancelScheduledNotificationAsync(id);
  if (toSchedule.length === 0) return;
  await prepareDelivery();
  for (const reminder of toSchedule) await schedule(reminder);
}

/** Cancels every scheduled reminder and forgets the snoozes, for a member who signs out. */
export async function clearReminders(): Promise<void> {
  const scheduled = await Notifications.getAllScheduledNotificationsAsync();
  const { cancel } = reconcileReminders([], scheduled.map(request => request.identifier));
  for (const id of cancel) await Notifications.cancelScheduledNotificationAsync(id);
  await writeSnoozes([]);
}

async function handleResponse(response: Notifications.NotificationResponse, open: (path: string) => void): Promise<void> {
  const reminder = parseReminderData(response.notification.request.content.data);
  if (!reminder) return;
  if (response.actionIdentifier === SNOOZE_ACTION_ID && reminder.daysLeft !== null) {
    return;
  } else if (response.actionIdentifier === SNOOZE_ACTION_ID) {
    const now = new Date();
    const snooze = snoozeReminder(reminder, now, localTime(now).utcOffsetMinutes);
    const others = (await readSnoozes(now)).filter(s => !(s.tankId === snooze.tankId && s.kind === snooze.kind));
    await writeSnoozes([...others, snooze]);
    await prepareDelivery();
    await schedule(reminderFromSnooze(reminder, snooze));
  } else if (response.actionIdentifier === Notifications.DEFAULT_ACTION_IDENTIFIER) {
    open(careViewPath(reminder.tankId));
  }
}

/**
 * Reacts to the taps and the snooze action of reminder notifications, including the one that
 * started the app. Returns the function that stops listening.
 */
export function startReminderListeners(open: (path: string) => void): () => void {
  const handle = (response: Notifications.NotificationResponse) => {
    void handleResponse(response, open).catch(() => {}).then(() => Notifications.clearLastNotificationResponseAsync());
  };
  const subscription = Notifications.addNotificationResponseReceivedListener(handle);
  void Notifications.getLastNotificationResponseAsync().then(response => { if (response) handle(response); });
  return () => subscription.remove();
}

