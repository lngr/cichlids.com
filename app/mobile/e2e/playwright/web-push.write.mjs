// Story: TASK-5.12
// Reminders in the browser through Web Push, as a member of the web app:
// - The setting is off by default. Switching it on shows the explanation of what the push service of
//   the browser maker learns, and only after the member agrees does the app ask the browser for the
//   permission. Declining the explanation asks for nothing.
// - After the agreement the app registers the service worker, subscribes with the public VAPID key
//   and stores the subscription with the time zone on the server (one row, one outbox event).
// - The service worker shows a pushed reminder in the language of the member, with the snooze
//   action, and its snooze function stores the snooze through the API with the session cookie and
//   the CSRF token.
// - Switching the setting off deletes the subscription in the browser and on the server, and so
//   does signing out.
// - A browser without Push support, an iPhone outside a home-screen web app and a browser that
//   blocks notifications show a calm hint instead.
//
// The browser's push service is replaced: the page gets a subscription from a fake PushManager with
// a real P-256 key, and a pushed message reaches the real service worker through the DevTools
// protocol. The delivery of an encrypted message by a real push service (FCM, Mozilla autopush,
// Apple, Windows) cannot run here; the API tests decrypt the sent message with the key of the
// subscription instead. Runs on the isolated write stack only (see e2e/write-stack.sh).
import assert from "node:assert/strict";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");
const TIME_ZONE = "Europe/Berlin";
const IPHONE_USER_AGENT = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");
function sql(statement) {
  return execFileSync("docker", ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
}

const localDate = (date, timeZone = TIME_ZONE) => new Intl.DateTimeFormat("en-CA", { timeZone }).format(date);
const base64Url = bytes => Buffer.from(bytes).toString("base64url");

/** A browser subscription with a real P-256 key, as a push service would hand it out. */
function newSubscription() {
  const { publicKey } = crypto.generateKeyPairSync("ec", { namedCurve: "prime256v1" });
  const jwk = publicKey.export({ format: "jwk" });
  const point = Buffer.concat([Buffer.from([4]), Buffer.from(jwk.x, "base64url"), Buffer.from(jwk.y, "base64url")]);
  return {
    endpoint: `https://fcm.googleapis.com/fcm/send/e2e-${crypto.randomBytes(12).toString("hex")}`,
    p256dh: base64Url(point),
    auth: base64Url(crypto.randomBytes(16)),
  };
}

/**
 * Replaces the push service of the page: the fake PushManager hands out the given subscription and
 * keeps it in localStorage, so it survives a reload like the real one. It counts the permission
 * requests, the subscribe calls and the unsubscribe calls.
 */
function installFakePush(context, subscription) {
  return context.addInitScript(({ endpoint, p256dh, auth }) => {
    const counters = { permissionRequests: 0, subscribeCalls: 0, unsubscribeCalls: 0 };
    window.__webPush = counters;
    const original = Notification.requestPermission.bind(Notification);
    Notification.requestPermission = (...args) => { counters.permissionRequests++; return original(...args); };
    const stored = () => localStorage.getItem("__fakePushSubscription") !== null;
    const fake = key => ({
      endpoint,
      options: { applicationServerKey: key },
      toJSON: () => ({ endpoint, expirationTime: null, keys: { p256dh, auth } }),
      unsubscribe: async () => { counters.unsubscribeCalls++; localStorage.removeItem("__fakePushSubscription"); return true; },
    });
    PushManager.prototype.subscribe = async function (options) {
      counters.subscribeCalls++;
      localStorage.setItem("__fakePushSubscription", "1");
      window.__lastApplicationServerKey = Array.from(new Uint8Array(options.applicationServerKey));
      return fake(options.applicationServerKey);
    };
    PushManager.prototype.getSubscription = async () => stored() ? fake(new Uint8Array(window.__lastApplicationServerKey ?? []).buffer) : null;
  }, subscription);
}

const counters = page => page.evaluate(() => window.__webPush);
const rows = (where = "true") => Number(sql(`select count(*) from web_push_subscription where ${where}`));

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
const profileId = Number(me.profile.id);
const day = 24 * 3600 * 1000;
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Push path", volume: { value: 200, unit: "liter" } }) });
assert.equal(created.status, 201);
const tank = await created.json();
const logged = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, {
  method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt: new Date(Date.now() - 7 * day).toISOString() }),
});
assert.equal(logged.status, 201);
const dueDate = localDate(new Date());
const tomorrow = localDate(new Date(Date.now() + day));

// The full Chromium build: the headless shell reports Notification.permission as denied and cannot show notifications.
const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  const subscription = newSubscription();
  const context = await browser.newContext({ locale: "en-US", timezoneId: TIME_ZONE, colorScheme: "light", viewport: { width: 390, height: 844 }, permissions: ["notifications"] });
  await installFakePush(context, subscription);
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  // The page is installable as a home-screen web app, which iOS requires for Web Push.
  const manifest = await page.evaluate(async () => {
    const href = document.querySelector('link[rel="manifest"]')?.href;
    return href ? await (await fetch(href)).json() : null;
  });
  assert.equal(manifest?.display, "standalone");
  assert.deepEqual(manifest.icons.map(icon => icon.sizes), ["192x192", "512x512"]);
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();

  // Off by default: no explanation, no permission request, no subscription.
  const state = byTestId(page, "web-push-state");
  await state.waitFor();
  assert.equal(await state.innerText(), "Off. This browser shows no care reminders.");
  assert.equal(await byTestId(page, "web-push-toggle").innerText(), "Switch on reminders in this browser");
  assert.equal(await byTestId(page, "web-push-explain").count(), 0);
  assert.deepEqual(await counters(page), { permissionRequests: 0, subscribeCalls: 0, unsubscribeCalls: 0 });
  assert.equal(rows(`profile_id = ${profileId}`), 0);

  // The explanation comes first; declining it asks the browser for nothing.
  await byTestId(page, "web-push-toggle").click();
  const explain = byTestId(page, "web-push-explain");
  await explain.waitFor();
  const explanation = await explain.innerText();
  assert.ok(explanation.includes("push service of your browser's maker (Google, Mozilla, Microsoft or Apple)"), explanation);
  assert.ok(explanation.includes("learns that this browser receives notifications from cichlids.com, and when, but not what they say"), explanation);
  assert.ok(explanation.includes("deletes the subscription on our server"), explanation);
  for (const id of ["web-push-agree", "web-push-cancel"]) {
    const bounds = await byTestId(page, id).boundingBox();
    assert.ok(bounds.height >= 48, `${id} is ${bounds.height}px high`);
  }
  await byTestId(page, "web-push").screenshot({ path: "/tmp/task-5.12-explain-light.png" });
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "web-push").screenshot({ path: "/tmp/task-5.12-explain-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });
  assert.equal((await counters(page)).permissionRequests, 0);
  await byTestId(page, "web-push-cancel").click();
  await state.waitFor();
  assert.equal(await byTestId(page, "web-push-explain").count(), 0);
  assert.deepEqual(await counters(page), { permissionRequests: 0, subscribeCalls: 0, unsubscribeCalls: 0 });

  // Agreeing asks the browser for the permission, subscribes and stores the subscription.
  await byTestId(page, "web-push-toggle").click();
  await byTestId(page, "web-push-agree").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="web-push-state"]')?.textContent?.startsWith("On."));
  assert.equal(await state.innerText(), "On. This browser shows a notification at 9:00 AM on the day care is due.");
  assert.equal(await byTestId(page, "web-push-toggle").innerText(), "Switch off reminders in this browser");
  assert.deepEqual(await counters(page), { permissionRequests: 1, subscribeCalls: 1, unsubscribeCalls: 0 });
  assert.equal(rows(`profile_id = ${profileId}`), 1);
  assert.equal(sql(`select endpoint || '|' || p256dh || '|' || auth || '|' || time_zone from web_push_subscription where profile_id = ${profileId}`),
    `${subscription.endpoint}|${subscription.p256dh}|${subscription.auth}|${TIME_ZONE}`);
  const subscriptionId = sql(`select id from web_push_subscription where profile_id = ${profileId}`);
  assert.equal(sql(`select count(*) from outbox_event where event_type = 'web_push.subscribed' and aggregate_id = '${subscriptionId}'`), "1");
  const vapidKey = await (await fetch(`${API_URL}/api/me/web-push`, { headers })).json();
  assert.equal(base64Url(await page.evaluate(() => window.__lastApplicationServerKey)), vapidKey.publicKey);
  await byTestId(page, "web-push").screenshot({ path: "/tmp/task-5.12-on-light.png" });

  // The service worker is registered at the root and holds the texts of the member's language.
  const registration = await page.evaluate(async () => {
    const found = await navigator.serviceWorker.ready;
    const cache = await caches.open("cichlids-web-push");
    return { scope: found.scope, active: found.active?.scriptURL, settings: await (await cache.match("/__cichlids/web-push-settings")).json() };
  });
  assert.equal(registration.scope, `${WEB_URL}/`);
  assert.equal(registration.active, `${WEB_URL}/sw.js`);
  assert.equal(registration.settings.apiUrl, API_URL);
  assert.equal(registration.settings.texts.water_change, "A water change is due today.");

  // A pushed reminder shows as a notification with the tank title, the text and the snooze action.
  const cdp = await context.newCDPSession(page);
  const registrations = new Map();
  cdp.on("ServiceWorker.workerRegistrationUpdated", event => event.registrations.forEach(r => registrations.set(r.scopeURL, r.registrationId)));
  await cdp.send("ServiceWorker.enable");
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null || true);
  const deadline = Date.now() + 10000;
  while (!registrations.has(`${WEB_URL}/`) && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 100));
  const reminder = { tankId: Number(tank.id), tankTitle: "Push path", kind: "water_change", dueDate, url: `/care?tank=${tank.id}` };
  const shownNotifications = () => page.evaluate(async () => (await (await navigator.serviceWorker.ready).getNotifications())
    .map(n => ({ title: n.title, body: n.body, tag: n.tag, data: n.data, actions: n.actions.map(a => ({ action: a.action, title: a.title })) })));
  await cdp.send("ServiceWorker.deliverPushMessage", { origin: WEB_URL, registrationId: registrations.get(`${WEB_URL}/`), data: JSON.stringify(reminder) });
  let shown = [];
  for (let attempt = 0; attempt < 50 && shown.length === 0; attempt++) { shown = await shownNotifications(); await new Promise(resolve => setTimeout(resolve, 100)); }
  assert.equal(shown.length, 1);
  assert.equal(shown[0].title, "Push path");
  assert.equal(shown[0].body, "A water change is due today.");
  assert.equal(shown[0].tag, `care-reminder:${tank.id}:water_change`);
  assert.deepEqual(shown[0].data, reminder);
  const supportsActions = await page.evaluate(() => Notification.maxActions > 0);
  assert.deepEqual(shown[0].actions, supportsActions ? [{ action: "snooze", title: "Remind me tomorrow" }] : []);

  // The snooze function of the worker stores the snooze through the API with cookie and CSRF token.
  const worker = context.serviceWorkers().find(w => w.url() === `${WEB_URL}/sw.js`);
  assert.ok(worker, "the service worker is running");
  await worker.evaluate(snoozed => snooze(snoozed), reminder);
  assert.equal(sql(`select kind || '|' || due_date || '|' || until_date from care_reminder_snooze where tank_id = ${tank.id}`), `water_change|${dueDate}|${tomorrow}`);
  assert.equal(sql(`select count(*) from outbox_event where event_type = 'care_reminder.snoozed' and payload->>'tankId' = '${tank.id}'`), "1");

  // The language of the member reaches the worker: the next reminder reads in German.
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(page, "web-push-state").waitFor();
  assert.equal(await byTestId(page, "web-push-toggle").innerText(), "Switch off reminders in this browser");
  assert.equal(rows(`profile_id = ${profileId}`), 1);
  await byTestId(page, "language-settings-toggle").click();
  await byTestId(page, "language-de").click();
  await page.waitForFunction(async () => (await (await (await caches.open("cichlids-web-push")).match("/__cichlids/web-push-settings")).json()).texts.water_change === "Heute ist ein Wasserwechsel fällig.");
  await byTestId(page, "language-settings-toggle").click();
  await byTestId(page, "language-en").click();
  await page.waitForFunction(async () => (await (await (await caches.open("cichlids-web-push")).match("/__cichlids/web-push-settings")).json()).texts.water_change === "A water change is due today.");

  // Switching off deletes the subscription in the browser and on the server.
  await byTestId(page, "web-push-toggle").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="web-push-state"]')?.textContent?.startsWith("Off."));
  assert.equal((await counters(page)).unsubscribeCalls, 1);
  assert.equal(rows(`profile_id = ${profileId}`), 0);
  assert.equal(sql(`select payload->>'reason' from outbox_event where event_type = 'web_push.unsubscribed' and aggregate_id = '${subscriptionId}'`), "member");
  assert.equal(await page.evaluate(() => caches.has("cichlids-web-push")), false);
  assert.equal(await byTestId(page, "web-push-toggle").innerText(), "Switch on reminders in this browser");

  // Signing out deletes the subscription of a browser that still holds one.
  await byTestId(page, "web-push-toggle").click();
  await byTestId(page, "web-push-agree").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="web-push-state"]')?.textContent?.startsWith("On."));
  assert.equal(rows(`profile_id = ${profileId}`), 1);
  await signOutThroughKeycloak(page);
  assert.equal(rows(`profile_id = ${profileId}`), 0);
  await context.close();

  // A browser without Push support shows a hint and no setting.
  const unsupported = await browser.newContext({ locale: "en-US", timezoneId: TIME_ZONE, viewport: { width: 390, height: 844 } });
  await unsupported.addInitScript(() => { delete window.PushManager; });
  page = await unsupported.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "web-push-hint").waitFor();
  assert.equal(await byTestId(page, "web-push-hint").innerText(), "This browser cannot show reminders from websites.");
  assert.equal(await byTestId(page, "web-push-toggle").count(), 0);
  await signOutThroughKeycloak(page);
  await unsupported.close();

  // iOS Safari outside a home-screen web app is asked to add the app to the home screen.
  const ios = await browser.newContext({ locale: "en-US", userAgent: IPHONE_USER_AGENT, viewport: { width: 390, height: 844 } });
  page = await ios.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "web-push-hint").waitFor();
  assert.equal(await byTestId(page, "web-push-hint").innerText(), "On iPhone and iPad, reminders work after you add cichlids to your Home Screen and open it from there.");
  assert.equal(await byTestId(page, "web-push-toggle").count(), 0);
  await signOutThroughKeycloak(page);
  await ios.close();

  // A browser that blocks notifications answers the permission request with a calm hint.
  const blocked = await browser.newContext({ locale: "en-US", timezoneId: TIME_ZONE, viewport: { width: 390, height: 844 } });
  await installFakePush(blocked, newSubscription());
  page = await blocked.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "web-push-toggle").click();
  await byTestId(page, "web-push-agree").click();
  await byTestId(page, "web-push-hint").waitFor();
  assert.equal(await byTestId(page, "web-push-hint").innerText(), "Notifications are blocked for cichlids in this browser. Allow them in the site settings of your browser to get reminders.");
  assert.equal(rows(`profile_id = ${profileId}`), 0);
  assert.equal((await counters(page)).subscribeCalls, 0);
  await signOutThroughKeycloak(page);
  await blocked.close();

  assert.deepEqual(errors, []);
  console.log("TASK-5.12: reminders in the browser are off by default, explained before the permission prompt, stored with the time zone, shown and snoozed by the service worker, deleted on switching off and on sign-out; unsupported, iOS and blocked browsers show hints (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-5.12-web-push-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  sql(`delete from web_push_subscription where profile_id = ${profileId}`);
}
