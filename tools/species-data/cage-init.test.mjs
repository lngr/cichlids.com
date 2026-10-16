// Tests for the init program of the cage: the relay specification, the TCP relays to unix sockets
// and the child handling. The child is a node process; no test starts bwrap.
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtempSync } from "node:fs";
import net from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

import { main, parseRelaySpec, startRelays } from "./cage-init.mjs";

const INIT = fileURLToPath(new URL("./cage-init.mjs", import.meta.url));

// A free loopback port.
async function freePort() {
  const server = net.createServer();
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  const { port } = server.address();
  await new Promise((done) => server.close(done));
  return port;
}

// A unix socket server that answers every chunk with "<tag>:<chunk>".
async function unixEcho(tag) {
  const path = join(mkdtempSync(join(tmpdir(), "cage-init-")), `${tag}.sock`);
  const sockets = new Set();
  const server = net.createServer((socket) => {
    sockets.add(socket);
    socket.on("data", (chunk) => socket.write(`${tag}:${chunk}`));
    socket.on("error", () => {});
  });
  await new Promise((done) => server.listen(path, done));
  return { path, close: () => new Promise((done) => { for (const s of sockets) s.destroy(); server.close(done); }) };
}

// Sends text to a loopback port and resolves the first answer.
const ask = (port, text) => new Promise((resolve, reject) => {
  const socket = net.connect(port, "127.0.0.1");
  socket.once("data", (chunk) => {
    socket.destroy();
    resolve(chunk.toString());
  });
  socket.once("connect", () => socket.write(text));
  socket.once("error", reject);
});

test("parseRelaySpec: port and socket pairs", () => {
  assert.deepEqual(parseRelaySpec("18080=/run/cage/api.sock,18081=/run/cage/egress.sock"), [
    { port: 18080, socket: "/run/cage/api.sock" }, { port: 18081, socket: "/run/cage/egress.sock" },
  ]);
  assert.deepEqual(parseRelaySpec("1=/s"), [{ port: 1, socket: "/s" }]);
  for (const bad of ["", "x", "0=/s", "65536=/s", "80=relative.sock", "80=", "=/s", "80=/a,", "80=/a,b", "80/s", "-1=/s"]) {
    assert.throws(() => parseRelaySpec(bad), /bad relay entry/, bad);
  }
});

test("startRelays: every loopback port relays to its own unix socket, in both directions", async () => {
  const api = await unixEcho("api");
  const egress = await unixEcho("egress");
  const [apiPort, egressPort] = [await freePort(), await freePort()];
  const relays = await startRelays([{ port: apiPort, socket: api.path }, { port: egressPort, socket: egress.path }]);
  try {
    assert.equal(await ask(apiPort, "one"), "api:one");
    assert.equal(await ask(egressPort, "two"), "egress:two");
    const [a, b] = await Promise.all([ask(apiPort, "x"), ask(apiPort, "y")]);
    assert.deepEqual([a, b], ["api:x", "api:y"]);
  } finally {
    relays.close();
    await api.close();
    await egress.close();
  }
});

test("startRelays: the listeners are bound to 127.0.0.1 only", async () => {
  const api = await unixEcho("api");
  const port = await freePort();
  const relays = await startRelays([{ port, socket: api.path }]);
  try {
    const probe = net.connect(port, "127.0.0.1");
    await new Promise((done) => probe.once("connect", done));
    assert.equal(probe.remoteAddress, "127.0.0.1");
    probe.destroy();
  } finally {
    relays.close();
    await api.close();
  }
});

test("startRelays: a connection to a socket that is not there is closed, the relay keeps running", async () => {
  const dir = mkdtempSync(join(tmpdir(), "cage-init-"));
  const port = await freePort();
  const relays = await startRelays([{ port, socket: join(dir, "absent.sock") }]);
  try {
    const socket = net.connect(port, "127.0.0.1");
    socket.on("error", () => {});
    await new Promise((done) => socket.once("close", done));
    const again = net.connect(port, "127.0.0.1");
    again.on("error", () => {});
    await new Promise((done) => again.once("close", done));
  } finally {
    relays.close();
  }
});

test("startRelays: a port in use fails", async () => {
  const taken = net.createServer();
  await new Promise((done) => taken.listen(0, "127.0.0.1", done));
  await assert.rejects(startRelays([{ port: taken.address().port, socket: "/s" }]), /EADDRINUSE/);
  taken.close();
});

// Runs the init program as a process; resolves { code, stdout, stderr } and offers the child handle.
function runInit(args) {
  const child = spawn(process.execPath, [INIT, ...args], { stdio: ["ignore", "pipe", "pipe"] });
  let stdout = "";
  let stderr = "";
  child.stdout.on("data", (c) => (stdout += c));
  child.stderr.on("data", (c) => (stderr += c));
  const done = new Promise((resolve) => child.on("close", (code, signal) => resolve({ code, signal, stdout, stderr })));
  return { child, done, output: () => stdout };
}

test("the init program starts the command with the relays up and exits with its exit code", async () => {
  const api = await unixEcho("api");
  const port = await freePort();
  const script = `
    const net = require("node:net");
    const s = net.connect(${port}, "127.0.0.1");
    s.on("data", (d) => { console.log(String(d)); process.exit(7); });
    s.on("connect", () => s.write("hello"));
  `;
  try {
    const result = await runInit([`${port}=${api.path}`, "--", process.execPath, "-e", script]).done;
    assert.equal(result.code, 7);
    assert.equal(result.stdout.trim(), "api:hello");
  } finally {
    await api.close();
  }
});

test("the child inherits the environment and the standard streams, and its exit code 0 passes", async () => {
  const api = await unixEcho("api");
  const port = await freePort();
  try {
    const result = await runInit([
      `${port}=${api.path}`, "--", process.execPath, "-e", 'console.error("err"); console.log(process.env.PATH ? "env" : "no env")',
    ]).done;
    assert.equal(result.code, 0);
    assert.equal(result.stdout.trim(), "env");
    assert.equal(result.stderr.trim(), "err");
  } finally {
    await api.close();
  }
});

test("SIGTERM to the init program reaches the child; a child ended by a signal gives 128 plus its number", async () => {
  const api = await unixEcho("api");
  const port = await freePort();
  try {
    const script = 'process.on("SIGTERM", () => process.kill(process.pid, "SIGKILL")); console.log("ready"); setInterval(() => {}, 1000);';
    const run = runInit([`${port}=${api.path}`, "--", process.execPath, "-e", script]);
    await new Promise((resolve) => {
      const timer = setInterval(() => {
        if (run.output().includes("ready")) {
          clearInterval(timer);
          resolve();
        }
      }, 20);
    });
    run.child.kill("SIGTERM");
    const result = await run.done;
    assert.equal(result.code, 128 + 9);
  } finally {
    await api.close();
  }
});

test("SIGINT is forwarded as well and a child that exits on it gives its own code", async () => {
  const api = await unixEcho("api");
  const port = await freePort();
  try {
    const script = 'process.on("SIGINT", () => process.exit(42)); console.log("ready"); setInterval(() => {}, 1000);';
    const run = runInit([`${port}=${api.path}`, "--", process.execPath, "-e", script]);
    await new Promise((resolve) => {
      const timer = setInterval(() => {
        if (run.output().includes("ready")) {
          clearInterval(timer);
          resolve();
        }
      }, 20);
    });
    run.child.kill("SIGINT");
    assert.equal((await run.done).code, 42);
  } finally {
    await api.close();
  }
});

test("a command that does not exist exits with 127 and a message", async () => {
  const api = await unixEcho("api");
  const port = await freePort();
  try {
    const result = await runInit([`${port}=${api.path}`, "--", "/nonexistent/command"]).done;
    assert.equal(result.code, 127);
    assert.match(result.stderr, /cage-init: \/nonexistent\/command/);
  } finally {
    await api.close();
  }
});

test("main: a missing separator, command or specification and a bad specification are usage errors", async () => {
  const lines = [];
  const stderr = { write: (text) => lines.push(text) };
  assert.equal(await main([], { stderr }), 2);
  assert.equal(await main(["80=/s"], { stderr }), 2);
  assert.equal(await main(["80=/s", "--"], { stderr }), 2);
  assert.equal(await main(["80=/s", "x", "cmd"], { stderr }), 2);
  assert.equal(await main(["bad", "--", "cmd"], { stderr }), 2);
  assert.match(lines.join(""), /Usage:/);
  assert.match(lines.join(""), /bad relay entry: bad/);
});
