// Story: TASK-7.2
// An installation without an account uses the app privately. The spec plays the app with Node's
// WebCrypto: it registers a device key and signs in with a device token. The provisional account
// creates a tank, logs a water change with a photo and uploads into its media library; all of it
// is private. A registered member and a visitor do not see the tank, its care or the profile.
// Every action others see (follow, comment, story, a wider tank level, profile images, onboarding,
// Web Push) answers registration_required, and the hourly upload limit of provisional accounts
// applies. No browser is involved. Runs on the isolated write stack only (see e2e/write-stack.sh),
// since it writes to the database and the bucket.
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { API_URL, passwordGrantToken } from "./support.mjs";

const { subtle } = webcrypto;
const PHOTO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");
const base64Url = (bytes) => Buffer.from(bytes).toString("base64url");

async function call(method, route, token, body) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  let payload;
  if (body instanceof FormData) payload = body;
  else if (body !== undefined) {
    headers["Content-Type"] = "application/json";
    payload = JSON.stringify(body);
  }
  const response = await fetch(`${API_URL}${route}`, { method, headers, body: payload });
  return { status: response.status, body: await response.json().catch(() => null) };
}

// The app side of the device authentication: a key pair, its registration and a signed challenge.
async function signInAsNewInstallation() {
  const pair = await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const publicKey = base64Url(await subtle.exportKey("spki", pair.publicKey));
  const registration = await call("POST", "/api/auth/device/credentials", null, { publicKey });
  assert.equal(registration.status, 201, JSON.stringify(registration.body));
  const challenge = await call("POST", "/api/auth/device/challenges", null);
  const timestamp = Math.floor(Date.now() / 1000);
  const message = new TextEncoder().encode(`cichlids-device-auth-v1\n${challenge.body.challenge}\n${registration.body.credentialId}\n${timestamp}`);
  const signature = await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, pair.privateKey, message);
  const issued = await call("POST", "/api/auth/device/tokens", null, {
    challenge: challenge.body.challenge, credentialId: registration.body.credentialId, timestamp, signature: base64Url(signature),
  });
  assert.equal(issued.status, 200, JSON.stringify(issued.body));
  return { token: issued.body.accessToken, profileId: registration.body.profileId };
}

function photoForm() {
  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(PHOTO)], { type: "image/jpeg" }), "photo.jpg");
  return form;
}

function assertRegistrationRequired(result, what) {
  assert.equal(result.status, 403, `${what}: ${JSON.stringify(result.body)}`);
  assert.equal(result.body.code, "registration_required", what);
}

const { token, profileId } = await signInAsNewInstallation();
const member = await passwordGrantToken();
const date = new Date().toISOString().slice(0, 10);

// A tank is private; the question about publishing is not asked and a public answer needs registration.
const title = `Provisional tank ${Date.now()}`;
const created = await call("POST", "/api/tanks", token, { title });
assert.equal(created.status, 201, JSON.stringify(created.body));
const tank = created.body.id;
assert.equal(created.body.visibility.profile, "private");
assert.equal(created.body.visibility.stream, "private");
assertRegistrationRequired(await call("POST", "/api/tanks", token, { title: "Public wish", isPublic: true }), "public tank");
assertRegistrationRequired(await call("PUT", `/api/tanks/${tank}/visibility`, token, { profile: "public", stream: "public" }), "visibility");
assert.equal((await call("PUT", `/api/tanks/${tank}/visibility`, token, { profile: "private", stream: "private" })).status, 200);

// Care: a water change with a photo, a measurement, a to-do, a plan and a vacation.
const entry = await call("POST", `/api/tanks/${tank}/care`, token, { kind: "water_change" });
assert.equal(entry.status, 201, JSON.stringify(entry.body));
const withPhoto = await call("POST", `/api/tanks/${tank}/care/${entry.body.id}/photos`, token, photoForm());
assert.equal(withPhoto.status, 201, JSON.stringify(withPhoto.body));
const thumb = withPhoto.body.photos[0].urls.thumb;
assert.match(thumb, /^\/api\/media\/photos\//);
assert.equal((await fetch(`${API_URL}${thumb}`)).status, 200);
const measurement = await call("POST", `/api/tanks/${tank}/care`, token, { kind: "measurement", values: [{ parameter: "ph", value: 7.2, origin: "test_kit" }] });
assert.equal(measurement.status, 201, JSON.stringify(measurement.body));
assert.equal((await call("POST", `/api/tanks/${tank}/todos`, token, { text: "Order filter wool" })).status, 201);
assert.equal((await call("PATCH", `/api/tanks/${tank}/care/plan/water_change?date=${date}`, token, { rhythmDays: 14 })).status, 200);
assert.equal((await call("GET", `/api/me/streaks?date=${date}`, token)).status, 200);
assert.equal((await call("POST", `/api/me/vacations?date=${date}`, token, {})).status, 201);
assert.equal((await call("POST", `/api/me/vacations/active/end?date=${date}`, token)).status, 200);
const library = await call("GET", "/api/me/media", token);
assert.equal(library.status, 200);
assert.equal(library.body.items.length, 1);

// Others do not see any of it.
for (const route of [`/api/tanks/${tank}`, `/api/tanks/${tank}/care`, `/api/tanks/${tank}/time-lapse`, `/api/tanks/${tank}/streaks?date=${date}`, `/api/profiles/${profileId}`, `/api/profiles/${profileId}/tanks`]) {
  assert.equal((await call("GET", route, member)).status, 404, `member ${route}`);
  assert.equal((await call("GET", route, null)).status, 404, `visitor ${route}`);
}
for (const route of ["/api/tanks?limit=100", "/api/discover", "/api/stories"]) {
  const listed = await fetch(`${API_URL}${route}`, { headers: { Authorization: `Bearer ${member}` } });
  assert.equal(listed.status, 200);
  assert.ok(!(await listed.text()).includes(title), `${route} lists the provisional tank`);
}
assert.equal((await call("POST", `/api/tanks/${tank}/care`, member, { kind: "water_change" })).status, 404);

// Everything others see needs registration.
const publicTank = await call("POST", "/api/tanks", member, { title: `Member tank ${Date.now()}`, isPublic: true });
assert.equal(publicTank.status, 201, JSON.stringify(publicTank.body));
const other = publicTank.body.id;
assertRegistrationRequired(await call("PUT", `/api/tanks/${other}/follow`, token), "follow");
assertRegistrationRequired(await call("POST", `/api/tanks/${other}/comments`, token, { body: "Nice", stars: 5 }), "comment");
assertRegistrationRequired(await call("GET", `/api/tanks/${tank}/selected-members`, token), "selected members");
assertRegistrationRequired(await call("GET", `/api/tanks/${tank}/follow-requests`, token), "follow requests");
assertRegistrationRequired(await call("POST", "/api/uploads", token, photoForm()), "story upload");
assertRegistrationRequired(await call("POST", "/api/me/stories", token, {}), "story");
assertRegistrationRequired(await call("POST", `/api/me/media/${library.body.items[0].id}/share`, token, {}), "quick share");
assertRegistrationRequired(await call("PUT", "/api/me/avatar", token, { mediaItemId: library.body.items[0].id }), "avatar");
assertRegistrationRequired(await call("POST", "/api/me/header/photo", token, photoForm()), "header");
assertRegistrationRequired(await call("POST", "/api/me/onboarding", token, { skip: true }), "onboarding");
assertRegistrationRequired(await call("POST", "/api/me/web-push/subscriptions", token, {}), "web push");
assert.equal((await call("GET", `/api/tanks/${other}`, token)).status, 200);

// The hourly upload limit of provisional accounts is lower than the one of members.
let accepted = 1;
let limited;
for (; accepted < 30; accepted++) {
  const upload = await call("POST", "/api/me/media/photos", token, photoForm());
  if (upload.status === 429) {
    limited = upload;
    break;
  }
  assert.equal(upload.status, 201, JSON.stringify(upload.body));
}
assert.ok(limited, "the upload limit of provisional accounts never applied");
assert.equal(limited.body.code, "upload_rate_limited");
assert.equal(accepted, 10, "provisional accounts upload ten files per hour");
assert.equal((await call("POST", "/api/me/media/photos", member, photoForm())).status, 201);

console.log("provisional-rights.write.mjs passed");
