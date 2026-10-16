// Tests for the egress proxy: the address policy as pure functions, and the proxy against local
// test servers with an injected resolver and connector. No test reaches the internet.
import { test } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import net from "node:net";
import { existsSync, mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  addressReason, connectAny, decideTarget, parseAuthority, startEgressProxy,
} from "./egress-proxy.mjs";

test("addressReason: IPv4 addresses that are not public are refused with their range", () => {
  const cases = {
    "0.0.0.0": "unspecified", "0.1.2.3": "unspecified", "10.0.0.1": "private", "10.255.255.255": "private",
    "100.64.0.1": "carrier-grade NAT", "100.127.255.255": "carrier-grade NAT", "127.0.0.1": "loopback", "127.255.255.254": "loopback",
    "169.254.169.254": "link-local", "172.16.0.1": "private", "172.31.255.255": "private", "192.0.0.1": "reserved",
    "192.0.2.5": "documentation", "192.168.1.1": "private", "198.18.0.1": "reserved", "198.19.255.255": "reserved",
    "198.51.100.7": "documentation", "203.0.113.9": "documentation", "224.0.0.1": "multicast", "239.255.255.255": "multicast",
    "240.0.0.1": "reserved", "255.255.255.255": "broadcast",
  };
  for (const [address, reason] of Object.entries(cases)) assert.equal(addressReason(address), reason, address);
});

test("addressReason: public IPv4 addresses next to the refused ranges pass", () => {
  for (const address of [
    "8.8.8.8", "1.1.1.1", "93.184.216.34", "9.255.255.255", "11.0.0.0", "100.63.255.255", "100.128.0.0", "126.255.255.255",
    "128.0.0.1", "169.253.255.255", "169.255.0.0", "172.15.255.255", "172.32.0.0", "192.169.0.1", "192.167.255.255", "198.17.255.255",
    "198.20.0.0", "223.255.255.255",
  ]) assert.equal(addressReason(address), null, address);
});

test("addressReason: IPv6 addresses that are not public are refused", () => {
  const cases = {
    "::": "unspecified or reserved", "::1": "unspecified or reserved", "fe80::1": "link-local", "febf::1": "link-local",
    "fc00::1": "unique local", "fd12:3456::1": "unique local", "ff02::1": "multicast", "2001:db8::1": "documentation",
    "fec0::1": "reserved", "100::1": "reserved", "2001::1": "reserved", "2001:1ff::1": "reserved", "3fff::1": "documentation",
  };
  for (const [address, reason] of Object.entries(cases)) assert.equal(addressReason(address), reason, address);
});

test("addressReason: public IPv6 addresses pass", () => {
  for (const address of ["2606:4700:4700::1111", "2a00:1450:4001:81b::200e", "2001:4860:4860::8888", "2001:200::1", "2620:fe::fe"]) {
    assert.equal(addressReason(address), null, address);
  }
});

test("addressReason: an IPv4 address inside an IPv6 form gets the verdict of the IPv4 address", () => {
  const cases = {
    "::ffff:127.0.0.1": "loopback", "::ffff:7f00:1": "loopback", "::FFFF:10.1.2.3": "private", "::ffff:a01:203": "private",
    "::ffff:169.254.169.254": "link-local", "::ffff:192.168.0.1": "private", "::ffff:0.0.0.0": "unspecified",
    "64:ff9b::7f00:1": "loopback", "64:ff9b::a00:1": "private", "2002:7f00:1::": "loopback", "2002:c0a8:101::1": "private",
    "::127.0.0.1": "unspecified or reserved", "0:0:0:0:0:ffff:7f00:1": "loopback", "64:ff9b:1::1": "reserved",
  };
  for (const [address, reason] of Object.entries(cases)) assert.equal(addressReason(address), reason, address);
  for (const address of ["::ffff:8.8.8.8", "::ffff:808:808", "64:ff9b::808:808", "2002:808:808::1"]) assert.equal(addressReason(address), null, address);
});

test("addressReason: text that is no IP address is invalid", () => {
  for (const text of ["", "localhost", "example.org", "1.2.3", "1.2.3.4.5", "256.1.1.1", "::g", "1::2::3", "0x7f.1"]) {
    assert.equal(addressReason(text), "invalid address", text);
  }
});

test("decideTarget: ports 80 and 443 with public addresses pass, anything else is refused with a reason", () => {
  assert.deepEqual(decideTarget({ port: 443, addresses: ["93.184.216.34", "2606:4700::1"] }), { allow: true, addresses: ["93.184.216.34", "2606:4700::1"] });
  assert.equal(decideTarget({ port: 80, addresses: ["8.8.8.8"] }).allow, true);
  for (const port of [0, 22, 25, 81, 444, 8080, 8443, 3306, 5432, 9229]) {
    assert.deepEqual(decideTarget({ port, addresses: ["8.8.8.8"] }), { allow: false, reason: `port ${port} is not allowed` });
  }
  assert.deepEqual(decideTarget({ port: 443, addresses: [] }), { allow: false, reason: "the host has no address" });
  // One refused address among public ones refuses the host.
  assert.deepEqual(decideTarget({ port: 443, addresses: ["8.8.8.8", "127.0.0.1"] }), { allow: false, reason: "the host resolves to a loopback address" });
  assert.deepEqual(decideTarget({ port: 443, addresses: ["2606:4700::1", "::ffff:10.0.0.1"] }), { allow: false, reason: "the host resolves to a private address" });
});

test("parseAuthority: host and port, bracketed IPv6, default port", () => {
  assert.deepEqual(parseAuthority("example.org:443"), { host: "example.org", port: 443 });
  assert.deepEqual(parseAuthority("[2606:4700::1]:80"), { host: "2606:4700::1", port: 80 });
  assert.deepEqual(parseAuthority("example.org", 443), { host: "example.org", port: 443 });
  assert.equal(parseAuthority("example.org"), null);
  for (const bad of ["", ":443", "a:b", "a:1:2", "[::1", "host name:1", "a:123456", "a b"]) assert.equal(parseAuthority(bad, 443), null, bad);
});

// Starts an echo server that also records what it receives; resolves { port, received, close }.
async function echoServer() {
  const received = [];
  const sockets = new Set();
  const server = net.createServer((socket) => {
    sockets.add(socket);
    socket.on("data", (chunk) => {
      received.push(chunk.toString());
      socket.write(`echo:${chunk}`);
    });
    socket.on("error", () => {});
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  return { port: server.address().port, received, close: () => new Promise((done) => { for (const s of sockets) s.destroy(); server.close(done); }) };
}

// A local http server that records requests and answers "hello".
async function httpServer() {
  const requests = [];
  const server = http.createServer((req, res) => {
    requests.push({ method: req.method, url: req.url, headers: req.headers });
    res.writeHead(200, { "x-origin": "yes", connection: "close" });
    res.end("hello");
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  return { port: server.address().port, requests, close: () => new Promise((done) => { server.close(done); server.closeAllConnections(); }) };
}

// Starts the proxy with a resolver table and a connector that leads every vetted address to the
// local port. Returns the proxy handle plus the calls that it made.
async function withProxy(table, local, fn) {
  const dir = mkdtempSync(join(tmpdir(), "egress-"));
  const socketPath = join(dir, "egress.sock");
  const lookups = [];
  const connects = [];
  const lookup = async (host) => {
    lookups.push(host);
    const answer = table[host];
    if (answer === undefined) throw Object.assign(new Error("not found"), { code: "ENOTFOUND" });
    return (typeof answer === "function" ? answer() : answer).map((address) => ({ address, family: address.includes(":") ? 6 : 4 }));
  };
  const connect = (addresses, port) => {
    connects.push({ addresses, port });
    return new Promise((done, fail) => net.connect(local.port, "127.0.0.1").once("connect", function () { done(this); }).once("error", fail));
  };
  const proxy = await startEgressProxy({ socketPath, lookup, connect });
  try {
    return await fn({ socketPath, lookups, connects, proxy });
  } finally {
    await proxy.close();
  }
}

// Sends CONNECT through the proxy. Resolves { status, socket, body } (socket is the tunnel on 200).
function connectVia(socketPath, target) {
  return new Promise((resolve, reject) => {
    const req = http.request({ socketPath, method: "CONNECT", path: target });
    // Node reports every answer to CONNECT here; a refusal carries its text in the stream.
    req.on("connect", (res, socket, head) => {
      if (res.statusCode === 200) return resolve({ status: 200, socket });
      const chunks = [head];
      socket.on("data", (c) => chunks.push(c));
      socket.on("close", () => resolve({ status: res.statusCode, body: Buffer.concat(chunks).toString() }));
    });
    req.on("error", reject);
    req.end();
  });
}

// Sends a plain request in absolute form. Resolves { status, headers, body }.
function plainVia(socketPath, { url, method = "GET", headers = {} }) {
  return new Promise((resolve, reject) => {
    const req = http.request({ socketPath, method, path: url, headers }, (res) => {
      const chunks = [];
      res.on("data", (c) => chunks.push(c));
      res.on("end", () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks).toString() }));
    });
    req.on("error", reject);
    req.end();
  });
}

test("CONNECT to a public host on 443 opens a tunnel to a vetted address", async () => {
  const echo = await echoServer();
  try {
    await withProxy({ "example.org": ["93.184.216.34", "2606:2800::1"] }, echo, async ({ socketPath, connects, lookups }) => {
      const { status, socket } = await connectVia(socketPath, "example.org:443");
      assert.equal(status, 200);
      const answer = await new Promise((done) => {
        socket.once("data", (chunk) => done(chunk.toString()));
        socket.write("ping");
      });
      assert.equal(answer, "echo:ping");
      socket.destroy();
      assert.deepEqual(connects, [{ addresses: ["93.184.216.34", "2606:2800::1"], port: 443 }]);
      assert.deepEqual(lookups, ["example.org"]);
    });
  } finally {
    await echo.close();
  }
});

test("CONNECT refusals answer 403 with a reason and open no connection", async () => {
  const echo = await echoServer();
  try {
    const table = {
      "internal.example": ["10.0.0.7"], "loop.example": ["127.0.0.1"], "mixed.example": ["8.8.8.8", "192.168.1.1"],
      "mapped.example": ["::ffff:127.0.0.1"], "meta.example": ["169.254.169.254"], "ok.example": ["8.8.8.8"],
    };
    await withProxy(table, echo, async ({ socketPath, connects }) => {
      const refused = [
        ["internal.example:443", /private/], ["loop.example:443", /loopback/], ["mixed.example:443", /private/],
        ["mapped.example:443", /loopback/], ["meta.example:80", /link-local/], ["ok.example:22", /port 22 is not allowed/],
        ["ok.example:8080", /port 8080/], ["127.0.0.1:443", /loopback/], ["10.1.2.3:443", /private/], ["[::1]:443", /unspecified or reserved/],
        ["[::ffff:7f00:1]:443", /loopback/], ["169.254.169.254:80", /link-local/], ["0.0.0.0:80", /unspecified/],
      ];
      for (const [target, reason] of refused) {
        const res = await connectVia(socketPath, target);
        assert.equal(res.status, 403, target);
        assert.match(res.body ?? "", reason, target);
      }
      assert.equal(connects.length, 0);
      assert.equal((await connectVia(socketPath, "missing.example:443")).status, 502);
      assert.equal(connects.length, 0);
    });
  } finally {
    await echo.close();
  }
});

test("a host that resolves a second time to a private address does not help: the first answer is the only lookup", async () => {
  const echo = await echoServer();
  try {
    let calls = 0;
    const table = { "rebind.example": () => (++calls === 1 ? ["93.184.216.34"] : ["127.0.0.1"]) };
    await withProxy(table, echo, async ({ socketPath, connects, lookups }) => {
      const { status, socket } = await connectVia(socketPath, "rebind.example:443");
      assert.equal(status, 200);
      socket.destroy();
      assert.deepEqual(connects, [{ addresses: ["93.184.216.34"], port: 443 }]);
      assert.deepEqual(lookups, ["rebind.example"]);
      // The next request resolves anew and is judged on its own answer.
      assert.equal((await connectVia(socketPath, "rebind.example:443")).status, 403);
    });
  } finally {
    await echo.close();
  }
});

test("a plain HTTP request in absolute form is forwarded in origin form with the original host header", async () => {
  const origin = await httpServer();
  try {
    await withProxy({ "example.org": ["93.184.216.34"] }, origin, async ({ socketPath, connects }) => {
      const res = await plainVia(socketPath, {
        url: "http://example.org/path/a?x=1", headers: { host: "example.org", "proxy-connection": "keep-alive", "proxy-authorization": "Basic eDp5", "x-kept": "yes" },
      });
      assert.equal(res.status, 200);
      assert.equal(res.body, "hello");
      assert.equal(res.headers["x-origin"], "yes");
      const [seen] = origin.requests;
      assert.equal(seen.method, "GET");
      assert.equal(seen.url, "/path/a?x=1");
      assert.equal(seen.headers.host, "example.org");
      assert.equal(seen.headers["x-kept"], "yes");
      assert.equal(seen.headers["proxy-connection"], undefined);
      assert.equal(seen.headers["proxy-authorization"], undefined);
      assert.deepEqual(connects, [{ addresses: ["93.184.216.34"], port: 80 }]);
    });
  } finally {
    await origin.close();
  }
});

test("plain HTTP refusals: private addresses and other ports answer 403, other forms 400/403, unresolved hosts 502", async () => {
  const origin = await httpServer();
  try {
    await withProxy({ "internal.example": ["10.0.0.7"], "ok.example": ["8.8.8.8"] }, origin, async ({ socketPath, connects }) => {
      assert.equal((await plainVia(socketPath, { url: "http://internal.example/" })).status, 403);
      assert.equal((await plainVia(socketPath, { url: "http://127.0.0.1/" })).status, 403);
      assert.equal((await plainVia(socketPath, { url: "http://[::1]/" })).status, 403);
      const port = await plainVia(socketPath, { url: "http://ok.example:8080/" });
      assert.equal(port.status, 403);
      assert.match(port.body, /port 8080 is not allowed/);
      assert.equal((await plainVia(socketPath, { url: "https://ok.example/" })).status, 403);
      assert.equal((await plainVia(socketPath, { url: "/origin-form" })).status, 400);
      assert.equal((await plainVia(socketPath, { url: "http://missing.example/" })).status, 502);
      assert.equal(connects.length, 0);
      assert.equal(origin.requests.length, 0);
    });
  } finally {
    await origin.close();
  }
});

test("the proxy answers a refused or failed connection without leaking the lookup error", async () => {
  const echo = await echoServer();
  try {
    await withProxy({ "ok.example": ["8.8.8.8"] }, { port: 1 }, async ({ socketPath }) => {
      const res = await connectVia(socketPath, "ok.example:443");
      assert.equal(res.status, 502);
      assert.doesNotMatch(res.body, /ECONNREFUSED|127\.0\.0\.1/);
    });
  } finally {
    await echo.close();
  }
});

test("close() removes the socket and ends open tunnels", async () => {
  const echo = await echoServer();
  try {
    const dir = mkdtempSync(join(tmpdir(), "egress-"));
    const socketPath = join(dir, "egress.sock");
    const connect = () => new Promise((done) => net.connect(echo.port, "127.0.0.1").once("connect", function () { done(this); }));
    const proxy = await startEgressProxy({ socketPath, lookup: async () => [{ address: "8.8.8.8", family: 4 }], connect });
    const { socket } = await connectVia(socketPath, "x.example:443");
    const closed = new Promise((done) => socket.once("close", done));
    await proxy.close();
    await closed;
    assert.ok(!existsSync(socketPath));
  } finally {
    await echo.close();
  }
});

test("the proxy writes nothing to the console and the module has no logging", async () => {
  const origin = await httpServer();
  const calls = [];
  const saved = {};
  for (const name of ["log", "info", "warn", "error", "debug"]) {
    saved[name] = console[name];
    console[name] = (...args) => calls.push(args);
  }
  try {
    await withProxy({ "ok.example": ["8.8.8.8"], "bad.example": ["10.0.0.1"] }, origin, async ({ socketPath }) => {
      await plainVia(socketPath, { url: "http://ok.example/secret-path?token=abc" });
      await plainVia(socketPath, { url: "http://bad.example/" });
      await connectVia(socketPath, "bad.example:443");
    });
  } finally {
    Object.assign(console, saved);
    await origin.close();
  }
  assert.deepEqual(calls, []);
  assert.doesNotMatch(readFileSync(new URL("./egress-proxy.mjs", import.meta.url), "utf8"), /console\.|process\.std/);
});

test("addressReason: IPv4-translated and SRv6 addresses are refused, public IPv6 passes", () => {
  for (const address of ["::ffff:0:7f00:1", "::ffff:0:808:808", "::ffff:0:0:0", "5f00::1", "5f00:ffff::"]) {
    assert.equal(addressReason(address), "reserved", address);
  }
  for (const address of ["2606:2800::1", "5f01::1", "2a00:1450:4001::200e"]) assert.equal(addressReason(address), null, address);
});

// A raw TCP server that answers every connection with the given bytes and closes.
async function rawServer(answer) {
  const server = net.createServer((socket) => {
    socket.on("error", () => {});
    socket.once("data", () => socket.end(answer));
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  return {
    port: server.address().port,
    close: () => new Promise((done) => { server.close(done); server.closeAllConnections?.(); }),
  };
}

test("an upstream answer with an invalid status gives 502 and the proxy keeps serving", async () => {
  const answers = [
    "HTTP/1.1 000 x\r\ncontent-length: 0\r\n\r\n",
    "HTTP/1.1 099 x\r\ncontent-length: 0\r\n\r\n",
    "HTTP/1.1 600 x\r\ncontent-length: 0\r\n\r\n",
    "HTTP/1.1 101 Switching Protocols\r\nconnection: upgrade\r\nupgrade: x\r\n\r\n",
    "HTTP/1.1 101 Switching Protocols\r\ncontent-length: 0\r\n\r\n",
  ];
  for (const answer of answers) {
    const upstream = await rawServer(answer);
    try {
      await withProxy({ "bad.example": ["93.184.216.34"] }, upstream, async ({ socketPath }) => {
        for (let i = 0; i < 2; i++) {
          const res = await plainVia(socketPath, { url: "http://bad.example/" });
          assert.equal(res.status, 502, JSON.stringify(answer));
          assert.match(res.body, /not valid|failed/);
        }
        assert.equal((await connectVia(socketPath, "127.0.0.1:443")).status, 403);
      });
    } finally {
      await upstream.close();
    }
  }
});

test("an upstream reason phrase with a control byte does not reach the client and does not stop the proxy", async () => {
  const upstream = await rawServer("HTTP/1.1 200 Bad\x01\r\ncontent-length: 2\r\n\r\nok");
  try {
    await withProxy({ "odd.example": ["93.184.216.34"] }, upstream, async ({ socketPath }) => {
      for (let i = 0; i < 2; i++) {
        const res = await plainVia(socketPath, { url: "http://odd.example/" });
        assert.equal(res.status, 200);
        assert.equal(res.body, "ok");
      }
    });
  } finally {
    await upstream.close();
  }
});

test("connectAny: takes the first address that accepts, rejects when none does", async () => {
  const echo = await echoServer();
  try {
    // 127.0.0.2 is a loopback address that the server on 127.0.0.1 does not accept.
    const socket = await connectAny(["127.0.0.2", "127.0.0.1"], echo.port);
    assert.equal(socket.remoteAddress, "127.0.0.1");
    socket.destroy();
  } finally {
    await echo.close();
  }
  const closed = await echoServer();
  const { port } = closed;
  await closed.close();
  await assert.rejects(connectAny(["127.0.0.1", "127.0.0.2"], port), { code: "ECONNREFUSED" });
});

test("connectAny: an address that does not answer in time counts as failed and the next one is tried", async () => {
  const sockets = [];
  const silent = () => {
    const socket = new net.Socket();
    sockets.push(socket);
    setImmediate(() => socket.emit("timeout"));
    return socket;
  };
  await assert.rejects(connectAny(["192.0.2.1", "192.0.2.2"], 80, 50, silent), /192\.0\.2\.2 timed out/);
  assert.equal(sockets.length, 2);
  assert.ok(sockets.every((socket) => socket.destroyed));
});

test("a tunnel that stays silent is closed after the idle timeout", async () => {
  const echo = await echoServer();
  try {
    const dir = mkdtempSync(join(tmpdir(), "egress-"));
    const socketPath = join(dir, "egress.sock");
    const connect = () => new Promise((done) => net.connect(echo.port, "127.0.0.1").once("connect", function () { done(this); }));
    const proxy = await startEgressProxy({
      socketPath, lookup: async () => [{ address: "8.8.8.8", family: 4 }], connect, idleTimeoutMs: 50,
    });
    try {
      const { socket } = await connectVia(socketPath, "x.example:443");
      await new Promise((done) => socket.once("close", done));
    } finally {
      await proxy.close();
    }
  } finally {
    await echo.close();
  }
});

test("the proxy takes at most maxConnections client connections at once", async () => {
  const dir = mkdtempSync(join(tmpdir(), "egress-"));
  const socketPath = join(dir, "egress.sock");
  const proxy = await startEgressProxy({ socketPath, maxConnections: 1 });
  try {
    const first = net.connect(socketPath);
    await new Promise((done) => first.once("connect", done));
    const second = net.connect(socketPath);
    second.on("error", () => {});
    await new Promise((done) => second.once("close", done));
    assert.ok(!first.destroyed);
    first.destroy();
  } finally {
    await proxy.close();
  }
});
