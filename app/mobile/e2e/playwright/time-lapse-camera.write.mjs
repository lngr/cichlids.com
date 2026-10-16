// Story: TASK-5.11
// The guided time-lapse camera on the web build: a tank with photos shows its last photo
// half-transparent over the viewfinder (opacity adjustable, hideable), the member reviews a shot
// and retakes it, and the used photo joins the series. Without the camera permission the screen
// explains the camera first, then shows a calm hint, and the library picker shows the picked photo
// under the last photo before the upload.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and
// the bucket. The browser gets a fake camera device and the camera permission of its context.
import assert from "node:assert/strict";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const PHOTO = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Guided camera tank", isPublic: true }) });
assert.equal(created.status, 201);
const tank = await created.json();

const browser = await chromium.launch({ args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] });
const errors = [];
const seriesOf = async () => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse`)).json()).photos;
const imageUrl = photo => photo.image.large ?? photo.image.medium ?? photo.image.small ?? photo.image.thumb;
const opacityOf = locator => locator.evaluate(element => Number(getComputedStyle(element).opacity));
const sizeOk = async (page, ids) => {
  for (const id of ids) {
    const box = await byTestId(page, id).boundingBox();
    assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
  }
};
try {
  // Two photos in the series, added through the API.
  const canvasPage = await browser.newPage();
  for (const hue of [20, 200]) {
    const bytes = await canvasPage.evaluate(async hueValue => {
      const canvas = document.createElement("canvas");
      canvas.width = 640;
      canvas.height = 480;
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = `hsl(${hueValue} 60% 40%)`;
      ctx.fillRect(0, 0, 640, 480);
      const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg"));
      return [...new Uint8Array(await blob.arrayBuffer())];
    }, hue);
    const body = new FormData();
    body.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), `series-${hue}.jpg`);
    const added = await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse/photos`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body });
    assert.equal(added.status, 201, await added.clone().text());
  }
  await canvasPage.close();
  const before = await seriesOf();
  assert.equal(before.length, 2);
  const lastUrl = imageUrl(before.at(-1));

  // Light: the camera with the overlay, the review with a retake, and the upload.
  const light = await browser.newContext({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  await light.grantPermissions(["camera"], { origin: new URL(WEB_URL).origin });
  const page = await light.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-time-lapse-photo").click();
  await byTestId(page, "time-lapse-viewfinder").waitFor({ timeout: 30000 });

  const overlay = byTestId(page, "time-lapse-overlay");
  await overlay.waitFor();
  assert.ok(Math.abs(await opacityOf(overlay) - 0.4) < 0.01, "the overlay starts at about 40 percent");
  await page.waitForFunction(url => [...document.querySelectorAll('[data-testid="time-lapse-overlay"] img')].some(image => image.src === url && image.naturalWidth > 0), lastUrl);
  await byTestId(page, "time-lapse-opacity").fill("80");
  await page.waitForFunction(() => Math.abs(Number(getComputedStyle(document.querySelector('[data-testid="time-lapse-overlay"]')).opacity) - 0.8) < 0.01);
  await byTestId(page, "time-lapse-overlay-toggle").click();
  await overlay.waitFor({ state: "detached" });
  await byTestId(page, "time-lapse-overlay-toggle").click();
  await overlay.waitFor();
  await sizeOk(page, ["time-lapse-capture", "time-lapse-overlay-toggle", "time-lapse-opacity", "time-lapse-pick"]);
  await page.screenshot({ path: "/tmp/task-5.11-camera-light.png" });

  // The fake camera needs a moment before it delivers frames.
  const shoot = async () => {
    for (let attempt = 0; attempt < 40; attempt++) {
      await byTestId(page, "time-lapse-capture").click();
      const reviewed = await byTestId(page, "time-lapse-review").waitFor({ timeout: 2000 }).then(() => true, () => false);
      if (reviewed) return;
    }
    throw new Error("The camera never delivered a picture");
  };
  await shoot();
  assert.ok(Math.abs(await opacityOf(byTestId(page, "time-lapse-overlay")) - 0.8) < 0.01, "the review keeps the overlay settings");
  await byTestId(page, "time-lapse-retake").click();
  await byTestId(page, "time-lapse-viewfinder").waitFor();
  assert.equal((await seriesOf()).length, 2, "a retaken shot is not uploaded");
  await shoot();
  await sizeOk(page, ["time-lapse-use", "time-lapse-retake"]);
  await byTestId(page, "time-lapse-use").click();
  await byTestId(page, "tank-time-lapse-photo").waitFor({ timeout: 30000 });
  const afterCamera = await seriesOf();
  assert.equal(afterCamera.length, 3, "the new photo joins the series");
  assert.equal(afterCamera.at(-1).mediaItemId > before.at(-1).mediaItemId, true);
  await page.getByText("Photos: 3", { exact: true }).waitFor({ timeout: 30000 });

  // Dark: the permission explanation, then the denied hint with the library picker.
  const dark = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  await dark.addInitScript(() => {
    let state = "prompt";
    navigator.permissions.query = async () => ({ state, addEventListener() {}, removeEventListener() {}, onchange: null });
    navigator.mediaDevices.getUserMedia = async () => {
      state = "denied";
      throw new DOMException("Permission denied", "NotAllowedError");
    };
  });
  const darkPage = await dark.newPage();
  darkPage.on("pageerror", error => errors.push(String(error)));
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(darkPage);
  await darkPage.goto(`${WEB_URL}/tanks/${tank.id}/camera`, { waitUntil: "networkidle" });
  await byTestId(darkPage, "time-lapse-explain").waitFor({ timeout: 30000 });
  await sizeOk(darkPage, ["time-lapse-allow", "time-lapse-pick"]);
  await darkPage.screenshot({ path: "/tmp/task-5.11-explain-dark.png" });
  await byTestId(darkPage, "time-lapse-allow").click();
  await byTestId(darkPage, "time-lapse-denied").waitFor({ timeout: 30000 });
  assert.equal(await byTestId(darkPage, "time-lapse-settings").count(), 0, "the web has no settings link");
  await darkPage.screenshot({ path: "/tmp/task-5.11-denied-dark.png" });

  // The library picker shows the picked photo under the last photo, then uploads it.
  await byTestId(darkPage, "time-lapse-pick-input").setInputFiles(PHOTO);
  await byTestId(darkPage, "time-lapse-review").waitFor({ timeout: 30000 });
  assert.ok(Math.abs(await opacityOf(byTestId(darkPage, "time-lapse-overlay")) - 0.4) < 0.01, "the picked photo is compared under the last photo");
  await darkPage.screenshot({ path: "/tmp/task-5.11-review-dark.png" });
  await byTestId(darkPage, "time-lapse-use").click();
  await byTestId(darkPage, "tank-time-lapse-photo").waitFor({ timeout: 30000 });
  assert.equal((await seriesOf()).length, 4);

  // A member who does not own the tank sees no action and cannot add to the series.
  const guest = await browser.newContext({ locale: "en-US", viewport: { width: 390, height: 844 } });
  const guestPage = await guest.newPage();
  await guestPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(guestPage, "tank-time-machine-open").waitFor({ timeout: 30000 });
  assert.equal(await byTestId(guestPage, "tank-time-lapse-photo").count(), 0);
  const refused = await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse/photos`, { method: "POST", body: new FormData() });
  assert.equal(refused.status, 401);
  assert.equal((await refused.json()).code, "tank_photo_auth_required");
  await guest.close();

  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(darkPage);
  await dark.close();
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page).catch(() => {});
  await light.close();
  assert.deepEqual(errors, []);
  console.log("TASK-5.11: overlay at 40 percent over the viewfinder, opacity and toggle, review with retake, upload into the series, permission explanation, denied hint, library comparison and owner only passed.");
} finally {
  await browser.close();
}
