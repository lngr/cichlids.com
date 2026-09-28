// Logs in through the real Keycloak login page as the local dev user and checks that the Me tab
// shows the profile from GET /api/me. Logs out and logs in a second time on the same page: Keycloak
// asks for credentials (logout ended its session), and the second authorization request has a
// fresh state and PKCE challenge. A page reload keeps the session, and after the final logout a
// reload keeps the anonymous state. Runs on the isolated write stack only (see
// e2e/write-stack.sh): the first /api/me call of a user creates their profile row.
import { chromium } from "playwright";

const WEB_URL = process.env.E2E_WEB_URL;
const KEYCLOAK_URL = process.env.E2E_KEYCLOAK_URL ?? "http://localhost:8180";
if (!WEB_URL) throw new Error("E2E_WEB_URL is not set; run this spec through `pnpm run e2e:write`.");

// Local development credentials of the seeded Keycloak realm (app/stack/keycloak/cichlids-realm.json).
const USERNAME = "dev-user";
const PASSWORD = "dev-password"; // gitleaks:allow
const EXPECTED_NAMES = ["dev-user", "Dev User"];
const AUTHORIZATION_ENDPOINT = `${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/auth`;

const browser = await chromium.launch();
try {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const authorizationRequests = [];
  context.on("request", (request) => {
    if (request.url().startsWith(AUTHORIZATION_ENDPOINT)) authorizationRequests.push(new URL(request.url()).searchParams);
  });
  const page = await context.newPage();
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  const loginButton = page.locator('[data-testid="login-button"]');
  const meName = page.locator('[data-testid="me-name"]');

  // On the web, expo-auth-session opens the Keycloak login page in a popup window and receives
  // the redirect back from it.
  async function logInThroughKeycloak() {
    await loginButton.waitFor({ timeout: 20000 });
    const popupPromise = page.waitForEvent("popup", { timeout: 20000 });
    await loginButton.click();
    const popup = await popupPromise;
    await popup.waitForSelector("#username", { timeout: 20000 });
    await popup.fill("#username", USERNAME);
    await popup.fill("#password", PASSWORD);
    await popup.click("#kc-login");

    await meName.waitFor({ timeout: 30000 });
    const loggedInName = (await meName.textContent())?.trim();
    if (!EXPECTED_NAMES.includes(loggedInName ?? "")) {
      throw new Error(`Expected the Me tab to show ${EXPECTED_NAMES.join(" or ")}, got "${loggedInName}"`);
    }
    return loggedInName;
  }

  async function logOut() {
    await page.locator('[data-testid="logout-button"]').click();
    await loginButton.waitFor({ timeout: 20000 });
    if ((await meName.count()) !== 0) throw new Error("The profile name is visible after logging out");
  }

  await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  await page.locator('[data-testid="tab-me"]').click({ timeout: 20000 });
  await page.waitForURL(/\/me$/, { timeout: 10000 });

  console.log(`Logged in, Me tab shows "${await logInThroughKeycloak()}"`);

  await logOut();
  console.log("Logged out, Me tab shows the login button");

  // Same page, no reload in between: the second login has to build a new authorization request.
  // The popup shows the credentials form only when logout ended the Keycloak session.
  console.log(`Logged in a second time, Me tab shows "${await logInThroughKeycloak()}"`);

  if (authorizationRequests.length !== 2) {
    throw new Error(`Expected 2 authorization requests, saw ${authorizationRequests.length}`);
  }
  const [first, second] = authorizationRequests;
  for (const param of ["state", "code_challenge"]) {
    if (!first.get(param) || first.get(param) === second.get(param)) {
      throw new Error(`Expected a fresh ${param} per login, got "${first.get(param)}" and "${second.get(param)}"`);
    }
  }
  console.log("Each login sends a fresh state and PKCE challenge");

  await page.reload({ waitUntil: "networkidle", timeout: 30000 });
  await meName.waitFor({ timeout: 30000 });
  console.log("Session survives a page reload");

  await logOut();
  await page.reload({ waitUntil: "networkidle", timeout: 30000 });
  await loginButton.waitFor({ timeout: 30000 });
  if ((await meName.count()) !== 0) throw new Error("The profile name is visible after a reload following logout");
  console.log("Logged-out state survives a page reload");

  if (pageErrors.length > 0) {
    throw new Error(`Page errors observed: ${pageErrors.join("; ")}`);
  }
} finally {
  await browser.close();
}
