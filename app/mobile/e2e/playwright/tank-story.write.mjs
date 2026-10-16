// Story: TASK-4.6
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const browser = await chromium.launch();
try {
  const token = await passwordGrantToken();
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers,
    body: JSON.stringify({ title: "Archive time machine", isPublic: true, region: "lake_tanganyika", volume: { value: 240, unit: "liter" } }) });
  assert.equal(created.status, 201);
  const tank = await created.json();
  const canvasPage = await browser.newPage();
  const storyIds = [];
  for (let i = 0; i < 8; i++) {
    const bytes = await canvasPage.evaluate(async (index) => {
      const canvas = document.createElement("canvas");
      canvas.width = 800;
      canvas.height = 600;
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = `hsl(${index * 30 + 100} 50% 35%)`;
      ctx.fillRect(0, 0, 800, 600);
      ctx.fillStyle = "white";
      ctx.font = "60px sans-serif";
      ctx.fillText(String(index + 1), 350, 300);
      const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg"));
      return [...new Uint8Array(await blob.arrayBuffer())];
    }, i);
    const body = new FormData();
    body.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), `tank-${i}.jpg`);
    const uploaded = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body });
    assert.equal(uploaded.status, 201, await uploaded.clone().text());
    const draft = await uploaded.json();
    const published = await fetch(`${API_URL}/api/stories/${draft.id}/publish`, { method: "POST", headers,
      body: JSON.stringify({ title: `Archive month ${i + 1}`, description: `Tank stage ${i + 1}`, topic: "tanks" }) });
    assert.equal(published.status, 200, await published.clone().text());
    storyIds.push(Number(draft.id));
  }
  assert.ok(Number.isSafeInteger(Number(tank.id)) && storyIds.every(Number.isSafeInteger));
  execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql",
    "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-c",
    `UPDATE tank SET legacy_id = 460006, water_ph = '8.2', water_notes = 'Weekly water changes', filtration = 'External filter' WHERE id = ${tank.id};
     ${storyIds.map((id, i) => `UPDATE story SET published_at = '2020-01-01'::timestamptz + interval '${i === 7 ? 12 : i} months' WHERE id = ${id}; INSERT INTO story_subject (story_id, subject_type, tank_id) VALUES (${id}, 'tank', ${tank.id});`).join("\n")}
     ${storyIds.map((id, i) => `UPDATE media_item SET taken_at_local = '2018-03-01 09:00'::timestamp + interval '${i === 0 ? 12 : 7 - i} months', taken_at_offset_minutes = 120 WHERE id IN (SELECT media_item_id FROM media_link WHERE story_id = ${id});`).join("\n")}
     INSERT INTO media_link (tank_id, media_item_id, context_type, role, position) SELECT ${tank.id}, media_item_id, 'tank', 'showcase', 0 FROM media_link WHERE story_id = ${storyIds[0]};`], { stdio: "pipe" });
  const series = await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse`);
  assert.equal(series.status, 200);
  const data = await series.json();
  assert.equal(data.isShowcase, true);
  assert.equal(data.photos.length, 8);
  // The capture times run opposite to the publication dates and decide the order.
  const taken = data.photos.map(photo => Date.parse(photo.takenAt));
  assert.ok(taken.every(Number.isFinite) && taken.every((value, i) => i === 0 || value > taken[i - 1]), "photos ordered by capture time");
  assert.match(data.photos[0].takenAt, /^2018-03-01T09:00:00\+02:00$/);
  for (const scheme of ["light", "dark"]) {
    const context = await browser.newContext({ locale: "en-US", colorScheme: scheme, viewport: { width: 390, height: 844 } });
    const page = await context.newPage();
    await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
    await byTestId(page, "tank-showcase").waitFor();
    await byTestId(page, "tank-time-machine-open").click();
    await byTestId(page, "tank-time-machine-play").click();
    await page.waitForFunction(() => document.querySelector('[data-testid="tank-time-machine-position"]')?.textContent?.startsWith("2 /"));
    await byTestId(page, "tank-time-machine-pause").click();
    const paused = await byTestId(page, "tank-time-machine-position").textContent();
    await page.waitForTimeout(1600);
    assert.equal(await byTestId(page, "tank-time-machine-position").textContent(), paused);
    await byTestId(page, "tank-time-machine-scrub").press("End");
    assert.match(await byTestId(page, "tank-time-machine-position").textContent(), /^8 \/ 8/);
    for (const id of ["tank-time-machine-play", "tank-time-machine-previous", "tank-time-machine-next", "tank-time-machine-scrub", "tank-time-machine-close"]) {
      const box = await byTestId(page, id).boundingBox();
      assert.ok(box && box.height >= 48 && box.width >= 48, id);
    }
    await page.waitForFunction(uri => {
      const layer = document.querySelector('[data-testid="tank-time-machine-frame"]');
      const image = layer?.querySelector("img");
      return image?.src === uri && image.complete && image.naturalWidth > 0 && getComputedStyle(layer).opacity === "1";
    }, data.photos.at(-1).image.large ?? data.photos.at(-1).image.medium ?? data.photos.at(-1).image.small ?? data.photos.at(-1).image.thumb);
    await page.screenshot({ path: `/tmp/task-4.6-player-${scheme}.png` });
    await byTestId(page, "tank-time-machine-close").click();
    const timeline = await byTestId(page, "tank-story-event").allTextContents();
    assert.match(timeline[0], /Archive month 8/);
    assert.match(timeline.at(-1), /Archive month 1/);
    await page.screenshot({ path: `/tmp/task-4.6-story-${scheme}.png` });
    await byTestId(page, "tank-view-care").click();
    await page.getByText("Weekly water changes", { exact: true }).waitFor();
    await page.getByText("External filter", { exact: true }).waitFor();
    await byTestId(page, "tank-care-empty").waitFor();
    await page.screenshot({ path: `/tmp/task-4.6-care-${scheme}.png` });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await byTestId(page, "tank-view-story").click();
    await byTestId(page, "tank-time-machine-open").click();
    await byTestId(page, "tank-time-machine-next").click();
    assert.match(await byTestId(page, "tank-time-machine-position").textContent(), /^2 \/ 8/);
    assert.equal(await byTestId(page, "tank-time-machine-frame").evaluate(element => getComputedStyle(element).opacity), "1");
    await context.close();
  }
  console.log("TASK-4.6: seeded legacy series ordered by capture time, showcase, playback, pause, keyboard scrub, care, light/dark and reduced motion passed.");
} finally {
  await browser.close();
}
