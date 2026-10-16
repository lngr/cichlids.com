// Story: TASK-7.1
// An installation without an account gets a device token. The spec plays the app with Node's
// WebCrypto: it creates an ECDSA P-256 key pair, registers the public key, asks for a challenge,
// signs challenge, credential id and timestamp, exchanges the signature for a token and calls
// /api/me with it. It then checks that a challenge is accepted once, that a signature of another
// key is refused, that the token reaches no endpoint for registered members and that the
// provisional profile is not public. No browser and no Keycloak session is involved. Runs on the
// isolated write stack only (see e2e/write-stack.sh), since it writes to the database.
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { API_URL } from "./support.mjs";

const { subtle } = webcrypto;

const base64Url = (bytes) => Buffer.from(bytes).toString("base64url");

async function newKeyPair() {
  const pair = await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const spki = await subtle.exportKey("spki", pair.publicKey);
  return { privateKey: pair.privateKey, publicKey: base64Url(spki) };
}

async function post(route, body) {
  const response = await fetch(`${API_URL}${route}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  });
  return { status: response.status, body: await response.json().catch(() => null) };
}

// The bytes the app signs: label, challenge, credential id and timestamp, one per line.
function message(challenge, credentialId, timestamp) {
  return new TextEncoder().encode(`cichlids-device-auth-v1\n${challenge}\n${credentialId}\n${timestamp}`);
}

async function exchange(privateKey, credentialId, challenge, timestamp = Math.floor(Date.now() / 1000)) {
  const signature = await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, privateKey, message(challenge, credentialId, timestamp));
  return post("/api/auth/device/tokens", { challenge, credentialId, timestamp, signature: base64Url(signature) });
}

async function get(route, token) {
  const response = await fetch(`${API_URL}${route}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
  return { status: response.status, body: await response.json().catch(() => null) };
}

const key = await newKeyPair();
const registration = await post("/api/auth/device/credentials", { publicKey: key.publicKey });
assert.equal(registration.status, 201, JSON.stringify(registration.body));
const { credentialId, profileId, displayName } = registration.body;
assert.match(displayName, /^user[1-9][0-9]{7}$/);

const challenge = await post("/api/auth/device/challenges");
assert.equal(challenge.status, 200);
const issued = await exchange(key.privateKey, credentialId, challenge.body.challenge);
assert.equal(issued.status, 200, JSON.stringify(issued.body));
assert.equal(issued.body.tokenType, "Bearer");
const token = issued.body.accessToken;
const lifetime = new Date(issued.body.expiresAt).getTime() - Date.now();
assert.ok(lifetime > 14 * 60 * 1000 && lifetime <= 15 * 60 * 1000 + 1000, `expires in ${lifetime} ms`);

const me = await get("/api/me", token);
assert.equal(me.status, 200, JSON.stringify(me.body));
assert.equal(me.body.provisional, true);
assert.equal(me.body.profile.id, profileId);
assert.equal(me.body.profile.displayName, displayName);

const replay = await exchange(key.privateKey, credentialId, challenge.body.challenge);
assert.equal(replay.status, 401);
assert.equal(replay.body.code, "device_auth_refused");

const attacker = await newKeyPair();
const fresh = await post("/api/auth/device/challenges");
const forged = await exchange(attacker.privateKey, credentialId, fresh.body.challenge);
assert.equal(forged.status, 401);
assert.equal(forged.body.code, "device_auth_refused");

assert.equal((await get("/api/me", `${token}x`)).status, 401);
const memberOnly = await get("/api/me/drafts", token);
assert.equal(memberOnly.status, 403);
assert.equal(memberOnly.body.code, "registration_required");
assert.equal((await get(`/api/profiles/${profileId}`)).status, 404);

const again = await post("/api/auth/device/credentials", { publicKey: key.publicKey });
assert.equal(again.status, 200);
assert.equal(again.body.credentialId, credentialId);

const renewal = await post("/api/auth/device/challenges");
const renewed = await exchange(key.privateKey, credentialId, renewal.body.challenge);
assert.equal(renewed.status, 200);
assert.equal((await get("/api/me", renewed.body.accessToken)).status, 200);

console.log("device-token.write.mjs passed");
