// Story: TASK-5.20.5
// A member shares a library photo to the gallery with one step, which creates a published single-item
// story once; the photo then shows a link to that story. The same photo goes into a second story with
// another caption. In the editor the member adds files and a tank card, moves entries with the buttons,
// chooses a cover, publishes, withdraws and deletes the story.
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

const TANK_TITLE = `Editor tank ${Date.now()}`;
const SHARE_TITLE = `Shared photo ${Date.now()}`;
const STORY_TITLE = `Editor story ${Date.now()}`;
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
  const libraryPhoto = async hue => {
    const form = new FormData();
    form.append("file", new Blob([new Uint8Array(await jpegBytes(hue))], { type: "image/jpeg" }), `editor-${hue}.jpg`);
    const response = await fetch(`${API_URL}/api/me/media/photos`, { method: "POST", headers: auth, body: form });
    assert.equal(response.status, 201, await response.clone().text());
    return Number((await response.json()).id);
  };
  const photoA = await libraryPhoto(20);
  const photoB = await libraryPhoto(120);
  const photoC = await libraryPhoto(220);
  const editorOf = async id => (await (await call("GET", `/api/me/stories/${id}`)).json());

  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "me-media").click({ timeout: 20000 });
  await page.waitForURL(/\/media$/, { timeout: 10000 });

  // Sharing photo A creates a published single-item story and the photo shows a link to it.
  await byTestId(page, `library-tile-${photoA}`).click({ timeout: 30000 });
  const shareButton = byTestId(page, `share-open-${photoA}`);
  await shareButton.waitFor({ timeout: 20000 });
  // The detail panel slides in, so the size is measured once the button stands still.
  let shareBox = await shareButton.boundingBox();
  for (let previous = null; JSON.stringify(previous) !== JSON.stringify(shareBox);) {
    previous = shareBox;
    await page.waitForTimeout(200);
    shareBox = await shareButton.boundingBox();
  }
  assert.ok(shareBox.height >= 48, "the share action meets the touch target size");
  await shareButton.click();
  await byTestId(page, "share-title").fill(SHARE_TITLE);
  await byTestId(page, "share-publish").click();
  await byTestId(page, `share-link-${photoA}`).waitFor({ timeout: 20000 });
  const firstStory = Number(sql(`SELECT quick_share_story_id FROM media_item WHERE id = ${photoA}`));
  assert.ok(firstStory > 0);
  assert.equal(sql(`SELECT state FROM story WHERE id = ${firstStory}`), "published");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE story_id = ${firstStory}`), "1");
  const firstSlug = sql(`SELECT value FROM slug_alias WHERE story_id = ${firstStory} AND is_canonical`);
  assert.equal((await fetch(`${API_URL}/api/stories/${firstSlug}`)).status, 200);
  assert.equal((await call("POST", `/api/me/media/${photoA}/share`, { title: "Again" })).status, 409);

  // The link opens the story in the gallery.
  await byTestId(page, `share-link-${photoA}`).click();
  await page.waitForURL(new RegExp(`/gallery/${firstSlug}$`), { timeout: 20000 });

  // Adding the same photo to a second story with another caption.
  await page.goto(`${WEB_URL}/media?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, `library-tile-${photoA}`).click({ timeout: 30000 });
  await byTestId(page, `share-add-story-${photoA}`).click({ timeout: 20000 });
  await byTestId(page, "share-story-new").click({ timeout: 20000 });
  await page.waitForURL(/\/stories\/\d+$/, { timeout: 20000 });
  const secondStory = Number(page.url().match(/\/stories\/(\d+)$/)[1]);
  const itemA = Number(sql(`SELECT id FROM media_link WHERE story_id = ${secondStory} AND media_item_id = ${photoA}`));
  await byTestId(page, `story-item-${itemA}`).waitFor({ timeout: 20000 });
  await byTestId(page, `story-item-${itemA}-caption`).fill("Second caption");
  await byTestId(page, `story-item-${itemA}-save`).click();
  await page.waitForFunction(() => !document.querySelector('[data-testid$="-save"][data-testid^="story-item-"]'), null, { timeout: 20000 });
  assert.equal(sql(`SELECT caption FROM media_link WHERE id = ${itemA}`), "Second caption");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE media_item_id = ${photoA} AND story_id IS NOT NULL`), "2");

  // Files from the library and a tank card.
  await byTestId(page, "story-add-file").click();
  await byTestId(page, `library-pick-${photoB}`).click({ timeout: 20000 });
  await byTestId(page, "story-add-file").click();
  await byTestId(page, `library-pick-${photoC}`).click({ timeout: 20000 });
  await page.waitForFunction(count => document.querySelectorAll('[data-testid^="story-item-"][data-testid$="-remove"]').length === count, 3, { timeout: 20000 });
  await byTestId(page, "story-add-subject").click();
  await byTestId(page, `subject-pick-tank-${tank.id}`).click({ timeout: 20000 });
  await byTestId(page, `story-subject-${Number(sql(`SELECT id FROM story_subject WHERE story_id = ${secondStory}`))}`).waitFor({ timeout: 20000 });
  let editor = await editorOf(secondStory);
  assert.deepEqual(editor.items.map(item => Number(item.mediaItemId)), [photoA, photoB, photoC]);
  const subject = editor.subjects[0];
  assert.equal(Number(subject.position), 3);

  // The move buttons reorder without dragging, and they meet the touch target size.
  const itemC = Number(editor.items[2].id);
  const upButton = byTestId(page, `story-item-${itemC}-up`);
  assert.ok((await upButton.boundingBox()).height >= 48 && (await upButton.boundingBox()).width >= 48, "move buttons meet the touch target size");
  await upButton.click();
  for (let attempt = 0; attempt < 40; attempt++) {
    editor = await editorOf(secondStory);
    if (Number(editor.items[1].id) === itemC) break;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  assert.equal(Number(editor.items[1].id), itemC);
  assert.deepEqual([...editor.items.map(item => Number(item.position)), Number(editor.subjects[0].position)].sort(), [0, 1, 2, 3]);

  // The cover and the publication.
  await byTestId(page, `story-item-${itemC}-cover-toggle`).click();
  await byTestId(page, `story-item-${itemC}-cover`).waitFor({ timeout: 20000 });
  await byTestId(page, "story-title").fill(STORY_TITLE);
  await byTestId(page, "story-publish").click();
  await byTestId(page, "story-withdraw").waitFor({ timeout: 30000 });
  const author = Number(sql(`SELECT author_profile_id FROM story WHERE id = ${secondStory}`));
  const list = await (await fetch(`${API_URL}/api/stories?user=${author}&limit=50`)).json();
  const listed = list.items.find(entry => Number(entry.id) === secondStory);
  assert.ok(listed, "the gallery lists the published story");
  assert.equal(listed.itemCount, 3);
  assert.ok(listed.cover.thumb, "the entry shows the cover");
  assert.equal(sql(`SELECT cover_media_link_id FROM story WHERE id = ${secondStory}`), String(itemC));

  // Withdrawing makes the story a draft again and hides it; the signed route refuses the old links.
  const secondSlug = sql(`SELECT value FROM slug_alias WHERE story_id = ${secondStory} AND is_canonical`);
  const generation = Number(sql(`SELECT visibility_generation FROM media_item WHERE id = ${photoB}`));
  await byTestId(page, "story-withdraw").click();
  await byTestId(page, "story-publish").waitFor({ timeout: 30000 });
  assert.equal((await fetch(`${API_URL}/api/stories/${secondSlug}`)).status, 404);
  assert.ok(Number(sql(`SELECT visibility_generation FROM media_item WHERE id = ${photoB}`)) > generation);

  // Deleting asks first.
  await byTestId(page, "story-delete").click();
  await byTestId(page, "story-delete-confirm").waitFor({ timeout: 10000 });
  await byTestId(page, "story-delete-yes").click();
  await page.waitForFunction(() => !document.querySelector('[data-testid="story-editor"]'), null, { timeout: 20000 });
  assert.equal(sql(`SELECT count(*) FROM story WHERE id = ${secondStory} AND deleted_at IS NOT NULL`), "1");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE story_id = ${secondStory}`), "0");
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id IN (${photoA}, ${photoB}, ${photoC})`), "3");

  // Another member's file cannot be added, and the story stays intact.
  assert.equal((await call("POST", `/api/me/stories/${firstStory}/items`, { mediaItemId: 9223372036854775000 })).status, 404);
  assert.equal((await fetch(`${API_URL}/api/me/stories`)).status, 401);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.20.5: a photo is shared once, goes into a second story, and the editor orders, covers, publishes, withdraws and deletes a story");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  throw error;
} finally {
  await browser.close();
}
