// Story: TASK-7.5
// The app flow of provisional accounts, on the web build of the write stack with the WebCrypto test
// key provider (the browser opts in through localStorage; without the opt-in the web app has no
// device account).
// - A first start creates a provisional account without a form. The app shows the note about the
//   device account once, the member creates a private tank, and a tap on follow opens the
//   registration sheet with its reason.
// - Registering a fresh Keycloak account from the sheet keeps the tank: the profile of the device
//   becomes the profile of the account and the device credential is gone.
// - Signing in to the existing dev user asks whether to take the content along; yes moves the tank
//   into the profile of the dev user.
// - A no keeps the content on the device: the Me tab says so and offers to take it along later.
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import {
  API_URL, KEYCLOAK_URL, WEB_URL, ZONE, byTestId, createKeycloakAccount, deleteKeycloakAccount, passwordGrantToken, submitKeycloakLogin,
} from "./support.mjs";

const OPT_IN_KEY = "cichlids.deviceAccount.testKeys";
const CREDENTIAL_KEY = "cichlids.device.credential";
const END_SESSION = `${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/logout`;

async function api(method, route, token, body) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(`${API_URL}${route}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: response.status, body: await response.json().catch(() => null) };
}

// A first start of the app in a fresh browser that opted into the device account.
async function firstStart(browser, errors) {
  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, viewport: { width: 390, height: 844 } });
  await context.addInitScript((key) => { try { localStorage.setItem(key, "1"); } catch { /* another origin */ } }, OPT_IN_KEY);
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(String(error)));
  page.on("console", (message) => { if (message.type() === "error") consoleLines.push(message.text()); });
  await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  return { context, page };
}

// Reads the note, continues and creates the first tank; resolves with the id of the new tank.
async function createFirstTank(page, title) {
  const note = byTestId(page, "device-note");
  await note.waitFor({ timeout: 30000 });
  assert.match((await note.textContent()) ?? "", /Your account lives on this device/);
  await byTestId(page, "device-note-continue").click();
  await byTestId(page, "onboarding-yes").click();
  assert.equal(await byTestId(page, "onboarding-visibility-public").count(), 0, "a provisional account is not asked about visibility");
  assert.equal(await byTestId(page, "onboarding-no").count(), 0, "a provisional account has no interests step");
  await page.locator('[data-testid^="standard-"]').first().click();
  await byTestId(page, "onboarding-title").fill(title);
  await byTestId(page, "region-lake_malawi").click();
  const created = page.waitForResponse((response) => response.url() === `${API_URL}/api/tanks` && response.request().method() === "POST");
  await byTestId(page, "onboarding-save").click();
  const response = await created;
  assert.equal(response.status(), 201);
  const tank = await response.json();
  assert.equal(tank.visibility.profile, "private");
  await byTestId(page, "tank-private-badge").waitFor({ timeout: 30000 });
  return Number(tank.id);
}

const deviceCredential = (page) => page.evaluate((key) => localStorage.getItem(key), CREDENTIAL_KEY);

// The Me tab signs out through Keycloak; the browser ends its session at the end session endpoint.
async function signOut(page) {
  await byTestId(page, "tab-me").click();
  const endSession = page.waitForRequest((request) => request.url().startsWith(END_SESSION), { timeout: 20000 });
  await byTestId(page, "logout-button").click({ timeout: 20000 });
  await endSession;
  await page.waitForURL(`${WEB_URL}/me`, { timeout: 30000 });
}

const stamp = Date.now();
const username = `device-${stamp}`;
const password = "device-password"; // gitleaks:allow
let accountId;
const errors = [];
const consoleLines = [];
const browser = await chromium.launch();
try {
  // A public tank of the dev user, so that there is something to follow.
  const member = await passwordGrantToken();
  const memberMe = await api("GET", "/api/me", member);
  assert.equal(memberMe.status, 200);
  const memberId = memberMe.body.profile.id;
  const publicTank = await api("POST", "/api/tanks", member, {
    title: `Followed tank ${stamp}`, region: "lake_malawi", isPublic: true,
    volume: { value: 54, unit: "liter" }, dimensions: { width: 60, depth: 30, height: 30, unit: "cm" },
  });
  assert.equal(publicTank.status, 201, JSON.stringify(publicTank.body));

  // First start, note, private tank, gated follow and a registration that keeps the tank.
  {
    const { context, page } = await firstStart(browser, errors);
    const tank = await createFirstTank(page, `Device tank ${stamp}`);
    const credential = JSON.parse((await deviceCredential(page)) ?? "null");
    assert.ok(credential?.profileId, "the first start registered the installation");
    console.log("A first start creates a provisional account, shows the note and keeps a private tank");

    await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle" });
    await byTestId(page, "tab-me").waitFor({ timeout: 30000 });
    assert.equal(await byTestId(page, "device-note").count(), 0, "the note is shown once");
    assert.doesNotMatch(page.url(), /onboarding/);
    await byTestId(page, "tab-me").click();
    await byTestId(page, "device-account").waitFor({ timeout: 30000 });
    assert.equal(await byTestId(page, "logout-button").count(), 0, "the device account has nothing to sign out of");
    console.log("The note is remembered and the Me tab shows the device account");

    await page.goto(`${WEB_URL}/tanks/${publicTank.body.id}`, { waitUntil: "networkidle" });
    await byTestId(page, "tank-follow-button").click({ timeout: 30000 });
    const sheet = byTestId(page, "registration-sheet");
    await sheet.waitFor({ timeout: 10000 });
    assert.match((await byTestId(page, "registration-reason").textContent()) ?? "", /Following a tank needs an account/);
    await page.screenshot({ path: "/tmp/task-7.5-sheet.png" });
    console.log("Follow opens the registration sheet with its reason");

    accountId = await createKeycloakAccount(username, password);
    await submitKeycloakLogin(page, byTestId(page, "registration-register"), username, password);
    await byTestId(page, "registration-done").waitFor({ timeout: 60000 });
    assert.match((await byTestId(page, "registration-done").textContent()) ?? "", /Your account is ready/);
    assert.equal((await byTestId(page, "registration-count-tanks").textContent())?.trim(), "Tanks1");
    await byTestId(page, "registration-close").click();
    assert.equal(await deviceCredential(page), null, "the device credential is gone");

    const account = await passwordGrantToken(username, password);
    const me = await api("GET", "/api/me", account);
    assert.equal(String(me.body.profile.id), String(credential.profileId), "the profile of the device became the profile of the account");
    assert.equal(me.body.provisional, false);
    const kept = await api("GET", `/api/tanks/${tank}`, account);
    assert.equal(kept.status, 200, JSON.stringify(kept.body));
    assert.equal(kept.body.visibility.profile, "private");
    await byTestId(page, "tab-me").click();
    await byTestId(page, "me-name").waitFor({ timeout: 30000 });
    console.log("Registering a new account keeps the tank");
    await context.close();
  }

  // Signing in to the existing dev user takes the content along on yes.
  {
    const { context, page } = await firstStart(browser, errors);
    const tank = await createFirstTank(page, `Moving tank ${stamp}`);
    await byTestId(page, "tab-me").click();
    await byTestId(page, "device-account-register").click();
    await submitKeycloakLogin(page, byTestId(page, "registration-register"), "dev-user", "dev-password");
    await byTestId(page, "registration-take-along").waitFor({ timeout: 60000 });
    assert.equal((await byTestId(page, "registration-count-tanks").textContent())?.trim(), "Tanks1");
    await byTestId(page, "registration-take-along").click();
    await byTestId(page, "registration-done").waitFor({ timeout: 60000 });
    assert.match((await byTestId(page, "registration-done").textContent()) ?? "", /Content taken over/);
    await byTestId(page, "registration-close").click();
    const moved = await api("GET", `/api/tanks/${tank}`, member);
    assert.equal(moved.status, 200, JSON.stringify(moved.body));
    assert.equal(String(moved.body.author.id), String(memberId));
    assert.equal(await deviceCredential(page), null);
    console.log("An existing account takes the content along");
    await signOut(page);
    await context.close();
  }

  // A no leaves the content on the device; Me offers to take it along later.
  {
    const { context, page } = await firstStart(browser, errors);
    const tank = await createFirstTank(page, `Staying tank ${stamp}`);
    await byTestId(page, "tab-me").click();
    await byTestId(page, "device-account-register").click();
    await submitKeycloakLogin(page, byTestId(page, "registration-register"), "dev-user", "dev-password");
    await byTestId(page, "registration-keep-on-device").waitFor({ timeout: 60000 });
    await byTestId(page, "registration-keep-on-device").click();
    await byTestId(page, "registration-done").waitFor({ timeout: 60000 });
    assert.match((await byTestId(page, "registration-done").textContent()) ?? "", /Content stays on this device/);
    await byTestId(page, "registration-close").click();
    assert.equal((await api("GET", `/api/tanks/${tank}`, member)).status, 404, "the tank is not in the account");
    await byTestId(page, "device-account-left-behind").waitFor({ timeout: 30000 });
    assert.ok(await deviceCredential(page), "the device credential stays");
    console.log("A no keeps the content on the device");

    await byTestId(page, "device-account-take-along").click();
    await byTestId(page, "registration-take-along").waitFor({ timeout: 60000 });
    await byTestId(page, "registration-take-along").click();
    await byTestId(page, "registration-done").waitFor({ timeout: 60000 });
    await byTestId(page, "registration-close").click();
    const moved = await api("GET", `/api/tanks/${tank}`, member);
    assert.equal(moved.status, 200, JSON.stringify(moved.body));
    assert.equal(String(moved.body.author.id), String(memberId));
    assert.equal(await byTestId(page, "device-account-left-behind").count(), 0);
    console.log("The content left on the device can be taken along later");
    await signOut(page);
    await context.close();
  }

  assert.deepEqual(errors, [], `Page errors observed: ${errors.join("; ")}`);
} catch (error) {
  console.error(`Console errors before the failure:\n${consoleLines.slice(-20).join("\n")}`);
  throw error;
} finally {
  if (accountId) await deleteKeycloakAccount(accountId);
  await browser.close();
}

console.log("provisional-app.write.mjs passed");
