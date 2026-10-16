// Service worker of the cichlids web app. It shows the care reminders that the server sends as
// Web Push messages, opens the care view of the tank when a notification is clicked, and snoozes a
// reminder through the API when the member chooses the snooze action.
//
// The web app stores the API address and the texts of the member's language in the cache
// "cichlids-web-push" under /__cichlids/web-push-settings, because the worker has no access to the
// catalogs of the app. The message from the server holds tankId, tankTitle, kind, dueDate and url, and
// daysLeft (the days until the water change streak breaks) for a reminder of a running streak.
"use strict";

const SETTINGS_CACHE = "cichlids-web-push";
const SETTINGS_URL = "/__cichlids/web-push-settings";
const KINDS = ["water_change", "measurement"];
const DEFAULT_TEXTS = {
  water_change: "A water change is due today.",
  measurement: "A water measurement is due today.",
  snooze: "Remind me tomorrow",
  streakToday: "Your water change streak breaks today. Do the water change now or enter a vacation if you are away.",
  streakBreaks: days => `Your water change streak breaks in ${days} ${days === 1 ? "day" : "days"}. Do the water change or enter a vacation if you are away.`,
};

self.addEventListener("install", () => { self.skipWaiting(); });
self.addEventListener("activate", event => { event.waitUntil(self.clients.claim()); });

async function readSettings() {
  try {
    const cache = await caches.open(SETTINGS_CACHE);
    const response = await cache.match(SETTINGS_URL);
    return response ? await response.json() : null;
  } catch {
    return null;
  }
}

function readReminder(event) {
  let data;
  try {
    data = event.data.json();
  } catch {
    return null;
  }
  const valid = data !== null && typeof data === "object"
    && Number.isInteger(data.tankId) && typeof data.tankTitle === "string" && KINDS.includes(data.kind)
    && typeof data.dueDate === "string" && typeof data.url === "string"
    && data.url.startsWith("/") && !data.url.startsWith("//")
    && (data.daysLeft === undefined || (Number.isInteger(data.daysLeft) && data.daysLeft >= 0));
  if (!valid) return null;
  const reminder = { tankId: data.tankId, tankTitle: data.tankTitle, kind: data.kind, dueDate: data.dueDate, url: data.url };
  return data.daysLeft === undefined ? reminder : { ...reminder, daysLeft: data.daysLeft };
}

// The text of a reminder: the day of the care, or for a reminder of a running streak the days until it breaks.
// settings.texts.streakBreaks holds one text per number of days left, as the page translated them.
function bodyOf(reminder, texts) {
  if (reminder.daysLeft === undefined) return texts[reminder.kind];
  if (reminder.daysLeft === 0) return texts.streakToday;
  const translated = texts.streakBreaks;
  const text = translated && typeof translated === "object" ? translated[String(reminder.daysLeft)] : undefined;
  return typeof text === "string" ? text : DEFAULT_TEXTS.streakBreaks(reminder.daysLeft);
}

async function showReminder(event) {
  const reminder = readReminder(event);
  if (reminder === null) return;
  const settings = await readSettings();
  const texts = { ...DEFAULT_TEXTS, ...(settings && settings.texts) };
  const options = {
    body: bodyOf(reminder, texts),
    tag: `care-reminder:${reminder.tankId}:${reminder.kind}`,
    data: reminder,
  };
  // The snooze dismisses the reminder of today; on the last day of a running streak no reminder follows.
  if (Notification.maxActions > 0 && reminder.daysLeft !== 0) options.actions = [{ action: "snooze", title: texts.snooze }];
  await self.registration.showNotification(reminder.tankTitle, options);
}

async function openCareView(url) {
  const target = new URL(url, self.location.origin).href;
  const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
  const open = windows.find(client => new URL(client.url).origin === self.location.origin);
  if (open) {
    try {
      await open.focus();
      await open.navigate(target);
      return;
    } catch {
      // A window the worker cannot navigate is replaced by a new one.
    }
  }
  await self.clients.openWindow(target);
}

async function snooze(reminder) {
  const settings = await readSettings();
  const base = ((settings && settings.apiUrl) || self.location.origin).replace(/\/+$/, "");
  const session = await (await fetch(`${base}/api/auth/session`, { credentials: "include" })).json();
  if (!session.authenticated || !session.csrfToken) throw new Error("There is no web session.");
  const response = await fetch(`${base}/api/me/care/reminders/snooze`, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": session.csrfToken },
    body: JSON.stringify({
      tankId: reminder.tankId,
      kind: reminder.kind,
      dueDate: reminder.dueDate,
      timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    }),
  });
  if (!response.ok) throw new Error("The reminder could not be snoozed.");
}

async function answerClick(event) {
  const reminder = event.notification.data;
  if (event.action === "snooze" && reminder.daysLeft !== undefined) return;
  if (event.action === "snooze") {
    try {
      await snooze(reminder);
      return;
    } catch {
      // Without a session or a connection the member can act in the care view.
    }
  }
  await openCareView(reminder.url);
}

self.addEventListener("push", event => { event.waitUntil(showReminder(event)); });
self.addEventListener("notificationclick", event => {
  event.notification.close();
  event.waitUntil(answerClick(event));
});
