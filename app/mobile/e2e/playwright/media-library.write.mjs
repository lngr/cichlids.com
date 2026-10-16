// Story: TASK-5.20.4
// Every member has a media library with all files they own, linked or not. A photo whose care entry is
// deleted is still there, a photo uploaded into the library sits there without a place, and the detail
// of a file names the places that show it. Deleting a file asks first, names the places, and then
// removes the file from all of them: the story that showed it as its only item disappears, the tank
// section loses the photo and the signed link of the file stops working.
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
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");

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

const TANK_TITLE = `Library path tank ${Date.now()}`;
const STORY_TITLE = `Library path story ${Date.now()}`;
const created = await call("POST", "/api/tanks", { title: TANK_TITLE, isPublic: true, volume: { value: 120, unit: "liter" } });
assert.equal(created.status, 201);
const tank = await created.json();

const browser = await chromium.launch();
const errors = [];
let page;
try {
  const canvasPage = await browser.newPage();
  const jpegBytes = async hue => canvasPage.evaluate(async hueValue => {
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
  const photoForm = async hue => {
    const form = new FormData();
    form.append("file", new Blob([new Uint8Array(await jpegBytes(hue))], { type: "image/jpeg" }), `library-${hue}.jpg`);
    return form;
  };
  const libraryList = async query => (await (await fetch(`${API_URL}/api/me/media${query ?? ""}`, { headers: auth })).json());

  // A care entry with a photo, deleted afterwards: the photo stays.
  const entryResponse = await call("POST", `/api/tanks/${tank.id}/care`, { kind: "water_change", amount: { value: 25, unit: "percent" } });
  assert.equal(entryResponse.status, 201);
  const entry = await entryResponse.json();
  const careAdded = await fetch(`${API_URL}/api/tanks/${tank.id}/care/${entry.id}/photos`, { method: "POST", headers: auth, body: await photoForm(20) });
  assert.equal(careAdded.status, 201, await careAdded.clone().text());
  const careItem = Number(sql(`SELECT media_item_id FROM media_link WHERE care_entry_id = ${entry.id} AND role = 'photo'`));
  assert.equal((await call("DELETE", `/api/tanks/${tank.id}/care/${entry.id}`)).status, 200);
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id = ${careItem}`), "1");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE media_item_id = ${careItem}`), "0");

  // A photo that sits in a tank section and in a published story.
  const draftResponse = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: auth, body: await photoForm(200) });
  assert.equal(draftResponse.status, 201, await draftResponse.clone().text());
  const draft = await draftResponse.json();
  const publish = await call("POST", `/api/stories/${draft.id}/publish`, { title: STORY_TITLE, topic: "tanks" });
  assert.equal(publish.status, 200, await publish.clone().text());
  const story = await publish.json();
  const sharedItem = Number(sql(`SELECT media_item_id FROM media_link WHERE story_id = ${draft.id}`));
  sql(`INSERT INTO media_link (media_item_id, context_type, tank_id, role, position) VALUES (${sharedItem}, 'tank', ${tank.id}, 'showcase', 0)`);

  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "me-media").click({ timeout: 20000 });
  await page.waitForURL(/\/media$/, { timeout: 10000 });
  await byTestId(page, `library-tile-${careItem}`).waitFor({ timeout: 30000 });

  // The photo of the deleted care entry is in the library, shown nowhere.
  const careTile = byTestId(page, `library-tile-${careItem}`);
  assert.equal(await careTile.locator('[data-testid="library-tile-unused"]').count(), 1);
  assert.match(await careTile.getAttribute("aria-label"), /not shown anywhere/);
  const tileBox = await careTile.boundingBox();
  assert.ok(tileBox.width >= 48 && tileBox.height >= 48, "tiles meet the touch target size");
  await careTile.click();
  await byTestId(page, "library-places-empty").waitFor({ timeout: 20000 });
  await byTestId(page, "library-detail-close").click();
  await byTestId(page, "library-detail").waitFor({ state: "detached", timeout: 10000 });

  // The unused filter lists the care photo and leaves out the photo that has places.
  await byTestId(page, "library-use-unlinked").click();
  await byTestId(page, `library-tile-${careItem}`).waitFor({ timeout: 20000 });
  await byTestId(page, `library-tile-${sharedItem}`).waitFor({ state: "detached", timeout: 20000 });
  await byTestId(page, "library-use-linked").click();
  await byTestId(page, `library-tile-${sharedItem}`).waitFor({ timeout: 20000 });
  await byTestId(page, `library-tile-${careItem}`).waitFor({ state: "detached", timeout: 20000 });
  await byTestId(page, "library-use-all").click();
  await byTestId(page, `library-tile-${careItem}`).waitFor({ timeout: 20000 });

  // Uploading a photo into the library adds a file without a place.
  const before = (await libraryList("?limit=1")).total;
  await byTestId(page, "library-upload-input").setInputFiles({ name: "library-upload.jpg", mimeType: "image/jpeg", buffer: Buffer.from(await jpegBytes(310)) });
  let uploadedId = 0;
  for (let attempt = 0; attempt < 40 && !uploadedId; attempt++) {
    const listed = await libraryList("?limit=1");
    if (listed.total === before + 1) uploadedId = Number(listed.items[0].id);
    else await new Promise(resolve => setTimeout(resolve, 500));
  }
  assert.ok(uploadedId, "the upload created a file in the library");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE media_item_id = ${uploadedId}`), "0");
  const uploadedTile = byTestId(page, `library-tile-${uploadedId}`);
  await uploadedTile.waitFor({ timeout: 20000 });
  assert.equal(await uploadedTile.locator('[data-testid="library-tile-unused"]').count(), 1);

  // The detail of a file names the places that show it.
  await byTestId(page, `library-tile-${sharedItem}`).click();
  await byTestId(page, "library-place").first().waitFor({ timeout: 20000 });
  const places = await byTestId(page, "library-place").allTextContents();
  assert.equal(places.length, 2);
  assert.ok(places.some(text => text.includes(TANK_TITLE)), places.join(" | "));
  assert.ok(places.some(text => text.includes(STORY_TITLE)), places.join(" | "));
  const thumb = (await libraryList("?limit=50")).items.find(item => Number(item.id) === sharedItem).image.thumb;
  assert.equal((await fetch(thumb)).status, 200);

  // Deleting asks first and names the places; keeping the file changes nothing.
  const deleteButton = byTestId(page, "library-delete");
  const deleteBox = await deleteButton.boundingBox();
  assert.ok(deleteBox.height >= 48, "the delete button meets the touch target size");
  await deleteButton.click();
  await byTestId(page, "library-confirm").waitFor({ timeout: 10000 });
  const confirmed = await byTestId(page, "library-confirm-place").allTextContents();
  assert.ok(confirmed.some(text => text.includes(TANK_TITLE)) && confirmed.some(text => text.includes(STORY_TITLE)), confirmed.join(" | "));
  await byTestId(page, "library-delete-cancel").click();
  await byTestId(page, "library-confirm").waitFor({ state: "detached", timeout: 10000 });
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id = ${sharedItem}`), "1");

  // Dark mode shows the same sheet with its own colors.
  const lightBackground = await byTestId(page, "library-detail").evaluate(element => getComputedStyle(element.parentElement).backgroundColor);
  await page.emulateMedia({ colorScheme: "dark" });
  await page.waitForFunction(light => getComputedStyle(document.querySelector('[data-testid="library-detail"]').parentElement).backgroundColor !== light, lightBackground, { timeout: 10000 });
  await page.emulateMedia({ colorScheme: "light" });

  await byTestId(page, "library-delete").click();
  await byTestId(page, "library-delete-confirm").click();
  await byTestId(page, "library-detail").waitFor({ state: "detached", timeout: 20000 });
  await byTestId(page, `library-tile-${sharedItem}`).waitFor({ state: "detached", timeout: 20000 });

  // The only app photo of a week of a time-lapse keeps the streak: the detail and the confirmation say so.
  const lapseTitle = `Library streak tank ${Date.now()}`;
  const lapseTank = await (await call("POST", "/api/tanks", { title: lapseTitle, isPublic: true, volume: { value: 90, unit: "liter" } })).json();
  const lapseForm = await photoForm(75);
  lapseForm.append("capturedInApp", "true");
  const lapseAdded = await fetch(`${API_URL}/api/tanks/${lapseTank.id}/time-lapse/photos`, { method: "POST", headers: auth, body: lapseForm });
  assert.equal(lapseAdded.status, 201, await lapseAdded.clone().text());
  const lapseItem = (await lapseAdded.json()).mediaItemId;
  const today = new Date().toISOString().slice(0, 10);
  const lapseDetail = await (await fetch(`${API_URL}/api/me/media/${lapseItem}?date=${today}&utcOffsetMinutes=0`, { headers: auth })).json();
  assert.equal(lapseDetail.keepsStreak.length, 1);
  assert.equal(lapseDetail.keepsStreak[0].tankTitle, lapseTitle);
  await page.reload({ waitUntil: "networkidle" });
  await byTestId(page, `library-tile-${lapseItem}`).click({ timeout: 30000 });
  await byTestId(page, "library-streak-note").waitFor({ timeout: 20000 });
  assert.ok((await byTestId(page, "library-streak-note").textContent()).includes(lapseTitle));
  await byTestId(page, "library-delete").click();
  await byTestId(page, "library-confirm-streak").waitFor({ timeout: 10000 });
  assert.match(await byTestId(page, "library-confirm-streak").textContent(), new RegExp(`${lapseTitle}.*(shortens or breaks)`));
  await byTestId(page, "library-delete-cancel").click();
  await byTestId(page, "library-detail-close").click();
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id = ${lapseItem}`), "1");

  // The file is gone from the story and the tank, and its signed link stops working.
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id = ${sharedItem}`), "0");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE media_item_id = ${sharedItem}`), "0");
  assert.equal((await fetch(`${API_URL}/api/stories/${story.slug}`)).status, 404);
  assert.equal((await fetch(thumb)).status, 404);
  assert.equal((await call("GET", `/api/me/media/${sharedItem}`)).status, 404);
  assert.equal((await call("DELETE", `/api/me/media/${sharedItem}`)).status, 404);
  assert.equal((await fetch(`${API_URL}/api/me/media`)).status, 401);
  const remaining = await libraryList("?limit=50");
  assert.ok(remaining.items.some(item => Number(item.id) === careItem), "the other files stay");

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.20.4: the library keeps files without places, names their places and deletes a file from all of them after a confirmation");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  throw error;
} finally {
  await browser.close();
}
