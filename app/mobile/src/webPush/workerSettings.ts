import type { WebPushSubscriptionRequest } from "@cichlids/client-core";

/** The address under which the page stores the settings that the service worker reads. */
export const WORKER_SETTINGS_URL = "/__cichlids/web-push-settings";

/** The name of the cache that holds the settings of the service worker. */
export const WORKER_SETTINGS_CACHE = "cichlids-web-push";

/** The most days a water change streak can have left: the grace of the longest plan rhythm of 90 days. */
export const MAX_STREAK_DAYS_LEFT = 30;

export type WorkerSettings = {
  apiUrl: string;
  texts: {
    water_change: string; measurement: string; snooze: string;
    streakToday: string;
    /** The text for each number of days left from 1 through MAX_STREAK_DAYS_LEFT, keyed by that number. */
    streakBreaks: Record<string, string>;
  };
};

/** The API address and the notification texts in the member's language, for the service worker. */
export function workerSettings(apiUrl: string, t: (key: string, options?: { count: number }) => string): WorkerSettings {
  const streakBreaks: Record<string, string> = {};
  for (let days = 1; days <= MAX_STREAK_DAYS_LEFT; days++) streakBreaks[String(days)] = t("care.reminders.notification.streakBreaks", { count: days });
  return {
    apiUrl,
    texts: {
      water_change: t("care.reminders.notification.water_change"),
      measurement: t("care.reminders.notification.measurement"),
      snooze: t("care.reminders.snooze"),
      streakToday: t("care.reminders.notification.streakToday"),
      streakBreaks,
    },
  };
}

/** The bytes of a base64url string, such as the VAPID public key the server serves. */
export function urlBase64ToBytes(value: string): Uint8Array<ArrayBuffer> {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
  const binary = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

/** The subscription the server stores: the endpoint and keys of the browser subscription and the browser's time zone. */
export function subscriptionRequest(subscription: PushSubscriptionJSON, timeZone: string): WebPushSubscriptionRequest {
  const { endpoint, keys } = subscription;
  if (!endpoint || !keys?.p256dh || !keys?.auth) throw new Error("The browser subscription has no endpoint or keys.");
  return { endpoint, keys: { p256dh: keys.p256dh, auth: keys.auth }, timeZone };
}
