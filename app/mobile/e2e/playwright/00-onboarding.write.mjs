// Story: TASK-4.5
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, submitKeycloakLogin, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
function sql(statement) {
  return execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
}
const browser = await chromium.launch();
try {
  const context = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(page, byTestId(page, "login-button"));
  await byTestId(page, "onboarding-yes").click();
  await byTestId(page, "standard-us-55").click();
  await byTestId(page, "onboarding-title").fill("My first Malawi");
  await byTestId(page, "region-lake_malawi").click();
  await byTestId(page, "onboarding-save").click();
  await byTestId(page, "onboarding-reward").waitFor();
  assert.match(await byTestId(page, "onboarding-reward").textContent(), /Your tank is created.*First tank/s);
  await byTestId(page, "onboarding-photo").waitFor();
  const token = await passwordGrantToken();
  const headers = { Authorization: `Bearer ${token}` };
  const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
  assert.deepEqual(me.profile.interests, ["lake_malawi"]);
  assert.deepEqual(me.profile.badges, ["first_tank"]);
  assert.ok(me.profile.onboardingCompletedAt);
  const tanks = await (await fetch(`${API_URL}/api/profiles/${me.profile.id}/tanks`)).json();
  const tank = tanks.items.find(t => t.title === "My first Malawi");
  assert.ok(tank);
  const detail = await (await fetch(`${API_URL}/api/tanks/${tank.id}`)).json();
  assert.equal(detail.volume.inputValue, 55);
  assert.equal(detail.dimensions.inputWidth, 48);
  assert.equal(detail.dimensions.inputHeight, 21);
  assert.equal(detail.dimensions.inputDepth, 13);
  assert.equal(detail.region, "lake_malawi");
  await page.screenshot({ path: "/tmp/task-4.5-reward-dark.png" });
  await byTestId(page, "onboarding-done").click();
  await byTestId(page, "badge-first_tank").waitFor();
  await page.reload({ waitUntil: "networkidle" });
  await byTestId(page, "me-name").waitFor();
  await byTestId(page, "badge-first_tank").waitFor();
  await signOutThroughKeycloak(page);

  const legacyToken = await passwordGrantToken("dev-mod");
  const legacyMe = await (await fetch(`${API_URL}/api/me`, { headers: { Authorization: `Bearer ${legacyToken}` } })).json();
  const legacyId = sql(`INSERT INTO public.tank (legacy_id, profile_id, title, category, region, state, created_at) VALUES (987654, ${Number(legacyMe.profile.id)}, 'My archive tank', 'american', 'americas_unassigned', 'published', now()) RETURNING id` ).split("\n")[0];
  const returningContext = await browser.newContext({ locale: "en-GB", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const returning = await returningContext.newPage();
  await returning.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(returning, byTestId(returning, "login-button"), "dev-mod");
  await byTestId(returning, "onboarding-yes").click();
  await byTestId(returning, `legacy-tank-${legacyId}`).click();
  await byTestId(returning, "region-south_america").click();
  await byTestId(returning, "onboarding-save").click();
  await byTestId(returning, "onboarding-reward").waitFor();
  assert.match(await byTestId(returning, "onboarding-reward").textContent(), /Returned/);
  const adopted = await (await fetch(`${API_URL}/api/tanks/${legacyId}`)).json();
  assert.equal(adopted.region, "south_america");
  assert.equal(sql(`SELECT count(*) FROM public.tank WHERE legacy_id = 987654`), "1");
  await returning.screenshot({ path: "/tmp/task-4.5-returned-light.png" });
  await byTestId(returning, "onboarding-done").click();
  const refineId = sql(`INSERT INTO public.tank (legacy_id, profile_id, title, category, region, state, created_at) VALUES (987655, ${Number(legacyMe.profile.id)}, 'Refine on tank page', 'american', 'americas_unassigned', 'published', now()) RETURNING id`).split("\n")[0];
  await returning.goto(`${WEB_URL}/tanks/${refineId}`, { waitUntil: "networkidle" });
  await byTestId(returning, "refine-central_america").click();
  await byTestId(returning, "tank-region").getByText("Central America", { exact: true }).waitFor();
  assert.equal((await (await fetch(`${API_URL}/api/tanks/${refineId}`)).json()).region, "central_america");
  const refinedProfile = await (await fetch(`${API_URL}/api/me`, { headers: { Authorization: `Bearer ${legacyToken}` } })).json();
  assert.deepEqual(refinedProfile.profile.interests, ["central_america", "south_america"]);
  await returning.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(returning);
  console.log("TASK-4.5: standard 55 gallon tank with dimensions and reward; legacy takeover with region refinement and Returned badge");
} finally { await browser.close(); }
