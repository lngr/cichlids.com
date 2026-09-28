// Logs in through the real Keycloak login page as the local dev user and checks that the Me tab
// shows the profile from GET /api/me. Logs out and logs in a second time on the same page: Keycloak
// asks for credentials (logout ended its session), and the second authorization request has a
// fresh state and PKCE challenge. A page reload keeps the session, and after the final logout a
// reload keeps the anonymous state. Runs on the isolated write stack only (see
// e2e/write-stack.sh): the first /api/me call of a user creates their profile row.
import { chromium } from "playwright";
import { AUTHORIZATION_ENDPOINT, WEB_URL, byTestId, logInThroughKeycloak } from "./support.mjs";

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

  const loginButton = byTestId(page, "login-button");
  const meName = byTestId(page, "me-name");

  async function logOut() {
    await byTestId(page, "logout-button").click();
    await loginButton.waitFor({ timeout: 20000 });
    if ((await meName.count()) !== 0) throw new Error("The profile name is visible after logging out");
  }

  await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "tab-me").click({ timeout: 20000 });
  await page.waitForURL(/\/me$/, { timeout: 10000 });

  console.log(`Logged in, Me tab shows "${await logInThroughKeycloak(page)}"`);

  await logOut();
  console.log("Logged out, Me tab shows the login button");

  // Same page, no reload in between: the second login has to build a new authorization request.
  // The popup shows the credentials form only when logout ended the Keycloak session.
  console.log(`Logged in a second time, Me tab shows "${await logInThroughKeycloak(page)}"`);

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
