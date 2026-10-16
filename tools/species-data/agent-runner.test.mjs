// Tests for the agent runner. A fake spawn function stands in for the bwrap and codex binaries; the
// cage, the proxy and the audit run for real against temporary directories.
import { test } from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { PassThrough } from "node:stream";
import http from "node:http";
import net from "node:net";
import { fork } from "node:child_process";
import {
  existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, rmSync, statSync,
  symlinkSync, writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { fakeClaude, fakeHome, fakeRepo } from "./agent-test-support.mjs";
import {
  API_RELAY_PORT, EGRESS_RELAY_PORT, PLACEHOLDER_API_KEY, PLACEHOLDER_OAUTH_TOKEN, relaySpec,
} from "./agent-cage.mjs";
import {
  RUNNERS, billingError, buildCommand, codexEnv, defaultApiKeyFile, defaultCredentialsFile, managedSettingsProblem, classifyOutput, resolveModel, runAgent, runAttempts,
} from "./agent-runner.mjs";

const CODEX_FALLBACKS = ["gpt-6-sol", "gpt-6-sol"];

test("RUNNERS lists codex and claude", () => {
  assert.deepEqual(RUNNERS, ["codex", "claude"]);
});

test("resolveModel: codex rows with the lake switch", () => {
  for (const role of ["generator", "checker-b", "checker-c"]) {
    assert.deepEqual(resolveModel({ runner: "codex", role, lake: "Malawisee" }),
      { runner: "codex", model: "gpt-6-luna", effort: "high", fallbacks: CODEX_FALLBACKS });
    assert.deepEqual(resolveModel({ runner: "codex", role, lake: "" }),
      { runner: "codex", model: "gpt-6.1-sol", effort: "medium", fallbacks: CODEX_FALLBACKS });
  }
  for (const lake of ["", "Tanganjikasee"]) {
    assert.deepEqual(resolveModel({ runner: "codex", role: "tiebreaker-3", lake }),
      { runner: "codex", model: "gpt-6.1-sol", effort: "medium", fallbacks: CODEX_FALLBACKS });
    assert.deepEqual(resolveModel({ runner: "codex", role: "adjudicator", lake }),
      { runner: "codex", model: "gpt-6.1-sol", effort: "high", fallbacks: CODEX_FALLBACKS });
    assert.deepEqual(resolveModel({ runner: "codex", role: "groupsync", lake }),
      { runner: "codex", model: "gpt-6-luna", effort: "high", fallbacks: CODEX_FALLBACKS });
  }
});

test("resolveModel: claude rows", () => {
  const sonnet = { runner: "claude", model: "claude-sonnet-5-5", effort: "high", fallbacks: [] };
  const opus = { runner: "claude", model: "claude-opus-5-5", effort: "high", fallbacks: [] };
  for (const lake of ["", "Malawisee"]) {
    for (const role of ["generator", "checker-b", "tiebreaker-1", "tiebreaker-12", "groupsync"]) {
      assert.deepEqual(resolveModel({ runner: "claude", role, lake }), sonnet);
    }
    for (const role of ["checker-c", "adjudicator"]) {
      assert.deepEqual(resolveModel({ runner: "claude", role, lake }), opus);
    }
  }
});

test("resolveModel: checker-a is the Claude checker for every runner", () => {
  const expected = { runner: "claude", model: "claude-sonnet-5-5", effort: "high", fallbacks: [] };
  assert.deepEqual(resolveModel({ runner: "codex", role: "checker-a", lake: "" }), expected);
  assert.deepEqual(resolveModel({ runner: "codex", role: "checker-a", lake: "Malawisee" }), expected);
  assert.deepEqual(resolveModel({ runner: "claude", role: "checker-a", lake: "" }), expected);
});

test("resolveModel rejects unknown runners and roles", () => {
  assert.throws(() => resolveModel({ runner: "gemini", role: "generator", lake: "" }), /unknown runner/);
  assert.throws(() => resolveModel({ runner: "codex", role: "poet", lake: "" }), /unknown role/);
  assert.throws(() => resolveModel({ runner: "codex", role: "tiebreaker-x", lake: "" }), /unknown role/);
});

test("buildCommand: codex", () => {
  assert.deepEqual(buildCommand({ runner: "codex", model: "gpt-6.1-sol", effort: "medium", cwd: "/w", lastMessagePath: "/l/generator.last" }), {
    command: "codex",
    args: ["exec", "-m", "gpt-6.1-sol", "-c", "model_reasoning_effort=medium", "-s", "workspace-write",
      "-c", "sandbox_workspace_write.network_access=true", "-c", "web_search=live", "--ephemeral",
      "--color", "never", "-o", "/l/generator.last", "-"],
  });
});
test("buildCommand: claude has no --settings and passes the tool list as one argument", () => {
  assert.deepEqual(buildCommand({ runner: "claude", model: "claude-opus-5-5", effort: "high", lastMessagePath: "/l/x.last" }), {
    command: "claude",
    args: ["-p", "--model", "claude-opus-5-5", "--effort", "high", "--permission-mode", "bypassPermissions",
      "--tools=Bash,Read,Write,Edit,Glob,Grep,WebSearch", "--strict-mcp-config", "--no-session-persistence",
      "--output-format", "json", "--setting-sources", ""],
  });
  assert.throws(() => buildCommand({ runner: "gemini", model: "m", effort: "high" }), /unknown runner/);
});

test("defaultApiKeyFile: the env override or the file in the config directory", () => {
  assert.equal(defaultApiKeyFile({ CICHLIDS_ANTHROPIC_KEY_FILE: "/x/key" }), "/x/key");
  assert.match(defaultApiKeyFile({}), /\/\.config\/cichlids\/anthropic\.key$/);
});

test("defaultCredentialsFile: the login file of claude below the home directory", () => {
  assert.equal(defaultCredentialsFile("/home/op"), "/home/op/.claude/.credentials.json");
});

const login = (expiresAt) => JSON.stringify({ claudeAiOauth: { accessToken: "dummy-access", refreshToken: "dummy-refresh", expiresAt } });

test("billingError: missing, invalid, api-key without a key file and subscription without a login", () => {
  const dir = mkdtempSync(join(tmpdir(), "agent-runner-billing-"));
  const apiKeyFile = join(dir, "anthropic.key");
  const credentialsFile = join(dir, "credentials.json");
  const options = { apiKeyFile, credentialsFile, managedFile: join(dir, "absent.json") };
  assert.match(billingError(undefined, options), /^missing --billing/);
  assert.match(billingError("both", options), /^--billing must be one of: api-key, subscription/);
  assert.match(billingError("api-key", options), new RegExp(`needs a usable key file: ${apiKeyFile} does not exist`));
  assert.match(billingError("subscription", options), new RegExp(`needs a usable claude login: ${credentialsFile} does not exist`));
  writeFileSync(apiKeyFile, "dummy-key");
  assert.equal(billingError("api-key", options), null);
  // The login file is not consulted under api-key, and the key file not under subscription.
  writeFileSync(credentialsFile, login(Date.now() - 1000));
  assert.match(billingError("subscription", options), /has an expired access token/);
  writeFileSync(credentialsFile, login(Date.now() + 3_600_000));
  assert.equal(billingError("subscription", options), null);
});

test("managedSettingsProblem and billingError refuse managed settings that select credentials", () => {
  const dir = mkdtempSync(join(tmpdir(), "agent-managed-"));
  const file = (name, content) => {
    const path = join(dir, name);
    writeFileSync(path, typeof content === "string" ? content : JSON.stringify(content));
    return path;
  };
  assert.equal(managedSettingsProblem("subscription", join(dir, "absent.json")), null);
  assert.equal(managedSettingsProblem("subscription", file("bad.json", "{not json")), "is not valid JSON");
  assert.equal(managedSettingsProblem("api-key", file("array.json", [])), "is not a JSON object");
  assert.match(managedSettingsProblem("subscription", dir), /^cannot be read \(EISDIR\)$/);
  assert.equal(managedSettingsProblem("subscription", file("plain.json", { permissions: {}, env: { OTHER: "1" } })), null);
  assert.equal(managedSettingsProblem("subscription", file("helper.json", { apiKeyHelper: "echo x" })), "sets apiKeyHelper");
  assert.equal(managedSettingsProblem("api-key", file("key.json", { env: { ANTHROPIC_API_KEY: "x" } })), "sets ANTHROPIC_API_KEY in env");
  assert.equal(managedSettingsProblem("subscription", file("url.json", { env: { ANTHROPIC_BASE_URL: "u" } })), "sets ANTHROPIC_BASE_URL in env");
  const oauth = file("oauth.json", { env: { CLAUDE_CODE_OAUTH_TOKEN: "t" } });
  assert.equal(managedSettingsProblem("subscription", oauth), null);
  assert.equal(managedSettingsProblem("api-key", oauth), "sets CLAUDE_CODE_OAUTH_TOKEN in env");
  const apiKeyFile = file("anthropic.key", "dummy-key");
  const credentialsFile = file("credentials.json", login(Date.now() + 3_600_000));
  const check = (billing, managedFile) => billingError(billing, { apiKeyFile, credentialsFile, managedFile });
  assert.match(check("subscription", file("h2.json", { apiKeyHelper: "x" })), /managed settings .*h2\.json sets apiKeyHelper/);
  assert.match(check("api-key", file("h3.json", { env: { ANTHROPIC_AUTH_TOKEN: "x" } })), /sets ANTHROPIC_AUTH_TOKEN in env/);
  assert.equal(check("api-key", join(dir, "absent.json")), null);
});

test("classifyOutput: codex", () => {
  const c = (log, lastMessage, exitCode = 0) => classifyOutput({ runner: "codex", log, lastMessage, exitCode });
  assert.equal(c("fine", "done"), "ok");
  assert.equal(c("You are out of credits", ""), "out-of-credits");
  assert.equal(c("model is at capacity", ""), "retry");
  assert.equal(c("flagged for possible misuse", "", 1), "retry");
  assert.equal(c("model is at capacity", "done"), "ok");
  assert.equal(c("out of credits", "done", 1), "out-of-credits");
  assert.equal(c("something odd", ""), "ok");
});

test("classifyOutput: claude matches log and last message", () => {
  const c = (log, lastMessage, exitCode = 0) => classifyOutput({ runner: "claude", log, lastMessage, exitCode });
  assert.equal(c("", "answer"), "ok");
  assert.equal(c("", "Claude usage limit reached", 1), "out-of-credits");
  assert.equal(c("Credit balance is too low", ""), "out-of-credits");
  assert.equal(c("", "You are out of extra usage", 1), "out-of-credits");
  assert.equal(c("API Error: 529 overloaded", "", 1), "retry");
  assert.equal(c("", "ECONNRESET", 1), "retry");
  assert.equal(c("request timed out", ""), "retry");
  assert.equal(c("", "plain failure", 1), "ok");
  assert.equal(c("usage limit", "answer"), "ok");
  assert.equal(c("HTTP 503 Service Unavailable", "", 1), "retry");
  assert.equal(c("", "wrote 15290 bytes to tmp-checker-a/page.html", 1), "ok");
  assert.equal(c("", "see doi 10.5032/x", 1), "ok");
});

test("classifyOutput: claude auth-failed", () => {
  const c = (log, lastMessage, exitCode = 1, apiErrorStatus = null) =>
    classifyOutput({ runner: "claude", log, lastMessage, exitCode, apiErrorStatus });
  assert.equal(c("", "Invalid API key \u00b7 Fix external API key"), "auth-failed");
  assert.equal(c("API Error: 401 invalid x-api-key", ""), "auth-failed");
  assert.equal(c("", "", 1, 401), "auth-failed");
  assert.equal(c("", "x", 1, 403), "auth-failed");
  assert.equal(c("authentication_error", ""), "auth-failed");
  assert.equal(c("", "x", 1, 500), "ok");
  assert.equal(c("", "answer", 0, 401), "ok");
  assert.equal(c("", "Invalid API key", 0), "ok");
  assert.equal(c("usage limit reached", "", 1, 401), "out-of-credits");
  assert.equal(classifyOutput({ runner: "codex", log: "Invalid API key", lastMessage: "", exitCode: 1, apiErrorStatus: 401 }), "ok");
});
// Fake spawn: each call takes the next scripted behaviour { stdout, stderr, code, last, never, act }.
// act(call) runs before the output, like a command of the agent that changes files.
function makeFake(script) {
  const calls = [];
  const spawnImpl = (command, args, options) => {
    const step = script[calls.length] ?? script[script.length - 1];
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    let input = "";
    child.stdin.on("data", (d) => { input += d; });
    const call = { command, args, options, killed: null, signals: [], input: () => input };
    calls.push(call);
    // A child closes on the first signal, or with ignore: ["SIGTERM"] only on a later one.
    child.kill = (signal) => {
      call.killed = signal;
      call.signals.push(signal);
      if (!(step.ignore ?? []).includes(signal)) setImmediate(() => child.emit("close", null, signal));
    };
    if (!step.never) {
      setImmediate(() => {
        step.act?.(call);
        if (step.last !== undefined) writeFileSync(args[args.indexOf("-o") + 1], step.last);
        if (step.stdout) child.stdout.write(step.stdout);
        for (const chunk of step.stdoutChunks ?? []) child.stdout.write(chunk);
        if (step.stderr) child.stderr.write(step.stderr);
        setImmediate(() => child.emit("close", step.code ?? 0));
      });
    }
    return child;
  };
  return { spawnImpl, calls };
}

// The JSON object that claude prints with --output-format json.
const json = (fields) => JSON.stringify({ is_error: false, result: "", total_cost_usd: 0, ...fields });

// The bwrap features that the tests inject, so that no test starts bwrap.
const FEATURES = { disableUserns: true, size: true };

// No test depends on the git state of the machine.
const noGit = () => {
  throw new Error("not a git repository");
};

// A repository tree, a fake claude on the PATH and an empty home directory.
function setup() {
  const dir = fakeRepo();
  const claude = fakeClaude();
  const home = fakeHome();
  const promptPath = join(dir, "prompt.txt");
  writeFileSync(promptPath, "the prompt");
  const sleeps = [];
  // The key file does not exist unless a test writes it, so no test reads the real key file.
  const keyFile = join(dir, "anthropic.key");
  const credentialsFile = join(dir, "credentials.json");
  // The per-call socket directories of the runs of this test go here.
  const tmp = mkdtempSync(join(tmpdir(), "agent-runner-tmp-"));
  return {
    dir, promptPath, keyFile, credentialsFile, home, claude, tmp, logDir: join(dir, "logs"), sleeps,
    sleep: async (ms) => { sleeps.push(ms); },
    env: (extra = {}) => ({ PATH: claude.dir, ...extra }),
  };
}

const base = (s, extra) => ({
  role: "generator", lake: "", promptPath: s.promptPath, logDir: s.logDir, cwd: s.dir, sleep: s.sleep, apiKeyFile: s.keyFile,
  credentialsFile: s.credentialsFile, home: s.home, env: s.env(), gitExec: noGit, features: FEATURES, tmp: s.tmp,
  billing: "subscription", ...extra,
});

// The proxy environment of the cage: the relays on the loopback interface of the cage and the
// archive search socket.
const egress = `http://127.0.0.1:${EGRESS_RELAY_PORT}`;
const PROXY_ENV = {
  ANTHROPIC_BASE_URL: `http://127.0.0.1:${API_RELAY_PORT}`, CICHLIDS_ARCHIVE_SEARCH_SOCKET: "/run/cage/archive.sock",
  HTTPS_PROXY: egress, HTTP_PROXY: egress, https_proxy: egress, http_proxy: egress,
  NO_PROXY: "127.0.0.1,localhost", no_proxy: "127.0.0.1,localhost", NODE_USE_ENV_PROXY: "1",
};

// The command that runs inside the cage: everything from the claude binary on.
const innerOf = (s, call) => call.args.slice(call.args.lastIndexOf(s.claude.bin));

test("runAgent codex: success writes log and passes the prompt on stdin", async () => {
  const s = setup();
  const fake = makeFake([{ last: "finished", stdout: "out\n", stderr: "err\n" }]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 1, costUsd: 0, billing: null });
  assert.equal(fake.calls[0].command, "codex");
  assert.equal(fake.calls[0].options.cwd, s.dir);
  assert.equal(fake.calls[0].input(), "the prompt");
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), "finished");
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  assert.match(log, /out/);
  assert.match(log, /err/);
});

test("runAgent claude: the JSON result becomes the last message, stderr the log", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }), stderr: "warn" }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription" });
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), "answer");
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /^=== attempt 1 claude-sonnet-5-5 \S+ billing subscription\nwarn\n=== exit 0\n=== cost 0 USD\n$/);
});

test("runAgent claude: stdout that is not JSON gives an empty last message and stays in the log", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: "plain answer", stderr: "warn" }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(result.status, "ok");
  assert.equal(result.costUsd, 0);
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), "");
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  assert.match(log, /plain answer/);
  assert.match(log, /warn/);
  assert.doesNotMatch(log, /=== cost/);
});

test("runAgent claude: the command is bwrap with the cage arguments and the claude argv", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  const call = fake.calls[0];
  assert.equal(call.command, "bwrap");
  assert.deepEqual(call.args.slice(0, 3), ["--ro-bind", "/", "/"]);
  assert.deepEqual(call.args.slice(call.args.indexOf("--chdir"), call.args.indexOf("--chdir") + 2), ["--chdir", s.dir]);
  assert.deepEqual(innerOf(s, call), [s.claude.bin, ...buildCommand({ runner: "claude", model: "claude-sonnet-5-5", effort: "high" }).args]);
  assert.ok(!call.args.includes("--settings"));
  // The cage has its own network namespace and starts claude through the init program.
  assert.ok(call.args.includes("--unshare-net"));
  const init = call.args.indexOf(join(s.dir, "tools", "species-data", "cage-init.mjs"));
  assert.deepEqual(call.args.slice(init - 1, init + 3), [realpathSync(process.execPath), join(s.dir, "tools", "species-data", "cage-init.mjs"), relaySpec(), "--"]);
  const bind = call.args.indexOf("/run/cage");
  assert.equal(call.args[bind - 2], "--ro-bind");
  assert.ok(call.args[bind - 1].startsWith(join(s.tmp, "cage-")));
  assert.equal(call.options.cwd, s.dir);
  assert.equal(call.input(), "the prompt");
});

test("runAgent claude: api-key billing gives the cage a placeholder key and never the real one", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-secret-key\n");
  const fake = makeFake([{ stdout: json({ result: "answer", total_cost_usd: 0.0037 }) }]);
  const env = s.env({ ANTHROPIC_API_KEY: "env-key", ANTHROPIC_AUTH_TOKEN: "env-token", CLAUDE_CODE_OAUTH_TOKEN: "oauth", GITHUB_TOKEN: "g", LANG: "C.UTF-8" });
  const at = new Date("2026-10-16T12:00:00.000Z");
  const result = await runAgent(base(s, { runner: "claude", billing: "api-key", spawnImpl: fake.spawnImpl, env, now: () => at }));
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0.0037, billing: "api-key" });
  const childEnv = fake.calls[0].options.env;
  assert.deepEqual(childEnv, {
    PATH: s.claude.dir, LANG: "C.UTF-8", HOME: s.home, CLAUDE_CONFIG_DIR: join(s.home, ".claude"),
    ...PROXY_ENV, ANTHROPIC_API_KEY: PLACEHOLDER_API_KEY,
  });
  assert.ok(!JSON.stringify(fake.calls).includes("dummy-secret-key"));
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  assert.equal(log, "=== attempt 1 claude-sonnet-5-5 2026-10-16T12:00:00.000Z billing api-key\n=== exit 0\n=== cost 0.0037 USD\n");
});

test("runAgent claude: subscription billing gives the cage a placeholder token and ignores a key file", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-secret-key\n");
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const env = s.env({ ANTHROPIC_API_KEY: "env-key", ANTHROPIC_BASE_URL: "u", CLAUDE_CODE_OAUTH_TOKEN: "oauth", TERM: "xterm", TZ: "UTC" });
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, env }));
  assert.equal(result.billing, "subscription");
  const childEnv = fake.calls[0].options.env;
  assert.deepEqual(childEnv, {
    PATH: s.claude.dir, TERM: "xterm", TZ: "UTC", HOME: s.home, CLAUDE_CONFIG_DIR: join(s.home, ".claude"),
    ...PROXY_ENV, CLAUDE_CODE_OAUTH_TOKEN: PLACEHOLDER_OAUTH_TOKEN,
  });
  assert.equal(childEnv.ANTHROPIC_BASE_URL, `http://127.0.0.1:${API_RELAY_PORT}`);
});

test("runAgent: a codex child gets the scrubbed environment and the codex command", async () => {
  const s = setup();
  const env = { PATH: "/p", GITHUB_TOKEN: "g", CLAUDECODE: "1" };
  const fake = makeFake([{ last: "answer" }]);
  await runAgent(base(s, { runner: "codex", env, spawnImpl: fake.spawnImpl }));
  assert.deepEqual(fake.calls[0].options.env, { PATH: "/p" });
  assert.equal(fake.calls[0].command, "codex");
});

// The per-call socket directory that a fake spawn finds in the bwrap arguments.
const runDirOf = (args) => args[args.indexOf("/run/cage") - 1];

// Sends a request over a unix socket. Resolves { status, body }.
const viaSocket = (socketPath, { method = "POST", path = "/v1/messages" } = {}) => new Promise((resolve, reject) => {
  const req = http.request({ socketPath, method, path }, (res) => {
    let body = "";
    res.on("data", (c) => (body += c));
    res.on("end", () => resolve({ status: res.statusCode, body }));
  });
  req.on("error", reject);
  req.end(method === "POST" ? "{}" : "");
});

test("runAgent claude: both proxies and the archive service answer on sockets in a private directory while the child runs, and are gone afterwards", async () => {
  const s = setup();
  writeFileSync(s.credentialsFile, login(Date.now() - 1000));
  let seen = null;
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const spawnImpl = (...args) => {
    const runDir = runDirOf(args[1]);
    seen = { runDir, mode: statSync(runDir).mode & 0o777, files: readdirSync(runDir).sort() };
    // The request of the agent reaches the credential proxy, which answers an expired login with 401.
    seen.answer = viaSocket(join(runDir, "api.sock"));
    // The egress proxy refuses a private target.
    seen.refused = new Promise((resolve, reject) => {
      const req = http.request({ socketPath: join(runDir, "egress.sock"), method: "CONNECT", path: "127.0.0.1:443" });
      req.on("connect", (res) => resolve(res.statusCode));
      req.on("error", reject);
      req.end();
    });
    // The archive service answers an invalid request with an error line, without a query.
    seen.archive = new Promise((resolve, reject) => {
      const socket = net.connect(join(runDir, "archive.sock"));
      let text = "";
      socket.on("data", (chunk) => { text += chunk; });
      socket.on("error", reject);
      socket.on("close", () => resolve(text));
      socket.write('{"term":"a"}\n');
    });
    return fake.spawnImpl(...args);
  };
  await runAgent(base(s, { runner: "claude", spawnImpl }));
  assert.equal(seen.mode, 0o700);
  assert.deepEqual(seen.files, ["api.sock", "archive.sock", "egress.sock"]);
  const answer = await seen.answer;
  assert.equal(answer.status, 401);
  assert.deepEqual(JSON.parse(answer.body), { type: "error", error: { type: "authentication_error", message: "subscription login expired" } });
  assert.equal(await seen.refused, 403);
  assert.match(await seen.archive, /^error: term must be a string of 2 to 100 characters/);
  assert.ok(!existsSync(seen.runDir));
  assert.deepEqual(readdirSync(s.tmp), []);
});

test("runAgent claude: the socket directory is removed and the proxies stop also when the run throws", async () => {
  const s = setup();
  const spawnImpl = () => {
    throw new Error("spawn exploded");
  };
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl })), /spawn exploded/);
  assert.deepEqual(readdirSync(s.tmp), []);
});

test("runAgent: a codex run starts no proxy and creates no socket directory", async () => {
  const s = setup();
  const fake = makeFake([{ last: "done" }]);
  await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(readdirSync(s.tmp), []);
  assert.ok(!fake.calls[0].args.includes("--unshare-net"));
});

test("runAgent: a claude role without a valid billing throws, a codex role does not need one", async () => {
  const s = setup();
  const fake = makeFake([{ last: "done" }]);
  for (const billing of [undefined, "both", null]) {
    await assert.rejects(runAgent(base(s, { runner: "claude", billing, spawnImpl: fake.spawnImpl })), /billing must be one of/);
    await assert.rejects(runAgent(base(s, { runner: "codex", role: "checker-a", billing, spawnImpl: fake.spawnImpl })), /billing must be one of/);
  }
  assert.equal(fake.calls.length, 0);
  const codex = await runAgent(base(s, { runner: "codex", billing: undefined, spawnImpl: fake.spawnImpl }));
  assert.equal(codex.status, "ok");
  assert.equal(codex.billing, null);
});

test("runAgent: api-key billing without a usable key file throws before any agent starts", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await assert.rejects(runAgent(base(s, { runner: "claude", billing: "api-key", spawnImpl: fake.spawnImpl })), /usable key file/);
  assert.equal(fake.calls.length, 0);
});

test("runAgent: the key file defaults to CICHLIDS_ANTHROPIC_KEY_FILE of the env", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-key");
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const { apiKeyFile, ...options } = base(s, { runner: "claude", billing: "api-key", spawnImpl: fake.spawnImpl, env: s.env({ CICHLIDS_ANTHROPIC_KEY_FILE: s.keyFile }) });
  assert.equal((await runAgent(options)).billing, "api-key");
});

test("runAgent claude: the cost is summed over the attempts", async () => {
  const s = setup();
  const fake = makeFake([
    { stdout: json({ is_error: true, result: "API Error: 529 overloaded", total_cost_usd: 0.25 }), code: 1 },
    { stdout: json({ result: "answer", total_cost_usd: 0.5 }) },
  ]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, retryDelayMs: 5 }));
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 2, costUsd: 0.75, billing: "subscription" });
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  assert.deepEqual(log.match(/^=== cost .*$/gm), ["=== cost 0.25 USD", "=== cost 0.5 USD"]);
});

test("runAgent claude: a rejected key ends the run after one attempt", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-key");
  const fake = makeFake([{ stdout: json({ is_error: true, api_error_status: 401, result: "Invalid API key \u00b7 Fix external API key" }), code: 1 }]);
  const result = await runAgent(base(s, { runner: "claude", billing: "api-key", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "auth-failed", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "api-key" });
  assert.equal(fake.calls.length, 1);
  assert.deepEqual(s.sleeps, []);
});

test("runAgent claude: a 401 without a known text is auth-failed through the JSON status", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ is_error: true, api_error_status: 401, result: "denied" }), code: 1 }]);
  assert.equal((await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }))).status, "auth-failed");
});

test("runAgent claude: out-of-credits is read from the JSON result", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ is_error: true, result: "Claude usage limit reached", total_cost_usd: 0 }), code: 1 }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(result.status, "out-of-credits");
  assert.equal(fake.calls.length, 1);
});

test("runAgent claude: a multibyte character split across two chunks stays intact", async () => {
  const s = setup();
  const text = "Cichliden: K\u00e4rpergr\u00f6\u00dfe \u2603";
  const bytes = Buffer.from(json({ result: text }), "utf8");
  const cut = bytes.indexOf(0xc3) + 1;
  const fake = makeFake([{ stdoutChunks: [bytes.subarray(0, cut), bytes.subarray(cut)] }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), text);
});

test("runAgent: a transient failure is retried and the second attempt succeeds", async () => {
  const s = setup();
  const fake = makeFake([{ stderr: "model is at capacity", code: 1 }, { last: "done" }]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, retryDelayMs: 5 }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 2, costUsd: 0, billing: null });
  assert.deepEqual(s.sleeps, [5]);
  assert.equal(fake.calls.length, 2);
});

test("runAgent codex: falls back to gpt-6-sol after three transient failures", async () => {
  const s = setup();
  const fake = makeFake([
    { stderr: "at capacity", code: 1 }, { stderr: "at capacity", code: 1 }, { stderr: "at capacity", code: 1 },
    { last: "done" },
  ]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6-sol", attempts: 4, costUsd: 0, billing: null });
  const models = fake.calls.map((c) => c.args[c.args.indexOf("-m") + 1]);
  assert.deepEqual(models, ["gpt-6.1-sol", "gpt-6.1-sol", "gpt-6.1-sol", "gpt-6-sol"]);
});

test("runAgent: all attempts transient gives failed", async () => {
  const s = setup();
  const codex = makeFake([{ stderr: "at capacity", code: 1 }]);
  assert.deepEqual(await runAgent(base(s, { runner: "codex", spawnImpl: codex.spawnImpl })),
    { status: "failed", model: "gpt-6-sol", attempts: 5, costUsd: 0, billing: null });
  assert.equal(s.sleeps.length, 4);
  const claude = makeFake([{ stdout: json({ is_error: true, result: "API Error: overloaded" }), code: 1 }]);
  assert.deepEqual(await runAgent(base(s, { runner: "claude", spawnImpl: claude.spawnImpl })),
    { status: "failed", model: "claude-sonnet-5-5", attempts: 3, costUsd: 0, billing: "subscription" });
});

test("runAgent: out-of-credits stops at once", async () => {
  const s = setup();
  const fake = makeFake([{ stderr: "You are out of credits", code: 1 }, { last: "never reached" }]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "out-of-credits", model: "gpt-6.1-sol", attempts: 1, costUsd: 0, billing: null });
  assert.equal(fake.calls.length, 1);
  assert.deepEqual(s.sleeps, []);
});

test("runAgent: timeout ends the child with SIGTERM", async () => {
  const s = setup();
  const fake = makeFake([{ never: true }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, timeoutMs: 20, killGraceMs: 1000 }));
  assert.deepEqual(fake.calls[0].signals, ["SIGTERM"]);
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription" });
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /\n=== exit -1 signal SIGTERM timeout after 20 ms\n$/);
});

test("runAgent: a child that ignores SIGTERM gets SIGKILL after the grace period", async () => {
  const s = setup();
  const fake = makeFake([{ never: true, ignore: ["SIGTERM"] }]);
  const started = Date.now();
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, timeoutMs: 20, killGraceMs: 30 }));
  // Timers fire with millisecond rounding, so the bound sits below timeout plus grace (50 ms).
  assert.ok(Date.now() - started >= 40);
  assert.deepEqual(fake.calls[0].signals, ["SIGTERM", "SIGKILL"]);
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 1, costUsd: 0, billing: null });
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /\n=== exit -1 signal SIGKILL timeout after 20 ms\n$/);
});

test("runAgent: a child whose streams stay open after SIGKILL ends after one more grace period", async () => {
  const s = setup();
  const fake = makeFake([{ never: true, ignore: ["SIGTERM", "SIGKILL"] }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, timeoutMs: 10, killGraceMs: 10 }));
  assert.deepEqual(fake.calls[0].signals, ["SIGTERM", "SIGKILL"]);
  assert.equal(result.attempts, 1);
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /=== exit -1 signal SIGKILL timeout after 10 ms\n$/);
});

test("runAgent: a cage that cannot start ends a claude run at once as failed", async () => {
  const s = setup();
  const spawnImpl = () => {
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.kill = () => {};
    setImmediate(() => child.emit("error", new Error("spawn bwrap ENOENT")));
    return child;
  };
  const result = await runAgent(base(s, { runner: "claude", spawnImpl }));
  assert.deepEqual(result, { status: "failed", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription" });
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /ENOENT/);
  // bwrap that starts and fails, for example without user namespaces, is the same.
  const fake = makeFake([{ stderr: "bwrap: No permissions to create new namespace\n", code: 1 }]);
  const failed = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(failed.status, "failed");
  assert.equal(fake.calls.length, 1);
  assert.deepEqual(s.sleeps, []);
  // The init program of the cage that cannot start claude or open its relays is the same.
  for (const [stderr, code] of [["cage-init: claude: spawn claude ENOENT\n", 127], ["cage-init: listen EADDRINUSE\n", 2]]) {
    const init = makeFake([{ stderr, code }]);
    assert.equal((await runAgent(base(s, { runner: "claude", spawnImpl: init.spawnImpl }))).status, "failed");
    assert.equal(init.calls.length, 1);
  }
  assert.deepEqual(s.sleeps, []);
});

test("runAgent: a codex spawn error does not throw", async () => {
  const s = setup();
  const spawnImpl = () => {
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.kill = () => {};
    setImmediate(() => child.emit("error", new Error("spawn codex ENOENT")));
    return child;
  };
  const result = await runAgent(base(s, { runner: "codex", spawnImpl }));
  assert.equal(result.status, "ok");
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /ENOENT/);
});

test("runAgent: checker-a in a Codex run runs claude in the cage and uses the claude output split", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-key");
  const fake = makeFake([{ stdout: json({ result: "answer" }), stderr: "warn" }]);
  const result = await runAgent(base(s, { runner: "codex", role: "checker-a", billing: "api-key", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "api-key" });
  assert.equal(fake.calls[0].command, "bwrap");
  const inner = innerOf(s, fake.calls[0]);
  assert.equal(inner[inner.indexOf("--model") + 1], "claude-sonnet-5-5");
  assert.equal(readFileSync(join(s.logDir, "checker-a.last"), "utf8"), "answer");
  assert.match(readFileSync(join(s.logDir, "checker-a.log"), "utf8"), /^=== attempt 1 claude-sonnet-5-5 \S+ billing api-key\nwarn\n=== exit 0\n=== cost 0 USD\n$/);
});

test("runAgent: the claude child env defaults to the process env through the allowlist", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const { env, ...options } = base(s, { runner: "claude", spawnImpl: fake.spawnImpl });
  const saved = process.env.PATH;
  process.env.PATH = s.claude.dir;
  try {
    await runAgent(options);
  } finally {
    process.env.PATH = saved;
  }
  const childEnv = fake.calls[0].options.env;
  assert.equal(childEnv.PATH, s.claude.dir);
  assert.deepEqual(Object.keys(childEnv).filter((k) => !["PATH", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "TZ"].includes(k)).sort(),
    ["ANTHROPIC_BASE_URL", "CICHLIDS_ARCHIVE_SEARCH_SOCKET", "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CONFIG_DIR", "HOME", "HTTPS_PROXY", "HTTP_PROXY", "NODE_USE_ENV_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy"].sort());
});

test("runAgent: every attempt is appended to the log and the classification reads the current attempt", async () => {
  const s = setup();
  // The first attempt is transient; the second finishes with exit 1 and no known pattern, which
  // is "ok" only when the transient text of the first attempt is left out of the classification.
  const fake = makeFake([{ stderr: "model is at capacity", code: 1 }, { stderr: "plain failure", code: 1 }]);
  const at = new Date("2026-10-16T08:00:00.000Z");
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, now: () => at }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 2, costUsd: 0, billing: null });
  assert.equal(readFileSync(join(s.logDir, "generator.log"), "utf8"),
    "=== attempt 1 gpt-6.1-sol 2026-10-16T08:00:00.000Z\nmodel is at capacity\n=== exit 1\n" +
    "=== attempt 2 gpt-6.1-sol 2026-10-16T08:00:00.000Z\nplain failure\n=== exit 1\n");
});

test("runAgent: an abort ends the running child and starts no further attempt", async () => {
  const s = setup();
  const fake = makeFake([{ never: true, ignore: ["SIGTERM"] }]);
  const controller = new AbortController();
  const running = runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, killGraceMs: 10, abortSignal: controller.signal }));
  await new Promise((done) => setTimeout(done, 5));
  controller.abort();
  assert.deepEqual(await running, { status: "aborted", model: "gpt-6.1-sol", attempts: 1, costUsd: 0, billing: null });
  assert.deepEqual(fake.calls[0].signals, ["SIGTERM", "SIGKILL"]);
  assert.equal(fake.calls.length, 1);
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /=== exit -1 signal SIGKILL\n$/);
});

test("runAgent: an abort during the retry wait starts no further attempt", async () => {
  const s = setup();
  const fake = makeFake([{ stderr: "at capacity", code: 1 }]);
  const controller = new AbortController();
  const sleep = async () => controller.abort();
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, sleep, abortSignal: controller.signal }));
  assert.deepEqual(result, { status: "aborted", model: "gpt-6.1-sol", attempts: 1, costUsd: 0, billing: null });
  assert.equal(fake.calls.length, 1);
});

test("runAgent: the default retry wait ends early on abort", async () => {
  const s = setup();
  const fake = makeFake([{ stderr: "at capacity", code: 1 }]);
  const controller = new AbortController();
  const started = Date.now();
  const running = runAgent({ ...base(s, { runner: "codex", spawnImpl: fake.spawnImpl, abortSignal: controller.signal }), sleep: undefined, retryDelayMs: 60_000 });
  setTimeout(() => controller.abort(), 20);
  assert.equal((await running).status, "aborted");
  assert.ok(Date.now() - started < 5_000);
});

test("runAttempts: an explicit model, effort and fallbacks drive the attempts and the log name", async () => {
  const s = setup();
  const fake = makeFake([
    { stdout: json({ is_error: true, result: "overloaded" }), code: 1 },
    { stdout: json({ is_error: true, result: "overloaded" }), code: 1 },
    { stdout: json({ is_error: true, result: "overloaded" }), code: 1 },
    { stdout: json({ result: "fine" }) },
  ]);
  const result = await runAttempts({
    runner: "claude", billing: "subscription", model: "claude-opus-5-5", effort: "low", fallbacks: ["claude-sonnet-5-5"], name: "job-1",
    promptPath: s.promptPath, logDir: s.logDir, cwd: s.dir, sleep: s.sleep, apiKeyFile: s.keyFile, spawnImpl: fake.spawnImpl,
    env: s.env(), home: s.home, gitExec: noGit,
  });
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 4, costUsd: 0, billing: "subscription" });
  assert.deepEqual(fake.calls.map((c) => c.args[c.args.indexOf("--model") + 1]), ["claude-opus-5-5", "claude-opus-5-5", "claude-opus-5-5", "claude-sonnet-5-5"]);
  assert.equal(fake.calls[0].args[fake.calls[0].args.indexOf("--effort") + 1], "low");
  assert.equal(readFileSync(join(s.logDir, "job-1.last"), "utf8"), "fine");
});

test("runAgent claude: is_error with exit 0 is unfinished", async () => {
  const cases = [
    ["Claude usage limit reached", "out-of-credits", 1],
    ["API Error: 529 overloaded", "failed", 3],
    ["Invalid API key", "auth-failed", 1],
    ["something unknown", "ok", 1],
  ];
  for (const [result, status, calls] of cases) {
    const s = setup();
    const fake = makeFake([{ stdout: json({ is_error: true, result }), code: 0 }]);
    const out = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
    assert.equal(out.status, status, result);
    assert.equal(fake.calls.length, calls, result);
    assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), "", result);
    assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), new RegExp(`=== result\\n${result}\\n`));
  }
});

test("runAgent claude: numbers in the JSON do not trigger a transient retry", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ is_error: true, result: "max turns", usage: { output_tokens: 529, input_tokens: 503 } }), code: 1 }]);
  const out = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(out.status, "ok");
  assert.equal(fake.calls.length, 1);
});

test("runAgent claude: stderr of a JSON run is classified", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ is_error: true, result: "" }), stderr: "ECONNRESET", code: 1 }]);
  const out = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, retryDelayMs: 1 }));
  assert.equal(out.status, "failed");
  assert.equal(fake.calls.length, 3);
});

test("the claude command never loads settings files", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  const args = fake.calls[0].args;
  assert.equal(args[args.indexOf("--setting-sources") + 1], "");
});

test("runAttempts: the cage binds the repository read-only and the write roots read-write", async () => {
  const s = setup();
  const extra = join(s.dir, "tools", "species-data");
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, extraWriteRoots: [extra] }));
  const { args } = fake.calls[0];
  const pairs = (flag) => args.flatMap((a, i) => (a === flag ? [[args[i + 1], args[i + 2]]] : []));
  assert.ok(pairs("--ro-bind").some(([a, b]) => a === s.dir && b === s.dir));
  assert.deepEqual(pairs("--bind").map(([a]) => a).filter((p) => p.startsWith(s.dir)), [join(s.dir, "data"), extra]);
});

test("runAttempts: a planted symlink out of the write roots fails the role as audit-failed", async () => {
  const s = setup();
  const planted = join(s.dir, "data", "species", "link");
  const fake = makeFake([{ stdout: json({ result: "answer" }), act: () => symlinkSync(s.home, planted) }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(result.status, "audit-failed");
  assert.equal(result.detail, `${planted} (symlink to ${s.home})`);
  assert.equal(fake.calls.length, 1);
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), new RegExp(`=== audit failed\\n${planted}`));
});

test("runAttempts: a planted .claude directory fails the role, a pre-existing one does not", async () => {
  const s = setup();
  mkdirSync(join(s.dir, "data", "species", "AGENTS.md"), { recursive: true });
  const planted = join(s.dir, "data", ".claude");
  const quiet = makeFake([{ stdout: json({ result: "answer" }) }]);
  assert.equal((await runAgent(base(s, { runner: "claude", spawnImpl: quiet.spawnImpl }))).status, "ok");
  const fake = makeFake([{ stdout: json({ result: "answer" }), act: () => mkdirSync(planted) }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "audit-failed", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription", detail: `${planted} (created)` });
  // The next role finds the entry in its snapshot and is not blamed for it.
  assert.equal((await runAgent(base(s, { runner: "claude", spawnImpl: quiet.spawnImpl }))).status, "ok");
});

test("runAttempts: the audit runs before the last message is written, which a failed audit leaves out", async () => {
  const s = setup();
  const planted = join(s.dir, "data", "link");
  const fake = makeFake([{ stdout: json({ result: "answer from a doubtful run" }), act: () => symlinkSync(s.home, planted) }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(result.status, "audit-failed");
  assert.ok(!existsSync(join(s.logDir, "generator.last")));
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  assert.match(log, /^=== attempt 1 .*\n=== exit 0\n=== cost 0 USD\n=== audit failed\n/);
});

test("runAttempts: a failed audit wins over a child that fails to start, and the run does not throw", async () => {
  const s = setup();
  const planted = join(s.dir, "data", "species", "escape");
  const spawnImpl = () => {
    symlinkSync(s.home, planted);
    throw new Error("spawn exploded");
  };
  const result = await runAgent(base(s, { runner: "claude", spawnImpl }));
  assert.deepEqual(result, { status: "audit-failed", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription", detail: `${planted} (symlink to ${s.home})` });
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /spawn exploded[\s\S]*=== audit failed/);
  assert.deepEqual(readdirSync(s.tmp), []);
});

test("runAttempts: a child that fails to start without an audit hit throws", async () => {
  const s = setup();
  const spawnImpl = () => {
    throw new Error("spawn exploded");
  };
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl })), /spawn exploded/);
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /spawn exploded/);
});

test("runAttempts: the audit runs after an abort, and its failure wins over aborted", async () => {
  const s = setup();
  const planted = join(s.dir, "data", ".claude");
  const controller = new AbortController();
  const fake = makeFake([{ never: true }]);
  let spawned;
  const started = new Promise((done) => { spawned = done; });
  const spawnImpl = (...args) => {
    const child = fake.spawnImpl(...args);
    // The agent plants a Claude Code directory and is then aborted.
    mkdirSync(planted);
    spawned();
    return child;
  };
  const running = runAgent(base(s, { runner: "claude", spawnImpl, killGraceMs: 10, abortSignal: controller.signal }));
  await started;
  controller.abort();
  const result = await running;
  assert.equal(result.status, "audit-failed");
  assert.equal(result.detail, `${planted} (created)`);
  assert.equal(fake.calls.length, 1);
});

test("runAttempts: the audit also decides when the run is an abort without a hit", async () => {
  const s = setup();
  const controller = new AbortController();
  const fake = makeFake([{ never: true }]);
  let spawned;
  const started = new Promise((done) => { spawned = done; });
  const spawnImpl = (...args) => {
    spawned();
    return fake.spawnImpl(...args);
  };
  const running = runAgent(base(s, { runner: "claude", spawnImpl, killGraceMs: 10, abortSignal: controller.signal }));
  await started;
  controller.abort();
  assert.equal((await running).status, "aborted");
});

test("runAttempts: a symlink out of the write roots that existed before is a warning, once per call, and does not fail", async () => {
  const s = setup();
  symlinkSync(s.home, join(s.dir, "data", "old-link"));
  const fake = makeFake([{ stdout: "overloaded", code: 1 }, { stdout: json({ result: "answer" }) }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(result.status, "ok");
  assert.equal(result.attempts, 2);
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  const lines = log.split("\n").filter((l) => l.startsWith("=== audit warning"));
  assert.deepEqual(lines, [`=== audit warning ${join(s.dir, "data", "old-link")} (symlink to ${s.home})`]);
  // A retargeted link fails.
  const retarget = () => {
    rmSync(join(s.dir, "data", "old-link"));
    symlinkSync(s.dir, join(s.dir, "data", "old-link"));
  };
  const again = makeFake([{ stdout: json({ result: "answer" }), act: retarget }]);
  assert.equal((await runAgent(base(s, { runner: "claude", spawnImpl: again.spawnImpl }))).status, "audit-failed");
});

test("runAttempts: the source archive root is audited", async () => {
  const s = setup();
  const archive = realpathSync(mkdtempSync(join(tmpdir(), "agent-runner-archive-")));
  const fake = makeFake([{ stdout: json({ result: "answer" }), act: () => symlinkSync(s.home, join(archive, "escape")) }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, env: s.env({ CICHLIDS_SOURCE_ARCHIVE: archive }) }));
  assert.equal(result.status, "audit-failed");
  assert.equal(result.detail, `${join(archive, "escape")} (symlink to ${s.home})`);
});

test("runAttempts: a source archive root inside the home directory fails before anything starts", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const env = s.env({ CICHLIDS_SOURCE_ARCHIVE: join(s.home, "archive") });
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, env })), /CICHLIDS_SOURCE_ARCHIVE .* inside the home directory/);
  assert.equal(fake.calls.length, 0);
  assert.deepEqual(readdirSync(s.tmp), []);
});

test("runAttempts: read directories are bound read-only and a read directory inside a write root fails", async () => {
  const s = setup();
  const briefs = realpathSync(mkdtempSync(join(tmpdir(), "agent-runner-briefs-")));
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, extraReadRoots: [briefs] }));
  const { args } = fake.calls[0];
  assert.deepEqual(args.slice(args.indexOf(briefs) - 1, args.indexOf(briefs) + 2), ["--ro-bind", briefs, briefs]);
  assert.ok(!args.some((a, i) => a === "--bind" && args[i + 1] === briefs));
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, extraReadRoots: [join(s.dir, "data")] })), /read directory .* lies inside the write root/);
});

test("runAttempts: the audit does not apply to a codex child", async () => {
  const s = setup();
  const fake = makeFake([{ last: "done", act: () => symlinkSync(s.home, join(s.dir, "data", "link")) }]);
  assert.equal((await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }))).status, "ok");
});

test("runAttempts: a retargeted work directory fails before anything starts", async () => {
  const s = setup();
  symlinkSync(s.home, join(s.dir, "data", "species", "work"));
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl })), /data\/species\/work resolves to .* it must be /);
  assert.equal(fake.calls.length, 0);
});

test("runAttempts: a forbidden write root, a missing claude and a cwd outside the roots fail before anything starts", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, extraWriteRoots: [s.home] })), /write root .* is not allowed/);
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, env: { PATH: "/nonexistent" } })), /claude is not on the PATH/);
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, root: s.dir, cwd: s.home })), /lies outside the repository/);
  assert.equal(fake.calls.length, 0);
});

test("runAttempts: the log and the last message are written with safe writes", async () => {
  const s = setup();
  mkdirSync(s.logDir);
  const elsewhere = join(s.dir, "elsewhere.txt");
  writeFileSync(elsewhere, "keep");
  symlinkSync(elsewhere, join(s.logDir, "generator.log"));
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl })), /refusing to write through the symlink/);
  assert.equal(readFileSync(elsewhere, "utf8"), "keep");
  assert.ok(existsSync(join(s.logDir, "generator.last")));
});

test("codexEnv drops the session variables, credential variables, agents, editor variables and the display", () => {
  const env = {
    PATH: "/bin", HOME: "/h", LANG: "C", CLAUDECODE: "1", CLAUDE_CODE_SESSION_ID: "s", CLAUDE_CODE_MESSAGING_TOKEN: "t",
    NuGetPackageSourceCredentials_feed: "u:p", GITHUB_TOKEN: "g", OPENAI_API_KEY: "o", AWS_SECRET_ACCESS_KEY: "a",
    DB_PASSWORD: "d", XAUTHORITY: "/h/.Xauthority", DISPLAY: ":0", SSH_AUTH_SOCK: "/s", GIT_ASKPASS: "/a", SSH_ASKPASS: "/b",
    GPG_AGENT_INFO: "/g", DBUS_SESSION_BUS_ADDRESS: "unix:path=/d", VSCODE_IPC_HOOK_CLI: "/v", VSCODE_GIT_IPC_HANDLE: "/w",
    VSCODE_INJECTION: "1", TERM_PROGRAM: "vscode",
  };
  assert.deepEqual(codexEnv(env), { PATH: "/bin", HOME: "/h", LANG: "C", TERM_PROGRAM: "vscode" });
});

test("runAttempts: audit hits and a refused log write give audit-failed with the hits in the detail, and no throw", async () => {
  const s = setup();
  const planted = join(s.dir, "data", "species", "profiles", "CLAUDE.md");
  const elsewhere = join(s.dir, "elsewhere.txt");
  writeFileSync(elsewhere, "keep");
  const act = () => {
    mkdirSync(join(s.dir, "data", "species", "profiles"), { recursive: true });
    writeFileSync(planted, "instructions");
    // The agent replaces the role log by a symlink, which a log write refuses.
    symlinkSync(elsewhere, join(s.logDir, "generator.log"));
  };
  const fake = makeFake([{ stdout: json({ result: "answer" }), act }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(result.status, "audit-failed");
  assert.ok(result.detail.includes(`${planted} (created)`), result.detail);
  assert.match(result.detail, /log write refused: refusing to write through the symlink/);
  assert.equal(readFileSync(elsewhere, "utf8"), "keep");
  assert.ok(!existsSync(join(s.logDir, "generator.last")));
  assert.deepEqual(readdirSync(s.tmp), []);
});

test("runAttempts: audit hits and a symlinked log directory give audit-failed", async () => {
  const s = setup();
  const planted = join(s.dir, "data", ".claude");
  const moved = join(s.dir, "data", "moved-logs");
  const act = () => {
    mkdirSync(planted);
    mkdirSync(moved);
    rmSync(s.logDir, { recursive: true });
    symlinkSync(moved, s.logDir);
  };
  const fake = makeFake([{ stdout: json({ result: "answer" }), act }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, logBase: s.dir }));
  assert.equal(result.status, "audit-failed");
  assert.ok(result.detail.includes(`${planted} (created)`), result.detail);
  assert.deepEqual(readdirSync(moved), []);
});

test("runAttempts: an error of the audit itself is a hit and does not throw", async () => {
  const s = setup();
  const auditImpl = () => {
    throw new Error("walk exploded");
  };
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, auditImpl }));
  assert.deepEqual(result, {
    status: "audit-failed", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription",
    detail: "audit error: walk exploded",
  });
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /=== audit failed\naudit error: walk exploded\n/);
});

// A fork that keeps the child of the proxy process, so that a test can end it.
function keptFork() {
  const kept = { child: null };
  kept.forkImpl = (...args) => {
    kept.child = fork(...args);
    return kept.child;
  };
  return kept;
}

test("runAttempts: a proxy process that ends during the attempt ends the child, fails the attempt and is audited", async () => {
  const s = setup();
  const kept = keptFork();
  const fake = makeFake([{ never: true }]);
  let spawned;
  const started = new Promise((done) => { spawned = done; });
  const spawnImpl = (...args) => {
    spawned();
    return fake.spawnImpl(...args);
  };
  const running = runAgent(base(s, { runner: "claude", spawnImpl, forkImpl: kept.forkImpl, killGraceMs: 10 }));
  await started;
  kept.child.kill("SIGKILL");
  const result = await running;
  assert.deepEqual(result, {
    status: "failed", model: "claude-sonnet-5-5", attempts: 1, costUsd: 0, billing: "subscription",
    detail: "the proxy process ended (signal SIGKILL)",
  });
  assert.equal(fake.calls.length, 1);
  assert.equal(fake.calls[0].killed, "SIGTERM");
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /=== proxy process ended \(signal SIGKILL\)\n$/);
  assert.deepEqual(readdirSync(s.tmp), []);
});

test("runAttempts: what the agent plants before the proxy process ends fails the audit", async () => {
  const s = setup();
  const kept = keptFork();
  const planted = join(s.dir, "data", "species", "CLAUDE.md");
  const fake = makeFake([{ never: true }]);
  let spawned;
  const started = new Promise((done) => { spawned = done; });
  const spawnImpl = (...args) => {
    spawned();
    return fake.spawnImpl(...args);
  };
  const running = runAgent(base(s, { runner: "claude", spawnImpl, forkImpl: kept.forkImpl, killGraceMs: 10 }));
  await started;
  writeFileSync(planted, "instructions");
  kept.child.kill("SIGKILL");
  const result = await running;
  assert.equal(result.status, "audit-failed");
  assert.equal(result.detail, `${planted} (created)`);
});

test("runAttempts: a proxy process that ended between two attempts starts no further child", async () => {
  const s = setup();
  const kept = keptFork();
  const fake = makeFake([{ stdout: "overloaded", code: 1, act: () => kept.child.kill("SIGKILL") }, { stdout: json({ result: "answer" }) }]);
  const sleep = async () => {
    // The end of the proxy process is noticed before the next attempt.
    await new Promise((done) => setTimeout(done, 50));
  };
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, forkImpl: kept.forkImpl, sleep }));
  assert.equal(result.status, "failed");
  assert.match(result.detail, /^the proxy process ended \(signal SIGKILL\)$/);
  assert.equal(fake.calls.length, 1);
});

test("runAttempts: a proxy process that cannot start throws before any child starts", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: json({ result: "answer" }) }]);
  const forkImpl = (path, args, options) => fork(path, args, { ...options, execArgv: ["--no-such-node-option"] });
  await assert.rejects(runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, forkImpl })), /proxy process ended before it was ready/);
  assert.equal(fake.calls.length, 0);
  assert.deepEqual(readdirSync(s.tmp), []);
});
