// Story: TASK-3.41
// The web app's durable session through the API (backend for frontend), as a member who signs in
// on the web build:
// - The browser holds only the httpOnly session cookie: no token appears in the script-visible
//   cookies, in localStorage or in sessionStorage.
// - An access token that expired months ago is renewed on the server with the offline token when
//   the app opens, and the member stays signed in.
// - Without a connection, a screen shows the connection message and the member stays signed in;
//   once the connection is back, the retry loads the screen.
// - The title and description typed for an uploaded photo survive a sign-out: after signing in
//   again, the draft opens with them.
// - After the account is suspended in Keycloak, opening the app signs the member out and the API
//   deletes the session.
//
// The member is a Keycloak account this spec creates and deletes again. Runs on the isolated write
// stack only (see e2e/write-stack.sh): the session row and the profile live in the write database.
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, KEYCLOAK_URL, WEB_URL, byTestId, submitKeycloakLogin } from "./support.mjs";

const SPEC_DIR = path.dirname(fileURLToPath(import.meta.url));
const COMPOSE_FILE = path.resolve(SPEC_DIR, "../../../stack/compose.yaml");
const UPLOAD_PHOTO = path.join(SPEC_DIR, "..", "fixtures", "upload-photo.jpg");
const UPLOAD_INPUT_KEY = "cichlids.upload.input";
const CONNECTION_MESSAGE = "The connection failed. You stay signed in; please try again in a moment.";
const JWT = /eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/;

// Runs one statement against the write database and returns its unaligned output.
function sql(statement) {
  if (process.env.E2E_DB !== "cichlids_e2e") throw new Error("Refusing to write outside the cichlids_e2e database");
  return execFileSync(
    "docker",
    [
      "compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e",
      "-v", "ON_ERROR_STOP=1", "-t", "-A", "-F", "|", "-c", statement,
    ],
    { stdio: ["ignore", "pipe", "pipe"] },
  ).toString().trim();
}

async function adminRequest(method, realmPath, body) {
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

async function createMember(username, password) {
  await adminRequest("POST", "users", {
    username,
    enabled: true,
    email: `${username}@example.org`,
    emailVerified: true,
    firstName: "Session",
    lastName: "Member",
    credentials: [{ type: "password", value: password, temporary: false }],
  });
  const users = await (await adminRequest("GET", `users?username=${encodeURIComponent(username)}&exact=true`)).json();
  if (users.length !== 1) throw new Error(`Expected one Keycloak user ${username}, found ${users.length}`);
  return users[0].id;
}

function sessionRow(subject) {
  const row = sql(`SELECT access_token, refresh_token, access_token_expires_at > now() FROM web_session WHERE subject = '${subject}'`);
  if (!row) return null;
  const [accessToken, refreshToken, valid] = row.split("|");
  return { accessToken, refreshToken, valid: valid === "t" };
}

const username = `e2e-session-${crypto.randomBytes(4).toString("hex")}`;
const password = crypto.randomBytes(12).toString("base64url");
let memberId = null;
const browser = await chromium.launch();
try {
  memberId = await createMember(username, password);
  if (!/^[0-9a-f-]{36}$/.test(memberId)) throw new Error(`Unexpected Keycloak user id ${memberId}`);

  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));
  const meName = byTestId(page, "me-name");
  const loginButton = byTestId(page, "login-button");

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle", timeout: 60000 });
  await submitKeycloakLogin(page, loginButton, username, password);
  await meName.waitFor({ timeout: 30000 });
  const session = sessionRow(memberId);
  if (!session) throw new Error("The API stored no web session for the member");
  console.log(`Signed in on the web as ${username}`);

  // The browser holds no token: the session cookie is httpOnly, and neither script-visible cookies
  // nor web storage contain a token.
  const cookies = await context.cookies(API_URL);
  const sessionCookie = cookies.find((cookie) => cookie.name.endsWith("cichlids-session"));
  if (!sessionCookie) throw new Error(`No session cookie for the API, cookies: ${cookies.map((c) => c.name).join(", ")}`);
  if (!sessionCookie.httpOnly) throw new Error("The session cookie is visible to scripts");
  if (sessionCookie.sameSite !== "Lax") throw new Error(`The session cookie is SameSite=${sessionCookie.sameSite}`);
  if ([session.accessToken, session.refreshToken].some((token) => sessionCookie.value.includes(token))) {
    throw new Error("The session cookie holds a token");
  }
  const visible = await page.evaluate(() => {
    const dump = (storage) => Array.from({ length: storage.length }, (_, i) => [storage.key(i), storage.getItem(storage.key(i))]);
    return { cookie: document.cookie, local: dump(localStorage), session: dump(sessionStorage) };
  });
  const visibleText = JSON.stringify(visible);
  if (visible.cookie.includes("cichlids-session")) throw new Error("The session cookie is readable from scripts");
  if (JWT.test(visibleText)) throw new Error(`A token is visible to scripts: ${visibleText}`);
  if ([session.accessToken, session.refreshToken].some((token) => visibleText.includes(token))) {
    throw new Error("A token of the session is visible to scripts");
  }
  console.log(`No token in script-visible cookies or web storage (keys: ${[...visible.local, ...visible.session].map(([key]) => key).join(", ") || "none"})`);

  // An access token that expired three months ago is renewed with the offline token on opening.
  sql(`UPDATE web_session SET access_token_expires_at = now() - interval '90 days' WHERE subject = '${memberId}'`);
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle", timeout: 60000 });
  await meName.waitFor({ timeout: 30000 });
  const renewed = sessionRow(memberId);
  if (!renewed?.valid || renewed.accessToken === session.accessToken) throw new Error("The expired access token was not renewed");
  console.log("After three months without a valid access token the app opens signed in");

  // Without a connection, a screen shows the connection message and the member stays signed in.
  await page.goto(`${WEB_URL}/gallery`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "upload-button").waitFor({ timeout: 30000 });
  await context.setOffline(true);
  await byTestId(page, "tab-me").click({ timeout: 20000 });
  await page.getByText(CONNECTION_MESSAGE, { exact: true }).waitFor({ timeout: 20000 });
  if ((await loginButton.count()) !== 0) throw new Error("The member was signed out without a connection");
  await byTestId(page, "logout-button").waitFor({ timeout: 5000 });
  console.log(`Without a connection the Me tab shows "${CONNECTION_MESSAGE}" and the member stays signed in`);
  await context.setOffline(false);
  await byTestId(page, "error-retry").click();
  await meName.waitFor({ timeout: 30000 });
  console.log("Back online, the retry loads the profile");

  // The input typed for an uploaded photo survives a sign-out and returns with the draft.
  const keptTitle = "Kept across a sign-out";
  const keptDescription = "Typed before signing out";
  await page.goto(`${WEB_URL}/gallery`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "upload-button").click({ timeout: 20000 });
  await page.waitForURL(/\/upload$/, { timeout: 10000 });
  await byTestId(page, "upload-file-input").setInputFiles(UPLOAD_PHOTO);
  await byTestId(page, "upload-preview").waitFor({ timeout: 30000 });
  await byTestId(page, "upload-title").fill(keptTitle);
  await byTestId(page, "upload-description").fill(keptDescription);
  const keptInput = (key) => page.evaluate((storageKey) => localStorage.getItem(storageKey) ?? "", key);
  await page.waitForFunction(
    ([key, text]) => (localStorage.getItem(key) ?? "").includes(text),
    [UPLOAD_INPUT_KEY, keptDescription],
    { timeout: 10000 },
  );
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "logout-button").click({ timeout: 20000 });
  await loginButton.waitFor({ timeout: 20000 });
  const keptWhileSignedOut = await keptInput(UPLOAD_INPUT_KEY);
  if (!keptWhileSignedOut.includes(keptTitle)) throw new Error("The typed upload input is gone after the sign-out");
  if (JWT.test(keptWhileSignedOut)) throw new Error("The kept upload input holds a token");
  await submitKeycloakLogin(page, loginButton, username, password);
  await meName.waitFor({ timeout: 30000 });
  await byTestId(page, "draft-open").first().click({ timeout: 20000 });
  await page.waitForURL(/\/upload\?draft=/, { timeout: 10000 });
  await byTestId(page, "upload-preview").waitFor({ timeout: 30000 });
  const shownTitle = await byTestId(page, "upload-title").inputValue();
  const shownDescription = await byTestId(page, "upload-description").inputValue();
  if (shownTitle !== keptTitle || shownDescription !== keptDescription) {
    throw new Error(`Expected the draft to open with "${keptTitle}" / "${keptDescription}", got "${shownTitle}" / "${shownDescription}"`);
  }
  console.log("The title and description typed before a sign-out return with the draft after signing in again");
  await byTestId(page, "upload-discard").click();
  await page.waitForURL((url) => !url.pathname.startsWith("/upload"), { timeout: 20000 });
  await page.waitForFunction((key) => localStorage.getItem(key) === null, UPLOAD_INPUT_KEY, { timeout: 10000 });
  console.log("Discarding the draft forgets its kept input");

  // A suspended account is signed out when the app opens, and its session is deleted.
  await adminRequest("PUT", `users/${memberId}`, { enabled: false });
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle", timeout: 60000 });
  await loginButton.waitFor({ timeout: 30000 });
  if ((await meName.count()) !== 0) throw new Error("The suspended member still sees the profile");
  if (sessionRow(memberId)) throw new Error("The session of the suspended account still exists");
  console.log("After the suspension, opening the app signs the member out and ends the session");

  const unexpected = pageErrors.filter((error) => !error.includes("Failed to fetch"));
  if (unexpected.length > 0) throw new Error(`Page errors: ${unexpected.join("; ")}`);
} finally {
  await browser.close();
  if (memberId) await adminRequest("DELETE", `users/${memberId}`);
}
