// Story: TASK-5.7
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, submitKeycloakLogin, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
function sql(statement) {
  return execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
}

const ownerToken = await passwordGrantToken();
const otherToken = await passwordGrantToken("dev-mod");
const owner = { Authorization: `Bearer ${ownerToken}`, "Content-Type": "application/json" };
const other = { Authorization: `Bearer ${otherToken}` };

// Creating a tank needs an explicit answer.
const refused = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers: owner, body: JSON.stringify({ title: "Unanswered" }) });
assert.equal(refused.status, 400);
assert.equal((await refused.json()).code, "tank_visibility_answer_required");

const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers: owner, body: JSON.stringify({ title: "Visibility path", isPublic: true }) });
assert.equal(created.status, 201);
const tank = await created.json();
assert.deepEqual(tank.visibility, { profile: "public", stream: "public" });
async function log(amount) {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers: owner, body: JSON.stringify({ amount: { value: amount, unit: "liter" } }) });
  assert.equal(response.status, 201);
  return (await response.json()).id;
}
const publicEntry = await log(10);
const secretEntry = await log(20);

const browser = await chromium.launch();
const errors = [];
try {
  // The owner marks the second entry only me in the care view.
  const ownerContext = await browser.newContext({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const ownerPage = await ownerContext.newPage();
  ownerPage.on("pageerror", error => errors.push(String(error)));
  await ownerPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(ownerPage);
  await ownerPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await ownerPage.getByTestId("tank-visibility").waitFor();
  await ownerPage.getByTestId("tank-private-badge").waitFor({ state: "detached" }).catch(() => {});
  assert.equal(await byTestId(ownerPage, "tank-private-badge").count(), 0);
  await byTestId(ownerPage, "tank-view-care").click();
  await byTestId(ownerPage, `care-entry-${publicEntry}`).waitFor();
  await byTestId(ownerPage, `care-entry-${secretEntry}`).waitFor();
  const toggle = byTestId(ownerPage, `care-only-me-${secretEntry}`);
  assert.ok((await toggle.boundingBox()).height >= 48);
  await toggle.click();
  await byTestId(ownerPage, `care-only-me-badge-${secretEntry}`).waitFor();
  assert.equal(await byTestId(ownerPage, `care-only-me-badge-${publicEntry}`).count(), 0);
  await ownerPage.screenshot({ path: "/tmp/task-5.7-owner-care-light.png" });

  // Another member opens the tank and sees only the public entry.
  const otherContext = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const otherPage = await otherContext.newPage();
  otherPage.on("pageerror", error => errors.push(String(error)));
  await otherPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(otherPage, byTestId(otherPage, "login-button"), "dev-mod");
  await otherPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(otherPage, "tank-view-care").click();
  await byTestId(otherPage, `care-entry-${publicEntry}`).waitFor();
  assert.equal(await byTestId(otherPage, `care-entry-${secretEntry}`).count(), 0);
  assert.equal(await byTestId(otherPage, "tank-visibility").count(), 0, "settings belong to the owner");
  await otherPage.screenshot({ path: "/tmp/task-5.7-other-care-dark.png" });
  const apiList = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: other })).json();
  assert.deepEqual(apiList.items.map(item => item.id), [publicEntry]);
  const anonymousList = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`)).json();
  assert.deepEqual(anonymousList.items.map(item => item.id), [publicEntry]);

  // The owner restricts the stream, then makes the whole tank private in the tank settings.
  await ownerPage.reload({ waitUntil: "networkidle" });
  await byTestId(ownerPage, "tank-visibility-open").click();
  await byTestId(ownerPage, "tank-visibility-stream-choice-private").click();
  await byTestId(ownerPage, "tank-visibility-save").click();
  await byTestId(ownerPage, "tank-private-badge").waitFor();
  assert.match(await byTestId(ownerPage, "tank-private-badge").textContent(), /Care stream private/);
  let streamClosed = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: other });
  assert.equal(streamClosed.status, 404);
  assert.equal((await streamClosed.json()).code, "care_not_found");
  await otherPage.reload({ waitUntil: "networkidle" });
  await byTestId(otherPage, "tank-view-care").click();
  await byTestId(otherPage, "tank-care-private").waitFor();
  assert.equal(await byTestId(otherPage, `care-entry-${publicEntry}`).count(), 0);

  await byTestId(ownerPage, "tank-visibility-open").click();
  await byTestId(ownerPage, "tank-visibility-profile-choice-private").click();
  await byTestId(ownerPage, "tank-visibility-save").click();
  await byTestId(ownerPage, "tank-private-badge").filter({ hasText: "Private tank" }).waitFor();
  assert.equal(sql(`SELECT profile_visibility || '/' || stream_visibility FROM public.tank WHERE id = ${Number(tank.id)}`), "private/private");
  assert.equal(sql(`SELECT count(*) FROM public.outbox_event WHERE event_type = 'tank.visibility_changed' AND aggregate_id = '${tank.id}'`), "2");
  await ownerPage.screenshot({ path: "/tmp/task-5.7-owner-private-light.png" });
  const hidden = await fetch(`${API_URL}/api/tanks/${tank.id}`, { headers: other });
  assert.equal(hidden.status, 404);
  assert.equal((await hidden.json()).code, "tank_not_found");
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}`)).status, 404);
  await otherPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await otherPage.getByText("This tank is unavailable.").first().waitFor();
  await otherPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(otherPage);

  // A new tank asks the question first; no answer means no tank.
  await ownerPage.goto(`${WEB_URL}/onboarding`, { waitUntil: "networkidle" });
  await byTestId(ownerPage, "onboarding-yes").click();
  const yes = byTestId(ownerPage, "onboarding-visibility-public");
  const no = byTestId(ownerPage, "onboarding-visibility-private");
  await yes.waitFor();
  const [yesBox, noBox] = [await yes.boundingBox(), await no.boundingBox()];
  assert.ok(yesBox.height >= 48 && noBox.height >= 48);
  assert.equal(yesBox.width, noBox.width);
  assert.equal(await byTestId(ownerPage, "standard-us-55").count() + await byTestId(ownerPage, "onboarding-save").count(), 0, "the form waits for the answer");
  await ownerPage.screenshot({ path: "/tmp/task-5.7-question-light.png" });
  await no.click();
  await byTestId(ownerPage, "standard-us-55").click();
  await byTestId(ownerPage, "onboarding-title").fill("Private by answer");
  await byTestId(ownerPage, "region-lake_malawi").click();
  await byTestId(ownerPage, "onboarding-save").click();
  await byTestId(ownerPage, "onboarding-reward").waitFor();
  const answered = sql(`SELECT t.profile_visibility || '/' || t.stream_visibility || '/' || c.answer FROM public.tank t JOIN public.tank_visibility_consent c ON c.tank_id = t.id WHERE t.title = 'Private by answer'`);
  assert.equal(answered, "private/private/false");
  await byTestId(ownerPage, "onboarding-done").click();
  await byTestId(ownerPage, "logout-button").waitFor();
  await signOutThroughKeycloak(ownerPage);
  assert.deepEqual(errors, []);
  console.log("TASK-5.7: a private and a public care entry, the other member sees only the public one; stream and profile levels in the tank settings; the question first on creating a tank");
} finally { await browser.close(); }
