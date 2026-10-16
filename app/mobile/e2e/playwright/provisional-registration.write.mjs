// Story: TASK-7.3
// An installation without an account registers and keeps or moves its content. The spec plays the
// app with Node's WebCrypto: it registers a device key, signs in with a device token, creates a tank
// with care and a vacation, and later presents the Keycloak token of an account together with a
// signed challenge of the device.
// - A fresh Keycloak account (created through the admin API) upgrades the provisional profile: the
//   tank and its care belong to the profile of the account, the device token and key stop working,
//   and a request with only the Keycloak token, only the device proof or only the device token is
//   refused.
// - A second installation moves its content into the existing profile of the write stack test member
//   (dev-user): the preview counts what moves, the tank belongs to the member afterwards, the
//   streaks of the member list it, and the provisional profile and its key are gone.
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database.
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { API_URL, KEYCLOAK_URL, passwordGrantToken } from "./support.mjs";

const { subtle } = webcrypto;
const base64Url = (bytes) => Buffer.from(bytes).toString("base64url");
const date = new Date().toISOString().slice(0, 10);

async function call(method, route, token, body) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const response = await fetch(`${API_URL}${route}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: response.status, body: await response.json().catch(() => null) };
}

// The app side of the device authentication: a key pair, its registration and signed challenges.
async function newInstallation() {
  const pair = await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const publicKey = base64Url(await subtle.exportKey("spki", pair.publicKey));
  const registration = await call("POST", "/api/auth/device/credentials", null, { publicKey });
  assert.equal(registration.status, 201, JSON.stringify(registration.body));
  const { credentialId, profileId } = registration.body;

  async function proof() {
    const challenge = await call("POST", "/api/auth/device/challenges", null);
    const timestamp = Math.floor(Date.now() / 1000);
    const message = new TextEncoder().encode(`cichlids-device-auth-v1\n${challenge.body.challenge}\n${credentialId}\n${timestamp}`);
    const signature = await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, pair.privateKey, message);
    return { challenge: challenge.body.challenge, credentialId, timestamp, signature: base64Url(signature) };
  }

  const issued = await call("POST", "/api/auth/device/tokens", null, await proof());
  assert.equal(issued.status, 200, JSON.stringify(issued.body));
  return { publicKey, credentialId, profileId, proof, token: issued.body.accessToken };
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

async function createAccount(username, password) {
  await adminRequest("POST", "users", {
    username,
    enabled: true,
    email: `${username}@example.org`,
    emailVerified: true,
    firstName: "Fresh",
    lastName: "Member",
    credentials: [{ type: "password", value: password, temporary: false }],
  });
  const users = await (await adminRequest("GET", `users?username=${encodeURIComponent(username)}&exact=true`)).json();
  assert.equal(users.length, 1);
  return users[0].id;
}

// A tank with a water change and a closed vacation, all private, as an installation creates them.
async function addContent(installation, title) {
  const created = await call("POST", "/api/tanks", installation.token, { title });
  assert.equal(created.status, 201, JSON.stringify(created.body));
  const tank = created.body.id;
  assert.equal(created.body.visibility.profile, "private");
  const entry = await call("POST", `/api/tanks/${tank}/care`, installation.token, { kind: "water_change" });
  assert.equal(entry.status, 201, JSON.stringify(entry.body));
  const vacation = await call("POST", `/api/me/vacations?date=${date}`, installation.token, { startDate: "2026-01-01", endDate: "2026-01-03" });
  assert.equal(vacation.status, 201, JSON.stringify(vacation.body));
  return { tank, entry: entry.body.id };
}

const username = `fresh-${Date.now()}`;
const password = "fresh-password"; // gitleaks:allow
let accountId;
try {
  // Registering a new account keeps the tank and its care.
  const first = await newInstallation();
  const kept = await addContent(first, `Registered tank ${Date.now()}`);
  accountId = await createAccount(username, password);
  const account = await passwordGrantToken(username, password);

  const onlyKeycloak = await call("POST", "/api/me/provisional/upgrade", account, {});
  assert.equal(onlyKeycloak.status, 403, JSON.stringify(onlyKeycloak.body));
  assert.equal(onlyKeycloak.body.code, "device_auth_refused");
  const onlyProof = await call("POST", "/api/me/provisional/upgrade", null, await first.proof());
  assert.equal(onlyProof.status, 401);
  const onlyDeviceToken = await call("POST", "/api/me/provisional/upgrade", first.token, await first.proof());
  assert.equal(onlyDeviceToken.status, 403);
  assert.equal(onlyDeviceToken.body.code, "registration_required");
  assert.equal((await call("GET", `/api/tanks/${kept.tank}`, first.token)).status, 200, "the provisional profile is untouched by the refusals");

  const preview = await call("POST", `/api/me/provisional/preview?date=${date}`, account, await first.proof());
  assert.equal(preview.status, 200, JSON.stringify(preview.body));
  assert.equal(preview.body.mode, "upgrade");
  assert.equal(preview.body.content.tanks, 1);
  assert.equal(preview.body.content.careEntries, 1);
  assert.equal(preview.body.targetDisplayName, null);

  const upgraded = await call("POST", "/api/me/provisional/upgrade", account, await first.proof());
  assert.equal(upgraded.status, 200, JSON.stringify(upgraded.body));
  assert.equal(upgraded.body.provisional, false);
  assert.equal(upgraded.body.profile.id, first.profileId);
  assert.equal(upgraded.body.profile.username, username);

  const me = await call("GET", "/api/me", account);
  assert.equal(me.body.profile.id, first.profileId);
  const tank = await call("GET", `/api/tanks/${kept.tank}`, account);
  assert.equal(tank.status, 200, JSON.stringify(tank.body));
  assert.equal(tank.body.visibility.profile, "private");
  const care = await call("GET", `/api/tanks/${kept.tank}/care`, account);
  assert.equal(care.status, 200);
  assert.ok(care.body.items.some((item) => item.id === kept.entry), "the care entry belongs to the registered profile");
  assert.equal((await call("GET", `/api/tanks/${kept.tank}`, null)).status, 404, "a visitor does not see the private tank");

  assert.equal((await call("GET", "/api/me", first.token)).status, 401, "the device token stops working");
  const exchange = await call("POST", "/api/auth/device/tokens", null, await first.proof());
  assert.equal(exchange.status, 401);
  assert.equal(exchange.body.code, "device_auth_refused");
  const registeredAgain = await call("POST", "/api/auth/device/credentials", null, { publicKey: first.publicKey });
  assert.equal(registeredAgain.status, 409);
  assert.equal(registeredAgain.body.code, "device_key_retired");
  const again = await call("POST", "/api/me/provisional/upgrade", account, await first.proof());
  assert.equal(again.status, 403);
  assert.equal(again.body.code, "device_auth_refused");
  console.log("A new account keeps the tank and the care of the installation and retires its key");

  // Signing in to an existing account moves the content into its profile.
  const member = await passwordGrantToken();
  const memberMe = await call("GET", "/api/me", member);
  assert.equal(memberMe.status, 200);
  const memberId = memberMe.body.profile.id;
  const second = await newInstallation();
  const moving = await addContent(second, `Moved tank ${Date.now()}`);

  const onlyKeycloakMove = await call("POST", `/api/me/provisional/move?date=${date}`, member, {});
  assert.equal(onlyKeycloakMove.status, 403);
  assert.equal(onlyKeycloakMove.body.code, "device_auth_refused");
  assert.equal((await call("POST", `/api/me/provisional/move?date=${date}`, null, await second.proof())).status, 401);
  assert.equal((await call("GET", `/api/tanks/${moving.tank}`, second.token)).status, 200);

  const movePreview = await call("POST", `/api/me/provisional/preview?date=${date}`, member, await second.proof());
  assert.equal(movePreview.status, 200, JSON.stringify(movePreview.body));
  assert.equal(movePreview.body.mode, "move");
  assert.equal(movePreview.body.content.tanks, 1);
  assert.equal(movePreview.body.content.careEntries, 1);
  assert.equal(movePreview.body.targetDisplayName, memberMe.body.profile.displayName);

  const moved = await call("POST", `/api/me/provisional/move?date=${date}`, member, await second.proof());
  assert.equal(moved.status, 200, JSON.stringify(moved.body));
  assert.equal(moved.body.me.profile.id, memberId);
  assert.equal(moved.body.moved.tanks, 1);
  assert.equal(moved.body.moved.vacations + moved.body.vacationsDropped, 1);

  const movedTank = await call("GET", `/api/tanks/${moving.tank}`, member);
  assert.equal(movedTank.status, 200, JSON.stringify(movedTank.body));
  assert.equal(movedTank.body.author.id, memberId);
  const movedCare = await call("GET", `/api/tanks/${moving.tank}/care`, member);
  assert.ok(movedCare.body.items.some((item) => item.id === moving.entry));
  const streaks = await call("GET", `/api/me/streaks?date=${date}`, member);
  assert.equal(streaks.status, 200);
  assert.ok(streaks.body.tanks.some((item) => item.tankId === moving.tank), "the streaks of the member list the moved tank");

  assert.equal((await call("GET", "/api/me", second.token)).status, 401, "the device token of the moved installation stops working");
  const movedExchange = await call("POST", "/api/auth/device/tokens", null, await second.proof());
  assert.equal(movedExchange.status, 401);
  const retired = await call("POST", "/api/auth/device/credentials", null, { publicKey: second.publicKey });
  assert.equal(retired.status, 409);
  assert.equal(retired.body.code, "device_key_retired");
  const movedAgain = await call("POST", `/api/me/provisional/move?date=${date}`, member, await second.proof());
  assert.equal(movedAgain.status, 403);
  assert.equal(movedAgain.body.code, "device_auth_refused");
  console.log("An existing account takes the content along and the provisional profile is gone");
} finally {
  if (accountId) await adminRequest("DELETE", `users/${accountId}`);
}

console.log("provisional-registration.write.mjs passed");
