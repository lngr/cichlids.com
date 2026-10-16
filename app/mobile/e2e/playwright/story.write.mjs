// Story: TASK-5.20.3
// A gallery entry is a story. A picture with one photo shows its photo, title and text at its address.
// A story of three photos with captions, a card for a public tank, a card for a private tank and a
// chosen cover shows that cover in the gallery; its page swipes through the photos and the public
// card in order, and the card for the private tank appears for the owner only.
//
// The photos of the story come from uploads that are merged into one story directly in the write
// database, since the app has no editor for stories yet. Runs on the isolated write stack only (see
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
  const upload = async hue => {
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
    form.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), `story-${hue}.jpg`);
    const response = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: auth, body: form });
    assert.equal(response.status, 201, await response.clone().text());
    return response.json();
  };
  const publish = async (draft, title) => {
    const response = await call("POST", `/api/stories/${draft.id}/publish`, { title, topic: "tanks" });
    assert.equal(response.status, 200, await response.clone().text());
    return response.json();
  };

  const page = await browser.newPage({ locale: "en-US", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));

  // A picture with one photo is a story with one item at the same address.
  const singleTitle = `Single story ${crypto.randomUUID()}`;
  const single = await publish(await upload(30), singleTitle);
  const singleDetail = await (await fetch(`${API_URL}/api/stories/${single.slug}`)).json();
  assert.equal(singleDetail.items.length, 1);
  assert.equal(singleDetail.subjects.length, 0);
  await page.goto(`${WEB_URL}/gallery/${single.slug}`, { waitUntil: "networkidle", timeout: 60000 });
  assert.equal((await byTestId(page, "picture-title").textContent())?.trim(), singleTitle);
  await byTestId(page, "story-page-0").waitFor({ timeout: 30000 });
  assert.equal(await byTestId(page, "story-position").count(), 0, "a story with one item has no position indicator");

  // A story of three photos, a public tank, a private tank and a chosen cover.
  const title = `Three photo story ${crypto.randomUUID()}`;
  const drafts = [await upload(60), await upload(140), await upload(250)];
  const story = await publish(drafts[0], title);
  const publicTank = await (await call("POST", "/api/tanks", { title: `Story public tank ${crypto.randomUUID()}`, isPublic: true })).json();
  const privateTank = await (await call("POST", "/api/tanks", { title: `Story private tank ${crypto.randomUUID()}`, isPublic: false })).json();
  assert.ok([story.id, publicTank.id, privateTank.id, drafts[1].id, drafts[2].id].every(id => Number.isSafeInteger(Number(id))));
  sql(`UPDATE media_link SET caption = 'First caption' WHERE story_id = ${story.id};
       INSERT INTO media_link (media_item_id, context_type, role, position, story_id, caption)
         SELECT media_item_id, 'story', 'item', 2, ${story.id}, 'Second caption' FROM media_link WHERE story_id = ${drafts[1].id};
       INSERT INTO media_link (media_item_id, context_type, role, position, story_id, caption)
         SELECT media_item_id, 'story', 'item', 3, ${story.id}, 'Third caption' FROM media_link WHERE story_id = ${drafts[2].id};
       DELETE FROM story WHERE id IN (${drafts[1].id}, ${drafts[2].id});
       UPDATE story SET cover_media_link_id = (SELECT id FROM media_link WHERE story_id = ${story.id} AND position = 3) WHERE id = ${story.id};
       INSERT INTO story_subject (story_id, subject_type, tank_id, position) VALUES
         (${story.id}, 'tank', ${publicTank.id}, 1), (${story.id}, 'tank', ${privateTank.id}, 4)`);
  await until("the photos of the story to move into the public area", () =>
    sql(`SELECT count(*) FROM media_item WHERE id IN (SELECT media_item_id FROM media_link WHERE story_id = ${story.id}) AND (NOT placed_public OR placement_pending)`) === "0");

  // The gallery shows the chosen cover, the third photo.
  const anonymous = await (await fetch(`${API_URL}/api/stories/${story.slug}`)).json();
  assert.deepEqual(anonymous.items.map(item => item.caption), ["First caption", "Second caption", "Third caption"]);
  assert.deepEqual(anonymous.subjects.map(subject => [subject.type, subject.position, subject.tank?.id]), [["tank", 1, publicTank.id]]);
  const coverUrl = anonymous.items[2].image.small;
  assert.notEqual(coverUrl, anonymous.items[0].image.small);
  const listed = await (await fetch(`${API_URL}/api/stories?limit=50`)).json();
  const entry = listed.items.find(item => item.id === story.id);
  assert.equal(entry.cover.small, coverUrl);
  assert.equal(Number(entry.itemCount), 3);
  await page.goto(`${WEB_URL}/gallery`, { waitUntil: "networkidle", timeout: 60000 });
  const card = byTestId(page, "picture-card").filter({ hasText: title });
  await card.waitFor({ timeout: 30000 });
  await page.waitForFunction(({ title: expectedTitle, url }) => {
    const cards = [...document.querySelectorAll('[data-testid="picture-card"]')].filter(element => element.textContent?.includes(expectedTitle));
    return cards.some(element => [...element.querySelectorAll("img")].some(image => image.src === url && image.complete && image.naturalWidth > 0));
  }, { title, url: coverUrl }, { timeout: 30000 });

  // The story page swipes through the photos and the public card in their order; the private card is left out.
  await page.goto(`${WEB_URL}/gallery/${story.slug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "story-page-0").waitFor({ timeout: 30000 });
  assert.equal(await byTestId(page, "story-position").textContent(), "1 / 4");
  assert.equal((await byTestId(page, "story-page-0").locator('[data-testid="story-caption"]').textContent())?.trim(), "First caption");
  assert.equal(await byTestId(page, "story-subject-tank").count(), 1);
  const pageContents = await byTestId(page, "story-sequence").locator('[data-testid^="story-page-"]').evaluateAll(
    pages => pages.map(element => element.querySelector('[data-testid="story-caption"]')?.textContent
      ?? (element.querySelector('[data-testid^="story-subject-"]') ? "card" : "")));
  assert.deepEqual(pageContents, ["First caption", "card", "Second caption", "Third caption"]);
  await page.evaluate(() => {
    const root = document.querySelector('[data-testid="story-sequence"]');
    const scroller = [...root.querySelectorAll("div")].find(element => element.scrollWidth > element.clientWidth + 1);
    scroller.scrollTo({ left: scroller.clientWidth * 3, behavior: "instant" });
  });
  await byTestId(page, "story-sequence").hover();
  await page.mouse.wheel(1200, 0);
  await page.waitForFunction(() => document.querySelector('[data-testid="story-position"]')?.textContent === "4 / 4", undefined, { timeout: 15000 });
  assert.equal(await byTestId(page, "picture-title").textContent(), title);

  // The owner also sees the card for the private tank.
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/gallery/${story.slug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "story-page-0").waitFor({ timeout: 30000 });
  await page.waitForFunction(() => document.querySelector('[data-testid="story-position"]')?.textContent === "1 / 5", undefined, { timeout: 30000 });
  assert.equal(await byTestId(page, "story-subject-tank").count(), 2);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.20.3: gallery entries are stories with items, captions, cards, a cover and subjects the viewer may see");
} finally {
  await browser.close();
}
