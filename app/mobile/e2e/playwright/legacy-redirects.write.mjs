// Story: TASK-3.38
// Requests legacy URLs on the API of the write stack. The legacy URL of a published picture
// redirects permanently to its canonical route, while wildcard characters in the alias answer 410.
// After the picture is archived its legacy URL answers 410, and after it is published again the
// URL redirects again. A tank behaves the same way between published and draft. No browser is
// involved; the spec calls Keycloak and the API directly and sets the states in the write
// database. Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to
// the database.
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { API_URL, KEYCLOAK_URL, PASSWORD, USERNAME } from "./support.mjs";

const E2E_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const COMPOSE_FILE = path.resolve(E2E_DIR, "../../stack/compose.yaml");
const FIXTURE = path.join(E2E_DIR, "fixtures", "upload-photo.jpg");

// Runs one statement against the write database and returns its unaligned output.
function sql(statement) {
  if (process.env.E2E_DB !== "cichlids_e2e") throw new Error("Refusing to write outside the cichlids_e2e database");
  return execFileSync(
    "docker",
    [
      "compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e",
      "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", statement,
    ],
    { stdio: ["ignore", "pipe", "pipe"] },
  ).toString().trim();
}

async function passwordGrantToken(username) {
  const response = await fetch(`${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "cichlids-app", grant_type: "password", username, password: PASSWORD }),
  });
  if (!response.ok) throw new Error(`Password grant for ${username} returned ${response.status}: ${await response.text()}`);
  return (await response.json()).access_token;
}

async function publishPicture(token, title) {
  const headers = { Authorization: `Bearer ${token}` };
  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(FIXTURE)], { type: "image/jpeg" }), "legacy-redirects-e2e.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers, body: form });
  if (!upload.ok) throw new Error(`POST /api/uploads returned ${upload.status}: ${await upload.text()}`);
  const draft = await upload.json();

  const publish = await fetch(`${API_URL}/api/stories/${draft.id}/publish`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (!publish.ok) throw new Error(`POST /api/stories/${draft.id}/publish returned ${publish.status}: ${await publish.text()}`);
  return publish.json();
}

// Requests a legacy URL without following the redirect.
async function legacy(urlPath) {
  const response = await fetch(`${API_URL}${urlPath}`, { redirect: "manual" });
  return { status: response.status, location: response.headers.get("location") };
}

async function expectRedirect(urlPath, expectedLocation) {
  const { status, location } = await legacy(urlPath);
  if (status !== 301 || location !== expectedLocation) {
    throw new Error(`${urlPath} answered ${status} ${location}, expected 301 ${expectedLocation}`);
  }
}

async function expectGone(urlPath) {
  const { status } = await legacy(urlPath);
  if (status !== 410) throw new Error(`${urlPath} answered ${status}, expected 410`);
}

const token = await passwordGrantToken(USERNAME);
const picture = await publishPicture(token, `Legacy redirect ${crypto.randomUUID()}`);
const storyId = String(picture.id);
if (!/^\d+$/.test(storyId)) throw new Error(`Unexpected post id ${storyId}`);
const slug = picture.canonicalSlug;
const profileId = sql(`SELECT author_profile_id FROM story WHERE id = ${storyId}`);

await expectRedirect(`/pictures/pic/${slug}.html`, `/pictures/${slug}`);
console.log(`The legacy URL of the published picture redirects to /pictures/${slug}`);

await expectGone("/pictures/pic/%25.html");
await expectGone(`/pictures/pic/${slug.slice(0, -1) + "_"}.html`);
console.log("Wildcard characters in a legacy picture alias answer 410");

sql(`UPDATE story SET state = 'archived' WHERE id = ${storyId}`);
await expectGone(`/pictures/pic/${slug}.html`);
console.log("The legacy URL of the archived picture answers 410");

sql(`UPDATE story SET state = 'published' WHERE id = ${storyId}`);
await expectRedirect(`/pictures/pic/${slug}.html`, `/pictures/${slug}`);
console.log("After the picture is published again its legacy URL redirects again");

const legacyTankId = 900000000 + Math.floor(Math.random() * 90000000);
const tankId = sql(
  `INSERT INTO tank (legacy_id, profile_id, title, state, created_at) ` +
    `VALUES (${legacyTankId}, ${Number(profileId)}, 'Legacy redirect tank', 'published', now()) RETURNING id`,
).split("\n")[0];
await expectRedirect(`/tanks/details/${legacyTankId}`, `/tanks/${tankId}`);
sql(`UPDATE tank SET state = 'draft' WHERE id = ${Number(tankId)}`);
await expectGone(`/tanks/details/${legacyTankId}`);
sql(`UPDATE tank SET state = 'published', deleted_at = now() WHERE id = ${Number(tankId)}`);
await expectGone(`/tanks/details/${legacyTankId}`);
console.log("The legacy URL of a tank redirects while it is published and answers 410 as a draft or when deleted");
