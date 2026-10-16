// Story: TASK-5.20.1
// Every photo a member attaches is a media link: the photos of a tank series, of a care entry, as
// evidence of a guided water change and in a gallery picture. A member adds each kind through the
// app's routes and sees them again where they belong (the time machine of the tank, the care entry,
// the verified evidence, the gallery and the picture page). The write database then holds one link
// per photo with its context, role and position, the capture source and the receive time on the
// media item, and none of the former attachment tables. Removing a care photo addresses the link.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and
// the bucket.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const minSeconds = Number(process.env.E2E_WATER_CHANGE_MIN_SECONDS);
assert.ok(minSeconds > 0 && minSeconds <= 10, "the write stack sets a short minimum duration");
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");

// Runs one read statement against the write database and returns its unaligned output.
function sql(statement) {
  return execFileSync(
    "docker",
    ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", statement],
    { stdio: ["ignore", "pipe", "pipe"] },
  ).toString().trim();
}

const token = await passwordGrantToken();
const json = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const auth = { Authorization: `Bearer ${token}` };
const call = async (method, url, body) => fetch(`${API_URL}${url}`, { method, headers: json, body: body === undefined ? undefined : JSON.stringify(body) });

const created = await call("POST", "/api/tanks", { title: "Media links path", isPublic: true, volume: { value: 160, unit: "liter" } });
assert.equal(created.status, 201);
const tank = await created.json();

const browser = await chromium.launch();
const errors = [];
let page;
try {
  const canvasPage = await browser.newPage();
  const photo = async hue => {
    const bytes = await canvasPage.evaluate(async hueValue => {
      const canvas = document.createElement("canvas");
      canvas.width = 640;
      canvas.height = 480;
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = `hsl(${hueValue} 60% 40%)`;
      ctx.fillRect(0, 0, 640, 480);
      ctx.fillStyle = `hsl(${(hueValue + 90) % 360} 70% 60%)`;
      ctx.fillRect(hueValue % 300, 40, 200, 120);
      const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg"));
      return [...new Uint8Array(await blob.arrayBuffer())];
    }, hue);
    const form = new FormData();
    form.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), `media-links-${hue}.jpg`);
    return form;
  };

  // Two photos of the tank series, shot with the camera of the app.
  for (const hue of [30, 210]) {
    const form = await photo(hue);
    form.append("capturedInApp", "true");
    const added = await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse/photos`, { method: "POST", headers: auth, body: form });
    assert.equal(added.status, 201, await added.clone().text());
  }
  const series = (await (await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse`)).json()).photos;
  assert.equal(series.length, 2);

  // A water change entry with a photo and a second photo that is removed again.
  const entryResponse = await call("POST", `/api/tanks/${tank.id}/care`, { kind: "water_change", amount: { value: 30, unit: "percent" } });
  assert.equal(entryResponse.status, 201);
  const entry = await entryResponse.json();
  const keptPhoto = await fetch(`${API_URL}/api/tanks/${tank.id}/care/${entry.id}/photos`, { method: "POST", headers: auth, body: await photo(120) });
  assert.equal(keptPhoto.status, 201, await keptPhoto.clone().text());
  const droppedPhoto = await fetch(`${API_URL}/api/tanks/${tank.id}/care/${entry.id}/photos`, { method: "POST", headers: auth, body: await photo(300) });
  assert.equal(droppedPhoto.status, 201);
  const withTwo = await droppedPhoto.json();
  assert.equal(withTwo.photos.length, 2);
  const [keptId, droppedId] = withTwo.photos.map(p => p.id);
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE care_entry_id = ${entry.id} AND role = 'photo' AND id IN (${keptId}, ${droppedId})`), "2");
  const removed = await call("DELETE", `/api/tanks/${tank.id}/care/${entry.id}/photos/${droppedId}`);
  assert.equal(removed.status, 200);
  assert.deepEqual((await removed.json()).photos.map(p => p.id), [keptId]);
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE id = ${droppedId}`), "0");

  // A guided water change whose photo, taken with the camera of the app, is the evidence.
  const started = await call("POST", `/api/tanks/${tank.id}/water-changes`);
  assert.equal(started.status, 201);
  const session = await started.json();
  assert.equal((await call("PUT", `/api/tanks/${tank.id}/water-changes/${session.id}/steps/draining_started`, { state: "done" })).status, 200);
  const evidenceForm = await photo(60);
  evidenceForm.append("capturedInApp", "true");
  const evidenceAdded = await fetch(`${API_URL}/api/tanks/${tank.id}/water-changes/${session.id}/photos`, { method: "POST", headers: auth, body: evidenceForm });
  assert.equal(evidenceAdded.status, 200, await evidenceAdded.clone().text());
  const evidenceSession = await evidenceAdded.json();
  assert.equal(evidenceSession.photos.length, 1);
  assert.equal(evidenceSession.photos[0].capturedInApp, true);
  await new Promise(resolve => setTimeout(resolve, (minSeconds + 1) * 1000));
  assert.equal((await call("PUT", `/api/tanks/${tank.id}/water-changes/${session.id}/steps/refill_started`, { state: "done" })).status, 200);
  const finishedResponse = await call("POST", `/api/tanks/${tank.id}/water-changes/${session.id}/finish`, {});
  assert.equal(finishedResponse.status, 200);
  const finished = await finishedResponse.json();
  assert.equal(finished.entry.evidence.state, "verified");
  assert.equal(finished.entry.photos.length, 1);

  // A picture of the gallery.
  const draftResponse = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: auth, body: await photo(180) });
  assert.equal(draftResponse.status, 201, await draftResponse.clone().text());
  const draft = await draftResponse.json();
  const publish = await call("POST", `/api/stories/${draft.id}/publish`, { title: "Media links picture", topic: "tanks" });
  assert.equal(publish.status, 200, await publish.clone().text());
  const picture = await publish.json();
  const listed = await (await fetch(`${API_URL}/api/stories?limit=50`)).json();
  assert.ok(listed.items.some(item => item.id === draft.id && item.cover?.thumb), "the gallery lists the picture with its image");

  // The links in the database: one per photo with its context, role and position.
  const links = sql(`SELECT context_type || ':' || role || ':' || position FROM media_link
      WHERE tank_id = ${tank.id} OR care_entry_id IN (${entry.id}, ${finished.entry.id})
        OR water_change_session_id = ${session.id} OR story_id = ${draft.id} ORDER BY 1`).split("\n");
  assert.deepEqual(links, [
    "care_entry:photo:0", "care_entry:photo:0", "story:item:0", "tank:time_lapse:0", "tank:time_lapse:1", "water_change_session:evidence:0",
  ]);
  // The finished water change shows the photo of its session as a care photo of its entry.
  assert.equal(sql(`SELECT count(DISTINCT media_item_id) FROM media_link WHERE water_change_session_id = ${session.id} OR care_entry_id = ${finished.entry.id}`), "1");
  // The capture source and the receive time belong to the media item.
  assert.equal(sql(`SELECT count(*) FROM media_item m JOIN media_link l ON l.media_item_id = m.id
      WHERE (l.tank_id = ${tank.id} OR l.water_change_session_id = ${session.id}) AND m.captured_in_app AND m.received_at IS NOT NULL`), "3");
  assert.equal(sql(`SELECT count(*) FROM media_item m JOIN media_link l ON l.media_item_id = m.id
      WHERE (l.care_entry_id = ${entry.id} OR l.story_id = ${draft.id}) AND NOT m.captured_in_app`), "2");
  assert.equal(sql(`SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename IN
      ('tank_media', 'post_media', 'care_photo', 'care_video', 'water_change_session_photo', 'discussion_post_media')`), "0");

  // The member sees the photos again where they belong.
  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-time-machine-open").click();
  await page.waitForFunction(() => [...document.querySelectorAll('[data-testid="tank-time-machine-frame"] img')].some(image => image.complete && image.naturalWidth > 0), undefined, { timeout: 30000 });
  await byTestId(page, "tank-time-machine-close").click();

  await byTestId(page, "tank-view-care").click();
  for (const careEntry of [entry.id, finished.entry.id]) {
    await byTestId(page, `care-photos-${careEntry}`).waitFor({ timeout: 30000 });
    await page.waitForFunction(
      id => {
        const image = document.querySelector(`[data-testid="care-photos-${id}"] img`);
        return Boolean(image?.complete && image.naturalWidth > 0 && image.src.includes("/api/media/photos/"));
      },
      careEntry,
      { timeout: 20000 },
    );
  }

  await page.goto(`${WEB_URL}/gallery`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-card").first().waitFor({ timeout: 30000 });
  await page.waitForFunction(() => [...document.querySelectorAll('[data-testid="picture-card"] img')].some(image => image.complete && image.naturalWidth > 0), undefined, { timeout: 30000 });
  await page.goto(`${WEB_URL}/gallery/${picture.slug ?? listed.items.find(item => item.id === draft.id).slug}`, { waitUntil: "networkidle" });
  await byTestId(page, "picture-title").waitFor({ timeout: 30000 });
  assert.equal((await byTestId(page, "picture-title").textContent())?.trim(), "Media links picture");

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.20.1: the photos of a tank series, a care entry, a guided water change and the gallery are media links and show where they belong");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  throw error;
} finally {
  await browser.close();
}
