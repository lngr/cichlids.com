import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { EventEmitter } from "node:events";
import { spawnSync } from "node:child_process";
import { main, nextSpecies, nextTiebreakerNumbers, parseGroupsyncWarnings, prepareWorkdir, runGroupsync, runPipeline, statusRows } from "./pipeline.mjs";

const tmp = () => mkdtempSync(join(tmpdir(), "pipeline-test-"));
// The pid of a finished child process stands for a dead process.
const deadPid = () => spawnSync(process.execPath, ["-e", ""]).pid;
const manifest = { legacyId: "7", name: "Fish", hint: "hint", lake: "", legacyIds: "", adjudicatorNote: "" };
const stamp = new Date(2026, 9, 16, 8, 5, 3);

function seedWorkdir(wd) {
  mkdirSync(join(wd, ".literature"), { recursive: true });
  writeFileSync(join(wd, ".literature", "paper.txt"), "text");
  writeFileSync(join(wd, "generator.yaml"), "g");
  mkdirSync(join(wd, "logs"));
  writeFileSync(join(wd, "logs", "status"), "08:00 start\n");
}

test("prepareWorkdir keeps role files for an equal manifest", () => {
  const wd = join(tmp(), "wd");
  prepareWorkdir({ wd, manifest, now: stamp });
  seedWorkdir(wd);
  assert.equal(prepareWorkdir({ wd, manifest, now: stamp }), null);
  assert.ok(existsSync(join(wd, "generator.yaml")));
});

test("prepareWorkdir moves role files for a differing manifest and keeps .literature", () => {
  const wd = join(tmp(), "wd");
  prepareWorkdir({ wd, manifest, now: stamp });
  seedWorkdir(wd);
  const previous = prepareWorkdir({ wd, manifest: { ...manifest, hint: "other" }, now: stamp });
  assert.equal(previous, "previous-20261016-080503");
  assert.ok(!existsSync(join(wd, "generator.yaml")));
  assert.ok(existsSync(join(wd, previous, "generator.yaml")));
  assert.ok(existsSync(join(wd, previous, "logs", "status")));
  assert.ok(existsSync(join(wd, ".literature", "paper.txt")));
  assert.equal(JSON.parse(readFileSync(join(wd, "run.json"), "utf8")).hint, "other");
});

test("prepareWorkdir moves role files with --fresh and when the manifest is missing", () => {
  const root = tmp();
  const a = join(root, "a");
  prepareWorkdir({ wd: a, manifest, now: stamp });
  seedWorkdir(a);
  assert.ok(prepareWorkdir({ wd: a, manifest, fresh: true, now: stamp }));
  assert.ok(!existsSync(join(a, "generator.yaml")));
  const b = join(root, "b");
  seedWorkdir(b);
  assert.ok(prepareWorkdir({ wd: b, manifest, now: stamp }));
  assert.ok(!existsSync(join(b, "generator.yaml")));
});

test("prepareWorkdir keeps role files when only the adjudicator note differs and stores the note", () => {
  const wd = join(tmp(), "wd");
  prepareWorkdir({ wd, manifest, now: stamp });
  seedWorkdir(wd);
  assert.equal(prepareWorkdir({ wd, manifest: { ...manifest, adjudicatorNote: "synonym" }, now: stamp }), null);
  assert.ok(existsSync(join(wd, "generator.yaml")));
  assert.equal(JSON.parse(readFileSync(join(wd, "run.json"), "utf8")).adjudicatorNote, "synonym");
  for (const key of ["legacyId", "name", "lake", "legacyIds"]) {
    const other = join(tmp(), "wd");
    prepareWorkdir({ wd: other, manifest, now: stamp });
    seedWorkdir(other);
    assert.ok(prepareWorkdir({ wd: other, manifest: { ...manifest, [key]: "8" }, now: stamp }), key);
  }
});

test("nextTiebreakerNumbers continues after the highest file", () => {
  const wd = tmp();
  assert.deepEqual(nextTiebreakerNumbers(wd), [1, 2]);
  for (const n of [1, 2, 3]) writeFileSync(join(wd, `tiebreaker-${n}.yaml`), "x");
  assert.deepEqual(nextTiebreakerNumbers(wd), [4, 5]);
});

test("parseGroupsyncWarnings builds the list", () => {
  const report = [
    "WARNING data/species/profiles/a-b.yaml /features/hybridGroups/value: hybrid groups naming Fish are missing: g1, g2",
    "WARNING data/species/profiles/c-d.yaml /x: something else",
    "WARNING data/species/profiles/e-f.yaml /features/hybridGroups/value: hybrid groups naming Other are missing: g3",
    "3 file(s), 0 error(s), 3 warning(s)",
  ].join("\n");
  assert.equal(parseGroupsyncWarnings(report), "- a-b.yaml: g1, g2\n- e-f.yaml: g3");
  assert.equal(parseGroupsyncWarnings("0 file(s)"), "");
});

// lockPid writes legacy-3/run.lock with that pid.
function makeSpeciesRoot({ runningStatus, lockPid } = {}) {
  const root = tmp();
  const species = join(root, "data", "species");
  mkdirSync(join(species, "profiles"), { recursive: true });
  mkdirSync(join(species, "work"), { recursive: true });
  const entry = (legacyId, target, match = "exact") => `  - legacyId: ${legacyId}\n    legacyName: Name ${legacyId}\n    target: ${target}\n    match: ${match}\n`;
  writeFileSync(
    join(species, "legacy-mapping.yaml"),
    "mappings:\n" +
      entry(1, "profiled") + // has a profile
      entry(2, "trade-hybrid") + // trade form with parents
      entry(3, "running-one") + // being processed
      entry(4, "low") +
      entry(5, "high") +
      entry(6, "high", "synonym") +
      "  - legacyId: 8\n    legacyName: Name 8\n    target: null\n    match: none\n",
  );
  const cat = (legacyId, photos) => ({ legacyId, category: `Cat ${legacyId}`, photos });
  writeFileSync(join(species, "legacy-catalog.json"), JSON.stringify([cat(1, 900), cat(2, 800), cat(3, 700), cat(4, 10), cat(5, 500), cat(6, 20), cat(8, 5)]));
  writeFileSync(join(species, "profiles", "profiled.yaml"), "x");
  writeFileSync(
    join(species, "trade-forms.yaml"),
    "forms:\n  - id: trade-hybrid\n    parents: [a, b]\n  - id: low\n    parents: []\n",
  );
  const wd = join(species, "work", "legacy-3");
  mkdirSync(join(wd, "logs"), { recursive: true });
  writeFileSync(join(wd, "run.json"), JSON.stringify({ legacyId: "3", name: "Name 3" }));
  if (runningStatus) writeFileSync(join(wd, "logs", "status"), runningStatus);
  if (lockPid) writeFileSync(join(wd, "run.lock"), `${lockPid}\n`);
  return root;
}

test("nextSpecies skips profiles, trade forms with parents and running targets, and orders by photos", () => {
  const root = makeSpeciesRoot({ runningStatus: "08:00 start\n08:10 generator finished (m)\n", lockPid: process.pid });
  assert.deepEqual(nextSpecies({ root }), [
    "high | Cat 5 | 5 Name 5 (exact), 6 Name 6 (synonym)",
    "low | Cat 4 | 4 Name 4 (exact)",
  ]);
  assert.equal(nextSpecies({ root, count: 1 }).length, 1);
});

test("nextSpecies lists the target of a work directory without a live lock", () => {
  const cases = [
    { runningStatus: "08:00 DONE\n" },
    { runningStatus: "08:00 FAILED: tally\n" },
    { runningStatus: "08:00 start\n08:10 generator finished (m)\n" },
    { runningStatus: "08:00 start\n", lockPid: deadPid() },
  ];
  for (const options of cases) {
    const rows = nextSpecies({ root: makeSpeciesRoot(options), count: 10 });
    assert.ok(rows.some((r) => r.startsWith("running-one |")), JSON.stringify(options));
  }
});

test("statusRows marks running and stale work directories", () => {
  const row = (options) => statusRows({ root: makeSpeciesRoot(options) });
  assert.deepEqual(row({ runningStatus: "08:00 start\n", lockPid: process.pid }), ["3 Name 3 | 08:00 start (running)"]);
  assert.deepEqual(row({ runningStatus: "08:00 start\n", lockPid: deadPid() }), ["3 Name 3 | 08:00 start (stale)"]);
  assert.deepEqual(row({ runningStatus: "08:00 start\n" }), ["3 Name 3 | 08:00 start (stale)"]);
  assert.deepEqual(row({ runningStatus: "08:00 DONE\n" }), ["3 Name 3 | 08:00 DONE"]);
  assert.deepEqual(row({ runningStatus: "08:00 FAILED: tally\n" }), ["3 Name 3 | 08:00 FAILED: tally"]);
  assert.deepEqual(row({}), []);
});

// Fakes for runPipeline. The fake agent writes the role file like a real agent would; the fake
// adjudicator writes data/species/profiles/fish.yaml with legacyIds [7] (or, with
// adjudicatorWrites "existing", adjudicator-profile.yaml and a PROFILE EXISTS report, or with
// "nothing" only its report) and its last message. foreignProfile makes it also write the
// profile of another species, as a parallel run does. tallyFails(round) makes tally.mjs fail.
function makeFakes({
  root, pendingRounds = 0, credits = [], calls = [], adjudicatorWrites = "profile", adjudicatorLast = "report",
  foreignProfile = false, tallyFails = () => false,
}) {
  const wdOf = (id) => join(root, "data", "species", "work", `legacy-${id}`);
  let tallyCalls = 0;
  let adjudications = 0;
  const runAgentImpl = async ({ role, promptPath, cwd, logDir }) => {
    calls.push({ role, prompt: readFileSync(promptPath, "utf8"), cwd });
    if (credits.includes(role)) return { status: "out-of-credits", model: "m", attempts: 1 };
    if (role === "adjudicator") {
      adjudications += 1;
      const profile = `id: fish\nlegacyIds: [7]\nrevision: ${adjudications}\n`;
      if (adjudicatorWrites === "profile") writeFileSync(join(root, "data", "species", "profiles", "fish.yaml"), profile);
      if (adjudicatorWrites === "existing") writeFileSync(join(wdOf("7"), "adjudicator-profile.yaml"), profile);
      if (foreignProfile) writeFileSync(join(root, "data", "species", "profiles", "other.yaml"), `id: other\nlegacyIds: [8, 70]\nrevision: ${adjudications}\n`);
      const last = adjudicatorWrites === "existing" ? `PROFILE EXISTS ${adjudicatorLast}` : adjudicatorLast;
      writeFileSync(join(logDir, "adjudicator.last"), last);
    } else {
      writeFileSync(join(wdOf("7"), `${role}.yaml`), "ok");
    }
    return { status: "ok", model: `model-${role}`, attempts: 1 };
  };
  const execNode = async (args) => {
    calls.push({ node: args[0].replace("tools/species-data/", ""), arg: args[1] });
    if (args[0].endsWith("tally.mjs")) {
      tallyCalls += 1;
      if (tallyFails(tallyCalls)) return { code: 1, stdout: "", stderr: "tally failed" };
      const pending = tallyCalls <= pendingRounds;
      writeFileSync(
        join(wdOf("7"), "tally.yaml"),
        pending
          ? "pendingTiebreakers: [predation]\nfeatures:\n  predation:\n    proposals:\n      - { file: generator, value: 1 }\n      - { file: checker-a, value: 2 }\n"
          : "pendingTiebreakers: []\nfeatures: {}\n",
      );
    }
    return { code: 0, stdout: "out", stderr: "" };
  };
  return { runAgentImpl, execNode };
}

function pipelineRoot() {
  const root = tmp();
  mkdirSync(join(root, "data", "species", "profiles"), { recursive: true });
  writeFileSync(join(root, "data", "species", "profiles", "p.yaml"), "x");
  return root;
}

const base = { runner: "codex", legacyId: "7", name: "Fish", hint: "hint" };

test("runPipeline runs all roles, a tie-break round and ends with DONE", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, pendingRounds: 1, calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "careful" }), 0);
  const roles = calls.filter((c) => c.role).map((c) => c.role);
  assert.deepEqual(roles.slice(0, 4).sort(), ["checker-a", "checker-b", "checker-c", "generator"]);
  assert.deepEqual(roles.slice(4), ["tiebreaker-1", "tiebreaker-2", "adjudicator"]);
  const checkerA = calls.find((c) => c.role === "checker-a");
  assert.match(checkerA.prompt, /WebSearch/);
  assert.match(checkerA.prompt, /claude-sonnet-5-5/);
  assert.match(calls.find((c) => c.role === "adjudicator").prompt, /careful/);
  assert.match(calls.find((c) => c.role === "tiebreaker-1").prompt, /predation: votes 1, 2/);
  assert.ok(calls.some((c) => c.node === "build-directory.mjs"));
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines[0], /^\d\d:\d\d start$/);
  assert.match(lines.at(-1), /^\d\d:\d\d DONE$/);
  assert.ok(lines.some((l) => l.endsWith("generator finished (model-generator)")));
  assert.ok(existsSync(join(root, "data/species/work/legacy-7/logs/generator.prompt")));
});

test("runPipeline skips role files that exist", async () => {
  const root = pipelineRoot();
  const wd = join(root, "data", "species", "work", "legacy-7");
  const calls = [];
  const fakes = makeFakes({ root, calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  rmSync(join(wd, "logs"), { recursive: true });
  rmSync(join(wd, "checker-b.yaml"));
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["checker-b", "adjudicator"]);
});

test("runPipeline stops with exit 3 on out-of-credits", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, credits: ["checker-c"], calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 3);
  assert.ok(!calls.some((c) => c.node === "tally.mjs"));
  const status = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8");
  assert.match(status, /FAILED: checker-c out of credits/);
});

test("runPipeline ends with the out-of-credits line when parallel roles finish later", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => {
    if (args.role === "generator") return { status: "out-of-credits", model: "m", attempts: 1 };
    await new Promise((done) => setTimeout(done, 20));
    return fakes.runAgentImpl(args);
  };
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 3);
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines.at(-1), /^\d\d:\d\d FAILED: generator out of credits$/);
  assert.equal(lines.filter((l) => l.includes("finished")).length, 3);
});

test("runPipeline names every role that ran out of credits in one line", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, credits: ["checker-a", "checker-c"] });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 3);
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines.at(-1), /FAILED: checker-a, checker-c out of credits$/);
});

test("runPipeline ends a tie-break round that runs out of credits with the FAILED line", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, pendingRounds: 1, credits: ["tiebreaker-1"] });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 3);
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines.at(-1), /FAILED: tiebreaker-1 out of credits$/);
  assert.ok(lines.some((l) => l.endsWith("tiebreaker-2 finished (model-tiebreaker-2)")));
});

test("runPipeline logs a role whose attempts all failed as failed", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => (args.role === "checker-b" ? { status: "failed", model: "m-b", attempts: 5 } : fakes.runAgentImpl(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  const status = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8");
  assert.match(status, /checker-b failed \(m-b\)/);
  assert.doesNotMatch(status, /checker-b finished/);
});

test("runPipeline fails when a role file is missing", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => (args.role === "checker-b" ? { status: "failed", model: "m", attempts: 5 } : fakes.runAgentImpl(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  assert.match(readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8"), /FAILED: checker-b invalid or missing/);
});

test("runPipeline fails when features stay pending after three rounds", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, pendingRounds: 3 });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 1);
  const wd = join(root, "data/species/work/legacy-7");
  assert.match(readFileSync(join(wd, "logs/status"), "utf8"), /FAILED: still pending after 3 rounds/);
  assert.deepEqual(readdirSync(wd).filter((e) => e.startsWith("tiebreaker-")).sort(), ["tiebreaker-1.yaml", "tiebreaker-2.yaml", "tiebreaker-3.yaml", "tiebreaker-4.yaml"]);
});

test("runPipeline reports a failing final validation", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const execNode = async (args) => (String(args[1]).startsWith("data/species/profiles/") ? { code: 1, stdout: "", stderr: "bad" } : fakes.execNode(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, execNode }), 1);
  assert.match(readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8"), /FAILED: final validation/);
});

test("runGroupsync prints nothing to sync without warnings", async () => {
  const root = pipelineRoot();
  const out = [];
  const code = await runGroupsync({ root, runner: "claude", execNode: async () => ({ code: 0, stdout: "1 file(s)", stderr: "" }), out: (t) => out.push(t) });
  assert.equal(code, 0);
  assert.deepEqual(out, ["nothing to sync"]);
});

test("runGroupsync runs the agent with the collected list", async () => {
  const root = pipelineRoot();
  const out = [];
  const seen = [];
  const code = await runGroupsync({
    root,
    runner: "claude",
    execNode: async () => ({
      code: 0,
      stdout: "WARNING data/species/profiles/a-b.yaml /f: hybrid groups naming X are missing: g1\n",
      stderr: "",
    }),
    execGit: async () => ({ code: 0, stdout: " 1 file changed", stderr: "" }),
    runAgentImpl: async ({ role, promptPath, logDir }) => {
      seen.push({ role, prompt: readFileSync(promptPath, "utf8") });
      writeFileSync(join(logDir, "groupsync.last"), "done");
      return { status: "ok", model: "m", attempts: 1 };
    },
    out: (t) => out.push(t),
  });
  assert.equal(code, 0);
  assert.equal(seen[0].role, "groupsync");
  assert.match(seen[0].prompt, /- a-b\.yaml: g1/);
  assert.deepEqual(out, ["done", " 1 file changed"]);
});

const statusOf = (root) => readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
const lockOf = (root) => join(root, "data/species/work/legacy-7/run.lock");

test("runPipeline holds the lock during the run and removes it at the end", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const seen = [];
  const runAgentImpl = async (args) => {
    seen.push(readFileSync(lockOf(root), "utf8"));
    return fakes.runAgentImpl(args);
  };
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl, pid: process.pid }), 0);
  assert.ok(seen.length > 0 && seen.every((text) => text === `${process.pid}\n`));
  assert.ok(!existsSync(lockOf(root)));
});

test("runPipeline refuses to start while a live process holds the lock", async () => {
  const root = pipelineRoot();
  const wd = join(root, "data/species/work/legacy-7");
  mkdirSync(wd, { recursive: true });
  writeFileSync(join(wd, "generator.yaml"), "g");
  writeFileSync(lockOf(root), `${process.pid}\n`);
  const errors = [];
  const calls = [];
  const fakes = makeFakes({ root, calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes, pid: 999999, err: (t) => errors.push(t), fresh: true }), 1);
  assert.match(errors[0], new RegExp(`legacy-7 is running in process ${process.pid}`));
  assert.deepEqual(calls, []);
  assert.ok(existsSync(join(wd, "generator.yaml")));
  assert.ok(!existsSync(join(wd, "logs")));
  assert.equal(readFileSync(lockOf(root), "utf8"), `${process.pid}\n`);
});

test("runPipeline replaces the lock of a dead process and keeps the lock out of previous-*", async () => {
  const root = pipelineRoot();
  const wd = join(root, "data/species/work/legacy-7");
  mkdirSync(wd, { recursive: true });
  writeFileSync(join(wd, "generator.yaml"), "g");
  const dead = deadPid();
  writeFileSync(lockOf(root), `${dead}\n`);
  const fakes = makeFakes({ root });
  let lockDuringRun = null;
  const runAgentImpl = async (args) => {
    lockDuringRun = readFileSync(lockOf(root), "utf8");
    return fakes.runAgentImpl(args);
  };
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 0);
  assert.equal(lockDuringRun, `${process.pid}\n`);
  const previous = readdirSync(wd).find((e) => e.startsWith("previous-"));
  assert.ok(previous);
  assert.ok(!existsSync(join(wd, previous, "run.lock")));
  assert.ok(!existsSync(lockOf(root)));
  assert.ok(statusOf(root).some((l) => l.endsWith(`stale lock of process ${dead} replaced`)));
});

test("runPipeline ends a thrown error with FAILED: crashed, aborts running agents and removes the lock", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  let aborted = 0;
  const runAgentImpl = async (args) => {
    if (args.role === "generator") throw new Error("disk full\nsecond line");
    // The other voting roles run until the abort reaches them.
    await new Promise((done) => args.abortSignal.addEventListener("abort", done, { once: true }));
    aborted += 1;
    return { status: "aborted", model: "m", attempts: 1 };
  };
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  assert.equal(aborted, 3);
  const lines = statusOf(root);
  assert.match(lines.at(-1), /^\d\d:\d\d FAILED: crashed \(disk full\)$/);
  assert.ok(!existsSync(lockOf(root)));
});

test("runPipeline ends SIGTERM with FAILED: crashed, ends the agents, removes the lock and exits 1", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const signals = new EventEmitter();
  let exited;
  const exitCalled = new Promise((done) => {
    exited = done;
  });
  let started = 0;
  const allStarted = new Promise((done) => {
    started = done;
  });
  let count = 0;
  const ended = [];
  const runAgentImpl = async (args) => {
    count += 1;
    if (count === 4) started();
    await new Promise((done) => args.abortSignal.addEventListener("abort", done, { once: true }));
    // A child needs a moment to end after SIGTERM; exit waits for it.
    await new Promise((done) => setTimeout(done, 10));
    ended.push(args.role);
    return { status: "aborted", model: "m", attempts: 1 };
  };
  const run = runPipeline({ ...base, root, ...fakes, runAgentImpl, signals, exit: (code) => exited(code) });
  await allStarted;
  assert.ok(existsSync(lockOf(root)));
  signals.emit("SIGTERM", "SIGTERM");
  assert.equal(await exitCalled, 1);
  assert.equal(ended.length, 4);
  assert.ok(!existsSync(lockOf(root)));
  assert.equal(await run, 1);
  const lines = statusOf(root);
  assert.match(lines.at(-1), /^\d\d:\d\d FAILED: crashed \(SIGTERM\)$/);
  assert.equal(signals.listenerCount("SIGINT") + signals.listenerCount("SIGTERM"), 0);
});

test("runPipeline removes its signal handlers after the run", async () => {
  const root = pipelineRoot();
  const signals = new EventEmitter();
  assert.equal(await runPipeline({ ...base, root, ...makeFakes({ root }), signals }), 0);
  assert.equal(signals.listenerCount("SIGINT") + signals.listenerCount("SIGTERM"), 0);
});

const wdOf7 = (root) => join(root, "data/species/work/legacy-7");

test("runPipeline fails when the adjudicator leaves an empty last message", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, adjudicatorLast: "" });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 1);
  assert.match(statusOf(root).at(-1), /FAILED: adjudicator wrote no profile \(empty last message\)$/);
  assert.ok(!existsSync(join(wdOf7(root), "adjudicator.done")));
});

test("runPipeline fails when the adjudicator writes no profile", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, calls, adjudicatorWrites: "nothing" });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 1);
  assert.match(statusOf(root).at(-1), /FAILED: adjudicator wrote no profile$/);
  assert.ok(!calls.some((c) => c.node === "build-directory.mjs"));
  assert.ok(!existsSync(join(wdOf7(root), "adjudicator.done")));
});

test("runPipeline warns when the profile exists and the adjudicator writes adjudicator-profile.yaml", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, adjudicatorWrites: "existing" });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  const lines = statusOf(root);
  assert.ok(lines.some((l) => l.endsWith("WARN: profile exists, see adjudicator-profile.yaml")));
  assert.match(lines.at(-1), /DONE$/);
  assert.deepEqual(JSON.parse(readFileSync(join(wdOf7(root), "adjudicator.done"), "utf8")),
    { outputs: ["data/species/work/legacy-7/adjudicator-profile.yaml"], profileExists: true });
});

test("runPipeline skips the adjudicator on resume after a passed output check", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, calls });
  let finalCode = 1;
  const execNode = async (args) => (String(args[1]).startsWith("data/species/profiles/") ? { code: finalCode, stdout: "", stderr: "" } : fakes.execNode(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, execNode }), 1);
  assert.match(statusOf(root).at(-1), /FAILED: final validation$/);
  assert.deepEqual(JSON.parse(readFileSync(join(wdOf7(root), "adjudicator.done"), "utf8")),
    { outputs: ["data/species/profiles/fish.yaml"], profileExists: false });
  calls.length = 0;
  finalCode = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes, execNode }), 0);
  assert.deepEqual(calls.filter((c) => c.role), []);
  const lines = statusOf(root);
  assert.ok(lines.some((l) => l.endsWith("adjudicator skipped (adjudicator.done)")));
  assert.match(lines.at(-1), /DONE$/);
});

test("runPipeline reruns the adjudicator on resume when its recorded output is missing", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  rmSync(join(root, "data/species/profiles/fish.yaml"));
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["adjudicator"]);
});

test("runPipeline keeps the research files and reruns the adjudicator when only the note differs", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "first" }), 0);
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "second" }), 0);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["adjudicator"]);
  assert.match(calls.find((c) => c.role === "adjudicator").prompt, /second/);
  const wd = wdOf7(root);
  assert.ok(!readdirSync(wd).some((e) => e.startsWith("previous-")));
  assert.equal(JSON.parse(readFileSync(join(wd, "run.json"), "utf8")).adjudicatorNote, "second");
  assert.ok(statusOf(root).some((l) => l.endsWith("adjudicator note changed")));
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "second" }), 0);
  assert.deepEqual(calls.filter((c) => c.role), []);
});

test("runGroupsync fails with a message when the agent leaves no last message", async () => {
  for (const write of [null, "", "  \n"]) {
    const root = pipelineRoot();
    const out = [];
    const errors = [];
    const code = await runGroupsync({
      root,
      runner: "codex",
      execNode: async () => ({ code: 0, stdout: "WARNING data/species/profiles/a-b.yaml /f: hybrid groups naming X are missing: g1\n", stderr: "" }),
      execGit: async () => ({ code: 0, stdout: "", stderr: "" }),
      runAgentImpl: async ({ logDir }) => {
        if (write !== null) writeFileSync(join(logDir, "groupsync.last"), write);
        return { status: "ok", model: "m", attempts: 1 };
      },
      out: (t) => out.push(t),
      err: (t) => errors.push(t),
    });
    assert.equal(code, 1);
    assert.deepEqual(out, []);
    assert.match(errors[0], /groupsync: the agent left no last message \(.*data\/species\/work\/groupsync\/logs\/groupsync\.log\)/);
  }
});

test("runGroupsync reports a failed agent and an account out of credits", async () => {
  for (const [status, expected] of [["failed", 1], ["out-of-credits", 3]]) {
    const root = pipelineRoot();
    const errors = [];
    const code = await runGroupsync({
      root,
      runner: "claude",
      execNode: async () => ({ code: 0, stdout: "WARNING data/species/profiles/a-b.yaml /f: hybrid groups naming X are missing: g1\n", stderr: "" }),
      runAgentImpl: async () => ({ status, model: "m", attempts: 1 }),
      out: () => {},
      err: (t) => errors.push(t),
    });
    assert.equal(code, expected);
    assert.equal(errors.length, 1);
  }
});

test("main exits with 2 and the usage on invalid arguments", async (t) => {
  const errors = [];
  t.mock.method(console, "error", (text) => errors.push(String(text)));
  const run = ["run", "--runner", "codex", "--name", "Fish", "--hint", "h"];
  const cases = [
    [...run],
    [...run, "--id", "7x"],
    [...run, "--id", "../legacy-7"],
    [...run, "--id", "-1"],
    [...run, "--id", ""],
    ["run", "--runner", "gemini", "--id", "7", "--name", "Fish", "--hint", "h"],
    ["groupsync"],
    ["next", "--count", "0"],
    ["run", "--unknown"],
    ["poet"],
    [],
  ];
  for (const argv of cases) {
    errors.length = 0;
    assert.equal(await main(argv), 2, JSON.stringify(argv));
    assert.ok(errors.some((e) => e.startsWith("Usage:")), JSON.stringify(argv));
  }
  errors.length = 0;
  await main([...run, "--id", "7x"]);
  assert.equal(errors[0], "--id must be a legacy catalog number");
});

test("runPipeline reruns the adjudicator after a note-only rerun that stopped before it", async () => {
  const root = pipelineRoot();
  const calls = [];
  let failTally = false;
  const fakes = makeFakes({ root, calls, tallyFails: () => failTally });
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "note-A" }), 0);
  failTally = true;
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "note-B" }), 1);
  assert.ok(!existsSync(join(wdOf7(root), "adjudicator.done")));
  assert.ok(statusOf(root).some((l) => l.endsWith("adjudicator note changed")));
  failTally = false;
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "note-B" }), 0);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["adjudicator"]);
  assert.match(calls.find((c) => c.role === "adjudicator").prompt, /note-B/);
});

test("runPipeline reruns the adjudicator after a redone role file in a run that stopped before it", async () => {
  const root = pipelineRoot();
  const calls = [];
  let failTally = false;
  const fakes = makeFakes({ root, calls, tallyFails: () => failTally });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  rmSync(join(wdOf7(root), "checker-b.yaml"));
  failTally = true;
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 1);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["checker-b"]);
  assert.ok(!existsSync(join(wdOf7(root), "adjudicator.done")));
  failTally = false;
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 0);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["adjudicator"]);
});

test("runPipeline counts no profile of another species written during the adjudicator step", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, adjudicatorWrites: "nothing", foreignProfile: true });
  assert.equal(await runPipeline({ ...base, root, ...fakes }), 1);
  assert.match(statusOf(root).at(-1), /FAILED: adjudicator wrote no profile$/);
  assert.ok(!existsSync(join(wdOf7(root), "adjudicator.done")));
});

test("runPipeline records only the own profile when another species' profile changes too", async () => {
  const root = pipelineRoot();
  writeFileSync(join(root, "data/species/profiles/broken.yaml"), "legacyIds: [7\n");
  const fakes = makeFakes({ root, foreignProfile: true });
  const runAgentImpl = async (args) => {
    const result = await fakes.runAgentImpl(args);
    // An unparsable file that changes during the step does not count either.
    if (args.role === "adjudicator") writeFileSync(join(root, "data/species/profiles/broken.yaml"), "legacyIds: [7, \n");
    return result;
  };
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 0);
  assert.deepEqual(JSON.parse(readFileSync(join(wdOf7(root), "adjudicator.done"), "utf8")).outputs, ["data/species/profiles/fish.yaml"]);
});

test("runPipeline reports a failed adjudicator as FAILED: adjudicator failed", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => (args.role === "adjudicator" ? { status: "failed", model: "m", attempts: 5 } : fakes.runAgentImpl(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  assert.match(statusOf(root).at(-1), /FAILED: adjudicator failed$/);
});

test("runPipeline logs a changed note only after an earlier adjudicator run", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, credits: ["adjudicator"] });
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "A" }), 3);
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "B" }), 3);
  assert.ok(!statusOf(root).some((l) => l.includes("adjudicator note changed")));
});

test("runPipeline removes the adjudicator record of a changed note before run.json takes the note", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, calls });
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "note-A" }), 0);
  // The first clock read of the run is the set-aside stamp; a throw there stops the run before
  // prepareWorkdir writes run.json.
  let reads = 0;
  const now = () => {
    reads += 1;
    if (reads === 1) throw new Error("clock");
    return new Date();
  };
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "note-B", now }), 1);
  assert.equal(JSON.parse(readFileSync(join(wdOf7(root), "run.json"), "utf8")).adjudicatorNote, "note-A");
  assert.ok(!existsSync(join(wdOf7(root), "adjudicator.done")));
  calls.length = 0;
  assert.equal(await runPipeline({ ...base, root, ...fakes, adjudicatorNote: "note-B" }), 0);
  assert.deepEqual(calls.filter((c) => c.role).map((c) => c.role), ["adjudicator"]);
});
