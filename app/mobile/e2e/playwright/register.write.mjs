// Story: TASK-3.39
// A new account that picks one of the reserved system usernames (community-archive,
// former-member-<id>, forum-member-<id>) is refused on the Keycloak registration form: the form
// stays open with the message "This username is reserved." and no account is created. Runs on the
// isolated write stack, which uses the local Keycloak with self-registration open.
import crypto from "node:crypto";
import { chromium } from "playwright";
import { KEYCLOAK_URL, WEB_URL } from "./support.mjs";

const RESERVED_USERNAMES = ["community-archive", "former-member-123", "forum-member-45"];
const REFUSAL = "This username is reserved.";

function authorizationUrl() {
  const verifier = crypto.randomBytes(32).toString("base64url");
  const challenge = crypto.createHash("sha256").update(verifier).digest("base64url");
  const params = new URLSearchParams({
    client_id: "cichlids-app",
    redirect_uri: `${WEB_URL}/`,
    response_type: "code",
    scope: "openid",
    state: crypto.randomBytes(8).toString("hex"),
    code_challenge: challenge,
    code_challenge_method: "S256",
  });
  return `${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/auth?${params}`;
}

async function adminToken() {
  const response = await fetch(`${KEYCLOAK_URL}/realms/master/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "admin-cli", grant_type: "password", username: "admin", password: "admin" }), // gitleaks:allow
  });
  if (!response.ok) throw new Error(`Admin token request returned ${response.status}`);
  return (await response.json()).access_token;
}

async function userExists(token, username) {
  const response = await fetch(
    `${KEYCLOAK_URL}/admin/realms/cichlids/users?username=${encodeURIComponent(username)}&exact=true`,
    { headers: { Authorization: `Bearer ${token}` } },
  );
  if (!response.ok) throw new Error(`User lookup returned ${response.status}`);
  return (await response.json()).length > 0;
}

const browser = await chromium.launch();
try {
  const token = await adminToken();
  for (const username of RESERVED_USERNAMES) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
    try {
      const page = await context.newPage();
      await page.goto(authorizationUrl(), { waitUntil: "domcontentloaded", timeout: 30000 });
      await page.locator("#kc-registration a").click({ timeout: 20000 });
      await page.waitForSelector("#username", { timeout: 20000 });

      await page.fill("#email", `${username}@example.org`);
      await page.fill("#firstName", "Reserved");
      await page.fill("#lastName", "Name");
      await page.fill("#username", username);
      await page.fill("#password", "Reserved-Name-Password-1"); // gitleaks:allow
      await page.fill("#password-confirm", "Reserved-Name-Password-1"); // gitleaks:allow
      await page.locator('input[type="submit"], button[type="submit"]').first().click();

      await page.waitForFunction(
        (expected) => document.body.innerText.includes(expected),
        REFUSAL,
        { timeout: 20000 },
      );
      if (!page.url().startsWith(KEYCLOAK_URL)) {
        throw new Error(`Registration of ${username} left the Keycloak page: ${page.url()}`);
      }
      if (await userExists(token, username)) {
        throw new Error(`An account named ${username} exists after the refused registration`);
      }
      console.log(`Registration with the reserved username ${username} is refused`);
    } finally {
      await context.close();
    }
  }
} finally {
  await browser.close();
}
