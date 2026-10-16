import { describe, expect, it } from "vitest";
import { MAX_STREAK_DAYS_LEFT, subscriptionRequest, urlBase64ToBytes, workerSettings, WORKER_SETTINGS_URL } from "./workerSettings";

describe("urlBase64ToBytes", () => {
  it("decodes the base64url VAPID key of the server", () => {
    expect(Array.from(urlBase64ToBytes("AQID-_8"))).toEqual([1, 2, 3, 251, 255]);
  });
});

describe("subscriptionRequest", () => {
  it("takes the endpoint and keys of the browser subscription and adds the time zone", () => {
    expect(subscriptionRequest({ endpoint: "https://push.test/a", expirationTime: null, keys: { p256dh: "P", auth: "A" } }, "Europe/Berlin"))
      .toEqual({ endpoint: "https://push.test/a", keys: { p256dh: "P", auth: "A" }, timeZone: "Europe/Berlin" });
  });
  it("refuses a subscription without keys", () => {
    expect(() => subscriptionRequest({ endpoint: "https://push.test/a" }, "UTC")).toThrow();
  });
});

describe("workerSettings", () => {
  it("holds the API address and the notification texts in the member's language", () => {
    const t = (key: string, options?: { count: number }) => options ? `[${key}:${options.count}]` : `[${key}]`;
    const settings = workerSettings("https://api.test", t);
    expect(settings.apiUrl).toBe("https://api.test");
    expect(settings.texts).toMatchObject({
      water_change: "[care.reminders.notification.water_change]", measurement: "[care.reminders.notification.measurement]", snooze: "[care.reminders.snooze]",
      streakToday: "[care.reminders.notification.streakToday]",
    });
    expect(WORKER_SETTINGS_URL).toBe("/__cichlids/web-push-settings");
  });
  it("translates the streak text for every number of days a streak can have left", () => {
    const { streakBreaks } = workerSettings("https://api.test", (key, options) => options ? `${key}:${options.count}` : key).texts;
    expect(Object.keys(streakBreaks)).toHaveLength(MAX_STREAK_DAYS_LEFT);
    expect(streakBreaks["1"]).toBe("care.reminders.notification.streakBreaks:1");
    expect(streakBreaks["30"]).toBe("care.reminders.notification.streakBreaks:30");
  });
});
