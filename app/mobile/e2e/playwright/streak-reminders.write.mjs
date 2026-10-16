// Story: TASK-5.18
// The daily water change reminders while the streak runs, as a member of the web app:
// - A weekly plan whose last water change is 7 days ago is due today and its streak holds through
//   day 10, so the due care names the reminder days from today through 3 days later; after that day
//   the streak is broken and the care names none.
// - The service worker shows a pushed reminder of the streak in the language of the member: the
//   days left until the streak breaks and the hint to enter a vacation, "breaks today" on the last
//   day. The snooze action is offered except on the last day, when no reminder follows.
// - Logging the water change moves the reminder days to the new period.
// - Entering a vacation leaves no reminder day.
//
// The browser's push service is replaced as in the web push spec: a fake PushManager hands out a
// subscription and a pushed message reaches the real service worker through the DevTools protocol.
// The 09:00 sender of the server is switched off on the write stack; the API tests cover the days
// and texts it sends, and the unit tests cover the local notifications of the phone apps.
// Runs on the isolated write stack only (see e2e/write-stack.sh).
import assert from "node:assert/strict";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");
const sql = statement => execFileSync("docker", ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
const base64Url = bytes => Buffer.from(bytes).toString("base64url");

const DAY = 24 * 3600 * 1000;
const dateOf = offset => new Date(Date.now() + offset * DAY).toISOString().slice(0, 10);
const today = dateOf(0);

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
const profileId = Number(me.profile.id);
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Streak reminder path", isPublic: true, volume: { value: 200, unit: "liter" } }) });
assert.equal(created.status, 201);
const tank = await created.json();
const logWaterChange = async occurredAt => {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt }) });
  assert.equal(response.status, 201);
};
await logWaterChange(new Date(Date.now() - 7 * DAY).toISOString());

const dueItem = async date => (await (await fetch(`${API_URL}/api/me/care/due?date=${date}&utcOffsetMinutes=0`, { headers })).json())
  .items.find(item => String(item.tankId) === String(tank.id) && item.kind === "water_change");

/** A browser subscription with a real P-256 key, as a push service would hand it out. */
function newSubscription() {
  const { publicKey } = crypto.generateKeyPairSync("ec", { namedCurve: "prime256v1" });
  const jwk = publicKey.export({ format: "jwk" });
  const point = Buffer.concat([Buffer.from([4]), Buffer.from(jwk.x, "base64url"), Buffer.from(jwk.y, "base64url")]);
  return { endpoint: `https://fcm.googleapis.com/fcm/send/e2e-${crypto.randomBytes(12).toString("hex")}`, p256dh: base64Url(point), auth: base64Url(crypto.randomBytes(16)) };
}

function installFakePush(context, subscription) {
  return context.addInitScript(({ endpoint, p256dh, auth }) => {
    const stored = () => localStorage.getItem("__fakePushSubscription") !== null;
    const fake = key => ({
      endpoint,
      options: { applicationServerKey: key },
      toJSON: () => ({ endpoint, expirationTime: null, keys: { p256dh, auth } }),
      unsubscribe: async () => { localStorage.removeItem("__fakePushSubscription"); return true; },
    });
    PushManager.prototype.subscribe = async function (options) {
      localStorage.setItem("__fakePushSubscription", "1");
      window.__lastApplicationServerKey = Array.from(new Uint8Array(options.applicationServerKey));
      return fake(options.applicationServerKey);
    };
    PushManager.prototype.getSubscription = async () => stored() ? fake(new Uint8Array(window.__lastApplicationServerKey ?? []).buffer) : null;
  }, subscription);
}

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  // The due care names the reminder days of the running streak: day 7 through day 10.
  const first = await dueItem(today);
  assert.equal(first.due.state, "due_today");
  assert.deepEqual(first.streak, { state: "running", reminderFrom: today, deadline: dateOf(3) });
  for (const offset of [1, 2, 3]) {
    const later = await dueItem(dateOf(offset));
    assert.deepEqual(later.streak, { state: "running", reminderFrom: today, deadline: dateOf(3) }, `day ${7 + offset}`);
  }
  const broken = await dueItem(dateOf(4));
  assert.ok(!broken.streak, "the streak is broken after day 10");

  const context = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 }, permissions: ["notifications"] });
  await installFakePush(context, newSubscription());
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
  await byTestId(page, "web-push-toggle").click();
  await byTestId(page, "web-push-agree").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="web-push-state"]')?.textContent?.startsWith("On."));
  const settingsTexts = await page.evaluate(async () => (await (await (await caches.open("cichlids-web-push")).match("/__cichlids/web-push-settings")).json()).texts);
  assert.equal(settingsTexts.streakToday, "Your water change streak breaks today. Do the water change now or enter a vacation if you are away.");
  assert.equal(Object.keys(settingsTexts.streakBreaks).length, 30);

  const cdp = await context.newCDPSession(page);
  const registrations = new Map();
  cdp.on("ServiceWorker.workerRegistrationUpdated", event => event.registrations.forEach(r => registrations.set(r.scopeURL, r.registrationId)));
  await cdp.send("ServiceWorker.enable");
  const deadline = Date.now() + 10000;
  while (!registrations.has(`${WEB_URL}/`) && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 100));
  const reminder = daysLeft => ({ tankId: Number(tank.id), tankTitle: "Streak reminder path", kind: "water_change", dueDate: today, url: `/care?tank=${tank.id}`, daysLeft });
  const shownNotifications = () => page.evaluate(async () => (await (await navigator.serviceWorker.ready).getNotifications())
    .map(n => ({ title: n.title, body: n.body, tag: n.tag, data: n.data, actions: n.actions.map(a => a.action) })));
  const pushAndRead = async (daysLeft, expectedBody) => {
    await cdp.send("ServiceWorker.deliverPushMessage", { origin: WEB_URL, registrationId: registrations.get(`${WEB_URL}/`), data: JSON.stringify(reminder(daysLeft)) });
    let shown = [];
    for (let attempt = 0; attempt < 50; attempt++) {
      shown = await shownNotifications();
      if (shown.length === 1 && shown[0].body === expectedBody) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.equal(shown.length, 1);
    assert.equal(shown[0].body, expectedBody);
    assert.equal(shown[0].title, "Streak reminder path");
    assert.deepEqual(shown[0].data, reminder(daysLeft));
    const supportsActions = await page.evaluate(() => Notification.maxActions > 0);
    assert.deepEqual(shown[0].actions, supportsActions && daysLeft > 0 ? ["snooze"] : []);
  };
  await pushAndRead(3, "Your water change streak breaks in 3 days. Do the water change or enter a vacation if you are away.");
  await pushAndRead(1, "Your water change streak breaks in 1 day. Do the water change or enter a vacation if you are away.");
  await pushAndRead(0, "Your water change streak breaks today. Do the water change now or enter a vacation if you are away.");

  // The language of the member reaches the worker: the reminder reads in German with the plural of the language.
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(page, "web-push-state").waitFor();
  await byTestId(page, "language-settings-toggle").click();
  await byTestId(page, "language-de").click();
  await page.waitForFunction(async () => (await (await (await caches.open("cichlids-web-push")).match("/__cichlids/web-push-settings")).json()).texts.streakToday.startsWith("Deine Wasserwechsel-Serie reißt heute ab."));
  await pushAndRead(2, "Deine Wasserwechsel-Serie reißt in 2 Tagen ab. Wechsle das Wasser oder starte einen Urlaub, falls du verreist bist.");
  await pushAndRead(1, "Deine Wasserwechsel-Serie reißt in 1 Tag ab. Wechsle das Wasser oder starte einen Urlaub, falls du verreist bist.");
  await byTestId(page, "language-settings-toggle").click();
  await byTestId(page, "language-en").click();
  await page.waitForFunction(async () => (await (await (await caches.open("cichlids-web-push")).match("/__cichlids/web-push-settings")).json()).texts.streakToday.startsWith("Your water change streak"));

  // Logging the water change moves the reminder days to the new period; no reminder day is left in the old one.
  await logWaterChange(new Date().toISOString());
  const renewed = await dueItem(today);
  assert.deepEqual(renewed.streak, { state: "running", reminderFrom: dateOf(7), deadline: dateOf(10) });
  assert.equal(renewed.due.state, "due_in");

  // An active vacation pauses the streak and leaves no reminder day.
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "vacation-start").click();
  await byTestId(page, "vacation-end").waitFor();
  assert.deepEqual((await dueItem(today)).streak, { state: "paused", reminderFrom: null, deadline: null });
  await byTestId(page, "web-push-toggle").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="web-push-state"]')?.textContent?.startsWith("Off."));
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-5.18: the due care names the reminder days from day 7 through day 10, the service worker shows the days left and the vacation hint in the language of the member with the snooze except on the last day, a logged water change moves the days and a vacation leaves none.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-5.18-streak-reminders-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  sql(`delete from web_push_subscription where profile_id = ${profileId}`);
  sql(`delete from outbox_event where event_type in ('vacation.started', 'vacation.ended') and payload->>'profileId' = '${profileId}'`);
  sql(`delete from vacation where profile_id = ${profileId}`);
}
