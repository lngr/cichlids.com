#!/usr/bin/env node
// Runs the species profile pipeline for one legacy catalog entry and offers the helper commands
// around it.
//
// Usage:
//   node tools/species-data/pipeline.mjs run --runner <codex|claude>
//        --billing <api-key|subscription>
//        --id <legacyId> --name "<legacy name>" --hint "<hint>" [--lake <lake>]
//        [--legacy-ids "a, b"] [--adjudicator-note "..."] [--fresh]
//   node tools/species-data/pipeline.mjs groupsync --runner <codex|claude>
//        --billing <api-key|subscription>
//   node tools/species-data/pipeline.mjs next [--count N]
//   node tools/species-data/pipeline.mjs status
//
// A run writes everything to data/species/work/legacy-<id>/ (logs in logs/, status lines in
// logs/status) and holds run.lock there while it runs. Exit codes of run: 0 done, 1 failed or
// locked by another process (also an agent whose credential is rejected or whose audit fails), 2
// usage, 3 an agent account is out of credits.
// data/species/work is a symlink into another checkout in a worktree. The run validates it once
// (see workRootOf) and builds every path below it from the real path, and every write below it
// refuses a symlink (see safe-write.mjs), because the agents can write there.
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import {
  existsSync, readFileSync, readdirSync, realpathSync, renameSync, rmSync, statSync,
} from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { parse } from "yaml";
import { gitInfo, validateWorkDir } from "./agent-cage.mjs";
import {
  RUNNERS, billingError, defaultApiKeyFile, defaultCredentialsFile, resolveModel, runAgent,
} from "./agent-runner.mjs";
import { LOCK_FILE, acquireLock, lockState, releaseLock } from "./pipeline-lock.mjs";
import { buildGroupsyncPrompt, buildPrompt, pendingFeatures } from "./pipeline-prompts.mjs";
import { safeAppendFile, safeMkdir, safeWriteFile } from "./safe-write.mjs";

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

const USAGE = `Usage:
  pipeline.mjs run --runner <codex|claude> --billing <api-key|subscription> --id <legacyId>
               --name "<legacy name>" --hint "<hint>" [--lake <lake>] [--legacy-ids "a, b"]
               [--adjudicator-note "..."] [--fresh]
  pipeline.mjs groupsync --runner <codex|claude> --billing <api-key|subscription>
  pipeline.mjs next [--count N]
  pipeline.mjs status`;

const ROLE_FILE = /^(generator|checker-[a-c]|tiebreaker-\d+|tally|adjudicator-profile)\.yaml$/;
const VOTING_ROLES = ["generator", "checker-a", "checker-b", "checker-c"];
const TALLY_ROUNDS = 3;

const pad = (n) => String(n).padStart(2, "0");
const workDirOf = (workRoot, legacyId) => join(workRoot, `legacy-${legacyId}`);

/**
 * Returns the validated real path of <root>/data/species/work and creates the directory when it
 * does not exist. The path is a real directory of the checkout or a symlink into the main
 * checkout; any other target throws (see validateWorkDir). A missing directory is created below
 * <root>/data (a directory of the read-only repository bind, so a trusted base) without following
 * a symlink, and the result is validated. exec is injectable for the git calls.
 */
export function workRootOf(root, exec) {
  const realRoot = realpathSync(root);
  const git = gitInfo(realRoot, exec);
  const real = validateWorkDir(realRoot, git);
  if (real) return real;
  safeMkdir(join(realRoot, "data", "species", "work"), join(realRoot, "data"));
  const created = validateWorkDir(realRoot, git);
  if (!created) throw new Error(`${join(realRoot, "data", "species", "work")} cannot be created`);
  return created;
}

// Run manifest: the inputs of a run. The research identity (every field except adjudicatorNote)
// decides whether existing role files belong to the same run; the adjudicator note affects the
// adjudicator only.
function buildManifest({ legacyId, name, hint, lake, legacyIds, adjudicatorNote }) {
  return {
    legacyId: String(legacyId),
    name,
    hint,
    lake: lake || "",
    legacyIds: legacyIds || "",
    adjudicatorNote: adjudicatorNote || "",
  };
}

const researchIdentity = (m) =>
  JSON.stringify([String(m.legacyId ?? ""), m.name ?? "", m.hint ?? "", m.lake || "", m.legacyIds || ""]);

/**
 * Prepares the work directory and writes run.json. Role files of a different run (missing
 * manifest, differing research identity, or fresh) move with everything else except .literature
 * and the run lock files (run.lock*) into previous-<YYYYMMDD-HHMMSS>/. Returns the name of that
 * directory or null. Writes refuse a symlink below base (default: the directory of wd).
 */
export function prepareWorkdir({
  wd, manifest, fresh = false, now = new Date(), base = dirname(wd),
}) {
  safeMkdir(wd, base);
  const manifestPath = join(wd, "run.json");
  const entries = readdirSync(wd);
  let previous = null;
  if (entries.some((e) => ROLE_FILE.test(e))) {
    let same = false;
    try {
      const stored = JSON.parse(readFileSync(manifestPath, "utf8"));
      same = researchIdentity(stored) === researchIdentity(manifest);
    } catch {
      same = false;
    }
    if (fresh || !same) {
      const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
      previous = `previous-${stamp}`;
      safeMkdir(join(wd, previous), base);
      for (const e of entries) {
        if (e !== ".literature" && !e.startsWith(LOCK_FILE)) {
          renameSync(join(wd, e), join(wd, previous, e));
        }
      }
    }
  }
  safeWriteFile(manifestPath, JSON.stringify(manifest, null, 2) + "\n", base);
  return previous;
}

/** Numbers of the next tie-breakers of a round, continuing after the highest existing file. */
export function nextTiebreakerNumbers(wd, count = 2) {
  let highest = 0;
  for (const e of readdirSync(wd)) {
    const m = /^tiebreaker-(\d+)\.yaml$/.exec(e);
    if (m) highest = Math.max(highest, Number(m[1]));
  }
  return Array.from({ length: count }, (_, i) => highest + 1 + i);
}

/** Turns validator output into the group sync list: one "- <file>: <ids>" line per warning. */
export function parseGroupsyncWarnings(report) {
  const lines = [];
  for (const line of report.split("\n")) {
    if (!line.includes("hybrid groups naming")) continue;
    const m = /^WARNING data\/species\/profiles\/(\S+) .*are missing: (.*)$/.exec(line);
    if (m) lines.push(`- ${m[1]}: ${m[2]}`);
  }
  return lines.join("\n");
}

function readStatusLines(wd) {
  try {
    return readFileSync(join(wd, "logs", "status"), "utf8").split("\n").filter(Boolean);
  } catch {
    return [];
  }
}

function workDirs(root) {
  const dir = join(root, "data", "species", "work");
  if (!existsSync(dir)) return [];
  return readdirSync(dir).filter((e) => e.startsWith("legacy-")).sort().map((e) => join(dir, e));
}

function readManifest(wd) {
  try {
    return JSON.parse(readFileSync(join(wd, "run.json"), "utf8"));
  } catch {
    return null;
  }
}

/**
 * Unprofiled species targets by legacy photo count, as "<profile id> | <category> | <legacy entries>"
 * rows.
 */
export function nextSpecies({ root = REPO_ROOT, count = 4 } = {}) {
  const species = join(root, "data", "species");
  const mappings = [...parse(readFileSync(join(species, "legacy-mapping.yaml"), "utf8")).mappings];
  const catalog = new Map(JSON.parse(readFileSync(join(species, "legacy-catalog.json"), "utf8")).map((c) => [c.legacyId, c]));
  const profiles = new Set(readdirSync(join(species, "profiles")).filter((f) => f.endsWith(".yaml")).map((f) => f.slice(0, -5)));
  const forms = new Map(parse(readFileSync(join(species, "trade-forms.yaml"), "utf8")).forms.map((f) => [f.id, f]));

  // A species is running while the lock of its work directory names a live process.
  const running = new Set();
  for (const wd of workDirs(root)) {
    const manifest = readManifest(wd);
    if (!manifest || lockState(wd) !== "running") continue;
    const mapped = mappings.find((m) => String(m.legacyId) === String(manifest.legacyId));
    if (mapped?.target) running.add(mapped.target);
  }

  const photos = (m) => catalog.get(m.legacyId)?.photos ?? 0;
  const seen = new Set();
  const rows = [];
  for (const x of mappings.sort((a, b) => photos(b) - photos(a))) {
    if (!x.target || profiles.has(x.target) || running.has(x.target) || seen.has(x.target)) continue;
    const form = forms.get(x.target);
    if (form && form.parents.length) continue;
    seen.add(x.target);
    const all = mappings.filter((z) => z.target === x.target).map((z) => `${z.legacyId} ${z.legacyName} (${z.match})`);
    rows.push(`${x.target} | ${catalog.get(x.legacyId)?.category ?? "?"} | ${all.join(", ")}`);
    if (rows.length >= count) break;
  }
  return rows;
}

const isFinal = (line) => {
  const text = line.replace(/^\S+ /, "");
  return text === "DONE" || text.startsWith("FAILED");
};

// Cost and billing from the role logs of a work directory: the sum of the "=== cost" lines and
// the billing of the latest claude attempt (by the ISO time in its header). Either is null when
// the logs have none.
function logTotals(wd) {
  const logDir = join(wd, "logs");
  let files = [];
  try {
    files = readdirSync(logDir).filter((f) => f.endsWith(".log"));
  } catch {
    return { cost: null, billing: null };
  }
  let cost = null;
  let latest = null;
  for (const file of files) {
    const text = readOrEmpty(join(logDir, file));
    for (const m of text.matchAll(/^=== cost (\S+) USD$/gm)) {
      const value = Number(m[1]);
      if (Number.isFinite(value)) cost = (cost ?? 0) + value;
    }
    for (const m of text.matchAll(/^=== attempt \d+ \S+ (\S+) billing (\S+)$/gm)) {
      if (latest === null || m[1] > latest.time) latest = { time: m[1], billing: m[2] };
    }
  }
  return { cost, billing: latest?.billing ?? null };
}

/**
 * The last status line of every work directory as "<id> <name> | <line>". A live lock appends
 * "(running)". A lock of a dead process, or a last line other than DONE or FAILED without a
 * lock, appends "(stale)": the run ended without writing its final line. The summed agent cost
 * of the role logs follows as " | cost <x.xxxx> USD" and the billing of the latest claude attempt
 * as " | billing <api-key|subscription>", each only when the logs have it.
 */
export function statusRows({ root = REPO_ROOT } = {}) {
  const rows = [];
  for (const wd of workDirs(root)) {
    const lines = readStatusLines(wd);
    const lock = lockState(wd);
    if (lines.length === 0 && lock === null) continue;
    const manifest = readManifest(wd);
    const id = wd.slice(wd.lastIndexOf("legacy-") + 7);
    const last = lines.at(-1) ?? "";
    const mark = lock === "running" ? " (running)" : lock === "stale" || !isFinal(last) ? " (stale)" : "";
    const { cost, billing } = logTotals(wd);
    const costPart = cost === null ? "" : ` | cost ${cost.toFixed(4)} USD`;
    const billingPart = billing === null ? "" : ` | billing ${billing}`;
    rows.push(`${id}${manifest ? ` ${manifest.name}` : ""} | ${last}${mark}${costPart}${billingPart}`);
  }
  return rows;
}

/** Runs a command in the repository root and resolves { code, stdout, stderr }. */
export function execIn(root, command, args) {
  return new Promise((done) => {
    const child = spawn(command, args, { cwd: root, stdio: ["ignore", "pipe", "pipe"] });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (d) => (stdout += d));
    child.stderr.on("data", (d) => (stderr += d));
    child.on("error", (e) => done({ code: 1, stdout, stderr: stderr + String(e) }));
    child.on("close", (code) => done({ code: code ?? 1, stdout, stderr }));
  });
}

const profileFiles = (root) =>
  readdirSync(join(root, "data", "species", "profiles"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => `data/species/profiles/${f}`);

const ADJUDICATOR_DONE = "adjudicator.done";

function readOrEmpty(path) {
  try {
    return readFileSync(path, "utf8");
  } catch {
    return "";
  }
}

// Modification time and content hash of every profile and of <wd>/adjudicator-profile.yaml,
// keyed by the path relative to root. The hash detects a rewrite within one tick of the coarse
// file timestamps; the time detects a rewrite with equal content.
function outputSnapshot(root, wd) {
  const files = [...profileFiles(root).map((f) => join(root, f)), join(wd, "adjudicator-profile.yaml")];
  const snapshot = new Map();
  for (const file of files) {
    try {
      const hash = createHash("sha256").update(readFileSync(file)).digest("hex");
      snapshot.set(relative(root, file), `${statSync(file).mtimeMs} ${hash}`);
    } catch {
      // A file that does not exist has no entry.
    }
  }
  return snapshot;
}

// True when the profile file parses and its top-level legacyIds contain legacyId.
function namesLegacyId(file, legacyId) {
  try {
    const ids = parse(readFileSync(file, "utf8"))?.legacyIds;
    return Array.isArray(ids) && ids.some((id) => String(id) === String(legacyId));
  } catch {
    return false;
  }
}

/**
 * Runs the adjudicator and checks its output: a non-empty logs/adjudicator.last and either a
 * profile in data/species/profiles/ whose top-level legacyIds contain legacyId or
 * <wd>/adjudicator-profile.yaml, created or changed by this run. Profiles of other species that
 * parallel runs write in the meantime do not count. A passed check is recorded in
 * <wd>/adjudicator.done with the written files. A resume skips the adjudicator while that record
 * exists, its files exist and adjudicator.last is not empty; runSteps removes the record when the
 * note changes or a voting role or tie-breaker runs. Returns an exit code or null.
 */
async function adjudicate({ root, wd, workRoot, logDir, legacyId, status, runRoles }) {
  const donePath = join(wd, ADJUDICATOR_DONE);
  const lastPath = join(logDir, "adjudicator.last");
  const warnExists = () => status("WARN: profile exists, see adjudicator-profile.yaml");
  let done = null;
  try {
    done = JSON.parse(readFileSync(donePath, "utf8"));
  } catch {
    done = null;
  }
  const doneValid = done && Array.isArray(done.outputs) && done.outputs.length > 0 &&
    done.outputs.every((f) => existsSync(join(root, f))) && readOrEmpty(lastPath).trim() !== "";
  if (doneValid) {
    status(`adjudicator skipped (${ADJUDICATOR_DONE})`);
    if (done.profileExists) warnExists();
    return null;
  }
  rmSync(donePath, { force: true });
  const before = outputSnapshot(root, wd);
  const statuses = {};
  const code = await runRoles(["adjudicator"], undefined, statuses);
  if (code !== null) return code;
  if (statuses.adjudicator === "failed") {
    status("FAILED: adjudicator failed");
    return 1;
  }
  const last = readOrEmpty(lastPath);
  if (last.trim() === "") {
    status("FAILED: adjudicator wrote no profile (empty last message)");
    return 1;
  }
  const candidate = relative(root, join(wd, "adjudicator-profile.yaml"));
  const outputs = [...outputSnapshot(root, wd)]
    .filter(([f, key]) => before.get(f) !== key)
    .map(([f]) => f)
    .filter((f) => f === candidate || namesLegacyId(join(root, f), legacyId));
  if (outputs.length === 0) {
    status("FAILED: adjudicator wrote no profile");
    return 1;
  }
  const profileExists = last.trimStart().startsWith("PROFILE EXISTS") || outputs.includes(candidate);
  if (profileExists) warnExists();
  safeWriteFile(donePath, JSON.stringify({ outputs, profileExists }, null, 2) + "\n", workRoot);
  return null;
}

// The hint after an authentication failure depends on what the agent used to log in.
const authHint = (billing) =>
  (billing === "api-key" ? "check the API key file" : "check the claude login");

const firstLine = (error) => String(error?.message ?? error).split("\n")[0];

/**
 * Runs the pipeline for one species and returns the exit code (0 done, 1 failed, a credential
 * rejected or an audit failed, 3 out of credits, and 1 with a message when data/species/work does
 * not pass the validation of workRootOf). The run holds <wd>/run.lock from before the set-aside
 * step until it ends; a lock of a live process refuses the run with exit 1. A thrown error ends
 * the run with "FAILED: crashed (<message>)", SIGINT and SIGTERM with "FAILED: crashed
 * (<signal>)", the end of all running agents and exit(1). runAgentImpl, execNode, gitExec,
 * signals and exit are injectable for tests.
 */
export async function runPipeline({
  root = REPO_ROOT,
  runner,
  billing,
  legacyId,
  name,
  hint,
  lake = "",
  legacyIds = "",
  adjudicatorNote = "",
  fresh = false,
  runAgentImpl = runAgent,
  execNode = (args) => execIn(root, process.execPath, args),
  now = () => new Date(),
  pid = process.pid,
  signals = process,
  exit = (code) => process.exit(code),
  err = (text) => console.error(text),
  gitExec,
}) {
  let workRoot;
  try {
    workRoot = workRootOf(root, gitExec);
  } catch (error) {
    err(`legacy-${legacyId} cannot start: ${firstLine(error)}`);
    return 1;
  }
  const wd = workDirOf(workRoot, legacyId);
  let lock;
  try {
    lock = acquireLock(wd, pid, workRoot);
  } catch (error) {
    err(`legacy-${legacyId} cannot start: ${firstLine(error)}`);
    return 1;
  }
  if (!lock.acquired) {
    err(`legacy-${legacyId} is running in process ${lock.pid} (lock ${join(wd, LOCK_FILE)})`);
    return 1;
  }
  const logDir = join(wd, "logs");
  // The first final line (DONE or FAILED) closes the status file, so roles that run in parallel
  // to a failure append nothing after it.
  let ended = false;
  const status = (text) => {
    if (ended) return;
    if (text === "DONE" || text.startsWith("FAILED")) ended = true;
    const t = now();
    safeMkdir(logDir, workRoot);
    const line = `${pad(t.getHours())}:${pad(t.getMinutes())} ${text}\n`;
    safeAppendFile(join(logDir, "status"), line, workRoot);
  };
  // A final line that cannot be written to the status file (for example a symlinked log
  // directory) goes to err.
  const finalLine = (text) => {
    try {
      status(text);
    } catch {
      err(text);
    }
  };
  const abort = new AbortController();
  const active = new Set();
  // The audit hits per role whose audit failed.
  const audits = {};
  const stopAgents = async () => {
    abort.abort();
    await Promise.allSettled([...active]);
  };
  let stopping = false;
  const onSignal = (signal) => {
    if (stopping) return;
    stopping = true;
    finalLine(`FAILED: crashed (${signal})`);
    stopAgents().finally(() => {
      releaseLock(wd, pid);
      exit(1);
    });
  };
  signals.on("SIGINT", onSignal);
  signals.on("SIGTERM", onSignal);
  try {
    return await runSteps({
      root, runner, billing, legacyId, name, hint, lake, legacyIds, adjudicatorNote, fresh,
      runAgentImpl, execNode, now, wd, workRoot, logDir, status, finalLine,
      abortSignal: abort.signal, active, lock, audits,
    });
  } catch (error) {
    finalLine(`FAILED: crashed (${firstLine(error)})`);
    await stopAgents();
    // The hits of a role whose audit failed are not lost when another role crashed the run.
    for (const [role, hits] of Object.entries(audits)) {
      err(`FAILED: ${role} audit failed (${hits})`);
    }
    return 1;
  } finally {
    signals.off("SIGINT", onSignal);
    signals.off("SIGTERM", onSignal);
    releaseLock(wd, pid);
  }
}

// The steps of a run inside the lock. Returns the exit code.
async function runSteps({
  root, runner, billing, legacyId, name, hint, lake, legacyIds, adjudicatorNote, fresh,
  runAgentImpl, execNode, now, wd, workRoot, logDir, status, finalLine, abortSignal, active, lock,
  audits,
}) {
  // A changed note invalidates the adjudicator record before run.json takes the note, so a run
  // that stops at any later point reruns the adjudicator on resume.
  const stored = readManifest(wd);
  const noteChanged = stored !== null && (stored.adjudicatorNote || "") !== (adjudicatorNote || "");
  const recordDropped = noteChanged && existsSync(join(wd, ADJUDICATOR_DONE));
  if (recordDropped) rmSync(join(wd, ADJUDICATOR_DONE));
  const previous = prepareWorkdir({
    wd,
    manifest: buildManifest({ legacyId, name, hint, lake, legacyIds, adjudicatorNote }),
    fresh,
    now: now(),
    base: workRoot,
  });
  safeMkdir(logDir, workRoot);
  const exec = async (args, logName) => {
    const r = await execNode(args);
    safeWriteFile(join(logDir, `${logName}.txt`), r.stdout + r.stderr, workRoot);
    return r;
  };
  if (previous) status(`previous role files moved to ${previous}`);
  if (lock.replaced !== undefined) status(`stale lock of process ${lock.replaced ?? "unknown"} replaced`);
  status("start");
  if (recordDropped && previous === null) status("adjudicator note changed");

  // The billing of every role that ran, for the hint of an authentication failure.
  const billings = {};
  // Runs one role and returns the runAgent status.
  const runRole = async (role, pending) => {
    safeMkdir(join(wd, `tmp-${role}`), workRoot);
    const resolved = resolveModel({ runner, role, lake });
    const prompt = buildPrompt({
      role,
      root,
      legacyId,
      name,
      hint,
      lake,
      legacyIds: legacyIds || undefined,
      note: role === "adjudicator" ? adjudicatorNote : "",
      model: resolved.model,
      runner: resolved.runner,
      pending,
    });
    const promptPath = join(logDir, `${role}.prompt`);
    safeWriteFile(promptPath, prompt, workRoot);
    const running = runAgentImpl({
      runner, billing, role, lake, promptPath, logDir, logBase: workRoot, cwd: root, abortSignal,
    });
    active.add(running);
    let result;
    try {
      result = await running;
    } finally {
      active.delete(running);
    }
    billings[role] = result.billing;
    if (result.status === "audit-failed") audits[role] = result.detail;
    if (result.status === "ok") status(`${role} finished (${result.model})`);
    else if (result.status === "failed") {
      status(`${role} failed (${result.model})${result.detail ? `: ${result.detail}` : ""}`);
    }
    return result.status;
  };
  // Runs several roles in parallel. Returns 3 when one of them ran out of credits, 1 when a
  // credential was rejected, an audit failed or the run was aborted, otherwise null. The FAILED
  // line follows the lines of all parallel roles, so that it is the last status line of the run.
  // statuses, when given, receives the runAgent status per role.
  const runRoles = async (roles, pending, statuses = {}) => {
    const results = await Promise.all(roles.map((r) => runRole(r, pending)));
    roles.forEach((r, i) => {
      statuses[r] = results[i];
    });
    const audited = roles.filter((_, i) => results[i] === "audit-failed");
    if (audited.length > 0) {
      // The line names the audit hits, so it reaches err when the status file refuses the write.
      finalLine(`FAILED: ${audited.map((r) => `${r} audit failed (${audits[r]})`).join("; ")}`);
      return 1;
    }
    const broke = roles.filter((_, i) => results[i] === "out-of-credits");
    if (broke.length > 0) {
      status(`FAILED: ${broke.join(", ")} out of credits`);
      return 3;
    }
    const rejected = roles.filter((_, i) => results[i] === "auth-failed");
    if (rejected.length > 0) {
      const hint = authHint(billings[rejected[0]]);
      status(`FAILED: ${rejected.join(", ")} authentication failed (${hint})`);
      return 1;
    }
    return abortSignal.aborted ? 1 : null;
  };
  // A voting role or tie-breaker that runs changes the input of the adjudicator, so its record
  // goes before the role starts; an interrupted run then reruns the adjudicator on resume.
  const runVotes = (roles, pending) => {
    if (roles.length > 0) rmSync(join(wd, ADJUDICATOR_DONE), { force: true });
    return runRoles(roles, pending);
  };

  const missing = VOTING_ROLES.filter((r) => !existsSync(join(wd, `${r}.yaml`)));
  const voted = await runVotes(missing);
  if (voted !== null) return voted;

  for (const role of VOTING_ROLES) {
    const file = join(wd, `${role}.yaml`);
    const r = await exec(["tools/species-data/validate.mjs", file], `${role}.validate`);
    if (!existsSync(file) || r.code !== 0) {
      status(`FAILED: ${role} invalid or missing`);
      return 1;
    }
  }

  for (let round = 1; round <= TALLY_ROUNDS; round++) {
    const t = await exec(["tools/species-data/tally.mjs", wd], `tally-${round}`);
    if (t.code !== 0) {
      status("FAILED: tally");
      return 1;
    }
    const pending = pendingFeatures(parse(readFileSync(join(wd, "tally.yaml"), "utf8")));
    if (pending.length === 0) break;
    if (round === TALLY_ROUNDS) {
      status(`FAILED: still pending after ${TALLY_ROUNDS} rounds`);
      return 1;
    }
    const roles = nextTiebreakerNumbers(wd).map((n) => `tiebreaker-${n}`);
    const broken = await runVotes(roles, pending);
    if (broken !== null) return broken;
  }

  const adjudicated = await adjudicate({ root, wd, workRoot, logDir, legacyId, status, runRoles });
  if (adjudicated !== null) return adjudicated;
  const build = await exec(["tools/species-data/build-directory.mjs"], "build-directory");
  if (build.code !== 0) status("WARN: build-directory failed");
  const final = await exec(["tools/species-data/validate.mjs", ...profileFiles(root)], "final.validate");
  if (final.code === 0) {
    status("DONE");
    return 0;
  }
  status("FAILED: final validation");
  return 1;
}

/**
 * Collects the validator warnings about missing hybrid groups and lets an agent sync them.
 * Returns 0 when synced or nothing to sync, 1 when the agent failed or left no last message,
 * 3 when the agent account is out of credits. The agent files go to
 * <data/species/work>/groupsync/logs, below the validated real path of the work directory.
 */
export async function runGroupsync({
  root = REPO_ROOT,
  runner,
  billing,
  runAgentImpl = runAgent,
  execNode = (args) => execIn(root, process.execPath, args),
  execGit = (args) => execIn(root, "git", args),
  out = (text) => console.log(text),
  err = (text) => console.error(text),
  gitExec,
}) {
  const report = await execNode(["tools/species-data/validate.mjs", ...profileFiles(root)]);
  const list = parseGroupsyncWarnings(report.stdout);
  if (!list) {
    out("nothing to sync");
    return 0;
  }
  let workRoot;
  try {
    workRoot = workRootOf(root, gitExec);
  } catch (error) {
    err(`groupsync cannot start: ${firstLine(error)}`);
    return 1;
  }
  const logDir = join(workRoot, "groupsync", "logs");
  const promptPath = join(logDir, "groupsync.prompt");
  try {
    safeMkdir(logDir, workRoot);
    safeWriteFile(promptPath, buildGroupsyncPrompt({ root, list }), workRoot);
  } catch (error) {
    err(`groupsync cannot start: ${firstLine(error)}`);
    return 1;
  }
  let result;
  try {
    result = await runAgentImpl({
      runner, billing, role: "groupsync", lake: "", promptPath, logDir, logBase: workRoot,
      cwd: root, timeoutMs: 2_400_000,
    });
  } catch (error) {
    err(`groupsync: the agent could not start (${firstLine(error)})`);
    return 1;
  }
  if (result.status === "out-of-credits") {
    err(`groupsync: the agent account is out of credits (${join(logDir, "groupsync.log")})`);
    return 3;
  }
  if (result.status === "audit-failed") {
    err(`groupsync: audit failed (${result.detail}) (${join(logDir, "groupsync.log")})`);
    return 1;
  }
  if (result.status === "auth-failed") {
    const hint = authHint(result.billing);
    err(`groupsync: authentication failed, ${hint} (${join(logDir, "groupsync.log")})`);
    return 1;
  }
  if (result.status !== "ok") {
    err(`groupsync: the agent failed (${join(logDir, "groupsync.log")})`);
    return 1;
  }
  const last = readOrEmpty(join(logDir, "groupsync.last"));
  if (last.trim() === "") {
    err(`groupsync: the agent left no last message (${join(logDir, "groupsync.log")})`);
    return 1;
  }
  out(last);
  out((await execGit(["diff", "--stat"])).stdout);
  return 0;
}

function usage(message) {
  if (message) console.error(message);
  console.error(USAGE);
  return 2;
}

/** Runs the command line argv (without node and script) and returns the exit code. */
export async function main(argv, {
  env = process.env, credentialsFile = defaultCredentialsFile(), managedFile,
  runPipelineImpl = runPipeline, runGroupsyncImpl = runGroupsync,
} = {}) {
  const [command, ...rest] = argv;
  let values;
  try {
    ({ values } = parseArgs({
      args: rest,
      options: {
        runner: { type: "string" },
        billing: { type: "string" },
        id: { type: "string" },
        name: { type: "string" },
        hint: { type: "string" },
        lake: { type: "string" },
        "legacy-ids": { type: "string" },
        "adjudicator-note": { type: "string" },
        fresh: { type: "boolean" },
        count: { type: "string" },
      },
    }));
  } catch (e) {
    return usage(e.message);
  }
  const needRunner = () => (RUNNERS.includes(values.runner) ? null : usage(`--runner must be one of: ${RUNNERS.join(", ")}`));
  // The billing of the Claude agents is an explicit choice; nothing starts before it is valid.
  const needBilling = () => {
    const problem = billingError(values.billing, {
      apiKeyFile: defaultApiKeyFile(env), credentialsFile, managedFile,
    });
    return problem === null ? null : usage(problem);
  };

  if (command === "run") {
    for (const key of ["runner", "id", "name", "hint"]) if (!values[key]) return usage(`missing --${key}`);
    const bad = needRunner();
    if (bad !== null) return bad;
    // The id names the work directory legacy-<id>, so it is a plain legacy catalog number.
    if (!/^\d+$/.test(values.id)) return usage("--id must be a legacy catalog number");
    const noBilling = needBilling();
    if (noBilling !== null) return noBilling;
    return runPipelineImpl({
      runner: values.runner,
      billing: values.billing,
      legacyId: values.id,
      name: values.name,
      hint: values.hint,
      lake: values.lake || "",
      legacyIds: values["legacy-ids"] || "",
      adjudicatorNote: values["adjudicator-note"] || "",
      fresh: Boolean(values.fresh),
    });
  }
  if (command === "groupsync") {
    const bad = needRunner();
    if (bad !== null) return bad;
    const noBilling = needBilling();
    if (noBilling !== null) return noBilling;
    return runGroupsyncImpl({ runner: values.runner, billing: values.billing });
  }
  if (command === "next" || command === "status") {
    for (const option of ["billing", "runner"]) if (values[option] !== undefined) return usage(`${command} takes no --${option} option`);
  }
  if (command === "next") {
    const count = values.count === undefined ? 4 : Number(values.count);
    if (!Number.isInteger(count) || count < 1) return usage("--count must be a positive integer");
    for (const row of nextSpecies({ count })) console.log(row);
    return 0;
  }
  if (command === "status") {
    for (const row of statusRows()) console.log(row);
    return 0;
  }
  return usage(command ? `unknown command: ${command}` : undefined);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main(process.argv.slice(2));
}
