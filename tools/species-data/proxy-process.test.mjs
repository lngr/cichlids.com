// Tests for the proxy child process: start, the three sockets, the end of the child, a failed start.
// The credential proxy answers a missing credential without a network request, the egress proxy
// refuses a private target, and the archive search service runs docker with a PATH that holds no
// docker, so no test reaches the internet or a container.
import { test } from "node:test";
import assert from "node:assert/strict";
import { fork } from "node:child_process";
import http from "node:http";
import { existsSync, mkdirSync, mkdtempSync, realpathSync, writeFileSync } from "node:fs";
import net from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { startProxyProcess } from "./proxy-process.mjs";

// A directory for the sockets and a login file that has expired. archiveEnv names an empty
// directory as the PATH, so that the archive search cannot start docker.
function setup() {
  const dir = realpathSync(mkdtempSync(join(tmpdir(), "proxy-process-")));
  const credentialsFile = join(dir, "credentials.json");
  writeFileSync(credentialsFile, JSON.stringify({
    claudeAiOauth: { accessToken: "dummy-access", expiresAt: Date.now() - 1000 },
  }));
  const empty = join(dir, "empty-path");
  mkdirSync(empty);
  return {
    dir, billing: "subscription", apiKeyFile: join(dir, "anthropic.key"), credentialsFile,
    apiSocket: join(dir, "api.sock"), egressSocket: join(dir, "egress.sock"),
    archiveSocket: join(dir, "archive.sock"), archiveEnv: { PATH: empty },
  };
}

const request = (socketPath, options) => new Promise((resolve, reject) => {
  const req = http.request({ socketPath, ...options }, (res) => {
    res.resume();
    res.on("end", () => resolve(res.statusCode));
  });
  req.on("connect", (res, socket) => {
    socket.destroy();
    resolve(res.statusCode);
  });
  req.on("error", reject);
  req.end();
});

// Sends one line to the archive search socket and resolves the answer until the connection closes.
const archiveAnswer = (socketPath, line) => new Promise((resolve, reject) => {
  const socket = net.connect(socketPath);
  let text = "";
  socket.on("data", (chunk) => { text += chunk; });
  socket.on("error", reject);
  socket.on("close", () => resolve(text));
  socket.write(`${line}\n`);
});

test("startProxyProcess: all three services answer on their sockets, close() ends the child and the sockets", async () => {
  const s = setup();
  const proxy = await startProxyProcess(s);
  let ended = false;
  proxy.died.then(() => { ended = true; });
  assert.equal(await request(s.apiSocket, { method: "POST", path: "/v1/messages" }), 401);
  assert.equal(await request(s.egressSocket, { method: "CONNECT", path: "127.0.0.1:443" }), 403);
  assert.match(await archiveAnswer(s.archiveSocket, '{"term":"x"}'), /^error: term must be a string of 2 to 100/);
  assert.equal(ended, false);
  await proxy.close();
  assert.equal(await proxy.died, "exit code 0");
  assert.ok(!existsSync(s.apiSocket));
  assert.ok(!existsSync(s.egressSocket));
  assert.ok(!existsSync(s.archiveSocket));
  await proxy.close();
});

test("startProxyProcess: the archive search answers a clear error line when docker is missing", async () => {
  const s = setup();
  const proxy = await startProxyProcess(s);
  assert.match(
    await archiveAnswer(s.archiveSocket, '{"term":"Altolamprologus","limit":3}'),
    /^error: the archive database is not available \(container stack-postgres-1 is not running or docker is not reachable\)\n$/,
  );
  await proxy.close();
});

test("startProxyProcess: the child has an empty environment, no inherited node options and no output streams", async () => {
  const s = setup();
  let seen = null;
  const forkImpl = (path, args, options) => {
    seen = options;
    return fork(path, args, options);
  };
  const proxy = await startProxyProcess({ ...s, forkImpl });
  await proxy.close();
  assert.deepEqual(seen.env, {});
  assert.deepEqual(seen.execArgv, []);
  assert.deepEqual(seen.stdio, ["ignore", "ignore", "ignore", "ipc"]);
});

test("startProxyProcess: died resolves with the signal when the child is killed", async () => {
  const s = setup();
  let child;
  const proxy = await startProxyProcess({ ...s, forkImpl: (...args) => (child = fork(...args)) });
  child.kill("SIGKILL");
  assert.equal(await proxy.died, "signal SIGKILL");
  await proxy.close();
});

test("startProxyProcess: a proxy that does not start rejects with its reason and leaves no child", async () => {
  const s = setup();
  let child;
  const forkImpl = (...args) => (child = fork(...args));
  await assert.rejects(
    startProxyProcess({ ...s, billing: "both", forkImpl }), /did not start: billing must be one of/,
  );
  const exited = child.exitCode !== null || child.signalCode !== null;
  await new Promise((done) => (exited ? done() : child.once("exit", done)));
});

test("startProxyProcess: a child that ends before it is ready or never answers rejects", async () => {
  const s = setup();
  const quiet = join(s.dir, "quiet.mjs");
  writeFileSync(quiet, "setInterval(() => {}, 1000);\n");
  const hanging = (options) => fork(quiet, [], options);
  await assert.rejects(
    startProxyProcess({
      ...s, forkImpl: (path, args, options) => hanging(options), readyTimeoutMs: 100,
    }),
    /did not start in time/,
  );
  const ending = join(s.dir, "ending.mjs");
  writeFileSync(ending, "process.exit(3);\n");
  await assert.rejects(
    startProxyProcess({ ...s, forkImpl: (path, args, options) => fork(ending, [], options) }),
    /ended before it was ready \(exit code 3\)/,
  );
});
