// Tests for the credential proxy against a local fake upstream.
import { test } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { chmodSync, existsSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import net from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  allowedTarget, checkApiKeyFile, checkLoginFile, loadCredential, startCredentialProxy,
} from "./credential-proxy.mjs";

// A fake upstream that records every request and answers with handler(req, res, body).
async function fakeUpstream(handler = (req, res) => res.end("{}")) {
  const requests = [];
  const server = http.createServer((req, res) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const body = Buffer.concat(chunks).toString("utf8");
      requests.push({ method: req.method, url: req.url, headers: req.headers, body });
      handler(req, res, body);
    });
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  const url = `http://127.0.0.1:${server.address().port}`;
  return { requests, url, close: () => new Promise((done) => { server.close(done); server.closeAllConnections(); }) };
}

// A socket path in a fresh directory.
const socketPath = () => join(mkdtempSync(join(tmpdir(), "credential-proxy-sock-")), "api.sock");

function files() {
  const dir = mkdtempSync(join(tmpdir(), "credential-proxy-"));
  return { dir, apiKeyFile: join(dir, "anthropic.key"), credentialsFile: join(dir, "credentials.json") };
}

const login = (expiresAt, accessToken = "dummy-access") => JSON.stringify({ claudeAiOauth: { accessToken, refreshToken: "dummy-refresh", expiresAt } });

// Sends a request to the proxy and resolves { status, headers, body }.
function send(proxy, { method = "POST", path = "/v1/messages", headers = {}, body = "{}" } = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({ socketPath: proxy.socketPath, method, path, headers }, (res) => {
      const chunks = [];
      res.on("data", (c) => chunks.push(c));
      res.on("end", () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks).toString("utf8") }));
    });
    req.on("error", reject);
    req.end(body);
  });
}

async function withProxy(options, fn, upstreamHandler) {
  const upstream = await fakeUpstream(upstreamHandler);
  const proxy = await startCredentialProxy({ ...options, socketPath: socketPath(), upstream: upstream.url });
  try {
    return await fn({ proxy, upstream });
  } finally {
    await proxy.close();
    await upstream.close();
  }
}

test("api-key: x-api-key comes from the key file, authorization is dropped, the rest is forwarded", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-real-key\n");
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    const res = await send(proxy, {
      path: "/v1/messages?beta=true", body: '{"model":"m"}',
      headers: { "x-api-key": "sk-ant-api03-placeholder-proxy", authorization: "Bearer placeholder", "anthropic-version": "2023-06-01", "content-type": "application/json" },
    });
    assert.equal(res.status, 200);
    const [seen] = upstream.requests;
    assert.equal(seen.method, "POST");
    assert.equal(seen.url, "/v1/messages?beta=true");
    assert.equal(seen.body, '{"model":"m"}');
    assert.equal(seen.headers["x-api-key"], "dummy-real-key");
    assert.equal(seen.headers.authorization, undefined);
    assert.equal(seen.headers["anthropic-version"], "2023-06-01");
    assert.equal(seen.headers["content-type"], "application/json");
    assert.equal(seen.headers.host, new URL(upstream.url).host);
  });
});

test("subscription: authorization carries the access token, x-api-key is dropped", async () => {
  const f = files();
  writeFileSync(f.credentialsFile, login(Date.now() + 3_600_000, "dummy-access-1"));
  await withProxy({ billing: "subscription", credentialsFile: f.credentialsFile }, async ({ proxy, upstream }) => {
    await send(proxy, { headers: { "x-api-key": "placeholder", authorization: "Bearer placeholder-oauth", "anthropic-beta": "oauth-2025-04-20" } });
    const [seen] = upstream.requests;
    assert.equal(seen.headers.authorization, "Bearer dummy-access-1");
    assert.equal(seen.headers["x-api-key"], undefined);
    assert.equal(seen.headers["anthropic-beta"], "oauth-2025-04-20");
  });
});

test("the credential is read fresh for each request", async () => {
  const f = files();
  writeFileSync(f.credentialsFile, login(Date.now() + 3_600_000, "token-one"));
  await withProxy({ billing: "subscription", credentialsFile: f.credentialsFile }, async ({ proxy, upstream }) => {
    await send(proxy);
    writeFileSync(f.credentialsFile, login(Date.now() + 3_600_000, "token-two"));
    await send(proxy);
    assert.deepEqual(upstream.requests.map((r) => r.headers.authorization), ["Bearer token-one", "Bearer token-two"]);
  });
  writeFileSync(f.apiKeyFile, "key-one");
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    await send(proxy);
    writeFileSync(f.apiKeyFile, " key-two \n");
    await send(proxy);
    assert.deepEqual(upstream.requests.map((r) => r.headers["x-api-key"]), ["key-one", "key-two"]);
  });
});

test("an expired access token answers 401 in the API format and reaches no upstream", async () => {
  const f = files();
  writeFileSync(f.credentialsFile, login(Date.now() - 1000));
  await withProxy({ billing: "subscription", credentialsFile: f.credentialsFile }, async ({ proxy, upstream }) => {
    const res = await send(proxy);
    assert.equal(res.status, 401);
    assert.equal(res.headers["content-type"], "application/json");
    assert.equal(res.body, '{"type":"error","error":{"type":"authentication_error","message":"subscription login expired"}}');
    assert.equal(upstream.requests.length, 0);
  });
});

test("the expiry follows the injected clock", async () => {
  const f = files();
  writeFileSync(f.credentialsFile, login(5000));
  const upstream = await fakeUpstream();
  let time = 4000;
  const proxy = await startCredentialProxy({
    billing: "subscription", credentialsFile: f.credentialsFile, socketPath: socketPath(), upstream: upstream.url, now: () => time,
  });
  try {
    assert.equal((await send(proxy)).status, 200);
    time = 5000;
    assert.equal((await send(proxy)).status, 401);
  } finally {
    await proxy.close();
    await upstream.close();
  }
});

test("a missing, unreadable or malformed credential answers 401 with a fitting message", async () => {
  const f = files();
  const cases = [
    ["subscription", "credentialsFile", null, "subscription login missing or unreadable"],
    ["subscription", "credentialsFile", "{not json", "subscription login missing or unreadable"],
    ["subscription", "credentialsFile", "{}", "subscription login missing or unreadable"],
    ["subscription", "credentialsFile", JSON.stringify({ claudeAiOauth: { accessToken: "", expiresAt: Date.now() + 9e6 } }), "subscription login missing or unreadable"],
    ["subscription", "credentialsFile", JSON.stringify({ claudeAiOauth: { accessToken: "t" } }), "subscription login missing or unreadable"],
    ["api-key", "apiKeyFile", null, "API key file is missing or unusable"],
    ["api-key", "apiKeyFile", " \n", "API key file is missing or unusable"],
  ];
  for (const [billing, field, content, message] of cases) {
    const path = join(f.dir, `${billing}-${Math.random()}`);
    if (content !== null) writeFileSync(path, content);
    await withProxy({ billing, [field]: path }, async ({ proxy, upstream }) => {
      const res = await send(proxy);
      assert.equal(res.status, 401, message);
      assert.deepEqual(JSON.parse(res.body), { type: "error", error: { type: "authentication_error", message } });
      assert.equal(upstream.requests.length, 0);
    });
  }
});

test("an unreadable login file answers 401", { skip: process.getuid?.() === 0 }, async () => {
  const f = files();
  writeFileSync(f.credentialsFile, login(Date.now() + 9e6));
  chmodSync(f.credentialsFile, 0o000);
  await withProxy({ billing: "subscription", credentialsFile: f.credentialsFile }, async ({ proxy }) => {
    assert.equal((await send(proxy)).status, 401);
  });
});

test("the response streams: the first chunk arrives before the upstream finishes", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  let release;
  const gate = new Promise((done) => { release = done; });
  const handler = (req, res) => {
    res.writeHead(200, { "content-type": "text/event-stream" });
    res.write("event: one\n\n");
    gate.then(() => res.end("event: two\n\n"));
  };
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy }) => {
    const first = await new Promise((resolve, reject) => {
      const req = http.request({ socketPath: proxy.socketPath, method: "POST", path: "/v1/messages" }, (res) => {
        assert.equal(res.headers["transfer-encoding"], "chunked");
        const chunks = [];
        res.on("data", (c) => {
          chunks.push(c.toString());
          if (chunks.length === 1) resolve({ res, chunks });
        });
      });
      req.on("error", reject);
      req.end("{}");
    });
    assert.equal(first.chunks[0], "event: one\n\n");
    release();
    await new Promise((done) => first.res.on("end", done));
    assert.equal(first.chunks.join(""), "event: one\n\nevent: two\n\n");
  }, handler);
});

test("the request body streams to the upstream and the status and headers of the answer pass through", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  const handler = (req, res, body) => {
    res.writeHead(429, { "retry-after": "7", "content-type": "application/json" });
    res.end(JSON.stringify({ echoed: body.length }));
  };
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    const big = "x".repeat(300_000);
    const res = await send(proxy, { body: big });
    assert.equal(res.status, 429);
    assert.equal(res.headers["retry-after"], "7");
    assert.equal(res.body, '{"echoed":300000}');
    assert.equal(upstream.requests[0].body.length, 300_000);
  }, handler);
});

test("a chunked request body reaches the upstream whole", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    await new Promise((resolve, reject) => {
      const req = http.request({ socketPath: proxy.socketPath, method: "POST", path: "/v1/messages", headers: { "transfer-encoding": "chunked" } }, (res) => {
        res.resume();
        res.on("end", resolve);
      });
      req.on("error", reject);
      req.write("part one,");
      req.end("part two");
    });
    assert.equal(upstream.requests[0].body, "part one,part two");
  });
});

test("hop-by-hop headers and the headers that the connection header names are not forwarded", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    await send(proxy, { headers: { connection: "x-hop", "x-hop": "1", "proxy-authorization": "Basic x", te: "trailers", "keep-alive": "timeout=5", "x-kept": "yes" } });
    const { headers } = upstream.requests[0];
    for (const name of ["x-hop", "proxy-authorization", "te", "keep-alive"]) assert.equal(headers[name], undefined, name);
    assert.equal(headers["x-kept"], "yes");
  });
});

test("an unreachable upstream answers 502 without details", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  const down = await fakeUpstream();
  await down.close();
  const proxy = await startCredentialProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile, socketPath: socketPath(), upstream: down.url });
  try {
    const res = await send(proxy);
    assert.equal(res.status, 502);
    assert.equal(JSON.parse(res.body).error.type, "api_error");
    assert.ok(!res.body.includes("dummy-key"));
  } finally {
    await proxy.close();
  }
});

// A raw TCP upstream that answers every connection with the given bytes and closes.
async function rawUpstream(answer) {
  const server = net.createServer((socket) => {
    socket.on("error", () => {});
    socket.once("data", () => socket.end(answer));
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  return {
    url: `http://127.0.0.1:${server.address().port}`,
    close: () => new Promise((done) => { server.close(done); server.closeAllConnections?.(); }),
  };
}

test("an upstream answer with an invalid status or reason phrase gives 502 and the proxy keeps serving", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  const answers = [
    "HTTP/1.1 000 x\r\ncontent-length: 0\r\n\r\n",
    "HTTP/1.1 099 x\r\ncontent-length: 0\r\n\r\n",
    "HTTP/1.1 200 Bad\x01\r\ncontent-length: 0\r\n\r\n",
    "HTTP/1.1 101 Switching Protocols\r\ncontent-length: 0\r\n\r\n",
  ];
  for (const answer of answers) {
    const upstream = await rawUpstream(answer);
    const proxy = await startCredentialProxy({
      billing: "api-key", apiKeyFile: f.apiKeyFile, socketPath: socketPath(), upstream: upstream.url,
    });
    try {
      const res = await send(proxy);
      // A reason phrase with a control byte is dropped, so that answer is forwarded with its status.
      assert.equal(res.status, answer.includes("Bad") ? 200 : 502, JSON.stringify(answer));
      const next = await send(proxy, { method: "GET", path: "/v1/models", body: "" });
      assert.equal(next.status, answer.includes("Bad") ? 200 : 502);
    } finally {
      await proxy.close();
      await upstream.close();
    }
  }
});

test("the proxy listens on its unix socket only and close() removes it", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  const path = socketPath();
  const proxy = await startCredentialProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile, socketPath: path });
  assert.equal(proxy.socketPath, path);
  assert.equal(proxy.port, undefined);
  assert.ok(existsSync(path));
  await new Promise((done, fail) => net.connect(path).once("connect", done).once("error", fail).once("connect", function () { this.end(); }));
  await proxy.close();
  assert.ok(!existsSync(path));
  await assert.rejects(send(proxy));
});

test("only GET, POST and HEAD are served; every other method answers 400 and reaches no upstream", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    for (const method of ["PUT", "DELETE", "PATCH", "OPTIONS", "TRACE"]) {
      const res = await send(proxy, { method, path: "/v1/messages" });
      assert.equal(res.status, 400, method);
      assert.equal(JSON.parse(res.body).error.type, "invalid_request_error");
    }
    assert.equal(upstream.requests.length, 0);
    for (const method of ["GET", "POST", "HEAD"]) {
      const res = await send(proxy, { method, path: "/v1/models", body: method === "POST" ? "{}" : "" });
      assert.equal(res.status, 200, method);
    }
    assert.deepEqual(upstream.requests.map((r) => r.method), ["GET", "POST", "HEAD"]);
  });
});

test("only the messages and models paths without //, ://, backslash or .. segments are served", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-key");
  const refused = [
    "/", "/v1", "/v2/messages", "/v1/../etc", "/v1/a/../b", "/v1/%2e%2e/x", "/v1/%2E%2E/x", "//evil.example/v1/x", "/v1//x",
    "/v1/http://evil.example/", "/v1/x?next=http://evil.example", "/v1/a%2f%2fb", "/v1/a\\b", "/v1/%zz", "http://evil.example/v1/messages",
    "*", "/v1/", "/v1/files", "/v1/files/file_1/content", "/v1/messages/batches", "/v1/messages/batches/b1/results",
    "/v1/messages/", "/v1/models/../files", "/v1/organizations/x", "/v1/message", "/v1/modelsx",
  ];
  await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy, upstream }) => {
    for (const path of refused) {
      const res = await send(proxy, { path });
      assert.equal(res.status, 400, path);
    }
    assert.equal(upstream.requests.length, 0);
    const served = [
      "/v1/messages", "/v1/messages?beta=true", "/v1/models", "/v1/models?limit=5", "/v1/models/claude-sonnet-5-5",
      "/v1/messages/count_tokens", "/v1/messages/count_tokens?beta=true",
    ];
    for (const path of served) {
      assert.equal((await send(proxy, { path })).status, 200, path);
    }
  });
});

test("allowedTarget: the pure rule", () => {
  const ok = [
    "/v1/messages", "/v1/messages?beta=true", "/v1/messages/count_tokens", "/v1/models", "/v1/models/m1",
    "/v1/models/a.b/c..d",
  ];
  for (const target of ok) assert.ok(allowedTarget(target), target);
  const bad = [
    "/v1/", "/v1/..", "/v1/x/..", "/v1/x/../y", "/v1//", "/v1/a://b", "/v1/%2e%2e", "/v1/a%5cb", "/V1/x", "v1/x", "",
    "/v1/files", "/v1/files/..hidden", "/v1/messages/batches", "/v1/messages/", "/v1/messages/x",
    "/v1/models/../files", "/v1/models/%2e%2e/files", "/v1/modelsx",
  ];
  for (const target of bad) assert.ok(!allowedTarget(target), target);
});

test("an unknown billing throws", async () => {
  await assert.rejects(startCredentialProxy({ billing: "both" }), /billing must be one of/);
  assert.throws(() => loadCredential({ billing: "both" }), /billing must be one of/);
});

test("the proxy writes no header or body to the console, and the module has no logging", async () => {
  const f = files();
  writeFileSync(f.apiKeyFile, "dummy-real-key");
  const calls = [];
  const saved = {};
  for (const name of ["log", "info", "warn", "error", "debug"]) {
    saved[name] = console[name];
    console[name] = (...args) => calls.push(args);
  }
  try {
    await withProxy({ billing: "api-key", apiKeyFile: f.apiKeyFile }, async ({ proxy }) => {
      await send(proxy, { headers: { "x-api-key": "placeholder" }, body: '{"secret":"body"}' });
      await send(proxy, { path: "/x" });
    });
    await withProxy({ billing: "api-key", apiKeyFile: join(f.dir, "absent") }, async ({ proxy }) => {
      await send(proxy);
    });
  } finally {
    Object.assign(console, saved);
  }
  assert.deepEqual(calls, []);
  const source = readFileSync(new URL("./credential-proxy.mjs", import.meta.url), "utf8");
  assert.doesNotMatch(source, /console\.|process\.std/);
});

test("checkApiKeyFile: null only for a readable regular file with non-whitespace content", () => {
  const f = files();
  const file = (name, content) => {
    const path = join(f.dir, name);
    if (content !== null) writeFileSync(path, content);
    return path;
  };
  assert.equal(checkApiKeyFile(file("missing.key", null)), "does not exist");
  assert.equal(checkApiKeyFile(file("empty.key", "")), "is empty");
  assert.equal(checkApiKeyFile(file("blank.key", " \n\t\n")), "is empty");
  assert.equal(checkApiKeyFile(file("present.key", "dummy-key\n")), null);
  assert.equal(checkApiKeyFile(f.dir), "is not a regular file");
  if (process.getuid?.() !== 0) {
    const locked = file("locked.key", "dummy-key");
    chmodSync(locked, 0o000);
    assert.equal(checkApiKeyFile(locked), "is not readable");
  }
});

test("checkLoginFile: null only for a login with an access token that has not expired", () => {
  const f = files();
  const now = 1_000_000;
  assert.equal(checkLoginFile(f.credentialsFile, now), "does not exist");
  const cases = [
    ["{not json", "is not valid JSON"],
    ["{}", "holds no claude.ai login"],
    [JSON.stringify({ claudeAiOauth: { expiresAt: now + 1 } }), "holds no access token"],
    [JSON.stringify({ claudeAiOauth: { accessToken: "t" } }), "holds no expiry time"],
    [login(now), "has an expired access token"],
    [login(now - 1), "has an expired access token"],
    [login(now + 1), null],
  ];
  for (const [content, expected] of cases) {
    writeFileSync(f.credentialsFile, content);
    assert.equal(checkLoginFile(f.credentialsFile, now), expected, content);
  }
  assert.equal(checkLoginFile(f.dir, now), "is not readable");
});
