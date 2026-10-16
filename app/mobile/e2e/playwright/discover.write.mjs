// Story: TASK-4.7
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
function sql(statement) {
  return execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql",
    "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
}
const browser = await chromium.launch();
try {
  const flags = await (await fetch(`${API_URL}/api/feature-flags`)).json();
  assert.equal(flags["discover-activity"], false);
  const token = await passwordGrantToken();
  const canvasPage = await browser.newPage();
  const bytes = await canvasPage.evaluate(async () => {
    const canvas = document.createElement("canvas");
    canvas.width = 800; canvas.height = 600;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "teal"; ctx.fillRect(0, 0, 800, 600);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg"));
    return [...new Uint8Array(await blob.arrayBuffer())];
  });
  const body = new FormData();
  body.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), "discover.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body });
  assert.equal(upload.status, 201);
  const draft = await upload.json();
  assert.ok(Number.isSafeInteger(Number(draft.id)));
  const publish = await fetch(`${API_URL}/api/posts/${draft.id}/publish`, { method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ title: "Discover archive picture", topic: "tanks" }) });
  assert.equal(publish.status, 200);
  const seriesDrafts = [];
  for (let i = 1; i < 8; i++) {
    const form = new FormData();
    form.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), `discover-${i}.jpg`);
    const response = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body: form });
    assert.equal(response.status, 201);
    const photo = await response.json();
    assert.ok(Number.isSafeInteger(Number(photo.id)));
    seriesDrafts.push(Number(photo.id));
  }
  for (const [index, id] of seriesDrafts.entries()) {
    const published = await fetch(`${API_URL}/api/posts/${id}/publish`, { method: "POST",
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
      body: JSON.stringify({ title: index === 0 ? "Discover global archive picture" : `Discover archive frame ${index}`, topic: "cichlids" }) });
    assert.equal(published.status, 200);
  }
  sql(`
    INSERT INTO rating (post_id, stars, created_at) SELECT ${seriesDrafts[0]}, 5, now() FROM generate_series(1, 20);
    INSERT INTO tank (profile_id, title, state, region, created_at)
      SELECT author_profile_id, 'Discover American archive', 'published', 'americas_unassigned', now() FROM post WHERE id = ${draft.id};
    UPDATE post SET tank_id = (SELECT id FROM tank WHERE title = 'Discover American archive'), published_at = '2020-01-01' WHERE id = ${draft.id};
    INSERT INTO rating (tank_id, stars, created_at) SELECT t.id, 5, now() FROM tank t, generate_series(1, 12) WHERE title = 'Discover American archive';
    INSERT INTO rating (post_id, stars, created_at) SELECT ${draft.id}, 5, now() FROM generate_series(1, 12);
    ${seriesDrafts.map((id, i) => `UPDATE media_item SET created_at = '2020-01-01'::timestamptz + interval '${i === 6 ? 12 : i + 1} months'
      WHERE id = (SELECT media_item_id FROM media_link WHERE post_id = ${id} LIMIT 1);`).join("\n")}
    INSERT INTO media_link (tank_id, media_item_id, context_type, role, position)
      SELECT t.id, pm.media_item_id, 'tank', 'showcase', 0 FROM tank t, media_link pm
      WHERE t.title = 'Discover American archive' AND pm.post_id IN (${seriesDrafts.join(",")});
    UPDATE profile SET interests = ARRAY['south_america'] WHERE id = (SELECT author_profile_id FROM post WHERE id = ${draft.id});
  `);
  const tankId = Number(sql("SELECT id FROM tank WHERE title = 'Discover American archive'"));
  // A fresh test API builds its shared archive snapshot from these seeded records.
  execFileSync("bash", ["e2e/write-stack.sh", "set-discover-activity", "false"], { stdio: "inherit" });
  const archive = JSON.parse(execFileSync("curl", ["--fail", "--silent", "--show-error",
    `${API_URL}/api/discover?regions=south_america`], { encoding: "utf8" }));
  assert.equal(archive.regions[0].tankOfTheWeek.id, tankId);
  assert.ok(archive.regions[0].pictures.some(p => p.id === Number(draft.id)));
  assert.ok(archive.regions[0].timeLapses.some(t => t.id === tankId));
  for (const scheme of ["light", "dark"]) {
    const context = await browser.newContext({ locale: "en-US", colorScheme: scheme, viewport: { width: 390, height: 844 } });
    const page = await context.newPage();
    await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
    await logInThroughKeycloak(page);
    await byTestId(page, "tab-discover").click();
    await byTestId(page, "discover-week").getByText("Discover American archive", { exact: true }).waitFor();
    assert.match(await byTestId(page, "discover-week").innerText(), /Discover American archive/);
    assert.equal(await byTestId(page, "discover-activity").count(), 0);
    const chips = page.locator('[data-testid^="discover-region-"]');
    assert.equal(await chips.count(), 7);
    assert.equal(await chips.first().getAttribute("data-testid"), "discover-region-south_america");
    for (const chip of await chips.all()) {
      const bounds = await chip.boundingBox();
      assert.ok(bounds.height >= 48 && bounds.width >= 48);
    }
    await byTestId(page, "discover-hall-tanks").getByText("Discover American archive", { exact: true }).waitFor();
    await byTestId(page, "discover-global-pictures").getByText("Discover global archive picture", { exact: true }).waitFor();
    await byTestId(page, "discover-hall-pictures").getByText("Discover archive picture", { exact: true }).click();
    await page.waitForURL(/\/gallery\//);
    await byTestId(page, "tab-discover").click();
    await byTestId(page, "discover-showcases").getByText("Discover American archive", { exact: true }).click();
    await page.waitForURL(`${WEB_URL}/tanks/${tankId}`);
    await byTestId(page, "tank-showcase").waitFor();
    await byTestId(page, "tank-time-machine-open").click();
    await byTestId(page, "tank-time-machine-close").click();
    await byTestId(page, "tab-discover").click();
    await byTestId(page, "discover-region-central_america").click();
    await byTestId(page, "discover-hall-tanks").getByText("Discover American archive", { exact: true }).waitFor();
    await byTestId(page, "discover-region-lake_victoria").click();
    await byTestId(page, "discover-week-empty").waitFor();
    await byTestId(page, "discover-hall-tanks-empty").waitFor();
    await byTestId(page, "discover-hall-pictures-empty").waitFor();
    await byTestId(page, "discover-showcases-empty").waitFor();
    await byTestId(page, "discover-global-pictures").getByText("Discover global archive picture", { exact: true }).waitFor();
    await page.screenshot({ path: `/tmp/task-4.7-empty-${scheme}.png` });
    await byTestId(page, "discover-region-south_america").click();
    await page.screenshot({ path: `/tmp/task-4.7-archive-${scheme}.png` });
    await byTestId(page, "tab-me").click();
    await signOutThroughKeycloak(page);
    await context.close();
  }
  console.log("TASK-4.7: rated regional archive, weekly tank, showcase navigation, interests, empty states, light/dark and activity flag off passed.");
} finally {
  await browser.close();
}
