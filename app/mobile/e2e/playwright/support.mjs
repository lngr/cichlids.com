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

/** The locator of an element by its React Native testID (data-testid on the web). */
export function byTestId(page, testId) {
  return page.locator(`[data-testid="${testId}"]`);
}

/**
 * Presses the given login button and submits the dev user's credentials on the Keycloak page. On
 * the web the app asks the API to start the login and navigates the page to Keycloak; after the
 * credentials, Keycloak returns through the API's callback to the page the login started from.
 * Resolves once the credentials are submitted; the caller waits for its own logged-in state.
 */
export async function submitKeycloakLogin(page, loginButton, username = USERNAME, password = PASSWORD) {
  await loginButton.waitFor({ timeout: 20000 });
  await loginButton.click();
  await page.waitForURL((url) => url.href.startsWith(AUTHORIZATION_ENDPOINT), { timeout: 20000 });
  await page.waitForSelector("#username", { timeout: 20000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await page.click("#kc-login");
  await page.waitForURL((url) => url.href.startsWith(WEB_URL), { timeout: 30000 });
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
