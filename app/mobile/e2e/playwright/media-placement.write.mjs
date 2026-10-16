// Story: TASK-5.20.2
// One rule decides where the files of a media item lie and who gets a link to them. A photo of a
// private tank is served through a signed path to its owner; once a published gallery picture shows
// the same photo, the photo loads from the public area for anonymous viewers while the tank stays
// hidden. A photo of a care entry in a public history moves to the private area when the entry is set to
// only me, and a signed link stops working when the photo is deleted.
//
// A gallery picture shows a tank photo through a media link that the write database receives directly,
// since the app has no route that shares a tank photo yet. Runs on the isolated write stack only (see
// e2e/write-stack.sh), since it writes to the database and the bucket.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");

function sql(statement) {
  return execFileSync(
    "docker",
    ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", statement],
    { stdio: ["ignore", "pipe", "pipe"] },
  ).toString().trim();
}

async function until(description, check) {
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline) {
    if (await check()) return;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`Timed out waiting for ${description}`);
}

const token = await passwordGrantToken();
const auth = { Authorization: `Bearer ${token}` };
const json = { ...auth, "Content-Type": "application/json" };
const call = async (method, url, body) => fetch(`${API_URL}${url}`, { method, headers: json, body: body === undefined ? undefined : JSON.stringify(body) });

const browser = await chromium.launch();
const errors = [];
try {
  const canvasPage = await browser.newPage();
  const photo = async hue => {
    const bytes = await canvasPage.evaluate(async hueValue => {
      const canvas = document.createElement("canvas");
      canvas.width = 1200;
      canvas.height = 800;
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = `hsl(${hueValue} 60% 40%)`;
      ctx.fillRect(0, 0, 1200, 800);
      ctx.fillStyle = `hsl(${(hueValue + 120) % 360} 70% 60%)`;
      ctx.fillRect(hueValue % 300, 60, 180, 140);
      const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg"));
      return [...new Uint8Array(await blob.arrayBuffer())];
    }, hue);
    const form = new FormData();
    form.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), `placement-${hue}.jpg`);
    return form;
  };

  // A photo of a private tank is stored privately and served through a signed path.
  const privateTank = await (await call("POST", "/api/tanks", { title: "Placement private", isPublic: false })).json();
  const added = await fetch(`${API_URL}/api/tanks/${privateTank.id}/time-lapse/photos`, { method: "POST", headers: auth, body: await photo(20) });
  assert.equal(added.status, 201, await added.clone().text());
  const tankPhoto = await added.json();
  assert.ok(tankPhoto.image.thumb.startsWith("/api/media/photos/"));
  assert.equal((await fetch(`${API_URL}${tankPhoto.image.thumb}`)).status, 200);
  assert.equal(sql(`SELECT count(*) FROM media_variant WHERE media_item_id = ${tankPhoto.mediaItemId} AND storage_key NOT LIKE 'private/variants/%'`), "0");
  assert.equal((await fetch(`${API_URL}/api/tanks/${privateTank.id}/time-lapse`)).status, 404);

  // A published picture shows the same photo: it moves to the public area and the tank stays hidden.
  const draft = await (await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: auth, body: await photo(200) })).json();
  const publish = await call("POST", `/api/posts/${draft.id}/publish`, { title: "Placement story", topic: "tanks" });
  assert.equal(publish.status, 200, await publish.clone().text());
  const story = await publish.json();
  sql(`UPDATE media_link SET media_item_id = ${tankPhoto.mediaItemId} WHERE post_id = ${draft.id};
       UPDATE media_item SET placement_pending = true, placement_next_attempt_at = NULL WHERE id = ${tankPhoto.mediaItemId}`);
  await until("the photo to move into the public area", () => sql(`SELECT placed_public AND NOT placement_pending FROM media_item WHERE id = ${tankPhoto.mediaItemId}`) === "t");
  const detail = await (await fetch(`${API_URL}/api/pictures/${story.slug}`)).json();
  assert.ok(detail.image.thumb.startsWith("http") && detail.image.thumb.includes("/variants/"), detail.image.thumb);
  assert.equal((await fetch(detail.image.thumb)).status, 200);
  assert.equal((await fetch(`${API_URL}/api/tanks/${privateTank.id}`)).status, 404);
  assert.equal((await fetch(`${API_URL}/api/tanks/${privateTank.id}/time-lapse`)).status, 404);

  const page = await browser.newPage({ locale: "en-US", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/gallery/${story.slug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 30000 });
  try {
    await page.waitForFunction(() => [...document.images].some(image => image.complete && image.naturalWidth > 0 && image.src.includes("/variants/")), undefined, { timeout: 30000 });
  } catch (error) {
    console.error(await page.evaluate(() => [...document.images].map(image => `${image.src} (${image.naturalWidth})`)));
    throw error;
  }

  // A care photo of a public history lies in the public area until the entry is set to only me.
  const publicTank = await (await call("POST", "/api/tanks", { title: "Placement public", isPublic: true })).json();
  const entry = await (await call("POST", `/api/tanks/${publicTank.id}/care`, { kind: "water_change", amount: { value: 20, unit: "percent" } })).json();
  const careAdded = await fetch(`${API_URL}/api/tanks/${publicTank.id}/care/${entry.id}/photos`, { method: "POST", headers: auth, body: await photo(100) });
  assert.equal(careAdded.status, 201, await careAdded.clone().text());
  const carePhoto = (await careAdded.json()).photos[0];
  const careItem = sql(`SELECT media_item_id FROM media_link WHERE care_entry_id = ${entry.id}`);
  await until("the care photo to move into the public area", () => sql(`SELECT placed_public AND NOT placement_pending FROM media_item WHERE id = ${careItem}`) === "t");
  assert.equal(sql(`SELECT count(*) FROM media_variant WHERE media_item_id = ${careItem} AND storage_key LIKE 'private/%'`), "0");

  const onlyMe = await call("PATCH", `/api/tanks/${publicTank.id}/care/${entry.id}`, { onlyMe: true });
  assert.equal(onlyMe.status, 200);
  await until("the care photo to move into the private area", () => sql(`SELECT NOT placed_public AND NOT placement_pending FROM media_item WHERE id = ${careItem}`) === "t");
  assert.equal(sql(`SELECT count(*) FROM media_variant WHERE media_item_id = ${careItem} AND storage_key NOT LIKE 'private/variants/%'`), "0");
  const anonymousCare = await fetch(`${API_URL}/api/tanks/${publicTank.id}/care`);
  assert.ok(!(await anonymousCare.text()).includes("/api/media/photos/"), "an anonymous viewer gets no link to an entry set to only me");
  const ownCare = await (await call("GET", `/api/tanks/${publicTank.id}/care`)).json();
  const ownThumb = ownCare.items[0].photos[0].urls.thumb;
  assert.equal((await fetch(`${API_URL}${ownThumb}`)).status, 200);

  // A signed link stops working when the photo is deleted.
  const removed = await call("DELETE", `/api/tanks/${publicTank.id}/care/${entry.id}/photos/${carePhoto.id}`);
  assert.equal(removed.status, 200);
  assert.equal((await fetch(`${API_URL}${ownThumb}`)).status, 404);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.20.2: the files of a media item follow one visibility rule and private media use one signed route");
} finally {
  await browser.close();
}
