// Tests for the agent CLI. Fake spawn and runner functions stand in for the claude binary.
import { test } from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { PassThrough } from "node:stream";
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { fakeClaude, fakeHome, fakeRepo } from "./agent-test-support.mjs";
import { PLACEHOLDER_API_KEY, PLACEHOLDER_OAUTH_TOKEN } from "./agent-cage.mjs";
import { main, outDirError } from "./claude-agent.mjs";

// No test depends on the git state of the machine.
const noGit = () => {
  throw new Error("not a git repository");
};

// The prompt, an out-dir outside the repository, a fake repository with a fake claude on the PATH,
// an empty home directory and a login file that has not expired. common holds the options that
// every call of main gets.
function setup() {
  const dir = realpathSync(mkdtempSync(join(tmpdir(), "claude-agent-")));
  const promptPath = join(dir, "prompt.txt");
  writeFileSync(promptPath, "the prompt");
  const lines = { out: [], err: [] };
  const repoRoot = fakeRepo();
  const home = fakeHome();
  const claude = fakeClaude();
  const credentialsFile = join(dir, "credentials.json");
  writeFileSync(credentialsFile, JSON.stringify({ claudeAiOauth: { accessToken: "dummy-access", expiresAt: Date.now() + 3_600_000 } }));
  return {
    dir,
    promptPath,
    repoRoot,
    home,
    claude,
    outDir: join(dir, "out"),
    keyFile: join(dir, "anthropic.key"),
    credentialsFile,
    lines,
    io: { out: (t) => lines.out.push(t), err: (t) => lines.err.push(t) },
    common: { repoRoot, home, credentialsFile, gitExec: noGit, env: { PATH: claude.dir, CICHLIDS_SOURCE_ARCHIVE: join(dir, "no-archive") } },
  };
}

const argvOf = (s, extra = []) => ["--name", "review-1", "--prompt-file", s.promptPath, "--out-dir", s.outDir, "--billing", "subscription", ...extra];

// A runAgentImpl that records its options and writes the final message like the runner does.
function fakeRunner({ status = "ok", last = "final message", costUsd = 0.0371, billing = "api-key", attempts = 1 } = {}) {
  const calls = [];
  const impl = async (options) => {
    calls.push(options);
    writeFileSync(join(options.logDir, `${options.name}.last`), last);
    return { status, model: options.model, attempts, costUsd, billing };
  };
  return { impl, calls };
}

async function run(s, extra, runner, more = {}) {
  mkdirSync(s.outDir, { recursive: true });
  return main({ argv: argvOf(s, extra), runAgentImpl: runner.impl, signals: new EventEmitter(), ...s.io, ...s.common, ...more });
}

test("main passes the defaults to the runner and prints one summary line", async () => {
  const s = setup();
  const runner = fakeRunner();
  assert.equal(await run(s, [], runner), 0);
  const [call] = runner.calls;
  assert.equal(call.runner, "claude");
  assert.equal(call.model, "claude-sonnet-5-5");
  assert.equal(call.effort, "high");
  assert.deepEqual(call.fallbacks, []);
  assert.equal(call.name, "review-1");
  assert.equal(call.promptPath, s.promptPath);
  assert.equal(call.logDir, s.outDir);
  assert.equal(call.cwd, process.cwd());
  assert.equal(call.timeoutMs, 3_600_000);
  assert.deepEqual(s.lines.out, [`review-1: ok billing api-key cost 0.0371 USD attempts 1 -> ${s.outDir}/review-1.last`]);
});

test("main passes model, effort, cwd and timeout", async () => {
  const s = setup();
  const runner = fakeRunner();
  assert.equal(await run(s, ["--model", "claude-opus-5-5", "--effort", "low", "--cwd", s.dir, "--timeout-min", "1.5"], runner), 0);
  const [call] = runner.calls;
  assert.equal(call.model, "claude-opus-5-5");
  assert.equal(call.effort, "low");
  assert.equal(call.cwd, s.dir);
  assert.equal(call.timeoutMs, 90_000);
});

test("main exit codes follow the status", async () => {
  for (const [status, code] of [["ok", 0], ["failed", 1], ["auth-failed", 1], ["aborted", 1], ["out-of-credits", 3]]) {
    const s = setup();
    const runner = fakeRunner({ status });
    assert.equal(await run(s, [], runner), code, status);
    assert.match(s.lines.out[0], new RegExp(`^review-1: ${status} billing `));
  }
});

test("main reports an ok run with an empty final message as failed", async () => {
  const s = setup();
  const runner = fakeRunner({ last: "  \n" });
  assert.equal(await run(s, [], runner), 1);
  assert.match(s.lines.out[0], /^review-1: failed /);
});

test("main exits with 2 and the usage on invalid arguments", async () => {
  const s = setup();
  const runner = fakeRunner();
  const cases = [
    [[], "missing --name"],
    [["--name", "x", "--out-dir", s.outDir], "missing --prompt-file"],
    [["--name", "x", "--prompt-file", s.promptPath], "missing --out-dir"],
    [["--name", "Bad_Name", "--prompt-file", s.promptPath, "--out-dir", s.outDir], "--name must match"],
    [["--name=-x", "--prompt-file", s.promptPath, "--out-dir", s.outDir], "--name must match"],
    [argvOf(s, ["--timeout-min", "0"]), "--timeout-min"],
    [argvOf(s, ["--timeout-min", "soon"]), "--timeout-min"],
    [argvOf(s, ["--timeout-min", "40000"]), "--timeout-min"],
    [argvOf(s, ["--effort", "hgih"]), "--effort"],
    [argvOf(s, ["--model", "gpt-6"]), "--model"],
    [argvOf(s, ["--out-dir", s.promptPath]), "--out-dir"],
    [["--name", "x", "--prompt-file", join(s.dir, "absent.txt"), "--out-dir", s.outDir, "--billing", "subscription"], "--prompt-file"],
    [argvOf(s, ["--cwd", join(s.dir, "absent")]), "--cwd"],
    [argvOf(s, ["--bogus"]), "bogus"],
  ];
  for (const [argv, message] of cases) {
    s.lines.err.length = 0;
    assert.equal(await main({ argv, runAgentImpl: runner.impl, signals: new EventEmitter(), ...s.io, ...s.common }), 2, message);
    assert.match(s.lines.err[0], new RegExp(message.replace(/[-]/g, "\\-")));
    assert.match(s.lines.err.at(-1), /Usage:/);
  }
  assert.equal(runner.calls.length, 0);
  assert.deepEqual(s.lines.out, []);
});

test("main exits with 2 for an existing out-dir that is not writable", { skip: process.getuid?.() === 0 }, async () => {
  const s = setup();
  mkdirSync(s.outDir);
  chmodSync(s.outDir, 0o500);
  const runner = fakeRunner();
  try {
    assert.equal(await main({ argv: argvOf(s), runAgentImpl: runner.impl, signals: new EventEmitter(), ...s.io, ...s.common }), 2);
  } finally {
    chmodSync(s.outDir, 0o700);
  }
  assert.equal(runner.calls.length, 0);
});

test("main aborts the run on SIGTERM and exits with 1", async () => {
  const s = setup();
  const signals = new EventEmitter();
  const impl = (options) => new Promise((resolve) => {
    options.abortSignal.addEventListener("abort", () => resolve({ status: "aborted", model: "m", attempts: 1, costUsd: 0, billing: "subscription" }));
  });
  mkdirSync(s.outDir, { recursive: true });
  const running = main({ argv: argvOf(s), runAgentImpl: impl, signals, ...s.io, ...s.common });
  await new Promise((done) => setTimeout(done, 5));
  assert.equal(signals.listenerCount("SIGTERM"), 1);
  signals.emit("SIGTERM");
  assert.equal(await running, 1);
  assert.match(s.lines.out[0], /^review-1: aborted billing subscription cost 0.0000 USD attempts 1 -> /);
  assert.equal(signals.listenerCount("SIGTERM"), 0);
  assert.equal(signals.listenerCount("SIGINT"), 0);
});

// Fake spawn that prints one scripted JSON object, as the claude binary does.
function fakeSpawn(steps) {
  const calls = [];
  const spawnImpl = (command, args, options) => {
    const step = steps[Math.min(calls.length, steps.length - 1)];
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.kill = () => {};
    calls.push({ command, args, options });
    setImmediate(() => {
      child.stdout.write(JSON.stringify(step.json));
      setImmediate(() => child.emit("close", step.code ?? 0));
    });
    return child;
  };
  return { spawnImpl, calls };
}

test("main with the real runner: key file billing, log and last files", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-key\n");
  const fake = fakeSpawn([{ json: { is_error: false, result: "the answer", total_cost_usd: 0.1234 } }]);
  const code = await main({
    argv: argvOf(s, ["--billing", "api-key", "--cwd", s.repoRoot]), signals: new EventEmitter(), spawnImpl: fake.spawnImpl, apiKeyFile: s.keyFile, ...s.io, ...s.common,
  });
  assert.equal(code, 0);
  assert.deepEqual(s.lines.out, [`review-1: ok billing api-key cost 0.1234 USD attempts 1 -> ${s.outDir}/review-1.last`]);
  assert.equal(readFileSync(join(s.outDir, "review-1.last"), "utf8"), "the answer");
  assert.match(readFileSync(join(s.outDir, "review-1.log"), "utf8"), /billing api-key\n=== exit 0\n=== cost 0.1234 USD\n$/);
  assert.equal(fake.calls[0].command, "bwrap");
  assert.ok(!fake.calls[0].args.includes("--settings"));
  assert.equal(fake.calls[0].options.env.ANTHROPIC_API_KEY, PLACEHOLDER_API_KEY);
});

test("main with the real runner: a rejected key gives exit 1 after one attempt", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-key\n");
  const fake = fakeSpawn([{ json: { is_error: true, api_error_status: 401, result: "Invalid API key", total_cost_usd: 0 }, code: 1 }]);
  const code = await main({
    argv: argvOf(s, ["--billing", "api-key", "--cwd", s.repoRoot]), signals: new EventEmitter(), spawnImpl: fake.spawnImpl, apiKeyFile: s.keyFile, ...s.io, ...s.common,
  });
  assert.equal(code, 1);
  assert.equal(fake.calls.length, 1);
  assert.match(s.lines.out[0], /^review-1: auth-failed billing api-key cost 0.0000 USD attempts 1 -> /);
});

test("main with the real runner: an empty account gives exit 3", async () => {
  const s = setup();
  const fake = fakeSpawn([{ json: { is_error: true, result: "Claude usage limit reached", total_cost_usd: 0 }, code: 1 }]);
  const code = await main({
    argv: argvOf(s, ["--cwd", s.repoRoot]), signals: new EventEmitter(), spawnImpl: fake.spawnImpl, apiKeyFile: s.keyFile, ...s.io, ...s.common,
  });
  assert.equal(code, 3);
  assert.match(s.lines.out[0], /billing subscription/);
});

test("main: --billing is required and must be api-key or subscription", async () => {
  const s = setup();
  const runner = fakeRunner();
  const argv = ["--name", "review-1", "--prompt-file", s.promptPath, "--out-dir", s.outDir];
  for (const [extra, message] of [[[], "missing --billing"], [["--billing", "both"], "--billing must be one of"], [["--billing", ""], "missing --billing|must be one of"]]) {
    s.lines.err.length = 0;
    assert.equal(await main({ argv: [...argv, ...extra], runAgentImpl: runner.impl, signals: new EventEmitter(), ...s.io, ...s.common }), 2);
    assert.match(s.lines.err[0], new RegExp(message));
  }
  assert.equal(runner.calls.length, 0);
});

test("main: api-key billing needs a usable key file and never falls back", async () => {
  const s = setup();
  const dirKey = join(s.dir, "keydir");
  mkdirSync(dirKey);
  const locked = join(s.dir, "locked.key");
  writeFileSync(locked, "dummy-key");
  chmodSync(locked, 0o000);
  const empty = join(s.dir, "empty.key");
  writeFileSync(empty, "");
  const blank = join(s.dir, "blank.key");
  writeFileSync(blank, " \n\t");
  const cases = [
    [join(s.dir, "absent.key"), "does not exist"],
    [empty, "is empty"],
    [blank, "is empty"],
    [dirKey, "is not a regular file"],
    ...(process.getuid?.() === 0 ? [] : [[locked, "is not readable"]]),
  ];
  const fake = fakeSpawn([{ json: { is_error: false, result: "x", total_cost_usd: 0 } }]);
  for (const [apiKeyFile, reason] of cases) {
    s.lines.err.length = 0;
    const code = await main({
      argv: argvOf(s, ["--billing", "api-key"]), signals: new EventEmitter(), spawnImpl: fake.spawnImpl, apiKeyFile, ...s.io, ...s.common,
    });
    assert.equal(code, 2, reason);
    assert.ok(s.lines.err[0].includes(`${apiKeyFile} ${reason}`), s.lines.err[0]);
  }
  assert.equal(fake.calls.length, 0);
  assert.deepEqual(s.lines.out, []);
});

test("main: the key file path comes from CICHLIDS_ANTHROPIC_KEY_FILE of the env", async () => {
  const s = setup();
  const runner = fakeRunner();
  const code = await main({
    argv: argvOf(s, ["--billing", "api-key"]), runAgentImpl: runner.impl, signals: new EventEmitter(),
    ...s.common, env: { CICHLIDS_ANTHROPIC_KEY_FILE: join(s.dir, "absent.key") }, ...s.io,
  });
  assert.equal(code, 2);
  assert.equal(runner.calls.length, 0);
});

test("main with the real runner: subscription billing gives the cage a placeholder token even with a key file present", async () => {
  const s = setup();
  writeFileSync(s.keyFile, "dummy-key\n");
  const fake = fakeSpawn([{ json: { is_error: false, result: "x", total_cost_usd: 0 } }]);
  const code = await main({
    argv: argvOf(s, ["--cwd", s.repoRoot]), signals: new EventEmitter(), spawnImpl: fake.spawnImpl, apiKeyFile: s.keyFile,
    ...s.io, ...s.common, env: { ...s.common.env, ANTHROPIC_API_KEY: "k", CLAUDE_CODE_OAUTH_TOKEN: "o" },
  });
  assert.equal(code, 0);
  const { env } = fake.calls[0].options;
  assert.equal(env.CLAUDE_CODE_OAUTH_TOKEN, PLACEHOLDER_OAUTH_TOKEN);
  assert.equal(env.ANTHROPIC_API_KEY, undefined);
  assert.match(s.lines.out[0], /billing subscription/);
});

test("main: subscription billing needs a login that has not expired", async () => {
  const s = setup();
  const runner = fakeRunner();
  writeFileSync(s.credentialsFile, JSON.stringify({ claudeAiOauth: { accessToken: "t", expiresAt: Date.now() - 1000 } }));
  assert.equal(await run(s, [], runner), 2);
  assert.match(s.lines.err[0], /--billing subscription needs a usable claude login: .*credentials\.json has an expired access token/);
  s.lines.err.length = 0;
  assert.equal(await run(s, [], runner, { credentialsFile: join(s.dir, "absent.json") }), 2);
  assert.match(s.lines.err[0], /absent\.json does not exist/);
  assert.equal(runner.calls.length, 0);
});

test("main: a runner that throws gives an error line and exit 1", async () => {
  const s = setup();
  const code = await main({
    argv: argvOf(s), runAgentImpl: async () => { throw new Error("api-key billing needs a usable key file: /k is empty"); },
    signals: new EventEmitter(), ...s.io, ...s.common,
  });
  assert.equal(code, 1);
  assert.match(s.lines.err[0], /^review-1: the agent could not start \(api-key billing needs a usable key file/);
  assert.deepEqual(s.lines.out, []);
});

test("main: managed settings that select credentials are a usage error", async () => {
  const s = setup();
  const managed = join(s.dir, "managed.json");
  writeFileSync(managed, JSON.stringify({ apiKeyHelper: "echo x" }));
  const runner = fakeRunner();
  const code = await main({
    argv: argvOf(s), runAgentImpl: runner.impl, signals: new EventEmitter(), managedFile: managed, ...s.io, ...s.common,
  });
  assert.equal(code, 2);
  assert.match(s.lines.err[0], /managed settings/);
  assert.equal(runner.calls.length, 0);
});

test("main: every --write directory becomes an extra write root and the out-dir none", async () => {
  const s = setup();
  const scratch = realpathSync(mkdtempSync(join(tmpdir(), "agent-scratch-")));
  const runner = fakeRunner();
  const code = await run(s, ["--write", "tools/species-data", "--write", join(s.repoRoot, "docs"), "--write", scratch], runner);
  assert.equal(code, 0, s.lines.err.join("\n"));
  assert.equal(runner.calls[0].root, s.repoRoot);
  assert.deepEqual(runner.calls[0].extraWriteRoots, [join(s.repoRoot, "tools", "species-data"), join(s.repoRoot, "docs"), scratch]);
});

test("main: without --write there is no extra write root", async () => {
  const s = setup();
  const runner = fakeRunner();
  assert.equal(await run(s, [], runner), 0);
  assert.deepEqual(runner.calls[0].extraWriteRoots, []);
  assert.deepEqual(runner.calls[0].extraReadRoots, []);
});

test("main: every --read directory becomes a read directory of the cage, relative paths resolve against the repository", async () => {
  const s = setup();
  const briefs = realpathSync(mkdtempSync(join(tmpdir(), "agent-briefs-")));
  const runner = fakeRunner();
  const code = await run(s, ["--read", briefs, "--read", "docs", "--write", "tools/species-data"], runner);
  assert.equal(code, 0, s.lines.err.join("\n"));
  assert.deepEqual(runner.calls[0].extraReadRoots, [briefs, join(s.repoRoot, "docs")]);
  assert.deepEqual(runner.calls[0].extraWriteRoots, [join(s.repoRoot, "tools", "species-data")]);
});

test("main: a --read directory below the Claude Code scratch directory of /tmp is allowed", async () => {
  const s = setup();
  const session = join(realpathSync(tmpdir()), `claude-${process.getuid?.() ?? 1000}`, `agent-test-${process.pid}`);
  const agents = join(session, "scratchpad", "agents");
  mkdirSync(agents, { recursive: true });
  try {
    const runner = fakeRunner();
    assert.equal(await run(s, ["--read", agents], runner), 0, s.lines.err.join("\n"));
    assert.deepEqual(runner.calls[0].extraReadRoots, [agents]);
    // The scratch directory itself stays refused.
    const t = fresh(s);
    assert.equal(await run(t, ["--read", join(session, "..")], fakeRunner()), 2);
  } finally {
    rmSync(session, { recursive: true, force: true });
  }
});

test("main: a --read value that is missing, the repository, a parent, the home directory, a git or .claude directory or /tmp is a usage error", async () => {
  const s = setup();
  for (const d of [".git/hooks", ".claude/worktrees", "docs/.git"]) mkdirSync(join(s.repoRoot, d), { recursive: true });
  symlinkSync(s.home, join(s.repoRoot, "docs-link"));
  const bad = [
    ".", s.repoRoot, "..", "/etc", "/home", ".git", ".git/hooks", ".claude/worktrees", "docs/.git", "docs-link", "/tmp", "absent", s.home,
    "data",
  ];
  mkdirSync(join(s.repoRoot, "data"), { recursive: true });
  for (const value of bad) {
    const t = fresh(s);
    const runner = fakeRunner();
    assert.equal(await run(t, ["--read", value, ...(value === "data" ? ["--write", "data"] : [])], runner), 2, value);
    assert.match(t.lines.err[0], /^--write or --read: (read directory|write root) .* (is not allowed: |lies inside)/, value);
    assert.equal(runner.calls.length, 0, value);
  }
});

test("main: a --read directory inside a write root is a usage error", async () => {
  const s = setup();
  mkdirSync(join(s.repoRoot, "data", "inputs"), { recursive: true });
  const runner = fakeRunner();
  assert.equal(await run(s, ["--read", "data/inputs"], runner), 2);
  assert.match(s.lines.err[0], /read directory .* lies inside the write root/);
  assert.equal(runner.calls.length, 0);
});

test("main: the usage text names --read", async () => {
  const s = setup();
  assert.equal(await main({ argv: [], runAgentImpl: fakeRunner().impl, signals: new EventEmitter(), ...s.io, ...s.common }), 2);
  assert.match(s.lines.err.join("\n"), /\[--read <dir>\]\.\.\. \[--write <dir>\]\.\.\./);
});

// A copy of the setup with its own line buffers, so that one test can check many cases.
const fresh = (s, extra = {}) => {
  const t = { ...s, ...extra, lines: { out: [], err: [] } };
  t.io = { out: (x) => t.lines.out.push(x), err: (x) => t.lines.err.push(x) };
  return t;
};

test("main: a --write value that is missing, the repository, a parent, outside, forbidden or /tmp is a usage error", async () => {
  const s = setup();
  for (const d of [".git/hooks", ".claude/worktrees", "docs/.git"]) mkdirSync(join(s.repoRoot, d), { recursive: true });
  symlinkSync(s.home, join(s.repoRoot, "docs-link"));
  const bad = [
    ".", s.repoRoot, "..", "/etc", "/home", ".git", ".git/hooks", ".claude/worktrees", "docs/.git", "docs/../.git", "docs-link", "/tmp", "/tmp/../etc",
    "absent", s.home,
  ];
  for (const value of bad) {
    const t = fresh(s);
    const runner = fakeRunner();
    assert.equal(await run(t, ["--write", value], runner), 2, value);
    assert.match(t.lines.err[0], /^--write or --read: write root .* is not allowed: /, value);
    assert.equal(runner.calls.length, 0, value);
  }
});

test("main: an --out-dir inside the repository, the home directory or a write root is a usage error, any other place is fine", async () => {
  const s = setup();
  const scratch = realpathSync(mkdtempSync(join(tmpdir(), "agent-scratch-")));
  const cases = [
    [join(s.repoRoot, "out"), []], [join(s.repoRoot, "data", "out"), []], [join(s.home, "out"), []],
    [join(scratch, "out"), ["--write", scratch]], [scratch, ["--write", scratch]],
  ];
  for (const [outDir, extra] of cases) {
    const t = fresh(s, { outDir });
    const runner = fakeRunner();
    assert.equal(await run(t, extra, runner), 2, outDir);
    assert.match(t.lines.err[0], /^--out-dir .* is not allowed: inside /, outDir);
    assert.equal(runner.calls.length, 0, outDir);
  }
  const runner = fakeRunner();
  assert.equal(await run(s, [], runner), 0);
});

test("outDirError: real paths decide, also for a directory that does not exist yet", () => {
  const s = setup();
  const options = { writable: [join(s.repoRoot, "data")], root: s.repoRoot, home: s.home };
  assert.equal(outDirError(join(s.dir, "new", "deeper"), options), null);
  assert.equal(outDirError(join(s.repoRoot, "new"), options), "inside the repository");
  assert.equal(outDirError(join(s.home, "new"), options), "inside the home directory");
  const link = join(s.dir, "link");
  symlinkSync(join(s.repoRoot, "data"), link);
  assert.equal(outDirError(join(link, "new"), options), "inside the repository");
  assert.equal(outDirError(join(s.dir, "x", ".."), { ...options, writable: [s.dir] }), "inside a write root of the agent");
});

test("main: an audit failure exits with 1 and names the hits", async () => {
  const s = setup();
  const impl = async (options) => {
    writeFileSync(join(options.logDir, `${options.name}.last`), "final message");
    return { status: "audit-failed", model: "m", attempts: 1, costUsd: 0, billing: "subscription", detail: "/r/data/.claude (created)" };
  };
  mkdirSync(s.outDir, { recursive: true });
  assert.equal(await main({ argv: argvOf(s), runAgentImpl: impl, signals: new EventEmitter(), ...s.io, ...s.common }), 1);
  assert.deepEqual(s.lines.err, ["review-1: audit failed (/r/data/.claude (created))"]);
  assert.match(s.lines.out[0], /^review-1: audit-failed billing subscription /);
});
