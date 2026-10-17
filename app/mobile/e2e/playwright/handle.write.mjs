// Story: TASK-3.43
// A member changes the public handle in the Me tab, as a member of the web app:
// - A handle that is taken, reserved, generated in form or malformed is refused with its reason at
//   the field and the handle stays.
// - A valid free handle is shown as the new handle with the day of the next change, the old handle
//   is reserved for the member for 90 days and the profile still opens by its id.
// - A second change right away is refused at the field with the day of the next change.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The spec puts the handle of the
// shared member back, removes the released handles and the profile it created for the taken name.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak, sql } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const TAKEN = "e2e.taken.name";
const CHOSEN = "e2e.fish.keeper";
const DAY = 24 * 3600 * 1000;

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const readMe = async () => (await fetch(`${API_URL}/api/me`, { headers })).json();
const me = await readMe();
const profileId = Number(me.profile.id);
const originalHandle = me.profile.username;

// The Me tab opens after the onboarding; the spec skips it so it does not depend on another spec.
if (!me.profile.onboardingCompletedAt) {
  const skipped = await fetch(`${API_URL}/api/me/onboarding`, { method: "POST", headers, body: JSON.stringify({ skip: true }) });
  assert.equal(skipped.status, 200);
}

const reset = () => {
  sql(`delete from outbox_event where event_type = 'profile.handle_changed' and aggregate_id = '${profileId}'`);
  sql(`delete from released_handle where profile_id = ${profileId}`);
  sql(`delete from profile where username = '${TAKEN}'`);
  sql(`update profile set username = '${originalHandle}', handle_changed_at = null where id = ${profileId}`);
};
reset();
sql(`insert into profile (username, kind, created_at) values ('${TAKEN}', 'member', now())`);

const expectText = async (page, id, expected) => {
  await page.waitForFunction(([testId, value]) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() === value, [id, expected], { timeout: 30000 })
    .catch(async error => { throw new Error(`${id}: expected "${expected}", got "${await byTestId(page, id).innerText().catch(() => "(missing)")}" (${error.message})`); });
};
const formatDay = instant => new Intl.DateTimeFormat("en-US", { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" }).format(instant);
const submit = async (page, handle) => {
  await byTestId(page, "handle-input").fill(handle);
  await byTestId(page, "handle-save").click();
};

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  const context = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "handle-settings").waitFor({ timeout: 30000 });
  await expectText(page, "handle-current", `@${originalHandle}`);
  const box = await byTestId(page, "handle-edit").boundingBox();
  assert.ok(box && box.height >= 48 && box.width >= 48, "the button to change the handle is at least 48 by 48");

  // Refusals show their reason at the field and keep the handle.
  await byTestId(page, "handle-edit").click();
  const refusals = [
    [TAKEN.toUpperCase(), "This handle is already in use or reserved for another member."],
    ["Admin", "This name is reserved. Choose another handle."],
    ["user12345678", "Names such as user12345678, guest_1 or provisional-abc are assigned by the app. Choose another handle."],
    ["a..b", "Use 3 to 30 characters: letters a to z, digits, underscore and dot. Start with a letter and end with a letter or digit."],
  ];
  for (const [handle, message] of refusals) {
    await submit(page, handle);
    await expectText(page, "handle-error", message);
  }
  await page.screenshot({ path: "/tmp/task-3.43-handle-error-light.png" });
  assert.equal((await readMe()).profile.username, originalHandle, "a refused handle changes nothing");

  // A valid free handle is taken, the old one is reserved for 90 days.
  await submit(page, CHOSEN.toUpperCase());
  await expectText(page, "handle-saved", `Your handle is @${CHOSEN}.`);
  await expectText(page, "handle-current", `@${CHOSEN}`);
  const changed = await readMe();
  assert.equal(changed.profile.username, CHOSEN);
  const nextChange = new Date(changed.handleChangeAvailableAt);
  assert.ok(Math.abs(nextChange.getTime() - (Date.now() + 30 * DAY)) < DAY, "the next change is 30 days away");
  await expectText(page, "handle-next-change", `You can change your handle again on ${formatDay(nextChange)}.`);
  assert.equal(sql(`select count(*) from released_handle where profile_id = ${profileId} and handle = lower('${originalHandle}') and reserved_until > now() + interval '89 days'`), "1");
  assert.equal(sql(`select count(*) from outbox_event where event_type = 'profile.handle_changed' and aggregate_id = '${profileId}'`), "1");
  await page.screenshot({ path: "/tmp/task-3.43-handle-changed-light.png" });

  // The profile opens by its id and shows the new handle.
  const profile = await fetch(`${API_URL}/api/profiles/${profileId}`);
  assert.equal(profile.status, 200);
  assert.equal((await profile.json()).username, CHOSEN);
  const profilePage = await page.goto(`${WEB_URL}/profile/${profileId}?lang=en`, { waitUntil: "networkidle" });
  assert.equal(profilePage?.status(), 200);

  // A second change right away names the day of the next change.
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "handle-edit").click();
  await submit(page, "e2e.second.try");
  await expectText(page, "handle-error", `You changed your handle recently. The next change is possible on ${formatDay(nextChange)}.`);
  assert.equal((await readMe()).profile.username, CHOSEN);

  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-3.43: refused handles show their reason at the field, a valid handle is shown with the day of the next change and reserves the old one, a second change is refused with that day.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-3.43-handle-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  reset();
}
