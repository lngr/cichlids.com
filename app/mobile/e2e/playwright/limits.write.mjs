// Story: TASK-3.37
// Logs in as the local dev user and checks the limits on comments and drafts. A comment of 10001
// characters shows the character counter above the limit and cannot be sent from the app; the API
// refuses it with the code comment_too_long, while a comment of exactly 10000 characters is
// posted. A member who holds 50 open drafts (seeded in the write
// database without photos) gets the draft limit message when uploading another photo and sees no
// preview. Runs on the isolated write stack
// only (see e2e/write-stack.sh), since seeding, commenting and uploading write to the database.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, KEYCLOAK_URL, PASSWORD, USERNAME, WEB_URL, byTestId, submitKeycloakLogin } from "./support.mjs";

const PICTURE_OWNER = "dev-mod";
const SPEC_DIR = path.dirname(fileURLToPath(import.meta.url));
const FIXTURE = path.join(SPEC_DIR, "..", "fixtures", "upload-photo.jpg");
const COMPOSE_FILE = path.resolve(SPEC_DIR, "../../../stack/compose.yaml");
const DRAFT_LIMIT = 50;
const COMMENT_LIMIT = 10000;

async function tokenOf(username) {
  const response = await fetch(`${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "cichlids-app", grant_type: "password", username, password: PASSWORD }),
  });
  if (!response.ok) throw new Error(`Password grant returned ${response.status}: ${await response.text()}`);
  return (await response.json()).access_token;
}

async function seedPublishedPicture(title) {
  const headers = { Authorization: `Bearer ${await tokenOf(PICTURE_OWNER)}` };

  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(FIXTURE)], { type: "image/jpeg" }), "limits-e2e.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers, body: form });
  if (!upload.ok) throw new Error(`POST /api/uploads returned ${upload.status}: ${await upload.text()}`);
  const draft = await upload.json();

  const publish = await fetch(`${API_URL}/api/posts/${draft.id}/publish`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (!publish.ok) throw new Error(`POST /api/posts/${draft.id}/publish returned ${publish.status}: ${await publish.text()}`);
  return (await publish.json()).slug;
}

// Runs a statement in the write database; the profile id is the only value spliced into it.
function psql(sql) {
  if (process.env.E2E_DB !== "cichlids_e2e") throw new Error("Refusing to write outside the cichlids_e2e database");
  execFileSync(
    "docker",
    ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-c", sql],
    { stdio: "pipe" },
  );
}

function seedEmptyDrafts(profileId, count) {
  if (!/^\d+$/.test(profileId) || !Number.isInteger(count)) throw new Error("Unexpected draft seed values");
  psql(
    `INSERT INTO post (author_profile_id, kind, topic, state, created_at, view_count, rating_count) ` +
      `SELECT ${profileId}, 'single', 'cichlids', 'draft', now(), 0, 0 FROM generate_series(1, ${count})`,
  );
}

// Removes the seeded drafts, which have no media, so later specs start without open drafts.
function removeEmptyDrafts(profileId) {
  if (!/^\d+$/.test(profileId)) throw new Error("Unexpected profile id");
  psql(
    `DELETE FROM post WHERE author_profile_id = ${profileId} AND state = 'draft' ` +
      `AND NOT EXISTS (SELECT 1 FROM post_media pm WHERE pm.post_id = post.id)`,
  );
}

const slug = await seedPublishedPicture(`Limits target ${Date.now()}`);
console.log(`Seeded published picture /gallery/${slug}`);

const browser = await chromium.launch();
let seededFor = null;
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  const input = byTestId(page, "comment-input");
  const submit = byTestId(page, "comment-submit");
  const counter = byTestId(page, "comment-counter");

  await page.goto(`${WEB_URL}/gallery/${slug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 20000 });
  await submitKeycloakLogin(page, byTestId(page, "comment-login"));
  await input.waitFor({ timeout: 30000 });

  // A comment above the limit shows the counter above the limit and cannot be sent.
  await input.fill("a".repeat(COMMENT_LIMIT + 1));
  const overText = (await counter.textContent())?.replace(/\s+/g, " ").trim();
  if (!overText?.includes(`${COMMENT_LIMIT + 1}`) || !overText.includes(`${COMMENT_LIMIT}`)) {
    throw new Error(`Expected the counter to show ${COMMENT_LIMIT + 1} of ${COMMENT_LIMIT}, got "${overText}"`);
  }
  if ((await submit.getAttribute("aria-disabled")) !== "true") throw new Error("Submit is enabled for a comment above the limit");
  console.log(`A comment of ${COMMENT_LIMIT + 1} characters shows the counter "${overText}" and cannot be sent`);

  // The API refuses the same comment with the code comment_too_long.
  const token = await tokenOf(USERNAME);
  const refused = await fetch(`${API_URL}/api/pictures/${slug}/comments`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ body: "a".repeat(COMMENT_LIMIT + 1) }),
  });
  const refusal = await refused.json();
  if (refused.status !== 400 || refusal.code !== "comment_too_long") {
    throw new Error(`Expected 400 comment_too_long, got ${refused.status} ${JSON.stringify(refusal)}`);
  }
  console.log(`The API refuses it: 400 "${refusal.detail}"`);

  // Trimmed to the limit, the comment is sent and appears.
  await input.fill("b".repeat(COMMENT_LIMIT));
  if ((await submit.getAttribute("aria-disabled")) === "true") throw new Error("Submit is disabled for a comment of exactly the limit");
  await submit.click();
  await byTestId(page, "comment-item").first().waitFor({ timeout: 20000 });
  if ((await input.inputValue()) !== "") throw new Error("The comment input is not cleared after posting");
  console.log(`A comment of exactly ${COMMENT_LIMIT} characters is posted`);

  // A member with the maximum number of open drafts is refused another upload with a message.
  const me = await (await fetch(`${API_URL}/api/me`, { headers: { Authorization: `Bearer ${token}` } })).json();
  seededFor = String(me.profile.id);
  seedEmptyDrafts(seededFor, DRAFT_LIMIT);

  await page.goto(`${WEB_URL}/gallery`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "upload-button").click({ timeout: 20000 });
  await page.waitForURL(/\/upload$/, { timeout: 10000 });
  await byTestId(page, "upload-file-input").setInputFiles({ name: "limit.jpg", mimeType: "image/jpeg", buffer: fs.readFileSync(FIXTURE) });
  const error = byTestId(page, "upload-error");
  await error.waitFor({ timeout: 20000 });
  const errorText = (await error.textContent())?.trim() ?? "";
  if (!errorText.includes("limit of 50 open drafts")) throw new Error(`Expected the draft limit message, got "${errorText}"`);
  if ((await byTestId(page, "upload-preview").count()) !== 0) throw new Error("A preview is visible after the refused upload");
  console.log(`A member with ${DRAFT_LIMIT} open drafts sees "${errorText}"`);

  if (pageErrors.length > 0) throw new Error(`Page errors: ${pageErrors.join("; ")}`);
} finally {
  if (seededFor) removeEmptyDrafts(seededFor);
  await browser.close();
}
