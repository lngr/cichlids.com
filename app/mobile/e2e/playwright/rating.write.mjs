// Story: TASK-3.36
// A member rates a picture of another member twice: the picture keeps one rating with the new
// value, and the stars of the second post show at its comment. On a picture of the member's own
// the composer offers no stars, a comment without stars is posted, and the API refuses stars with
// 403 problem details (code self_rating) without writing anything. Pictures are seeded through the
// API as the local moderator and as the dev user. Runs on the isolated write stack only (see
// e2e/write-stack.sh), since seeding and rating write to the database.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, KEYCLOAK_URL, PASSWORD, USERNAME, WEB_URL, byTestId, submitKeycloakLogin } from "./support.mjs";

const FIXTURE = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");

async function tokenOf(username) {
  const response = await fetch(`${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "cichlids-app", grant_type: "password", username, password: PASSWORD }),
  });
  if (!response.ok) throw new Error(`Password grant for ${username} returned ${response.status}: ${await response.text()}`);
  return (await response.json()).access_token;
}

async function seedPublishedPicture(username, title) {
  const headers = { Authorization: `Bearer ${await tokenOf(username)}` };

  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(FIXTURE)], { type: "image/jpeg" }), "rating-e2e.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers, body: form });
  if (!upload.ok) throw new Error(`POST /api/uploads returned ${upload.status}: ${await upload.text()}`);
  const draft = await upload.json();

  const publish = await fetch(`${API_URL}/api/stories/${draft.id}/publish`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (!publish.ok) throw new Error(`POST /api/stories/${draft.id}/publish returned ${publish.status}: ${await publish.text()}`);
  return (await publish.json()).slug;
}

async function getPicture(slug) {
  const response = await fetch(`${API_URL}/api/stories/${slug}`);
  if (!response.ok) throw new Error(`GET /api/stories/${slug} returned ${response.status}`);
  return response.json();
}

const otherSlug = await seedPublishedPicture("dev-mod", `Rating target ${Date.now()}`);
const ownSlug = await seedPublishedPicture(USERNAME, `Own picture ${Date.now()}`);
console.log(`Seeded /gallery/${otherSlug} (owned by dev-mod) and /gallery/${ownSlug} (owned by ${USERNAME})`);

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  const waitForSummary = (summary) =>
    page.waitForFunction(
      (expected) => document.querySelector('[data-testid="picture-meta"]')?.textContent?.includes(expected),
      summary,
      { timeout: 20000 },
    );

  await page.goto(`${WEB_URL}/gallery/${otherSlug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 20000 });
  await submitKeycloakLogin(page, byTestId(page, "comment-login"));
  await byTestId(page, "comment-input").waitFor({ timeout: 30000 });

  await byTestId(page, "comment-star-2").click();
  await byTestId(page, "comment-submit").click();
  await waitForSummary("★★ (1)");
  console.log("A first rating of 2 stars is counted once");

  const commentText = `Changed my mind ${Date.now()}`;
  await byTestId(page, "comment-input").fill(commentText);
  await byTestId(page, "comment-star-5").click();
  await byTestId(page, "comment-submit").click();
  await waitForSummary("★★★★★ (1)");
  const postedItem = byTestId(page, "comment-item").filter({ hasText: commentText });
  await postedItem.waitFor({ timeout: 20000 });
  const stars = (await byTestId(postedItem, "comment-item-stars").textContent())?.trim();
  if (stars !== "★★★★★") throw new Error(`Expected 5 stars on the new comment, got "${stars}"`);
  const rated = await getPicture(otherSlug);
  if (Number(rated.ratingCount) !== 1 || Number(rated.ratingAverage) !== 5) {
    throw new Error(`Expected one rating of 5 stars, got ${rated.ratingCount} rating(s) averaging ${rated.ratingAverage}`);
  }
  console.log("A repeat rating of 5 stars replaces the first: one rating with the new value, shown at the new comment");

  await page.goto(`${WEB_URL}/gallery/${ownSlug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 20000 });
  await byTestId(page, "comment-input").waitFor({ timeout: 30000 });
  await byTestId(page, "comment-stars").waitFor({ state: "detached", timeout: 20000 });
  if ((await byTestId(page, "comment-star-1").count()) !== 0) throw new Error("The star picker is offered on the member's own picture");
  const ownText = `Thanks for looking ${Date.now()}`;
  await byTestId(page, "comment-input").fill(ownText);
  await byTestId(page, "comment-submit").click();
  const ownItem = byTestId(page, "comment-item").filter({ hasText: ownText });
  await ownItem.waitFor({ timeout: 20000 });
  if ((await byTestId(ownItem, "comment-item-stars").count()) !== 0) throw new Error("The comment on the own picture shows stars");
  console.log("The own picture offers no stars and takes a comment without stars");

  const refused = await fetch(`${API_URL}/api/stories/${ownSlug}/comments`, {
    method: "POST",
    headers: { Authorization: `Bearer ${await tokenOf(USERNAME)}`, "Content-Type": "application/json" },
    body: JSON.stringify({ body: null, stars: 5 }),
  });
  const problem = await refused.json().catch(() => ({}));
  if (refused.status !== 403 || problem.code !== "self_rating") {
    throw new Error(`Expected 403 with code self_rating, got ${refused.status} ${JSON.stringify(problem)}`);
  }
  const own = await getPicture(ownSlug);
  if (Number(own.ratingCount) !== 0) throw new Error(`Expected no rating on the own picture, got ${own.ratingCount}`);
  console.log("The API refuses stars on the own picture with 403 self_rating and writes no rating");

  if (pageErrors.length > 0) throw new Error(`Page errors: ${pageErrors.join("; ")}`);
} finally {
  await browser.close();
}
