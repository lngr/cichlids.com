// Story: TASK-3.29
// Uploads a photo carrying GPS tags and checks that the app shows it from its public variants while
// its original stays private: the draft response names no original, and an anonymous request for
// the original object is refused while its thumbnail answers. A legacy picture libvips cannot
// decode (a BMP) has only a public copy; its detail page loads the image from that copy.
//
// The copy-only picture is set up directly in the write database and bucket, the way the media
// seed leaves such a legacy picture. Runs on the isolated write stack only (see
// e2e/write-stack.sh), since it writes to the database and the bucket.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken } from "./support.mjs";

const E2E_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const COMPOSE_FILE = path.resolve(E2E_DIR, "../../stack/compose.yaml");
const DOTNET = process.env.DOTNET ?? path.join(os.homedir(), ".dotnet", "dotnet");
const BUCKET = "cichlids-e2e";
const GPS_PHOTO = path.join(E2E_DIR, "fixtures", "gps-photo.jpg");
const LEGACY_BMP = path.join(E2E_DIR, "fixtures", "legacy-undecodable.bmp");
const LEGACY_ORIGINAL_KEY = "originals/user_pics/e2e/legacy-undecodable.jpg";
const LEGACY_COPY_KEY = `variants/copy/${LEGACY_ORIGINAL_KEY}`;

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

// Stores a file in the write bucket through bucket-tool.cs, which refuses every other bucket.
function putObject(key, file, contentType) {
  execFileSync(DOTNET, ["run", "bucket-tool.cs", "--", "put", BUCKET, key, file, contentType], { cwd: E2E_DIR, stdio: "pipe" });
}

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "tab-me").click({ timeout: 20000 });
  await page.waitForURL(/\/me$/, { timeout: 10000 });
  console.log(`Logged in as "${await logInThroughKeycloak(page)}"`);

  // The photo with GPS tags is uploaded through the upload screen; its preview loads from a variant.
  await byTestId(page, "tab-discover").click({ timeout: 20000 });
  await byTestId(page, "discover-gallery").click();
  await page.waitForURL(/\/gallery$/, { timeout: 10000 });
  await byTestId(page, "upload-button").click({ timeout: 20000 });
  await page.waitForURL(/\/upload$/, { timeout: 10000 });
  await byTestId(page, "upload-file-input").setInputFiles(GPS_PHOTO);
  await byTestId(page, "upload-preview").waitFor({ timeout: 30000 });
  await page.waitForFunction(
    () => {
      const image = document.querySelector('[data-testid="upload-preview"] img');
      return Boolean(image?.complete && image.naturalWidth > 0 && image.src.includes("/variants/"));
    },
    null,
    { timeout: 20000 },
  );

  const token = await passwordGrantToken();
  const draftsResponse = await fetch(`${API_URL}/api/me/drafts`, { headers: { Authorization: `Bearer ${token}` } });
  if (!draftsResponse.ok) throw new Error(`GET /api/me/drafts returned ${draftsResponse.status}`);
  const drafts = await draftsResponse.json();
  // The list is newest first, so its first entry is the draft this upload created.
  const [draft] = drafts;
  if (!draft) throw new Error("The upload created no draft");
  if (!/^\d+$/.test(String(draft.id))) throw new Error("Unexpected draft id");
  if ("original" in draft.image) throw new Error("The draft's image names an original");
  if (JSON.stringify(draft).includes(`/${BUCKET}/originals/`)) throw new Error("The draft response contains an original URL");
  console.log("The preview loads from a variant and the draft names no original");

  // The thumbnail key is variants/thumb/ followed by the original's key and .jpg; the key derived
  // from it is the one the database records for the draft's photo.
  const thumbUrl = draft.image.thumb;
  const thumbMatch = /^(.*\/cichlids-e2e\/)variants\/thumb\/(originals\/.+)\.jpg$/.exec(thumbUrl ?? "");
  if (!thumbMatch) throw new Error(`Unexpected thumbnail URL "${thumbUrl}"`);
  const storedKey = sql(
    `SELECT media_item.storage_key FROM media_link JOIN media_item ON media_item.id = media_link.media_item_id
     WHERE media_link.post_id = ${draft.id}`,
  );
  if (storedKey !== thumbMatch[2]) {
    throw new Error(`The thumbnail names the original "${thumbMatch[2]}", the database records "${storedKey}"`);
  }
  const originalUrl = `${thumbMatch[1]}${storedKey}`;
  const originalStatus = (await fetch(originalUrl)).status;
  const thumbStatus = (await fetch(thumbUrl)).status;
  if (originalStatus !== 403) throw new Error(`Expected 403 for the original at ${originalUrl}, got ${originalStatus}`);
  if (thumbStatus !== 200) throw new Error(`Expected 200 for the thumbnail at ${thumbUrl}, got ${thumbStatus}`);
  console.log("An anonymous request for the original is refused, the thumbnail answers");

  // The draft is published and its photo replaced by a legacy picture that has only a public copy.
  const published = await fetch(`${API_URL}/api/posts/${draft.id}/publish`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ title: `E2E legacy copy ${Date.now()}`, description: null, topic: "cichlids" }),
  });
  if (!published.ok) throw new Error(`Publishing the draft returned ${published.status}`);
  const { slug } = await published.json();
  putObject(LEGACY_COPY_KEY, LEGACY_BMP, "image/bmp");
  const copySize = fs.statSync(LEGACY_BMP).size;
  sql(
    `WITH item AS (
       INSERT INTO media_item (kind, storage_key, original_filename, created_at)
       VALUES ('photo', '${LEGACY_ORIGINAL_KEY}', 'legacy-undecodable.jpg', now()) RETURNING id),
     copy AS (
       INSERT INTO media_variant (media_item_id, label, width, storage_key, byte_size)
       SELECT id, 'copy', 64, '${LEGACY_COPY_KEY}', ${copySize} FROM item)
     UPDATE media_link SET media_item_id = (SELECT id FROM item) WHERE post_id = ${draft.id}`,
  );

  const detail = await (await fetch(`${API_URL}/api/pictures/${slug}`)).json();
  for (const size of ["thumb", "small", "medium", "large"]) {
    if (!detail.image?.[size]?.endsWith(`/${LEGACY_COPY_KEY}`)) {
      throw new Error(`Expected the ${size} URL to be the public copy, got "${detail.image?.[size]}"`);
    }
  }

  await page.goto(`${WEB_URL}/gallery/${slug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 20000 });
  try {
    await page.waitForFunction(
      () => {
        const image = document.querySelector('img[src*="/variants/copy/"]');
        return Boolean(image?.complete && image.naturalWidth > 0);
      },
      null,
      { timeout: 20000 },
    );
  } catch {
    const sources = await page.evaluate(() => Array.from(document.images, (image) => `${image.src} (${image.naturalWidth})`));
    throw new Error(`The detail page shows no loaded image from the public copy; images: ${sources.join(", ")}`);
  }
  console.log(`The legacy picture on /gallery/${slug} loads from its public copy`);

  if (pageErrors.length > 0) {
    throw new Error(`Page errors observed: ${pageErrors.join("; ")}`);
  }
} finally {
  await browser.close();
}
