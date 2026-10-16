import assert from "node:assert/strict";
import {
  chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, rmSync, statSync,
  symlinkSync, writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { EventEmitter } from "node:events";
import { spawnSync } from "node:child_process";
import {
  main, nextSpecies, nextTiebreakerNumbers, parseGroupsyncWarnings, prepareWorkdir, runGroupsync, runPipeline, statusRows,
  workRootOf,
} from "./pipeline.mjs";

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

test("prepareWorkdir refuses a run.json that is a symlink", () => {
  const dir = tmp();
  const wd = join(dir, "wd");
  mkdirSync(wd);
  writeFileSync(join(dir, "elsewhere.json"), "keep");
  symlinkSync(join(dir, "elsewhere.json"), join(wd, "run.json"));
  assert.throws(() => prepareWorkdir({ wd, manifest, now: stamp }), /refusing to write through the symlink/);
  assert.equal(readFileSync(join(dir, "elsewhere.json"), "utf8"), "keep");
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

test("statusRows adds the summed cost and the billing of the latest claude attempt", () => {
  const root = makeSpeciesRoot({ runningStatus: "08:00 DONE\n" });
  const logs = join(root, "data", "species", "work", "legacy-3", "logs");
  writeFileSync(join(logs, "generator.log"),
    "=== attempt 1 gpt-6.1-sol 2026-10-16T08:00:00.000Z\n=== exit 0\n");
  assert.deepEqual(statusRows({ root }), ["3 Name 3 | 08:00 DONE"]);
  writeFileSync(join(logs, "checker-a.log"),
    "=== attempt 1 claude-sonnet-5-5 2026-10-16T08:01:00.000Z billing subscription\n=== exit 0\n=== cost 0.5 USD\n");
  writeFileSync(join(logs, "checker-b.log"),
    "=== attempt 1 claude-sonnet-5-5 2026-10-16T08:02:00.000Z billing api-key\n=== exit 1\n=== cost 0.25 USD\n" +
    "=== attempt 2 claude-sonnet-5-5 2026-10-16T08:03:00.000Z billing api-key\n=== exit 0\n=== cost 0.5 USD\n");
  assert.deepEqual(statusRows({ root }), ["3 Name 3 | 08:00 DONE | cost 1.2500 USD | billing api-key"]);
});

test("statusRows shows the billing alone when no attempt reported a cost", () => {
  const root = makeSpeciesRoot({ runningStatus: "08:00 DONE\n" });
  writeFileSync(join(root, "data", "species", "work", "legacy-3", "logs", "checker-a.log"),
    "=== attempt 1 claude-sonnet-5-5 2026-10-16T08:01:00.000Z billing subscription\nnoise\n=== exit 1\n");
  assert.deepEqual(statusRows({ root }), ["3 Name 3 | 08:00 DONE | billing subscription"]);
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

// No test depends on the git state of the machine.
const noGit = () => {
  throw new Error("not a git repository");
};

const base = { runner: "codex", legacyId: "7", name: "Fish", hint: "hint", gitExec: noGit };

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

test("runPipeline ends with exit 1 when an agent replaced the log directory by a symlink", async () => {
  const root = pipelineRoot();
  const wd = join(root, "data", "species", "work", "legacy-7");
  const elsewhere = join(tmp(), "elsewhere");
  mkdirSync(elsewhere);
  mkdirSync(wd, { recursive: true });
  symlinkSync(elsewhere, join(wd, "logs"));
  const errors = [];
  const fakes = makeFakes({ root });
  assert.equal(await runPipeline({ ...base, root, ...fakes, err: (t) => errors.push(t) }), 1);
  assert.match(errors.join("\n"), /FAILED: crashed \(refusing to write through the symlink/);
  assert.deepEqual(readdirSync(elsewhere), []);
});

test("workRootOf: a symlinked data/species is refused and nothing is created behind it", () => {
  const root = realpathSync(tmp());
  mkdirSync(join(root, "data"));
  const target = realpathSync(tmp());
  symlinkSync(target, join(root, "data", "species"));
  assert.throws(() => workRootOf(root, noGit), /refusing to write through the symlink .*species/);
  assert.deepEqual(readdirSync(target), []);
});

test("workRootOf: a missing work directory is created below data and returned as a real path", () => {
  const root = realpathSync(tmp());
  const created = workRootOf(root, noGit);
  assert.equal(created, join(root, "data", "species", "work"));
  assert.ok(statSync(created).isDirectory());
});

test("workRootOf: a dangling work link is refused without creating its target", () => {
  const root = realpathSync(pipelineRoot());
  const target = join(tmp(), "gone");
  symlinkSync(target, join(root, "data", "species", "work"));
  assert.throws(() => workRootOf(root, noGit), /refusing to write through the symlink .*work/);
  assert.ok(!existsSync(target));
});

// A worktree root whose data/species/work is a symlink to the work directory of a main checkout,
// and a git that names that checkout. Returns { root, main, work, gitExec }.
function linkedRoot() {
  const main = realpathSync(tmp());
  const work = join(main, "data", "species", "work");
  mkdirSync(work, { recursive: true });
  mkdirSync(join(main, ".git"));
  const root = realpathSync(pipelineRoot());
  symlinkSync(work, join(root, "data", "species", "work"));
  const gitExec = (cmd, args) => `${args[1] === "--git-common-dir" ? join(main, ".git") : join(main, ".git", "worktrees", "w")}\n`;
  return { root, main, work, gitExec };
}

test("workRootOf: the real work directory, the one of the main checkout, a created one; a retargeted link fails", () => {
  const plain = realpathSync(pipelineRoot());
  assert.equal(workRootOf(plain, noGit), join(plain, "data", "species", "work"));
  assert.ok(existsSync(join(plain, "data", "species", "work")));
  assert.equal(workRootOf(plain, noGit), join(plain, "data", "species", "work"));
  const linked = linkedRoot();
  assert.equal(workRootOf(linked.root, linked.gitExec), linked.work);
  for (const target of [tmp(), linked.root, linked.main, join(linked.main, "data")]) {
    const wrong = realpathSync(pipelineRoot());
    symlinkSync(target, join(wrong, "data", "species", "work"));
    assert.throws(() => workRootOf(wrong, noGit), /data\/species\/work resolves to .* it must be /);
  }
});

test("runPipeline builds every path from the real work directory and writes below it as a trusted base", async () => {
  const linked = linkedRoot();
  const seen = [];
  const calls = [];
  const fakes = makeFakes({ root: linked.root, calls });
  const wd = join(linked.work, "legacy-7");
  const runAgentImpl = async (options) => {
    seen.push({ logDir: options.logDir, logBase: options.logBase, promptPath: options.promptPath });
    return fakes.runAgentImpl(options);
  };
  const execNode = async (args) => {
    seen.push({ node: args[1] });
    return fakes.execNode(args);
  };
  assert.equal(await runPipeline({ ...base, root: linked.root, gitExec: linked.gitExec, ...fakes, runAgentImpl, execNode }), 0);
  for (const entry of seen.filter((e) => e.logDir)) {
    assert.equal(entry.logDir, join(wd, "logs"));
    assert.equal(entry.logBase, linked.work);
    assert.ok(entry.promptPath.startsWith(`${wd}/`));
  }
  assert.ok(seen.filter((e) => e.node).every((e) => e.node.startsWith(linked.work) || e.node.includes(".yaml")));
  assert.ok(existsSync(join(wd, "logs", "status")));
});

test("runPipeline refuses to start when data/species/work was retargeted, and starts no agent", async () => {
  const root = realpathSync(pipelineRoot());
  const elsewhere = realpathSync(tmp());
  symlinkSync(elsewhere, join(root, "data", "species", "work"));
  const errors = [];
  const fakes = makeFakes({ root });
  assert.equal(await runPipeline({ ...base, root, ...fakes, err: (t) => errors.push(t) }), 1);
  assert.match(errors.join("\n"), /^legacy-7 cannot start: .*data\/species\/work resolves to /);
  assert.deepEqual(readdirSync(elsewhere), []);
});

test("runPipeline refuses to start when the work directory of the species is a symlink", async () => {
  const root = realpathSync(pipelineRoot());
  const elsewhere = realpathSync(tmp());
  mkdirSync(join(root, "data", "species", "work"));
  symlinkSync(elsewhere, join(root, "data", "species", "work", "legacy-7"));
  const errors = [];
  assert.equal(await runPipeline({ ...base, root, ...makeFakes({ root }), err: (t) => errors.push(t) }), 1);
  assert.match(errors.join("\n"), /^legacy-7 cannot start: refusing to write through the symlink/);
  assert.deepEqual(readdirSync(elsewhere), []);
});

test("runPipeline: a role directory that an agent replaced by a symlink ends the run and creates nothing behind it", async () => {
  const root = realpathSync(pipelineRoot());
  const wd = join(root, "data", "species", "work", "legacy-7");
  const elsewhere = realpathSync(tmp());
  mkdirSync(wd, { recursive: true });
  symlinkSync(elsewhere, join(wd, "tmp-generator"));
  const errors = [];
  assert.equal(await runPipeline({ ...base, root, ...makeFakes({ root }), err: (t) => errors.push(t) }), 1);
  assert.match(readFileSync(join(wd, "logs", "status"), "utf8"), /FAILED: crashed \(refusing to write through the symlink/);
  assert.deepEqual(readdirSync(elsewhere), []);
});

test("runGroupsync: the agent files lie below the real work directory, which is the trusted base", async () => {
  const linked = linkedRoot();
  const seen = [];
  const code = await runGroupsync({
    root: linked.root, runner: "claude", gitExec: linked.gitExec,
    execNode: async () => ({ code: 0, stdout: "WARNING data/species/profiles/a.yaml hybrid groups naming x are missing: g1", stderr: "" }),
    execGit: async () => ({ stdout: "" }), out: () => {},
    runAgentImpl: async (options) => {
      seen.push(options);
      writeFileSync(join(options.logDir, "groupsync.last"), "synced");
      return { status: "ok", model: "m", attempts: 1 };
    },
  });
  assert.equal(code, 0);
  assert.equal(seen[0].logDir, join(linked.work, "groupsync", "logs"));
  assert.equal(seen[0].logBase, linked.work);
});

test("runGroupsync: a retargeted work directory or a symlinked groupsync directory fails with a message", async () => {
  const root = realpathSync(pipelineRoot());
  const elsewhere = realpathSync(tmp());
  mkdirSync(join(root, "data", "species", "work"));
  symlinkSync(elsewhere, join(root, "data", "species", "work", "groupsync"));
  const execNode = async () => ({ code: 0, stdout: "WARNING data/species/profiles/a.yaml hybrid groups naming x are missing: g1", stderr: "" });
  const runAgentImpl = async () => assert.fail("no agent starts");
  const first = [];
  assert.equal(await runGroupsync({ root, runner: "claude", gitExec: noGit, execNode, runAgentImpl, err: (t) => first.push(t) }), 1);
  assert.match(first[0], /^groupsync cannot start: refusing to write through the symlink/);
  assert.deepEqual(readdirSync(elsewhere), []);
  const wrong = realpathSync(pipelineRoot());
  symlinkSync(elsewhere, join(wrong, "data", "species", "work"));
  const errors = [];
  assert.equal(await runGroupsync({ root: wrong, runner: "claude", gitExec: noGit, execNode, runAgentImpl, err: (t) => errors.push(t) }), 1);
  assert.match(errors[0], /^groupsync cannot start: .*resolves to /);
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

test("runPipeline ends with exit 1 and a FAILED line when an API key is rejected", async () => {
  const root = pipelineRoot();
  const calls = [];
  const fakes = makeFakes({ root, calls });
  const runAgentImpl = async (args) => (["checker-a", "checker-c"].includes(args.role)
    ? { status: "auth-failed", model: "m", attempts: 1, billing: "api-key" }
    : fakes.runAgentImpl(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  assert.ok(!calls.some((c) => c.node === "tally.mjs"));
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines.at(-1), /^\d\d:\d\d FAILED: checker-a, checker-c authentication failed \(check the API key file\)$/);
});

test("runPipeline ends with exit 1 and a FAILED line that names the hits of a failed audit", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, credits: ["checker-c"] });
  const runAgentImpl = async (args) => (args.role === "checker-a"
    ? { status: "audit-failed", model: "m", attempts: 1, billing: "api-key", detail: "/r/data/.claude (created), /r/data/x (symlink to /home)" }
    : fakes.runAgentImpl(args));
  // An audit failure outranks a role that ran out of credits in the same step.
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines.at(-1), /^\d\d:\d\d FAILED: checker-a audit failed \(\/r\/data\/\.claude \(created\), \/r\/data\/x \(symlink to \/home\)\)$/);
});

test("runPipeline reports the hits of a failed audit to err when another role crashes the run", async () => {
  const root = pipelineRoot();
  const wd = join(root, "data", "species", "work", "legacy-7");
  const elsewhere = join(tmp(), "status-target");
  writeFileSync(elsewhere, "keep");
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => {
    if (args.role !== "checker-a") return fakes.runAgentImpl(args);
    // The agent replaces the status file by a symlink, so that the status line of every other role is refused.
    rmSync(join(wd, "logs", "status"));
    symlinkSync(elsewhere, join(wd, "logs", "status"));
    return { status: "audit-failed", model: "m", attempts: 1, billing: "api-key", detail: "/r/data/.claude (created)" };
  };
  const errors = [];
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl, err: (t) => errors.push(t) }), 1);
  assert.equal(errors.length, 2);
  assert.match(errors[0], /^FAILED: crashed \(refusing to write through the symlink/);
  assert.equal(errors[1], "FAILED: checker-a audit failed (/r/data/.claude (created))");
  assert.equal(readFileSync(elsewhere, "utf8"), "keep");
});

test("runPipeline reports the hits of a failed audit to err when the status file refuses the final line", async () => {
  const root = pipelineRoot();
  const wd = join(root, "data", "species", "work", "legacy-7");
  mkdirSync(wd, { recursive: true });
  for (const role of ["generator", "checker-a", "checker-b", "checker-c"]) writeFileSync(join(wd, `${role}.yaml`), "ok");
  writeFileSync(join(wd, "run.json"), JSON.stringify({ legacyId: "7", name: "Fish", hint: "hint", lake: "", legacyIds: "", adjudicatorNote: "" }));
  const elsewhere = join(tmp(), "status-target");
  writeFileSync(elsewhere, "keep");
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => {
    assert.equal(args.role, "adjudicator");
    rmSync(join(wd, "logs", "status"));
    symlinkSync(elsewhere, join(wd, "logs", "status"));
    return { status: "audit-failed", model: "m", attempts: 1, billing: "api-key", detail: "/r/data/CLAUDE.md (created)" };
  };
  const errors = [];
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl, err: (t) => errors.push(t) }), 1);
  assert.deepEqual(errors, ["FAILED: adjudicator audit failed (/r/data/CLAUDE.md (created))"]);
  assert.equal(readFileSync(elsewhere, "utf8"), "keep");
});

test("runPipeline words the authentication hint by billing", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => (args.role === "checker-a"
    ? { status: "auth-failed", model: "m", attempts: 1, billing: "subscription" }
    : fakes.runAgentImpl(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 1);
  const lines = readFileSync(join(root, "data/species/work/legacy-7/logs/status"), "utf8").trim().split("\n");
  assert.match(lines.at(-1), /FAILED: checker-a authentication failed \(check the claude login\)$/);
});

test("runPipeline reports out of credits before a rejected key", async () => {
  const root = pipelineRoot();
  const fakes = makeFakes({ root, credits: ["checker-c"] });
  const runAgentImpl = async (args) => (args.role === "checker-a" ? { status: "auth-failed", model: "m", attempts: 1 } : fakes.runAgentImpl(args));
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl }), 3);
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

test("runGroupsync reports a failed audit with the hits", async () => {
  const errors = [];
  const code = await runGroupsync({
    root: pipelineRoot(),
    runner: "claude",
    execNode: async () => ({ code: 0, stdout: "WARNING data/species/profiles/a-b.yaml /f: hybrid groups naming X are missing: g1\n", stderr: "" }),
    runAgentImpl: async () => ({ status: "audit-failed", model: "m", attempts: 1, detail: "/r/data/.git (created)" }),
    out: () => {},
    err: (t) => errors.push(t),
  });
  assert.equal(code, 1);
  assert.match(errors[0], /^groupsync: audit failed \(\/r\/data\/\.git \(created\)\) \(.*groupsync\.log\)$/);
});

test("runGroupsync reports a failed agent, an account out of credits and a rejected key", async () => {
  for (const [status, expected] of [["failed", 1], ["out-of-credits", 3], ["auth-failed", 1]]) {
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

test("main requires --billing for run and groupsync before anything starts", async (t) => {
  const errors = [];
  t.mock.method(console, "error", (text) => errors.push(String(text)));
  const dir = tmp();
  const empty = join(dir, "empty.key");
  writeFileSync(empty, "");
  const blank = join(dir, "blank.key");
  writeFileSync(blank, " \n");
  const keyDir = join(dir, "keydir");
  mkdirSync(keyDir);
  const present = join(dir, "present.key");
  writeFileSync(present, "dummy-key");
  const locked = join(dir, "locked.key");
  writeFileSync(locked, "dummy-key");
  chmodSync(locked, 0o000);
  const run = ["run", "--runner", "codex", "--id", "7", "--name", "Fish", "--hint", "h"];
  const group = ["groupsync", "--runner", "claude"];
  const bad = [
    [run, {}, /^missing --billing/],
    [group, {}, /^missing --billing/],
    [[...run, "--billing", "both"], {}, /^--billing must be one of/],
    [[...group, "--billing", ""], {}, /^(missing --billing|--billing must be one of)/],
    [[...run, "--billing", "api-key"], { CICHLIDS_ANTHROPIC_KEY_FILE: join(dir, "absent.key") }, /absent\.key does not exist/],
    [[...group, "--billing", "api-key"], { CICHLIDS_ANTHROPIC_KEY_FILE: empty }, /empty\.key is empty/],
    [[...run, "--billing", "api-key"], { CICHLIDS_ANTHROPIC_KEY_FILE: blank }, /blank\.key is empty/],
    [[...run, "--billing", "api-key"], { CICHLIDS_ANTHROPIC_KEY_FILE: keyDir }, /keydir is not a regular file/],
    ...(process.getuid?.() === 0 ? [] : [[[...run, "--billing", "api-key"], { CICHLIDS_ANTHROPIC_KEY_FILE: locked }, /locked\.key is not readable/]]),
  ];
  const credentialsFile = join(dir, "credentials.json");
  writeFileSync(credentialsFile, JSON.stringify({ claudeAiOauth: { accessToken: "dummy-access", expiresAt: Date.now() + 3_600_000 } }));
  const calls = [];
  const impls = { credentialsFile, runPipelineImpl: async (o) => { calls.push(o); return 0; }, runGroupsyncImpl: async (o) => { calls.push(o); return 0; } };
  for (const [argv, env, message] of bad) {
    errors.length = 0;
    assert.equal(await main(argv, { env, ...impls }), 2, JSON.stringify(argv));
    assert.match(errors[0], message);
    assert.ok(errors.some((e) => e.startsWith("Usage:")));
  }
  assert.deepEqual(calls, []);
  // Valid choices reach the implementation with the billing.
  assert.equal(await main([...run, "--billing", "subscription"], { env: {}, ...impls }), 0);
  assert.equal(await main([...group, "--billing", "api-key"], { env: { CICHLIDS_ANTHROPIC_KEY_FILE: present }, ...impls }), 0);
  assert.deepEqual(calls.map((c) => [c.runner, c.billing]), [["codex", "subscription"], ["claude", "api-key"]]);
});

test("main: subscription billing needs a login file with an access token that has not expired", async (t) => {
  const errors = [];
  t.mock.method(console, "error", (text) => errors.push(String(text)));
  const dir = tmp();
  const credentialsFile = join(dir, "credentials.json");
  const calls = [];
  const impls = { runPipelineImpl: async (o) => { calls.push(o); return 0; } };
  const run = ["run", "--runner", "claude", "--billing", "subscription", "--id", "7", "--name", "Fish", "--hint", "h"];
  assert.equal(await main(run, { env: {}, credentialsFile, ...impls }), 2);
  assert.match(errors[0], /needs a usable claude login: .*credentials\.json does not exist/);
  writeFileSync(credentialsFile, JSON.stringify({ claudeAiOauth: { accessToken: "dummy-access", expiresAt: Date.now() - 1000 } }));
  errors.length = 0;
  assert.equal(await main(run, { env: {}, credentialsFile, ...impls }), 2);
  assert.match(errors[0], /has an expired access token/);
  assert.deepEqual(calls, []);
});

test("runPipeline and runGroupsync hand the billing to every agent", async () => {
  const root = pipelineRoot();
  const seen = [];
  const fakes = makeFakes({ root });
  const runAgentImpl = async (args) => { seen.push(args.billing); return fakes.runAgentImpl(args); };
  assert.equal(await runPipeline({ ...base, root, ...fakes, runAgentImpl, billing: "api-key" }), 0);
  assert.ok(seen.length > 0 && seen.every((b) => b === "api-key"));
  const groupSeen = [];
  await runGroupsync({
    root: pipelineRoot(), runner: "claude", billing: "subscription",
    execNode: async () => ({ code: 0, stdout: "WARNING data/species/profiles/a-b.yaml /f: hybrid groups naming X are missing: g1\n", stderr: "" }),
    runAgentImpl: async (a) => { groupSeen.push(a.billing); return { status: "failed", model: "m", attempts: 1 }; },
    out: () => {}, err: () => {},
  });
  assert.deepEqual(groupSeen, ["subscription"]);
});

test("main refuses managed settings that select credentials before anything starts", async (t) => {
  const errors = [];
  t.mock.method(console, "error", (text) => errors.push(String(text)));
  const dir = tmp();
  const managed = join(dir, "managed.json");
  writeFileSync(managed, JSON.stringify({ env: { ANTHROPIC_API_KEY: "x" } }));
  const calls = [];
  const credentialsFile = join(dir, "credentials.json");
  writeFileSync(credentialsFile, JSON.stringify({ claudeAiOauth: { accessToken: "dummy-access", expiresAt: Date.now() + 3_600_000 } }));
  const code = await main(["groupsync", "--runner", "claude", "--billing", "subscription"], {
    env: {}, credentialsFile, managedFile: managed, runGroupsyncImpl: async (o) => { calls.push(o); return 0; },
  });
  assert.equal(code, 2);
  assert.match(errors[0], /sets ANTHROPIC_API_KEY in env/);
  assert.deepEqual(calls, []);
});

test("next and status take no --billing or --runner option", async (t) => {
  const errors = [];
  t.mock.method(console, "error", (text) => errors.push(String(text)));
  for (const argv of [["next", "--billing", "api-key"], ["status", "--billing", "subscription"], ["next", "--runner", "claude"], ["status", "--runner", "codex"]]) {
    errors.length = 0;
    assert.equal(await main(argv), 2, JSON.stringify(argv));
    assert.match(errors[0], /takes no --(billing|runner) option/);
  }
});

test("runGroupsync reports a runner that throws and exits with 1", async () => {
  const errors = [];
  const code = await runGroupsync({
    root: pipelineRoot(), runner: "claude", billing: "api-key",
    execNode: async () => ({ code: 0, stdout: "WARNING data/species/profiles/a-b.yaml /f: hybrid groups naming X are missing: g1\n", stderr: "" }),
    runAgentImpl: async () => { throw new Error("api-key billing needs a usable key file: /k is empty"); },
    out: () => {}, err: (t) => errors.push(t),
  });
  assert.equal(code, 1);
  assert.match(errors[0], /^groupsync: the agent could not start \(api-key billing needs a usable key file/);
});

test("main exits with 2 and the usage on invalid arguments", async (t) => {
  const errors = [];
  t.mock.method(console, "error", (text) => errors.push(String(text)));
  const run = ["run", "--runner", "codex", "--billing", "subscription", "--name", "Fish", "--hint", "h"];
  const cases = [
    [...run],
    [...run, "--id", "7x"],
    [...run, "--id", "../legacy-7"],
    [...run, "--id", "-1"],
    [...run, "--id", ""],
    ["run", "--runner", "gemini", "--billing", "subscription", "--id", "7", "--name", "Fish", "--hint", "h"],
    ["groupsync", "--billing", "subscription"],
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
