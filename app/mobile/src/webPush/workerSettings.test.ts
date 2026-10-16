import { describe, expect, it } from "vitest";
import { subscriptionRequest, urlBase64ToBytes, workerSettings, WORKER_SETTINGS_URL } from "./workerSettings";

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
    const t = (key: string) => `[${key}]`;
    expect(workerSettings("https://api.test", t)).toEqual({
      apiUrl: "https://api.test",
      texts: { water_change: "[care.reminders.notification.water_change]", measurement: "[care.reminders.notification.measurement]", snooze: "[care.reminders.snooze]" },
    });
    expect(WORKER_SETTINGS_URL).toBe("/__cichlids/web-push-settings");
  });
});
