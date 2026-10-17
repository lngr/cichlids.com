// Story: TASK-9.2
// The page and timeline of a named fish on the web build. The owner opens Bruno from the stocking list,
// adds two growth entries (cm and inch), two spawnings (F1 and a hybrid) and three photos (from the device,
// from the media library; the camera step opens), reads the congratulation on the first year on Today and
// dismisses it. A visitor of the public tank reads the page with the length in the metric units of the
// visitor. A move to a private tank hides the page from the visitor (the same answer as a missing fish) and
// keeps it for the owner, a move back shows it with both moves on the timeline. The death turns the page
// into a memorial with its dates, and after the owner deleted the tank the page stays for the owner only.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and the
// bucket. The spec creates its own tanks, species, fish and files and removes them at the end; it does not
// depend on the order of the other specs.
import assert from "node:assert/strict";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, ZONE, byTestId, localDate, logInThroughKeycloak, passwordGrantToken, sql } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const FIXTURE = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const today = localDate();
const movedIn = (() => {
  const day = new Date(`${today}T00:00:00Z`);
  day.setUTCFullYear(day.getUTCFullYear() - 1);
  day.setUTCDate(day.getUTCDate() - 2);
  return localDate(day);
})();
const formatDay = day => new Intl.DateTimeFormat("en", { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" })
  .format(new Date(`${day}T00:00:00Z`));

const titleA = `Fish page tank A ${Date.now()}`;
const titleB = `Fish page tank B ${Date.now()}`;
const createTank = async (title, isPublic) => {
  const response = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title, isPublic }) });
  assert.equal(response.status, 201);
  return Number((await response.json()).id);
};
const tankA = await createTank(titleA, true);
const tankB = await createTank(titleB, false);
const genus = `Fishpage${Date.now()}`;
const speciesId = Number(sql(`INSERT INTO species (genus, name, display_name, breeding, aggression, intra_aggression, diet, created_at)
  VALUES ('${genus}', 'testfish', '${genus} testfish', 'unspecified', 'low', 'low', 'omnivore', now()) RETURNING id`).split("\n")[0]);
const added = await fetch(`${API_URL}/api/tanks/${tankA}/inhabitants?date=${today}`, {
  method: "POST", headers, body: JSON.stringify({ name: "Bruno", speciesId, movedInOn: movedIn }),
});
assert.equal(added.status, 201);
const fish = Number((await added.json()).id);
const libraryMediaIds = [];

const getFish = async (extra = {}) => fetch(`${API_URL}/api/fish/${fish}?date=${today}`, extra);
const text = async (page, id) => (await byTestId(page, id).first().innerText()).trim();
const sizeOk = async (page, ids) => {
  for (const id of ids) {
    const box = await byTestId(page, id).first().boundingBox();
    assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
  }
};
const gone = (page, id) => page.waitForSelector(`[data-testid="${id}"]`, { state: "detached" });
const photoCount = page => page.evaluate(() => [...document.querySelectorAll("[data-testid]")].filter(node => /^fish-photo-\d+$/.test(node.dataset.testid)).length);

const browser = await chromium.launch();
const errors = [];
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

  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // The route /fish/{id} opens from the stocking list of the tank (named fish only).
  await page.goto(`${WEB_URL}/tanks/${tankA}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, `stocking-open-${fish}`).click();
  await byTestId(page, "fish-page").waitFor();
  assert.equal(new URL(page.url()).pathname, `/fish/${fish}`);
  assert.equal(await text(page, "fish-name"), "Bruno");
  assert.ok((await text(page, "fish-species")).includes(`${genus} testfish`));
  assert.equal(await text(page, "fish-tank"), `Lives in ${titleA}`);
  assert.equal(await text(page, "fish-since"), `Moved in on ${formatDay(movedIn)}`);
  assert.equal(await byTestId(page, "fish-event-moved_in").count(), 1);
  assert.equal(await byTestId(page, "fish-event-anniversary").count(), 1);
  assert.match(await text(page, "fish-event-anniversary"), /1 year together/);
  await sizeOk(page, ["fish-add-photo", "fish-add-growth", "fish-add-spawning", "fish-move", "fish-death"]);

  // Growth in cm and in inch; the length of the page follows the newest entry in the unit typed.
  await byTestId(page, "fish-add-growth").click();
  await byTestId(page, "fish-growth-length").fill("0");
  await byTestId(page, "fish-growth-length-problem").waitFor();
  assert.equal(await byTestId(page, "fish-growth-save").getAttribute("aria-disabled"), "true");
  await byTestId(page, "fish-growth-length").fill("7,5");
  await byTestId(page, "fish-growth-unit-cm").click();
  await byTestId(page, "fish-growth-save").click();
  await gone(page, "fish-growth-save");
  assert.equal(await text(page, "fish-length"), "Length: 7.5 cm");
  await byTestId(page, "fish-add-growth").click();
  await byTestId(page, "fish-growth-length").fill("3.5");
  await byTestId(page, "fish-growth-unit-inch").click();
  await byTestId(page, "fish-growth-save").click();
  await gone(page, "fish-growth-save");
  assert.equal(await text(page, "fish-length"), "Length: 3.5 in");
  assert.equal(await byTestId(page, "fish-event-growth").count(), 2);
  assert.match((await byTestId(page, "fish-event-growth").first().innerText()), /Measured: 3\.5 in/);

  // Spawnings: F1 and a hybrid.
  await byTestId(page, "fish-add-spawning").click();
  await byTestId(page, "fish-spawning-generation").fill("0");
  await byTestId(page, "fish-spawning-generation-problem").waitFor();
  await byTestId(page, "fish-spawning-generation").fill("1");
  await byTestId(page, "fish-spawning-save").click();
  await gone(page, "fish-spawning-save");
  await byTestId(page, "fish-add-spawning").click();
  await byTestId(page, "fish-spawning-hybrid").click();
  await byTestId(page, "fish-spawning-save").click();
  await gone(page, "fish-spawning-save");
  const spawnings = await byTestId(page, "fish-event-spawning").allInnerTexts();
  assert.equal(spawnings.length, 2);
  assert.ok(spawnings.some(entry => entry.includes("Spawned, offspring F1")), spawnings.join("|"));
  assert.ok(spawnings.some(entry => entry.includes("Spawned, hybrid offspring")), spawnings.join("|"));

  // Photos: one from the device, one from the media library; the camera step opens and goes back.
  await byTestId(page, "fish-add-photo").click();
  await byTestId(page, "fish-photo-upload-input").setInputFiles(FIXTURE);
  await gone(page, "fish-photo-cancel");
  await page.waitForFunction(() => [...document.querySelectorAll('[data-testid]')].filter(node => /^fish-photo-[0-9]+$/.test(node.dataset.testid)).length === 1);
  const form = new FormData();
  form.append("file", new Blob([new Uint8Array(await jpegBytes(200))], { type: "image/jpeg" }), "fish-library.jpg");
  const uploaded = await fetch(`${API_URL}/api/me/media/photos`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body: form });
  assert.equal(uploaded.status, 201);
  const libraryMedia = Number((await uploaded.json()).id);
  libraryMediaIds.push(libraryMedia);
  await byTestId(page, "fish-add-photo").click();
  await byTestId(page, "fish-photo-camera").click();
  await page.waitForSelector('[data-testid="fish-camera-explain"], [data-testid="fish-camera-denied"], [data-testid="fish-camera-viewfinder"]');
  await byTestId(page, "fish-camera-back").click();
  await byTestId(page, "fish-photo-library").click();
  await byTestId(page, `fish-library-tile-${libraryMedia}`).click();
  await gone(page, "fish-photo-cancel");
  await page.waitForFunction(() => [...document.querySelectorAll('[data-testid]')].filter(node => /^fish-photo-[0-9]+$/.test(node.dataset.testid)).length === 2);

  const owned = await (await getFish({ headers })).json();
  assert.equal(owned.photos.length, 2);
  assert.equal(Number(owned.photos[0].mediaItemId), libraryMedia, "the newest file comes first");
  assert.equal(owned.photos.filter(photo => typeof photo.image.thumb === "string").length, 2, "both photos have an address");
  await page.waitForTimeout(800);
  await page.screenshot({ path: "/tmp/task-9.2-fish-owner-light.png" });

  // Today congratulates on the first year and the owner dismisses it.
  await page.goto(`${WEB_URL}/today?lang=en`, { waitUntil: "networkidle" });
  const milestone = `today-milestone-fish_anniversary:${fish}:1`;
  await byTestId(page, milestone).waitFor();
  assert.ok((await text(page, milestone)).includes(`Bruno has lived with you in ${titleA} for 1 year.`), await text(page, milestone));
  await sizeOk(page, [`today-milestone-dismiss-fish_anniversary:${fish}:1`, `today-milestone-open-fish_anniversary:${fish}:1`]);
  await byTestId(page, `today-milestone-dismiss-fish_anniversary:${fish}:1`).click();
  await gone(page, milestone);
  const openMilestones = async () => (await (await fetch(`${API_URL}/api/me/today/milestones?date=${today}&utcOffsetMinutes=0`, { headers })).json())
    .items.filter(item => item.kind === "fish_anniversary" && Number(item.inhabitantId) === fish).length;
  for (let attempt = 0; attempt < 20 && (await openMilestones()) > 0; attempt++) await new Promise(resolve => setTimeout(resolve, 250));
  assert.equal(await openMilestones(), 0, "the dismissal is stored");

  // A visitor of the public tank reads the page; the length is in the metric units of the visitor.
  const visitor = await browser.newContext({ locale: "en-GB", timezoneId: ZONE, colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const visitorPage = await visitor.newPage();
  visitorPage.on("pageerror", error => errors.push(String(error)));
  await visitorPage.goto(`${WEB_URL}/fish/${fish}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(visitorPage, "fish-page").waitFor();
  assert.equal(await text(visitorPage, "fish-name"), "Bruno");
  assert.equal(await text(visitorPage, "fish-length"), "Length: 8.89 cm");
  assert.equal(await photoCount(visitorPage), 2);
  assert.equal(await byTestId(visitorPage, "fish-actions").count(), 0);
  assert.equal(await byTestId(visitorPage, "fish-event-growth").count(), 2);
  await visitorPage.screenshot({ path: "/tmp/task-9.2-fish-visitor-dark.png" });

  // The move to a private tank: the owner keeps the page, the visitor gets the answer for a missing fish.
  await page.goto(`${WEB_URL}/fish/${fish}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "fish-move").click();
  assert.match(await text(page, "fish-move-warning"), /follow the visibility of the new tank/);
  await sizeOk(page, [`fish-move-tank-${tankB}`]);
  await byTestId(page, `fish-move-tank-${tankB}`).click();
  await byTestId(page, "fish-move-save").click();
  await gone(page, "fish-move-save");
  assert.equal(await text(page, "fish-tank"), `Lives in ${titleB}`);
  assert.match(await text(page, "fish-event-moved"), new RegExp(`Moved to ${titleB}`));
  assert.equal(await photoCount(page), 2);
  const hidden = await getFish();
  assert.equal(hidden.status, 404);
  assert.equal((await hidden.json()).code, "fish_not_found");
  await visitorPage.reload({ waitUntil: "networkidle" });
  assert.equal(await visitorPage.getByText("This fish page is not available.").count(), 1);
  const missing = await fetch(`${API_URL}/api/fish/999999999?date=${today}`);
  assert.deepEqual([hidden.status, (await missing.json()).code], [404, "fish_not_found"]);

  // After the move back the page shows with both moves on the timeline.
  await byTestId(page, "fish-move").click();
  await byTestId(page, `fish-move-tank-${tankA}`).click();
  await byTestId(page, "fish-move-save").click();
  await gone(page, "fish-move-save");
  assert.equal(await byTestId(page, "fish-event-moved").count(), 2);
  await visitorPage.reload({ waitUntil: "networkidle" });
  await byTestId(visitorPage, "fish-page").waitFor();
  assert.equal(await byTestId(visitorPage, "fish-event-moved").count(), 2, "the visitor sees the moves between tanks it may see");
  assert.equal(await photoCount(visitorPage), 2);

  // The death: a memorial with its dates for the owner and for the visitor.
  await byTestId(page, "fish-death").click();
  await byTestId(page, "fish-death-save").click();
  await gone(page, "fish-death-save");
  await byTestId(page, "fish-memorial").waitFor();
  assert.equal(await text(page, "fish-memorial-dates"), `${formatDay(movedIn)} to ${formatDay(today)}`);
  assert.equal(await byTestId(page, "fish-move").count(), 0);
  assert.equal(await byTestId(page, "fish-add-photo").count(), 1, "photos can still be added to a memorial");
  assert.equal(await byTestId(page, "fish-event-died").count(), 1);
  await visitorPage.reload({ waitUntil: "networkidle" });
  await byTestId(visitorPage, "fish-memorial").waitFor();
  assert.equal(await text(visitorPage, "fish-memorial-dates"), `${formatDay(movedIn)} to ${formatDay(today)}`);
  await visitorPage.screenshot({ path: "/tmp/task-9.2-fish-memorial-dark.png" });
  const stocking = await (await fetch(`${API_URL}/api/tanks/${tankA}/inhabitants`)).json();
  assert.equal(stocking.current.length, 0);
  assert.equal(stocking.history[0].removalReason, "died");

  // Deleting the tank keeps the page for the owner; everybody else gets the answer for a missing fish.
  sql(`UPDATE tank SET deleted_at = now() WHERE id = ${tankA}`);
  await page.reload({ waitUntil: "networkidle" });
  await byTestId(page, "fish-memorial").waitFor();
  assert.equal(await text(page, "fish-tank"), `Lived in ${titleA} (deleted)`);
  assert.equal((await getFish()).status, 404);
  assert.equal((await getFish({ headers })).status, 200);

  const kinds = sql(`SELECT event_type FROM outbox_event WHERE aggregate_type = 'inhabitant' AND aggregate_id = '${fish}' ORDER BY occurred_at, event_type`).split("\n");
  assert.deepEqual(kinds.slice().sort(), [
    "inhabitant.added", "inhabitant.growth_recorded", "inhabitant.growth_recorded", "inhabitant.moved", "inhabitant.moved",
    "inhabitant.removed", "inhabitant.spawned", "inhabitant.spawned",
  ]);
  assert.equal(sql(`SELECT count(*) FROM inhabitant_event WHERE inhabitant_id = ${fish}`), "7");
  assert.deepEqual(errors, []);
  console.log("TASK-9.2: page opened from the stocking list, growth in cm and inch, spawnings, photos from the device and the library, the congratulation on the first year, visitors reading in their own units, the move hiding and showing the page, the memorial and the deleted tank passed.");
} finally {
  await browser.close();
  sql(`DELETE FROM outbox_event WHERE aggregate_type = 'inhabitant' AND aggregate_id = '${fish}'`);
  sql(`DELETE FROM milestone_dismissal WHERE key LIKE 'fish_anniversary:${fish}:%'`);
  const linked = sql(`SELECT media_item_id FROM media_link WHERE inhabitant_id = ${fish}`).split("\n").filter(Boolean);
  sql(`DELETE FROM inhabitant WHERE id = ${fish}`);
  for (const id of [...linked, ...libraryMediaIds]) sql(`DELETE FROM media_item WHERE id = ${Number(id)}`);
  sql(`DELETE FROM species WHERE id = ${speciesId}`);
  sql(`UPDATE tank SET deleted_at = coalesce(deleted_at, now()) WHERE id IN (${tankA}, ${tankB})`);
}
