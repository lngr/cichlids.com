// Story: TASK-5.13
// The guided water change on the web build: a member starts it from the care view of an own tank,
// checks off the steps, takes two photos while draining and one after refilling with the camera of the
// app (a fake camera device) and finishes. The water change entry then exists with its duration and
// the three photos, and its evidence is verified. A second water change is started from the due care on
// Today, survives a reload and ends with calm "not verified" wording when steps were skipped; a
// third one has a long enough draining time but no photo, so it is partial with the missing photo.
// The write stack counts the draining time as long enough from E2E_WATER_CHANGE_MIN_SECONDS seconds
// between the start of draining and the refill step.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and
// the bucket.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const minSeconds = Number(process.env.E2E_WATER_CHANGE_MIN_SECONDS);
assert.ok(minSeconds > 0 && minSeconds <= 10, "the write stack sets a short minimum duration");
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const createTank = async title => {
  const response = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title, isPublic: true, volume: { value: 200, unit: "liter" } }) });
  assert.equal(response.status, 201);
  return response.json();
};
const entriesOf = async tank => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers })).json());
const tank = await createTank("Guided water change tank");
const todayTank = await createTank("Guided water change from Today");
const logOverdue = await fetch(`${API_URL}/api/tanks/${todayTank.id}/care`, {
  method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt: new Date(Date.now() - 10 * 24 * 3600 * 1000).toISOString() }),
});
assert.equal(logOverdue.status, 201);

const browser = await chromium.launch({ args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] });
const errors = [];
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const sizeOk = async (page, ids) => {
  for (const id of ids) {
    const box = await byTestId(page, id).boundingBox();
    assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
  }
};
const progress = (page, current) => page.waitForFunction(
  text => document.querySelector('[data-testid="water-change-progress"]')?.textContent === text, `Step ${current} of 8`, { timeout: 30000 });
// The fake camera needs a moment before it delivers frames.
const shoot = async page => {
  await byTestId(page, "water-change-viewfinder").waitFor({ timeout: 30000 });
  for (let attempt = 0; attempt < 40; attempt++) {
    await byTestId(page, "water-change-capture").click();
    if (await byTestId(page, "water-change-review").waitFor({ timeout: 2000 }).then(() => true, () => false)) return;
  }
  throw new Error("The camera never delivered a picture");
};
const finishWithAmount = async (page, percent) => {
  await byTestId(page, "water-change-amount-input").waitFor({ timeout: 30000 });
  await byTestId(page, "water-change-tank-volume").waitFor();
  await byTestId(page, "water-change-unit-percent").click();
  await byTestId(page, "water-change-amount-input").fill(String(percent));
  await byTestId(page, "water-change-amount-reference").waitFor();
  await byTestId(page, "water-change-finish").click();
  await byTestId(page, "water-change-summary").waitFor({ timeout: 30000 });
};
let light, dark;
try {
  light = await browser.newContext({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  await light.grantPermissions(["camera"], { origin: new URL(WEB_URL).origin });
  const page = await light.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // AC #1: start from the care view, check off the steps, take several photos in the app, finish.
  await page.goto(`${WEB_URL}/tanks/${tank.id}?view=care`, { waitUntil: "networkidle" });
  await byTestId(page, "care-guided-water-change").click();
  await progress(page, 1);
  await sizeOk(page, ["water-change-done", "water-change-skip", "water-change-cancel"]);
  const timerText = () => byTestId(page, "water-change-timer").innerText();
  assert.match(await timerText(), /\d+:\d{2}/);
  await page.screenshot({ path: "/tmp/task-5.13-heater-light.png" });
  await byTestId(page, "water-change-done").click();
  await progress(page, 2);
  const drainStarted = Date.now();
  await byTestId(page, "water-change-done").click();
  await progress(page, 3);
  await byTestId(page, "water-change-drain-timer").waitFor();
  await shoot(page);
  await sizeOk(page, ["water-change-use", "water-change-retake", "water-change-photo-skip"]);
  await page.screenshot({ path: "/tmp/task-5.13-review-light.png" });
  await byTestId(page, "water-change-use").click();
  await page.getByText("Photo added (1 of 6)", { exact: true }).waitFor({ timeout: 30000 });
  await sizeOk(page, ["water-change-done", "water-change-another"]);
  await byTestId(page, "water-change-another").click();
  await shoot(page);
  await byTestId(page, "water-change-use").click();
  await page.getByText("Photo added (2 of 6)", { exact: true }).waitFor({ timeout: 30000 });
  await byTestId(page, "water-change-done").click();
  await progress(page, 4);
  await byTestId(page, "water-change-tip-conditioner").click();
  await sizeOk(page, ["water-change-tip-conditioner", "water-change-tip-temperature_matched"]);
  await byTestId(page, "water-change-done").click();
  await progress(page, 5);
  const wait = drainStarted + (minSeconds + 1) * 1000 - Date.now();
  if (wait > 0) await sleep(wait);
  await byTestId(page, "water-change-done").click();
  await progress(page, 6);
  await byTestId(page, "water-change-done").click();
  await progress(page, 7);
  await shoot(page);
  await byTestId(page, "water-change-use").click();
  await page.getByText("Photo added (3 of 6)", { exact: true }).waitFor({ timeout: 30000 });
  await byTestId(page, "water-change-done").click();
  await progress(page, 8);
  await finishWithAmount(page, 25);
  await page.screenshot({ path: "/tmp/task-5.13-summary-light.png" });
  await page.getByText("Counted as a verified water change.", { exact: true }).waitFor();
  assert.equal(await byTestId(page, "water-change-missing").count(), 0);
  await page.getByText("Amount: 25 %", { exact: true }).waitFor();
  await sizeOk(page, ["water-change-close"]);

  const first = (await entriesOf(tank)).items;
  assert.equal(first.length, 1);
  const entry = first[0];
  assert.equal(entry.kind, "water_change");
  assert.ok(entry.durationSeconds >= minSeconds, "the entry has the duration");
  assert.equal(entry.evidence.state, "verified");
  assert.deepEqual(entry.evidence.missing, []);
  assert.equal(entry.photos.length, 3, "all three photos are attached");
  assert.equal(entry.amount.unit, "percent");
  assert.ok(entry.waterChangeSessionId > 0);
  for (const photo of entry.photos) assert.ok(photo.urls.thumb.startsWith("/api/media/photos/"), "the photos use private signed URLs");
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}/water-changes/current`, { headers })).status, 404, "no session stays open");
  await byTestId(page, "water-change-close").click();
  await byTestId(page, `care-entry-${entry.id}`).waitFor({ timeout: 30000 });
  await page.getByText(/Guided, \d+:\d{2}, Counted as a verified water change\./).waitFor();

  // Today: the due care offers the guided mode; a reload resumes the wizard; skipped steps never block.
  dark = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  await dark.grantPermissions(["camera"], { origin: new URL(WEB_URL).origin });
  const darkPage = await dark.newPage();
  darkPage.on("pageerror", error => errors.push(String(error)));
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(darkPage);
  await darkPage.goto(WEB_URL, { waitUntil: "networkidle" });
  await byTestId(darkPage, `today-due-guided-${todayTank.id}`).click();
  await progress(darkPage, 1);
  await byTestId(darkPage, "water-change-done").click();
  await progress(darkPage, 2);
  // The mark of a step travels in the background; the reload comes after the server holds it.
  for (let attempt = 0; attempt < 50; attempt++) {
    const open = await fetch(`${API_URL}/api/tanks/${todayTank.id}/water-changes/current`, { headers });
    if (open.status === 200 && (await open.json()).steps.some(item => item.step === "heater_filter_off")) break;
    await sleep(200);
  }
  await darkPage.reload({ waitUntil: "networkidle" });
  await progress(darkPage, 2);
  await darkPage.screenshot({ path: "/tmp/task-5.13-resumed-dark.png" });
  for (let step = 2; step <= 7; step++) {
    await progress(darkPage, step);
    await byTestId(darkPage, step === 3 || step === 7 ? "water-change-photo-skip" : "water-change-skip").click();
  }
  await finishWithAmount(darkPage, 10);
  await darkPage.getByText("Logged. This one is not counted as verified.", { exact: true }).waitFor();
  await darkPage.getByText("The draining step was skipped.", { exact: true }).waitFor();
  await darkPage.getByText("No photo was taken with the camera of the app. With a photo, a water change counts for your streak.", { exact: true }).waitFor();
  await darkPage.screenshot({ path: "/tmp/task-5.13-partial-dark.png" });
  const second = (await entriesOf(todayTank)).items.find(item => item.waterChangeSessionId !== null);
  assert.equal(second.evidence.state, "partial");
  assert.deepEqual(second.evidence.missing, ["draining_missing", "photo_missing"]);
  assert.equal(second.photos.length, 0);

  // Long enough but without any photo: partial with the missing photo.
  await darkPage.goto(`${WEB_URL}/tanks/${tank.id}/water-change`, { waitUntil: "networkidle" });
  await progress(darkPage, 1);
  await byTestId(darkPage, "water-change-done").click();
  await progress(darkPage, 2);
  const started = Date.now();
  await byTestId(darkPage, "water-change-done").click();
  await progress(darkPage, 3);
  await byTestId(darkPage, "water-change-photo-skip").click();
  await progress(darkPage, 4);
  await byTestId(darkPage, "water-change-skip").click();
  await progress(darkPage, 5);
  const remaining = started + (minSeconds + 1) * 1000 - Date.now();
  if (remaining > 0) await sleep(remaining);
  await byTestId(darkPage, "water-change-done").click();
  await progress(darkPage, 6);
  await byTestId(darkPage, "water-change-skip").click();
  await progress(darkPage, 7);
  await byTestId(darkPage, "water-change-photo-skip").click();
  await progress(darkPage, 8);
  await byTestId(darkPage, "water-change-finish").click();
  await byTestId(darkPage, "water-change-summary").waitFor({ timeout: 30000 });
  await darkPage.getByText("Logged. This one is not counted as verified.", { exact: true }).waitFor();
  await darkPage.getByText("No photo was taken with the camera of the app. With a photo, a water change counts for your streak.", { exact: true }).waitFor();
  const third = (await entriesOf(tank)).items.find(item => item.id !== entry.id);
  assert.equal(third.evidence.state, "partial");
  assert.deepEqual(third.evidence.missing, ["photo_missing"]);
  assert.equal(third.photos.length, 0);

  // Stopping a water change discards it.
  await darkPage.goto(`${WEB_URL}/tanks/${tank.id}/water-change`, { waitUntil: "networkidle" });
  await progress(darkPage, 1);
  await byTestId(darkPage, "water-change-cancel").click();
  await byTestId(darkPage, "water-change-cancel-yes").click();
  await byTestId(darkPage, "care-guided-water-change").waitFor({ timeout: 30000 });
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}/water-changes/current`, { headers })).status, 404);

  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(darkPage);
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page).catch(() => {});
  assert.deepEqual(errors, []);
  console.log("TASK-5.13: guided steps, timers, several app camera photos, private evidence photos, verified entry with duration, resume after reload, skipped steps without blocking, partial evidence with the missing photo and cancel passed.");
} finally {
  await dark?.close();
  await light?.close();
  await browser.close();
}
