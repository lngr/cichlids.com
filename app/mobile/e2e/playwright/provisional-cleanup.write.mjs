// Story: TASK-7.4
// A provisional account without a signed request for twelve months is removed with its content, an
// account used yesterday and a registered member stay. The spec registers two installations, sets
// their last signed request through the database (twelve months and a day ago, yesterday), gives the
// old account a tank, and waits for the cleanup loop of the write stack (it runs every few seconds
// there, see e2e/write-stack.sh). The removal of stored media objects is covered by the API
// integration tests, which run against a real object store. Runs on the isolated write stack only.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { webcrypto } from "node:crypto";
import { API_URL, passwordGrantToken } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const { subtle } = webcrypto;
const base64Url = (bytes) => Buffer.from(bytes).toString("base64url");

function sql(statement) {
  return execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();
}

async function post(route, body, headers = {}) {
  const response = await fetch(`${API_URL}${route}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body ?? {}),
  });
  return { status: response.status, body: await response.json().catch(() => null) };
}

async function registerDevice() {
  const pair = await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const publicKey = base64Url(await subtle.exportKey("spki", pair.publicKey));
  const registration = await post("/api/auth/device/credentials", { publicKey });
  assert.equal(registration.status, 201, JSON.stringify(registration.body));
  return { privateKey: pair.privateKey, ...registration.body };
}

async function signIn(device) {
  const challenge = await post("/api/auth/device/challenges");
  const timestamp = Math.floor(Date.now() / 1000);
  const message = new TextEncoder().encode(`cichlids-device-auth-v1\n${challenge.body.challenge}\n${device.credentialId}\n${timestamp}`);
  const signature = await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, device.privateKey, message);
  return post("/api/auth/device/tokens", { challenge: challenge.body.challenge, credentialId: device.credentialId, timestamp, signature: base64Url(signature) });
}

const profileExists = (id) => Number(sql(`SELECT count(*) FROM profile WHERE id = ${id}`)) === 1;

const abandoned = await registerDevice();
const inUse = await registerDevice();
// The tanks come first: the cleanup loop removes an account as soon as its last signed request is old enough.
sql(`INSERT INTO tank (profile_id, title, state, created_at) VALUES (${abandoned.profileId}, 'Abandoned tank', 'draft', now() - interval '13 months')`);
sql(`INSERT INTO tank (profile_id, title, state, created_at) VALUES (${inUse.profileId}, 'Tank in use', 'draft', now() - interval '2 days')`);
sql(`UPDATE device_credential SET last_signed_request_at = now() - interval '12 months 1 day' WHERE id = '${abandoned.credentialId}'`);
sql(`UPDATE device_credential SET last_signed_request_at = now() - interval '1 day' WHERE id = '${inUse.credentialId}'`);

const memberToken = await passwordGrantToken();
const created = await fetch(`${API_URL}/api/tanks`, {
  method: "POST",
  headers: { Authorization: `Bearer ${memberToken}`, "Content-Type": "application/json" },
  body: JSON.stringify({ title: "Member tank of the cleanup path", isPublic: true }),
});
assert.equal(created.status, 201);
const memberTank = await created.json();
const memberProfileId = sql(`SELECT profile_id FROM tank WHERE id = ${memberTank.id}`);

const deadline = Date.now() + 60_000;
while (profileExists(abandoned.profileId) && Date.now() < deadline) {
  await new Promise((resolve) => setTimeout(resolve, 1000));
}

assert.equal(profileExists(abandoned.profileId), false, "the abandoned provisional profile is removed");
assert.equal(sql(`SELECT count(*) FROM tank WHERE profile_id = ${abandoned.profileId}`), "0");
assert.equal(sql(`SELECT count(*) FROM device_credential WHERE id = '${abandoned.credentialId}'`), "0");
assert.equal((await signIn(abandoned)).status, 401, "the removed installation gets no token");

assert.equal(profileExists(inUse.profileId), true, "the provisional profile used yesterday stays");
assert.equal(sql(`SELECT count(*) FROM tank WHERE profile_id = ${inUse.profileId}`), "1");
assert.equal((await signIn(inUse)).status, 200, "the installation in use still signs in");

assert.equal(profileExists(memberProfileId), true, "the registered member stays");
assert.equal(sql(`SELECT count(*) FROM tank WHERE id = ${memberTank.id}`), "1");
