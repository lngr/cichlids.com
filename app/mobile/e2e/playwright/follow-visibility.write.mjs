// Story: TASK-5.15
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
const other = { Authorization: `Bearer ${otherToken}`, "Content-Type": "application/json" };
const otherProfile = (await (await fetch(`${API_URL}/api/me`, { headers: other })).json()).profile;

const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers: owner, body: JSON.stringify({ title: "Followers path", isPublic: true }) });
assert.equal(created.status, 201);
const tank = await created.json();
const entry = (await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers: owner, body: JSON.stringify({ amount: { value: 10, unit: "liter" } }) })).json()).id;

async function touchTarget(locator) {
  assert.ok((await locator.boundingBox()).height >= 48, "a touch target is at least 48 high");
}

const browser = await chromium.launch();
const errors = [];
try {
  // The owner sets the tank to followers in the tank settings.
  const ownerContext = await browser.newContext({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const ownerPage = await ownerContext.newPage();
  ownerPage.on("pageerror", error => errors.push(String(error)));
  await ownerPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(ownerPage);
  await ownerPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(ownerPage, "tank-visibility-open").click();
  for (const level of ["public", "followers", "selected", "private"]) {
    await touchTarget(byTestId(ownerPage, `tank-visibility-profile-choice-${level}`));
  }
  await byTestId(ownerPage, "tank-visibility-profile-choice-followers").click();
  await byTestId(ownerPage, "tank-visibility-save").click();
  await byTestId(ownerPage, "tank-private-badge").filter({ hasText: "Followers only" }).waitFor();
  assert.equal(sql(`SELECT profile_visibility || '/' || stream_visibility FROM public.tank WHERE id = ${Number(tank.id)}`), "followers/followers");
  await ownerPage.screenshot({ path: "/tmp/task-5.15-owner-settings-light.png" });

  // Another member sees a locked page and asks to follow.
  const otherContext = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const otherPage = await otherContext.newPage();
  otherPage.on("pageerror", error => errors.push(String(error)));
  await otherPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(otherPage, byTestId(otherPage, "login-button"), "dev-mod");
  await otherPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  const ask = byTestId(otherPage, "tank-follow-gate-button");
  await ask.waitFor();
  await touchTarget(ask);
  assert.equal(await ask.textContent(), "Ask to follow");
  await ask.click();
  await byTestId(otherPage, "tank-follow-gate-requested").waitFor();
  assert.equal(await byTestId(otherPage, "tank-follow-gate-button").textContent(), "Requested");
  await otherPage.screenshot({ path: "/tmp/task-5.15-requested-dark.png" });
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}`, { headers: other })).status, 404);
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}`)).status, 404);

  // The owner approves the request on the audience screen.
  await ownerPage.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(ownerPage, "tank-audience-pending").waitFor();
  await touchTarget(byTestId(ownerPage, "tank-audience-open"));
  await byTestId(ownerPage, "tank-audience-open").click();
  const approve = byTestId(ownerPage, `audience-approve-${otherProfile.id}`);
  await approve.waitFor();
  await touchTarget(approve);
  await touchTarget(byTestId(ownerPage, `audience-decline-${otherProfile.id}`));
  assert.match(await approve.getAttribute("aria-label"), /^Approve the request of /);
  await ownerPage.screenshot({ path: "/tmp/task-5.15-audience-light.png" });
  await approve.click();
  await byTestId(ownerPage, `audience-remove-follower-${otherProfile.id}`).waitFor();
  assert.equal(await byTestId(ownerPage, `audience-request-${otherProfile.id}`).count(), 0);

  // The approved follower sees the tank and its stream, anonymous callers and other members do not.
  await otherPage.reload({ waitUntil: "networkidle" });
  const following = byTestId(otherPage, "tank-follow-button");
  await following.waitFor();
  assert.equal(await following.textContent(), "Following");
  await byTestId(otherPage, "tank-view-care").click();
  await byTestId(otherPage, `care-entry-${entry}`).waitFor();
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: other })).status, 200);
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}/care`)).status, 404);
  assert.equal(sql(`SELECT count(*) FROM public.outbox_event WHERE aggregate_id = '${tank.id}' AND event_type IN ('tank.follow_requested', 'tank.follow_approved')`), "2");
  await otherPage.screenshot({ path: "/tmp/task-5.15-following-dark.png" });

  // Removing the follower closes the tank again.
  await byTestId(ownerPage, `audience-remove-follower-${otherProfile.id}`).click();
  await byTestId(ownerPage, "audience-followers-empty").waitFor();
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}`, { headers: other })).status, 404);
  await otherPage.reload({ waitUntil: "networkidle" });
  await byTestId(otherPage, "tank-follow-gate-button").waitFor();
  assert.equal(await byTestId(otherPage, "tank-follow-gate-button").textContent(), "Ask to follow");

  // A selected member sees a tank for selected members without following it.
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}/visibility`, { method: "PUT", headers: owner, body: JSON.stringify({ profile: "selected", stream: "selected" }) })).status, 200);
  const handle = byTestId(ownerPage, "audience-handle");
  await ownerPage.reload({ waitUntil: "networkidle" });
  await handle.fill("no-such-member-here");
  await byTestId(ownerPage, "audience-add").click();
  await ownerPage.getByText("There is no member with this handle.").waitFor();
  await handle.fill(otherProfile.username);
  await touchTarget(byTestId(ownerPage, "audience-add"));
  await byTestId(ownerPage, "audience-add").click();
  await byTestId(ownerPage, `audience-remove-selected-${otherProfile.id}`).waitFor();
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}`, { headers: other })).status, 200);
  await otherPage.reload({ waitUntil: "networkidle" });
  await byTestId(otherPage, "tank-view-care").click();
  await byTestId(otherPage, `care-entry-${entry}`).waitFor();
  await byTestId(ownerPage, `audience-remove-selected-${otherProfile.id}`).click();
  await byTestId(ownerPage, "audience-selected-empty").waitFor();
  assert.equal((await fetch(`${API_URL}/api/tanks/${tank.id}`, { headers: other })).status, 404);

  await otherPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(otherPage);
  await ownerPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(ownerPage, "logout-button").waitFor();
  await signOutThroughKeycloak(ownerPage);
  assert.deepEqual(errors, []);
  console.log("TASK-5.15: a tank for followers, a follow request approved by the owner, the follower removed, a selected member added and removed");
} finally {
  await browser.close();
  sql(`DELETE FROM public.follow WHERE target_tank_id = ${Number(tank.id)}; DELETE FROM public.tank_selected_member WHERE tank_id = ${Number(tank.id)}`);
}
