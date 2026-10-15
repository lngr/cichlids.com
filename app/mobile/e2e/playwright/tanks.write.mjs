// Story: TASK-4.4
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import http from "node:http";
import { API_URL, passwordGrantToken } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
function query(sql) {
  return execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres",
    "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-At", "-v", "ON_ERROR_STOP=1", "-c", sql], { encoding: "utf8" }).trim();
}

const deliveries = [];
const listener = http.createServer((request, response) => {
  let body = "";
  request.on("data", (chunk) => { body += chunk; });
  request.on("end", () => {
    deliveries.push(JSON.parse(body));
    response.writeHead(200).end();
  });
});
await new Promise((resolve) => listener.listen(0, "127.0.0.1", resolve));
try {
  const admin = await passwordGrantToken("dev-mod");
  const token = await passwordGrantToken();
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const registration = await fetch(`${API_URL}/api/admin/webhooks`, {
    method: "POST",
    headers: { Authorization: `Bearer ${admin}`, "Content-Type": "application/json" },
    body: JSON.stringify({ url: `http://127.0.0.1:${listener.address().port}/tank`, secret: "tank-e2e", eventTypes: ["tank.created"] }),
  });
  assert.equal(registration.status, 201);
  assert.deepEqual((await registration.json()).eventTypes, ["tank.created"]);

  const before = query("SELECT (SELECT count(*) FROM tank) || ':' || (SELECT count(*) FROM outbox_event)");
  for (const [body, code] of [
    [{ title: " " }, "tank_title_invalid"],
    [{ title: "Invalid", dimensions: { width: -1, unit: "cm" } }, "tank_dimensions_invalid"],
    [{ title: "Invalid", volume: { value: 0, unit: "liter" } }, "tank_volume_invalid"],
  ]) {
    const refused = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify(body) });
    assert.equal(refused.status, 400);
    assert.match(refused.headers.get("content-type"), /application\/problem\+json/);
    assert.equal((await refused.json()).code, code);
  }
  assert.equal(query("SELECT (SELECT count(*) FROM tank) || ':' || (SELECT count(*) FROM outbox_event)"), before);

  const response = await fetch(`${API_URL}/api/tanks`, {
    method: "POST", headers, body: JSON.stringify({ title: "Tank event delivery" }),
  });
  assert.equal(response.status, 201);
  const tank = await response.json();
  const deadline = Date.now() + 10000;
  while (!deliveries.some((e) => e.payload?.tankId === tank.id) && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  const event = deliveries.find((e) => e.payload?.tankId === tank.id);
  assert.ok(event, "tank.created reaches its subscriber");
  assert.equal(event.eventType, "tank.created");
  assert.equal(event.aggregateType, "tank");
  assert.equal(event.aggregateId, String(tank.id));
  assert.equal(event.payload.profileId, tank.author.id);
  console.log("Tank creation: coded refusals write nothing; tank.created reaches the registered subscriber.");
} finally {
  await new Promise((resolve, reject) => listener.close((error) => error ? reject(error) : resolve()));
}
