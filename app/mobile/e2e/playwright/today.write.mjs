// Story: TASK-4.8
// Given a member with a tank and a draft, opening the app shows Today and links to the tank and Discover.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const browser = await chromium.launch();
let token;
let draftId;
try {
  token = await passwordGrantToken();
  const authorization = { Authorization: `Bearer ${token}` };
  const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers: { ...authorization, "Content-Type": "application/json" },
    body: JSON.stringify({ title: "Today starting tank", isPublic: true, region: "lake_malawi" }) });
  assert.equal(created.status, 201);
  const tank = await created.json();
  const imagePage = await browser.newPage();
  const bytes = await imagePage.evaluate(async () => {
    const canvas = document.createElement("canvas");
    canvas.width = 800; canvas.height = 600;
    const context = canvas.getContext("2d");
    context.fillStyle = "teal"; context.fillRect(0, 0, 800, 600);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg"));
    return [...new Uint8Array(await blob.arrayBuffer())];
  });
  await imagePage.close();
  const body = new FormData();
  body.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), "today.jpg");
  const uploaded = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: authorization, body });
  assert.equal(uploaded.status, 201);
  const draft = await uploaded.json();
  draftId = draft.id;
  const photoUpload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: authorization, body });
  assert.equal(photoUpload.status, 201);
  const photo = await photoUpload.json();
  const published = await fetch(`${API_URL}/api/posts/${photo.id}/publish`, { method: "POST",
    headers: { ...authorization, "Content-Type": "application/json" },
    body: JSON.stringify({ title: "Today tank photo", topic: "tanks" }) });
  assert.equal(published.status, 200);
  assert.ok([tank.id, photo.id].every(id => Number.isSafeInteger(Number(id))));
  execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql",
    "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-c",
    `UPDATE post SET tank_id = ${Number(tank.id)} WHERE id = ${Number(photo.id)}`], { stdio: "pipe" });
  for (const colorScheme of ["light", "dark"]) {
    const context = await browser.newContext({ locale: "en-US", colorScheme, viewport: { width: 390, height: 844 } });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
    await logInThroughKeycloak(page);
    await page.goto(WEB_URL, { waitUntil: "networkidle" });
    await byTestId(page, "today-member").waitFor({ timeout: 15000 });
    assert.match(await byTestId(page, "today-greeting").textContent(), /Dev User|dev-user/);
    await byTestId(page, "today-tanks").getByText(tank.title, { exact: true }).waitFor();
    await byTestId(page, "today-drafts").waitFor();
    const ids = await page.locator('[data-testid="today-member"] > div').evaluateAll(nodes => nodes.map(n => n.dataset.testid).filter(Boolean));
    assert.ok(ids.indexOf("today-tanks") < ids.indexOf("today-drafts"));
    const ownTank = byTestId(page, `today-tank-${tank.id}`);
    await ownTank.locator("img").waitFor();
    await page.waitForFunction(id => {
      const image = document.querySelector(`[data-testid="${id}"] img`);
      return image?.complete && image.naturalWidth > 0 && getComputedStyle(image).opacity === "1";
    }, `today-tank-${tank.id}`);
    const box = await ownTank.boundingBox();
    assert.ok(box.width >= 48 && box.height >= 48);
    await ownTank.click();
    await page.waitForURL(`${WEB_URL}/tanks/${tank.id}`);
    await byTestId(page, "tank-view-story").waitFor();
    await byTestId(page, "tab-today").click();
    await byTestId(page, `today-draft-${draft.id}`).click();
    await page.waitForURL(`${WEB_URL}/upload?draft=${draft.id}`);
    await page.locator('[data-testid="navigation-back"]:visible').click();
    await page.waitForURL(`${WEB_URL}/today`);
    await byTestId(page, "today-discover").click();
    await page.waitForURL(`${WEB_URL}/discover`);
    await byTestId(page, "discover-archive").waitFor();
    await page.route("**/api/me/today?*", route => route.abort("connectionfailed"));
    await byTestId(page, "tab-today").click();
    await byTestId(page, "error-retry").waitFor();
    await page.unroute("**/api/me/today?*");
    await byTestId(page, "error-retry").click();
    await byTestId(page, "today-member").waitFor();
    await page.waitForLoadState("networkidle");
    await page.screenshot({ path: `/tmp/task-4.8-today-${colorScheme}.png` });
    await byTestId(page, "tab-me").click();
    await signOutThroughKeycloak(page);
    await byTestId(page, "tab-today").click();
    await byTestId(page, "today-welcome").waitFor();
    await byTestId(page, "today-login").waitFor();
    await byTestId(page, "today-discover").click();
    await page.waitForURL(`${WEB_URL}/discover`);
    assert.deepEqual(errors, []);
    await context.close();
  }
  console.log("TASK-4.8: Today greeting, tanks, drafts, tank and Discover navigation, retry, anonymous welcome and light/dark passed.");
} finally {
  await browser.close();
  if (draftId != null) {
    const discarded = await fetch(`${API_URL}/api/posts/${draftId}`, { method: "DELETE", headers: { Authorization: `Bearer ${token}` } });
    assert.equal(discarded.status, 204, "The Today spec discards its draft.");
  }
}
