// Story: TASK-5.6
// A water change entry gets a photo, a note and a to-do: the entry shows all three, the photo opens in
// a viewer, the to-do appears in the to-do list (reached from Today) until it is done and can be
// reopened, and a to-do without an entry can be added from the list. The photo is private: its
// stored objects answer no anonymous request, and the care photo links serve only while they are valid.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and
// the bucket.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const E2E_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const COMPOSE_FILE = path.resolve(E2E_DIR, "../../stack/compose.yaml");
const PHOTO = path.join(E2E_DIR, "fixtures", "upload-photo.jpg");
const BUCKET = "cichlids-e2e";
const PUBLIC_HOST = process.env.CICHLIDS_WRITE_PUBLIC_HOST ?? "127.0.0.1";

// Runs one read statement against the write database and returns its unaligned output.
function sql(statement) {
  if (process.env.E2E_DB !== "cichlids_e2e") throw new Error("Refusing to read outside the cichlids_e2e database");
  return execFileSync(
    "docker",
    ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", statement],
    { stdio: ["ignore", "pipe", "pipe"] },
  ).toString().trim();
}

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Attachments care path", isPublic: true }) });
assert.equal(created.status, 201);
const tank = await created.json();

const browser = await chromium.launch();
const errors = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // A water change entry, logged with one tap.
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
  await byTestId(page, "care-water-change").click();
  await byTestId(page, "care-confirmation").waitFor();
  const list = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers })).json();
  assert.equal(list.total, 1);
  const entry = list.items[0];
  const entryView = byTestId(page, `care-entry-${entry.id}`);
  await entryView.waitFor();

  // A photo.
  await byTestId(page, `care-photo-add-${entry.id}-input`).setInputFiles(PHOTO);
  await byTestId(page, `care-photos-${entry.id}`).waitFor({ timeout: 30000 });
  await page.waitForFunction(
    id => {
      const image = document.querySelector(`[data-testid="care-photos-${id}"] img`);
      return Boolean(image?.complete && image.naturalWidth > 0 && image.src.includes("/api/care-photos/"));
    },
    entry.id,
    { timeout: 20000 },
  );

  // A note.
  await byTestId(page, `care-note-${entry.id}`).click();
  await byTestId(page, `care-note-editor-${entry.id}-input`).fill("  Added water conditioner  ");
  await byTestId(page, `care-note-editor-${entry.id}-save`).click();
  await byTestId(page, `care-note-text-${entry.id}`).filter({ hasText: "Added water conditioner" }).waitFor();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });

  // A to-do.
  await byTestId(page, `care-todo-add-${entry.id}`).click();
  await byTestId(page, `care-todo-editor-${entry.id}-input`).fill("Buy water conditioner");
  await byTestId(page, `care-todo-editor-${entry.id}-save`).click();
  await entryView.locator('[data-testid^="care-todo-"]').filter({ hasText: "Buy water conditioner" }).waitFor();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await page.screenshot({ path: "/tmp/task-5.6-care-light.png" });

  // The API returns the same three things, and the photo is private.
  const stored = (await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers })).json()).items[0];
  assert.equal(stored.note, "Added water conditioner");
  assert.equal(stored.photos.length, 1);
  assert.equal(stored.todos.length, 1);
  assert.equal(stored.todos[0].done, false);
  assert.ok(!JSON.stringify(stored).includes(`/${BUCKET}/`), "The entry names a bucket URL");
  const keys = sql(
    `SELECT media_item.storage_key FROM care_photo JOIN media_item ON media_item.id = care_photo.media_item_id WHERE care_photo.care_entry_id = ${entry.id}
     UNION ALL SELECT media_variant.storage_key FROM care_photo JOIN media_variant ON media_variant.media_item_id = care_photo.media_item_id WHERE care_photo.care_entry_id = ${entry.id}`,
  ).split("\n");
  assert.ok(keys.length >= 2 && keys.every(key => key.startsWith("private/")), `Unexpected storage keys ${keys}`);
  for (const key of keys) {
    const direct = await fetch(`http://${PUBLIC_HOST}:9000/${BUCKET}/${key}`);
    assert.equal(direct.status, 403, `Expected 403 for ${key}`);
  }
  const thumbPath = stored.photos[0].urls.thumb;
  assert.equal((await fetch(`${API_URL}${thumbPath}`)).status, 200);
  const tampered = await fetch(`${API_URL}${thumbPath.slice(0, -4)}AAAA`);
  assert.equal(tampered.status, 404);
  assert.equal((await tampered.json()).code, "care_photo_not_found");
  const otherToken = await passwordGrantToken("dev-mod");
  const refused = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: { Authorization: `Bearer ${otherToken}` } });
  assert.equal(refused.status, 404);
  assert.equal((await refused.json()).code, "care_not_found");

  // The viewer shows the photo large and closes.
  await byTestId(page, `care-photo-${stored.photos[0].id}`).click();
  await byTestId(page, "care-photo-viewer").waitFor();
  await page.waitForFunction(() => {
    const image = document.querySelector('[data-testid="care-photo-viewer-image"] img, img[data-testid="care-photo-viewer-image"]');
    return Boolean(image?.complete && image.naturalWidth > 0);
  }, null, { timeout: 20000 });
  await byTestId(page, "care-photo-close").click();
  await byTestId(page, "care-photo-viewer").waitFor({ state: "hidden" });

  // Today links to the to-do list while a to-do is open.
  await page.goto(WEB_URL, { waitUntil: "networkidle" });
  const link = byTestId(page, "today-todos");
  await link.waitFor();
  assert.match(await link.innerText(), /Open to-dos: 1/);
  const bounds = await link.boundingBox();
  assert.ok(bounds.height >= 48 && bounds.width >= 48);
  await link.click();
  await page.waitForURL(/\/todos$/);
  const item = byTestId(page, "todos-open").locator('[data-testid^="todo-item-"]').filter({ hasText: "Buy water conditioner" });
  await item.waitFor();
  await item.filter({ hasText: "Attachments care path" }).waitFor();

  // Checking it off moves it to the done list; reopening brings it back.
  const todoId = stored.todos[0].id;
  await byTestId(page, `todo-toggle-${todoId}`).click();
  await byTestId(page, "todos-open").locator(`[data-testid="todo-item-${todoId}"]`).waitFor({ state: "detached" });
  await byTestId(page, "todos-done").locator(`[data-testid="todo-item-${todoId}"]`).waitFor();
  await byTestId(page, `todo-toggle-${todoId}`).click();
  await byTestId(page, "todos-open").locator(`[data-testid="todo-item-${todoId}"]`).waitFor();

  // A to-do without an entry belongs to a tank only.
  await byTestId(page, `todo-tank-${tank.id}`).click();
  await byTestId(page, "todo-new-input").fill("Clean the filter");
  await byTestId(page, "todo-new-add").click();
  await byTestId(page, "todos-open").locator('[data-testid^="todo-item-"]').filter({ hasText: "Clean the filter" }).waitFor();
  await page.emulateMedia({ colorScheme: "dark" });
  await page.screenshot({ path: "/tmp/task-5.6-todos-dark.png" });
  const open = await (await fetch(`${API_URL}/api/me/todos`, { headers })).json();
  assert.equal(open.total, 2);
  const standalone = open.items.find(todo => todo.text === "Clean the filter");
  assert.equal(standalone.careEntryId, null);
  assert.equal(String(standalone.tankId), String(tank.id));

  // Both are done: the Today link goes away.
  for (const todo of open.items) await byTestId(page, `todo-toggle-${todo.id}`).click();
  await byTestId(page, "todos-open").locator('[data-testid^="todo-item-"]').first().waitFor({ state: "detached" });
  await page.goto(WEB_URL, { waitUntil: "networkidle" });
  await byTestId(page, "today-member").waitFor();
  assert.equal(await byTestId(page, "today-todos").count(), 0);

  // The Me tab links to the list as well.
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(page, "me-todos").click();
  await page.waitForURL(/\/todos$/);
  await byTestId(page, "todos-done").locator('[data-testid^="todo-item-"]').first().waitFor();
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.6: a water change entry gets a photo, a note and a to-do; the to-do stays in the list until done (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.6-failure.png" });
  throw error;
} finally {
  await browser.close();
}
