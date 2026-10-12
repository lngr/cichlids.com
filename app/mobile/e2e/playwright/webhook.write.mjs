// Story: TASK-3.34
// Registers a webhook subscriber as the local admin and follows two published pictures through the
// outbox dispatcher of the write-stack API. Registrations for an ftp URL and for a host outside the
// allowlist are rejected. The subscriber is a local HTTP listener on 127.0.0.1: it fails the first
// two deliveries and then receives the post.published event with a valid signature. It then fails
// every delivery until the next event is given up, and after the administrator triggers the
// redelivery it receives that event with the same id. The write stack runs the dispatcher with a
// one-second poll interval and a fifteen-second give-up period. No browser is involved; the specs
// call Keycloak and the API directly. Runs on the isolated write stack only (see
// e2e/write-stack.sh), since it writes to the database.
import crypto from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { API_URL, KEYCLOAK_URL, PASSWORD, USERNAME } from "./support.mjs";

const FIXTURE = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");
const ADMIN_USERNAME = "dev-mod";
const SECRET = `webhook-e2e-${crypto.randomUUID()}`;

async function passwordGrantToken(username) {
  const response = await fetch(`${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "cichlids-app", grant_type: "password", username, password: PASSWORD }),
  });
  if (!response.ok) throw new Error(`Password grant for ${username} returned ${response.status}: ${await response.text()}`);
  return (await response.json()).access_token;
}

async function publishPicture(token, title) {
  const headers = { Authorization: `Bearer ${token}` };
  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(FIXTURE)], { type: "image/jpeg" }), "webhook-e2e.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers, body: form });
  if (!upload.ok) throw new Error(`POST /api/uploads returned ${upload.status}: ${await upload.text()}`);
  const draft = await upload.json();

  const publish = await fetch(`${API_URL}/api/posts/${draft.id}/publish`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (!publish.ok) throw new Error(`POST /api/posts/${draft.id}/publish returned ${publish.status}: ${await publish.text()}`);
  return draft.id;
}

function registerWebhook(adminToken, url) {
  return fetch(`${API_URL}/api/admin/webhooks`, {
    method: "POST",
    headers: { Authorization: `Bearer ${adminToken}`, "Content-Type": "application/json" },
    body: JSON.stringify({ url, secret: SECRET, eventTypes: ["post.published"] }),
  });
}

async function waitFor(description, timeoutMs, check) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const result = await check();
    if (result) return result;
    if (Date.now() > deadline) throw new Error(`Timed out after ${timeoutMs} ms waiting for ${description}`);
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
}

// The subscriber: answers with the status the current mode decides and records every delivery.
// The short outage fails the first two deliveries of every event, so an event published by an
// earlier spec and still pending cannot use up the failures meant for this spec's event.
const deliveries = [];
let mode = "ok";
const listener = http.createServer((request, response) => {
  const chunks = [];
  request.on("data", (chunk) => chunks.push(chunk));
  request.on("end", () => {
    const body = Buffer.concat(chunks).toString("utf8");
    const envelope = JSON.parse(body);
    const earlier = deliveries.filter((d) => d.envelope.id === envelope.id).length;
    const status = mode === "down" || (mode === "short-outage" && earlier < 2) ? 500 : 200;
    deliveries.push({ body, envelope, signature: request.headers["x-cichlids-signature"], status });
    response.writeHead(status).end();
  });
});
await new Promise((resolve) => listener.listen(0, "127.0.0.1", resolve));
const listenerUrl = `http://127.0.0.1:${listener.address().port}/hook`;

function deliveriesFor(postId) {
  return deliveries.filter((d) => d.envelope.eventType === "post.published" && d.envelope.payload?.postId === postId);
}

try {
  const adminToken = await passwordGrantToken(ADMIN_USERNAME);
  const userToken = await passwordGrantToken(USERNAME);

  for (const url of ["ftp://127.0.0.1/x", "https://not-listed.test"]) {
    const rejected = await registerWebhook(adminToken, url);
    if (rejected.status !== 400) throw new Error(`Registering ${url} returned ${rejected.status}, expected 400`);
  }
  console.log("Registrations for an ftp URL and for a host outside the allowlist are rejected");

  const registered = await registerWebhook(adminToken, listenerUrl);
  if (registered.status !== 201) throw new Error(`Registering the listener returned ${registered.status}: ${await registered.text()}`);

  // A short outage: the first two deliveries fail, the retry after the backoff succeeds.
  mode = "short-outage";
  const firstPostId = await publishPicture(userToken, `Webhook target ${Date.now()}`);
  const accepted = await waitFor("the accepted delivery of the first picture", 60000, () =>
    deliveriesFor(firstPostId).find((d) => d.status === 200),
  );
  const failedBefore = deliveriesFor(firstPostId).filter((d) => d.status === 500).length;
  if (failedBefore !== 2) throw new Error(`Expected two failed deliveries before the accepted one, got ${failedBefore}`);
  const expectedSignature = crypto.createHmac("sha256", SECRET).update(accepted.body).digest("hex");
  if (accepted.signature !== expectedSignature) throw new Error("The accepted delivery has an invalid signature");
  console.log(`After two failed deliveries the subscriber received post.published ${accepted.envelope.id} with a valid signature`);

  // A subscriber that keeps failing: the event is given up and listed for the administrator.
  mode = "down";
  const secondPostId = await publishPicture(userToken, `Webhook give-up ${Date.now()}`);
  const givenUp = await waitFor("the given-up event of the second picture", 90000, async () => {
    const response = await fetch(`${API_URL}/api/admin/outbox/events?state=given-up&limit=50`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });
    if (!response.ok) throw new Error(`GET /api/admin/outbox/events returned ${response.status}`);
    const page = await response.json();
    const failedIds = new Set(deliveriesFor(secondPostId).map((d) => d.envelope.id));
    return page.items.find((item) => failedIds.has(item.id));
  });
  console.log(`Event ${givenUp.id} is given up after ${givenUp.attemptCount} attempts: ${givenUp.lastError}`);

  // The administrator triggers the redelivery once the subscriber is healthy.
  mode = "ok";
  const redeliver = await fetch(`${API_URL}/api/admin/outbox/events/${givenUp.id}/redeliver`, {
    method: "POST",
    headers: { Authorization: `Bearer ${adminToken}` },
  });
  if (redeliver.status !== 202) throw new Error(`Redelivery returned ${redeliver.status}: ${await redeliver.text()}`);
  const redelivered = await waitFor("the redelivered event", 30000, () =>
    deliveries.find((d) => d.envelope.id === givenUp.id && d.status === 200),
  );
  const redeliveredSignature = crypto.createHmac("sha256", SECRET).update(redelivered.body).digest("hex");
  if (redelivered.signature !== redeliveredSignature) throw new Error("The redelivered event has an invalid signature");
  console.log(`After the redelivery the subscriber received event ${redelivered.envelope.id} again`);
} finally {
  listener.close();
}
