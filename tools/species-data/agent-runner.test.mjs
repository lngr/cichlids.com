// Tests for the agent runner. A fake spawn function stands in for the codex and claude binaries.
import { test } from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { PassThrough } from "node:stream";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { RUNNERS, buildCommand, classifyOutput, resolveModel, runAgent } from "./agent-runner.mjs";

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

test("buildCommand: claude", () => {
  assert.deepEqual(buildCommand({ runner: "claude", model: "claude-opus-5-5", effort: "high", cwd: "/w", lastMessagePath: "/l/x.last" }), {
    command: "claude",
    args: ["-p", "--model", "claude-opus-5-5", "--effort", "high", "--permission-mode", "bypassPermissions",
      "--tools", "Bash,Read,Write,Edit,Glob,Grep,WebSearch", "--strict-mcp-config", "--no-session-persistence",
      "--output-format", "text"],
  });
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

// Fake spawn: each call takes the next scripted behaviour { stdout, stderr, code, last, never }.
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

function setup() {
  const dir = mkdtempSync(join(tmpdir(), "agent-runner-"));
  const promptPath = join(dir, "prompt.txt");
  writeFileSync(promptPath, "the prompt");
  const sleeps = [];
  return { dir, promptPath, logDir: join(dir, "logs"), sleeps, sleep: async (ms) => { sleeps.push(ms); } };
}

const base = (s, extra) => ({ role: "generator", lake: "", promptPath: s.promptPath, logDir: s.logDir, cwd: s.dir, sleep: s.sleep, ...extra });

test("runAgent codex: success writes log and passes the prompt on stdin", async () => {
  const s = setup();
  const fake = makeFake([{ last: "finished", stdout: "out\n", stderr: "err\n" }]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 1 });
  assert.equal(fake.calls[0].command, "codex");
  assert.equal(fake.calls[0].options.cwd, s.dir);
  assert.equal(fake.calls[0].input(), "the prompt");
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), "finished");
  const log = readFileSync(join(s.logDir, "generator.log"), "utf8");
  assert.match(log, /out/);
  assert.match(log, /err/);
});

test("runAgent claude: stdout becomes the last message, stderr the log", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: "answer", stderr: "warn" }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1 });
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), "answer");
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /^=== attempt 1 claude-sonnet-5-5 \S+\nwarn\n=== exit 0\n$/);
});

test("runAgent claude: a multibyte character split across two chunks stays intact", async () => {
  const s = setup();
  const bytes = Buffer.from("Cichliden: K\u00e4rpergr\u00f6\u00dfe \u2603", "utf8");
  const cut = bytes.indexOf(0xc3) + 1;
  const fake = makeFake([{ stdoutChunks: [bytes.subarray(0, cut), bytes.subarray(cut)] }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(readFileSync(join(s.logDir, "generator.last"), "utf8"), bytes.toString("utf8"));
});

test("runAgent: a transient failure is retried and the second attempt succeeds", async () => {
  const s = setup();
  const fake = makeFake([{ stderr: "model is at capacity", code: 1 }, { last: "done" }]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, retryDelayMs: 5 }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 2 });
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
  assert.deepEqual(result, { status: "ok", model: "gpt-6-sol", attempts: 4 });
  const models = fake.calls.map((c) => c.args[c.args.indexOf("-m") + 1]);
  assert.deepEqual(models, ["gpt-6.1-sol", "gpt-6.1-sol", "gpt-6.1-sol", "gpt-6-sol"]);
});

test("runAgent: all attempts transient gives failed", async () => {
  const s = setup();
  const codex = makeFake([{ stderr: "at capacity", code: 1 }]);
  assert.deepEqual(await runAgent(base(s, { runner: "codex", spawnImpl: codex.spawnImpl })),
    { status: "failed", model: "gpt-6-sol", attempts: 5 });
  assert.equal(s.sleeps.length, 4);
  const claude = makeFake([{ stdout: "API Error: overloaded", code: 1 }]);
  assert.deepEqual(await runAgent(base(s, { runner: "claude", spawnImpl: claude.spawnImpl })),
    { status: "failed", model: "claude-sonnet-5-5", attempts: 3 });
});

test("runAgent: out-of-credits stops at once", async () => {
  const s = setup();
  const fake = makeFake([{ stderr: "You are out of credits", code: 1 }, { last: "never reached" }]);
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "out-of-credits", model: "gpt-6.1-sol", attempts: 1 });
  assert.equal(fake.calls.length, 1);
  assert.deepEqual(s.sleeps, []);
});

test("runAgent: timeout ends the child with SIGTERM", async () => {
  const s = setup();
  const fake = makeFake([{ never: true }]);
  const result = await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, timeoutMs: 20, killGraceMs: 1000 }));
  assert.deepEqual(fake.calls[0].signals, ["SIGTERM"]);
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1 });
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
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 1 });
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

test("runAgent: a spawn error does not throw", async () => {
  const s = setup();
  const spawnImpl = () => {
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.kill = () => {};
    setImmediate(() => child.emit("error", new Error("spawn claude ENOENT")));
    return child;
  };
  const result = await runAgent(base(s, { runner: "claude", spawnImpl }));
  assert.equal(result.status, "ok");
  assert.match(readFileSync(join(s.logDir, "generator.log"), "utf8"), /ENOENT/);
});

test("runAgent: checker-a in a Codex run uses the claude command and the claude output split", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: "answer", stderr: "warn" }]);
  const result = await runAgent(base(s, { runner: "codex", role: "checker-a", spawnImpl: fake.spawnImpl }));
  assert.deepEqual(result, { status: "ok", model: "claude-sonnet-5-5", attempts: 1 });
  assert.equal(fake.calls[0].command, "claude");
  assert.equal(fake.calls[0].args[fake.calls[0].args.indexOf("--model") + 1], "claude-sonnet-5-5");
  assert.equal(readFileSync(join(s.logDir, "checker-a.last"), "utf8"), "answer");
  assert.match(readFileSync(join(s.logDir, "checker-a.log"), "utf8"), /^=== attempt 1 claude-sonnet-5-5 \S+\nwarn\n=== exit 0\n$/);
});

test("runAgent: the child env drops the session variables of a parent Claude Code session", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: "answer" }]);
  const session = Object.fromEntries([
    "CLAUDECODE", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_MESSAGING_SOCKET",
    "CLAUDE_CODE_MESSAGING_TOKEN", "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDE_CODE_CHILD_SESSION",
    "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_PID", "CLAUDE_EFFORT",
  ].map((key) => [key, "1"]));
  const env = { PATH: "/usr/bin", HOME: "/home/x", ANTHROPIC_API_KEY: "k", ...session };
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl, env }));
  assert.deepEqual(fake.calls[0].options.env, { PATH: "/usr/bin", HOME: "/home/x", ANTHROPIC_API_KEY: "k" });
  assert.equal(env.CLAUDECODE, "1");
});

test("runAgent: the child env defaults to process.env", async () => {
  const s = setup();
  const fake = makeFake([{ stdout: "answer" }]);
  await runAgent(base(s, { runner: "claude", spawnImpl: fake.spawnImpl }));
  assert.equal(fake.calls[0].options.env.PATH, process.env.PATH);
  assert.equal(fake.calls[0].options.env.CLAUDECODE, undefined);
});

test("runAgent: every attempt is appended to the log and the classification reads the current attempt", async () => {
  const s = setup();
  // The first attempt is transient; the second finishes with exit 1 and no known pattern, which
  // is "ok" only when the transient text of the first attempt is left out of the classification.
  const fake = makeFake([{ stderr: "model is at capacity", code: 1 }, { stderr: "plain failure", code: 1 }]);
  const at = new Date("2026-10-16T08:00:00.000Z");
  const result = await runAgent(base(s, { runner: "codex", spawnImpl: fake.spawnImpl, now: () => at }));
  assert.deepEqual(result, { status: "ok", model: "gpt-6.1-sol", attempts: 2 });
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
  assert.deepEqual(await running, { status: "aborted", model: "gpt-6.1-sol", attempts: 1 });
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
  assert.deepEqual(result, { status: "aborted", model: "gpt-6.1-sol", attempts: 1 });
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
