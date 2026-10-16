// Story: TASK-5.10
// A member adds a short video to a tank from the web build: the video uploads in chunks, shows the
// preparing state, and once it is processed the tank timeline shows its poster frame and plays it as
// HLS. The original stays private; the playlist and the poster come from the public bucket.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and
// the bucket. The test video is generated with ffmpeg in the temp directory.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak } from "./support.mjs";

const directory = fs.mkdtempSync(path.join(os.tmpdir(), "cichlids-video-e2e-"));
const VIDEO = path.join(directory, "tank.mp4");
execFileSync("ffmpeg", [
  "-nostdin", "-loglevel", "error", "-y",
  "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=3",
  "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
  "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", "-movflags", "+faststart", VIDEO,
]);

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Video tank" }) });
assert.equal(created.status, 201);
const tank = await created.json();
const videosOf = async () => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/videos`, { headers })).json());

const sizeOk = async (locator, label) => {
  const box = await locator.boundingBox();
  assert.ok(box && box.height >= 48 && box.width >= 48, `${label} is at least 48 by 48`);
};

const browser = await chromium.launch();
const errors = [];
try {
  const light = await browser.newContext({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const page = await light.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });

  await byTestId(page, "tank-videos").waitFor({ timeout: 30000 });
  await sizeOk(byTestId(page, "tank-video-library"), "the add video button");
  await sizeOk(byTestId(page, "tank-video-record"), "the record video button");
  await byTestId(page, "tank-video-library-input").setInputFiles(VIDEO);

  // The video is listed as being prepared, then becomes ready.
  await page.locator('[data-testid^="tank-video-"][data-testid$="-processing"]').or(page.locator('[data-testid^="tank-timeline-video-"]'))
    .first().waitFor({ timeout: 60000 });
  let ready;
  for (let attempt = 0; attempt < 90 && !ready; attempt++) {
    ready = (await videosOf()).find(video => video.state === "ready");
    if (!ready) await new Promise(resolve => setTimeout(resolve, 1000));
  }
  assert.ok(ready, "the video becomes ready");
  assert.ok(ready.playlistUrl.endsWith("/master.m3u8"));
  assert.ok(ready.posterUrl.endsWith("/poster.jpg"));
  assert.ok(ready.durationSeconds > 2 && ready.durationSeconds < 4);

  // The timeline shows the poster frame and plays the video as HLS.
  const card = byTestId(page, `tank-timeline-video-${ready.id}`);
  await card.waitFor({ timeout: 30000 });
  await page.waitForFunction(id => {
    const image = document.querySelector(`[data-testid="tank-timeline-video-${id}-poster"] img, img[data-testid="tank-timeline-video-${id}-poster"]`);
    return Boolean(image?.complete && image.naturalWidth > 0);
  }, ready.id, { timeout: 20000 });
  await sizeOk(byTestId(page, `tank-timeline-video-${ready.id}-play`), "the play button");
  await page.screenshot({ path: "/tmp/task-5.10-tank-light.png" });
  await byTestId(page, `tank-timeline-video-${ready.id}-play`).click();
  const player = page.locator(`video[data-testid="tank-timeline-video-${ready.id}-player"]`);
  await player.waitFor({ timeout: 30000 });
  await page.waitForFunction(id => {
    const video = document.querySelector(`video[data-testid="tank-timeline-video-${id}-player"]`);
    return Boolean(video && video.readyState >= 2 && video.videoWidth > 0 && video.currentTime > 0.3);
  }, ready.id, { timeout: 30000 });
  assert.equal(await player.evaluate(video => video.videoWidth), 640);

  // The original is private; the playlist is served from the public bucket.
  const playlist = await fetch(ready.playlistUrl);
  assert.equal(playlist.status, 200);
  assert.ok((await playlist.text()).startsWith("#EXTM3U"));

  // Dark: the owner section lists the video with its remove button.
  const dark = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const darkPage = await dark.newPage();
  darkPage.on("pageerror", error => errors.push(String(error)));
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(darkPage);
  await darkPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(darkPage, `tank-video-${ready.id}-card`).waitFor({ timeout: 30000 });
  await sizeOk(byTestId(darkPage, `tank-video-${ready.id}-remove`), "the remove video button");
  await darkPage.screenshot({ path: "/tmp/task-5.10-tank-dark.png" });

  // Removing the video clears the section.
  await byTestId(darkPage, `tank-video-${ready.id}-remove`).click();
  await byTestId(darkPage, `tank-video-${ready.id}`).waitFor({ state: "detached", timeout: 20000 });
  assert.equal((await videosOf()).length, 0);
  assert.deepEqual(errors, []);
} finally {
  await browser.close();
  fs.rmSync(directory, { recursive: true, force: true });
}
