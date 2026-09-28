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
 * Presses the Me tab's login button and submits the dev user's credentials on the Keycloak page.
 * On the web, expo-auth-session opens that page in a popup window and receives the redirect back
 * from it. Resolves with the name the Me tab shows once the login completed.
 */
export async function logInThroughKeycloak(page) {
  const loginButton = byTestId(page, "login-button");
  await loginButton.waitFor({ timeout: 20000 });
  const popupPromise = page.waitForEvent("popup", { timeout: 20000 });
  await loginButton.click();
  const popup = await popupPromise;
  await popup.waitForSelector("#username", { timeout: 20000 });
  await popup.fill("#username", USERNAME);
  await popup.fill("#password", PASSWORD);
  await popup.click("#kc-login");

  const meName = byTestId(page, "me-name");
  await meName.waitFor({ timeout: 30000 });
  const loggedInName = (await meName.textContent())?.trim();
  if (!EXPECTED_NAMES.includes(loggedInName ?? "")) {
    throw new Error(`Expected the Me tab to show ${EXPECTED_NAMES.join(" or ")}, got "${loggedInName}"`);
  }
  return loggedInName;
}
