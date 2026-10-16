import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Shared pieces of the write specs (*.write.mjs): the write stack's URLs, the local dev user's
// credentials, and the Keycloak login through the real login page.

export const WEB_URL = process.env.E2E_WEB_URL;
export const API_URL = process.env.E2E_API_URL;
export const KEYCLOAK_URL = process.env.E2E_KEYCLOAK_URL ?? "http://localhost:8180";
if (!WEB_URL || !API_URL) {
  throw new Error("E2E_WEB_URL or E2E_API_URL is not set; run the write specs through `pnpm run e2e:write`.");
}

// Local development credentials of the seeded Keycloak realm (app/stack/keycloak/cichlids-realm.json).
export const USERNAME = "dev-user";
export const PASSWORD = "dev-password"; // gitleaks:allow
export const EXPECTED_NAMES = ["dev-user", "Dev User"];
export const AUTHORIZATION_ENDPOINT = `${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/auth`;
export const END_SESSION_ENDPOINT = `${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/logout`;

/** The locator of an element by its React Native testID (data-testid on the web). */
export function byTestId(page, testId) {
  return page.locator(`[data-testid="${testId}"]`);
}

/**
 * Presses the given login button and submits the dev user's credentials on the Keycloak page. On
 * the web the app asks the API to start the login and navigates the page to Keycloak; after the
 * credentials, Keycloak returns through the API's callback to the page the login started from.
 * When the browser is still signed in to Keycloak, Keycloak asks for the password alone. Resolves
 * once the credentials are submitted; the caller waits for its own logged-in state.
 */
export async function submitKeycloakLogin(page, loginButton, username = USERNAME, password = PASSWORD) {
  await loginButton.waitFor({ timeout: 20000 });
  await loginButton.click();
  await page.waitForURL((url) => url.href.startsWith(AUTHORIZATION_ENDPOINT), { timeout: 20000 });
  await page.waitForSelector("#password", { timeout: 20000 });
  if ((await page.locator("#username").count()) > 0) await page.fill("#username", username);
  await page.fill("#password", password);
  await page.click("#kc-login");
  await page.waitForURL((url) => url.href.startsWith(WEB_URL), { timeout: 30000 });
}

/**
 * Presses the logout button and waits until the page has passed Keycloak's end session endpoint
 * and is back on the Me tab of the web app, signed out. Resolves with the address of the end
 * session request.
 */
export async function signOutThroughKeycloak(page) {
  const endSession = page.waitForRequest((request) => request.url().startsWith(END_SESSION_ENDPOINT), { timeout: 20000 });
  await byTestId(page, "logout-button").click({ timeout: 20000 });
  const request = await endSession;
  await page.waitForURL(`${WEB_URL}/me`, { timeout: 30000 });
  await byTestId(page, "login-button").waitFor({ timeout: 30000 });
  return new URL(request.url());
}

/**
 * An access token of the dev user for direct API calls of a spec, from the password grant of the
 * local realm. The web app itself keeps no token in the browser.
 */
export async function passwordGrantToken(username = USERNAME, password = PASSWORD) {
  const response = await fetch(`${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "cichlids-app", grant_type: "password", username, password }),
  });
  if (!response.ok) throw new Error(`Password grant for ${username} returned ${response.status}: ${await response.text()}`);
  return (await response.json()).access_token;
}

/**
 * Logs in from the Me tab's login button. Resolves with the name the Me tab shows once the login
 * completed.
 */
export async function logInThroughKeycloak(page) {
  await submitKeycloakLogin(page, byTestId(page, "login-button"));

  const meName = byTestId(page, "me-name");
  await meName.waitFor({ timeout: 30000 });
  const loggedInName = (await meName.textContent())?.trim();
  if (!EXPECTED_NAMES.includes(loggedInName ?? "")) {
    throw new Error(`Expected the Me tab to show ${EXPECTED_NAMES.join(" or ")}, got "${loggedInName}"`);
  }
  return loggedInName;
}

/**
 * The time zone every browser context of a spec runs in. The app sends the local date of its browser
 * as the date of the member, so a spec that computes dates builds them in this zone and gives each
 * context this zone as its timezoneId.
 */
export const ZONE = "UTC";

/** The calendar date (YYYY-MM-DD) in ZONE of the instant, moved by the given number of days. */
export function localDate(instant = new Date(), days = 0) {
  return new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: ZONE })
    .format(new Date(instant.getTime() + days * 24 * 3600 * 1000));
}

const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");

/** Runs a statement on the database of the write stack and returns its trimmed output. */
export function sql(statement) {
  if (process.env.E2E_DB !== "cichlids_e2e") throw new Error("Refusing to run SQL outside the cichlids_e2e database");
  return execFileSync("docker", ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
}

/**
 * Removes the vacations of a member and their events. A spec that reads streaks calls it before it
 * starts and when it ends, so the vacations of another spec never move its deadlines.
 */
export function clearVacations(profileId) {
  sql(`delete from outbox_event where event_type in ('vacation.started', 'vacation.ended') and payload->>'profileId' = '${profileId}'`);
  sql(`delete from vacation where profile_id = ${profileId}`);
}

async function keycloakAdminRequest(method, realmPath, body) {
  const tokenResponse = await fetch(`${KEYCLOAK_URL}/realms/master/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "admin-cli", grant_type: "password", username: "admin", password: "admin" }), // gitleaks:allow
  });
  if (!tokenResponse.ok) throw new Error(`Admin token request returned ${tokenResponse.status}`);
  const { access_token: token } = await tokenResponse.json();
  const response = await fetch(`${KEYCLOAK_URL}/admin/realms/cichlids/${realmPath}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, ...(body ? { "Content-Type": "application/json" } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${realmPath} returned ${response.status}: ${await response.text()}`);
  return response;
}

/** Creates a Keycloak account through the admin API of the local stack and resolves with its id. */
export async function createKeycloakAccount(username, password) {
  await keycloakAdminRequest("POST", "users", {
    username,
    enabled: true,
    email: `${username}@example.org`,
    emailVerified: true,
    firstName: "Fresh",
    lastName: "Member",
    credentials: [{ type: "password", value: password, temporary: false }],
  });
  const users = await (await keycloakAdminRequest("GET", `users?username=${encodeURIComponent(username)}&exact=true`)).json();
  if (users.length !== 1) throw new Error(`Expected one Keycloak user named ${username}, found ${users.length}`);
  return users[0].id;
}

/** Deletes a Keycloak account created by createKeycloakAccount, which ends all its sessions. */
export async function deleteKeycloakAccount(id) {
  await keycloakAdminRequest("DELETE", `users/${id}`);
}
