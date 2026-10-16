// Story: TASK-3.28
// Given a migrated member with pictures, tanks and drafts, opening Me shows their pictures.
// Switching sections compares each list with the API; scrolling checks pagination. Cards open
// their details, the public profile stays reachable, and failed requests offer a retry.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");
function sql(statement) {
  assert.equal(process.env.E2E_DB, "cichlids_e2e");
  return execFileSync("docker", ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", statement], { stdio: ["ignore", "pipe", "pipe"] }).toString().trim();
}
const token = await passwordGrantToken();
async function get(route) {
  const response = await fetch(`${API_URL}/api/${route}`, { headers: { Authorization: `Bearer ${token}` } });
  assert.equal(response.status, 200, route);
  return response.json();
}
const { profile } = await get("me");
const profileId = Number(profile.id);
assert.ok(Number.isSafeInteger(profileId));
const legacyId = sql(`SELECT COALESCE(legacy_id::text, 'NULL') FROM profile WHERE id = ${profileId}`);
const browser = await chromium.launch();
try {
  sql(`UPDATE profile SET legacy_id = 928000028 WHERE id = ${profileId};
    WITH seeded AS (
      INSERT INTO story (author_profile_id, topic, state, title, created_at, updated_at, published_at, view_count, rating_count)
      SELECT ${profileId}, 'cichlids', 'published', 'Me story picture ' || n, now(), now(), now(), 0, 0 FROM generate_series(1, 23) n RETURNING id
    ) INSERT INTO slug_alias (story_id, value, is_canonical, created_at) SELECT id, 'me-story-' || id, true, now() FROM seeded;
    INSERT INTO tank (profile_id, title, state, created_at) SELECT ${profileId}, 'Me story tank ' || n, 'published', now() FROM generate_series(1, 21) n;
    INSERT INTO story (author_profile_id, topic, state, title, created_at, updated_at, view_count, rating_count)
    VALUES (${profileId}, 'cichlids', 'draft', 'Me story draft', now(), now(), 0, 0)`);
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const errors = [];
  page.on("pageerror", (error) => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "me-tab-pictures").waitFor({ timeout: 10000 });
  assert.equal(await byTestId(page, "me-tab-pictures").getAttribute("aria-selected"), "true");
  assert.equal(await byTestId(page, "drafts-section").count(), 0);

  async function compareCards(kind, cardId) {
    const expected = await get(`profiles/${profileId}/${kind === "pictures" ? "stories" : kind}?limit=100`);
    assert.ok(expected.total > 20);
    await page.mouse.move(195, 650);
    for (let attempt = 0; attempt < 40; attempt++) {
      if (await byTestId(page, cardId).count() === expected.total) break;
      await byTestId(page, cardId).last().scrollIntoViewIfNeeded();
      await page.mouse.wheel(0, -500);
      await page.waitForTimeout(100);
      await page.mouse.wheel(0, 2000);
      await page.waitForTimeout(150);
    }
    const labels = await byTestId(page, cardId).evaluateAll((cards) => cards.map((card) => card.getAttribute("aria-label")));
    assert.deepEqual(labels, expected.items.map((item) => item.title));
    return expected.items;
  }
  const pictures = await compareCards("pictures", "picture-card");
  await byTestId(page, "picture-card").first().click();
  await page.waitForURL(`${WEB_URL}/gallery/${pictures[0].slug}`);
  await byTestId(page, "picture-title").waitFor();
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(page, "me-tab-tanks").click();
  const tanks = await compareCards("tanks", "tank-card");
  await byTestId(page, "tank-card").first().click();
  await page.waitForURL(`${WEB_URL}/tanks/${tanks[0].id}`);
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(page, "me-tab-drafts").click();
  const drafts = await get("me/drafts");
  await page.waitForFunction((count) => document.querySelectorAll('[data-testid="draft-item"]').length === count, drafts.length);
  assert.deepEqual(await byTestId(page, "draft-title").allTextContents(), drafts.map((draft) => draft.title?.trim() || "Untitled"));
  assert.equal(await byTestId(page, "picture-card").count(), 0);
  await byTestId(page, "me-profile-button").click();
  await page.waitForURL(`${WEB_URL}/profile/${profileId}`);
  await byTestId(page, "picture-card").first().waitFor();

  for (const kind of ["pictures", "tanks"]) {
    const route = `**/api/profiles/${profileId}/${kind === "pictures" ? "stories" : kind}?*`;
    await page.route(route, (request) => request.abort("connectionfailed"));
    await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
    await byTestId(page, `me-tab-${kind}`).click();
    await byTestId(page, "error-retry").waitFor();
    assert.ok((await page.locator("body").textContent()).includes("The connection failed. You stay signed in"));
    assert.equal(await byTestId(page, "login-button").count(), 0);
    await page.unroute(route);
    await byTestId(page, "error-retry").click();
    await byTestId(page, kind === "pictures" ? "picture-card" : "tank-card").first().waitFor();
  }
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("Me pictures and tanks match the profile API across pages; drafts match the private API; details, profile link, retry and logout pass.");
} finally {
  await browser.close();
  sql(`DELETE FROM slug_alias WHERE story_id IN (SELECT id FROM story WHERE author_profile_id = ${profileId} AND title LIKE 'Me story %'); DELETE FROM story WHERE author_profile_id = ${profileId} AND title LIKE 'Me story %'; DELETE FROM tank WHERE profile_id = ${profileId} AND title LIKE 'Me story %'; UPDATE profile SET legacy_id = ${legacyId} WHERE id = ${profileId}`);
}
