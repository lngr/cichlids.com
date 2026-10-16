#!/usr/bin/env node
// Runs one Claude agent for a one-off job (research, review, coding) and writes its log and final
// message to a directory.
//
// Usage:
//   node tools/species-data/claude-agent.mjs --name <label> --prompt-file <path> --out-dir <dir>
//        --billing <api-key|subscription>
//        [--model claude-sonnet-5-5] [--effort high] [--cwd <dir>] [--timeout-min 60]
//        [--read <dir>]... [--write <dir>]...
//
// The agent runs like a pipeline role, in the cage of agent-cage.mjs: same tools, an explicit
// billing choice, retries of transient failures and detection of an empty account or a rejected
// credential. Besides the default write roots it may write to every --write directory and read
// every --read directory, which the cage binds read-only at its real path (briefs and other
// inputs; the cage hides the rest of /tmp). A --write directory can hold code, for example tools/;
// what the agent changes there must be read and approved before it runs. The CLI itself writes
// <out-dir>/<name>.log and <out-dir>/<name>.last outside the cage and prints one summary line.
// Exit codes: 0 ok, 1 failed, auth-failed, audit-failed or aborted, 2 usage error, 3 the account
// is out of credits.
import { accessSync, constants, mkdirSync, readFileSync, statSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { isInside, realPathOf, writeRootsOf } from "./agent-cage.mjs";
import { billingError, defaultApiKeyFile, defaultCredentialsFile, runAttempts } from "./agent-runner.mjs";

const USAGE = `Usage:
  claude-agent.mjs --name <label> --prompt-file <path> --out-dir <dir> --billing <api-key|subscription>
                   [--model claude-sonnet-5-5] [--effort high] [--cwd <dir>] [--timeout-min 60]
                   [--read <dir>]... [--write <dir>]...`;

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const EFFORTS = ["low", "medium", "high", "xhigh", "max"];
// setTimeout takes at most 2^31-1 ms.
const MAX_TIMEOUT_MIN = Math.floor((2 ** 31 - 1) / 60_000);

const EXIT_CODES = { ok: 0, "out-of-credits": 3 };

/**
 * Returns the usage error for an --out-dir, or null. The CLI writes the log and the last message
 * there outside the cage, so the directory must be out of the agent's reach: its real path (the
 * nearest existing ancestor plus the rest) lies in none of the write roots, the repository or the
 * home directory.
 */
export function outDirError(outDir, { writable, root, home }) {
  const real = realPathOf(outDir);
  if (isInside(real, root)) return "inside the repository";
  if (isInside(real, home)) return "inside the home directory";
  return writable.some((r) => isInside(real, r)) ? "inside a write root of the agent" : null;
}

/**
 * Runs the command line argv (without node and script) and returns the exit code. The runner,
 * spawn function, signal source, environment and output functions are injectable for tests;
 * extra options go to runAttempts.
 */
export async function main({
  argv,
  runAgentImpl = runAttempts,
  signals = process,
  out = (text) => console.log(text),
  err = (text) => console.error(text),
  managedFile,
  repoRoot = REPO_ROOT,
  ...options
}) {
  const usage = (message) => {
    if (message) err(message);
    err(USAGE);
    return 2;
  };
  let values;
  try {
    ({ values } = parseArgs({
      args: argv,
      options: {
        name: { type: "string" },
        "prompt-file": { type: "string" },
        "out-dir": { type: "string" },
        billing: { type: "string" },
        model: { type: "string", default: "claude-sonnet-5-5" },
        effort: { type: "string", default: "high" },
        cwd: { type: "string" },
        "timeout-min": { type: "string", default: "60" },
        read: { type: "string", multiple: true },
        write: { type: "string", multiple: true },
      },
    }));
  } catch (e) {
    return usage(e.message);
  }
  for (const key of ["name", "prompt-file", "out-dir"]) if (!values[key]) return usage(`missing --${key}`);
  if (!/^[a-z0-9][a-z0-9-]*$/.test(values.name)) return usage("--name must match [a-z0-9][a-z0-9-]*");
  const timeoutMin = Number(values["timeout-min"]);
  if (!Number.isFinite(timeoutMin) || timeoutMin <= 0 || timeoutMin > MAX_TIMEOUT_MIN) {
    return usage(`--timeout-min must be a positive number of at most ${MAX_TIMEOUT_MIN}`);
  }
  if (!/^claude-/.test(values.model)) return usage("--model must start with claude-");
  if (!EFFORTS.includes(values.effort)) return usage(`--effort must be one of: ${EFFORTS.join(", ")}`);
  const cwd = resolve(values.cwd ?? process.cwd());
  const outDir = resolve(values["out-dir"]);
  const writeDirs = (values.write ?? []).map((dir) => resolve(repoRoot, dir));
  const readDirs = (values.read ?? []).map((dir) => resolve(repoRoot, dir));
  let roots;
  try {
    roots = writeRootsOf({
      root: repoRoot, env: options.env, home: options.home, extraWriteRoots: writeDirs,
      extraReadRoots: readDirs, exec: options.gitExec,
    });
  } catch (e) {
    return usage(`--write or --read: ${e.message}`);
  }
  const outProblem = outDirError(outDir, roots);
  if (outProblem) return usage(`--out-dir ${outDir} is not allowed: ${outProblem}`);
  const billingProblem = billingError(values.billing, {
    apiKeyFile: options.apiKeyFile ?? defaultApiKeyFile(options.env ?? process.env),
    credentialsFile: options.credentialsFile ?? defaultCredentialsFile(options.home ?? homedir()),
    managedFile,
  });
  if (billingProblem !== null) return usage(billingProblem);
  for (const [flag, path, kind] of [["--prompt-file", values["prompt-file"], "isFile"], ["--cwd", cwd, "isDirectory"]]) {
    let ok = false;
    try {
      ok = statSync(path)[kind]();
    } catch {
      ok = false;
    }
    if (!ok) return usage(`${flag} does not name an existing ${kind === "isFile" ? "file" : "directory"}: ${path}`);
  }

  try {
    mkdirSync(values["out-dir"], { recursive: true });
    accessSync(values["out-dir"], constants.W_OK);
  } catch {
    return usage(`--out-dir cannot be created or written: ${values["out-dir"]}`);
  }

  const abort = new AbortController();
  const onSignal = () => abort.abort();
  signals.on("SIGINT", onSignal);
  signals.on("SIGTERM", onSignal);
  let result;
  try {
    result = await runAgentImpl({
      ...options,
      runner: "claude",
      billing: values.billing,
      model: values.model,
      effort: values.effort,
      fallbacks: [],
      name: values.name,
      promptPath: values["prompt-file"],
      logDir: values["out-dir"],
      cwd,
      root: repoRoot,
      extraWriteRoots: writeDirs,
      extraReadRoots: readDirs,
      timeoutMs: Math.round(timeoutMin * 60_000),
      abortSignal: abort.signal,
    });
  } catch (error) {
    err(`${values.name}: the agent could not start (${String(error?.message ?? error).split("\n")[0]})`);
    return 1;
  } finally {
    signals.off("SIGINT", onSignal);
    signals.off("SIGTERM", onSignal);
  }

  const lastPath = join(values["out-dir"], `${values.name}.last`);
  let status = result.status;
  // The runner reports an unfinished run without a known failure pattern as "ok"; an empty final
  // message means that the agent produced nothing.
  if (status === "ok" && readLast(lastPath).trim() === "") status = "failed";
  if (status === "audit-failed") err(`${values.name}: audit failed (${result.detail})`);
  out(`${values.name}: ${status} billing ${result.billing} cost ${result.costUsd.toFixed(4)} USD attempts ${result.attempts} -> ${lastPath}`);
  return EXIT_CODES[status] ?? 1;
}

function readLast(path) {
  try {
    return readFileSync(path, "utf8");
  } catch {
    return "";
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main({ argv: process.argv.slice(2) });
}
