// Runs one pipeline role (generator, checker, tie-breaker, adjudicator, group sync) with an agent
// CLI: Codex via "codex exec" or Claude via "claude -p". The prompt goes in on stdin.
//
// runAgent tries the main model of the role three times, then each fallback model once, and
// stops early on success or when the account is out of credits. Every attempt of a role is
// appended to <logDir>/<role>.log and its last message is written to <logDir>/<role>.last.
import { spawn } from "node:child_process";
import { appendFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";

export const RUNNERS = ["codex", "claude"];

// Model table. A rule is { model, effort } or { lake: {...}, other: {...} } when the lake switch
// selects the row. Every tiebreaker-<n> role uses the "tiebreaker" row.
const MODEL_TABLE = {
  codex: {
    fallbacks: ["gpt-6-sol", "gpt-6-sol"],
    roles: {
      generator: { lake: { model: "gpt-6-luna", effort: "high" }, other: { model: "gpt-6.1-sol", effort: "medium" } },
      "checker-b": { lake: { model: "gpt-6-luna", effort: "high" }, other: { model: "gpt-6.1-sol", effort: "medium" } },
      "checker-c": { lake: { model: "gpt-6-luna", effort: "high" }, other: { model: "gpt-6.1-sol", effort: "medium" } },
      tiebreaker: { model: "gpt-6.1-sol", effort: "medium" },
      adjudicator: { model: "gpt-6.1-sol", effort: "high" },
      groupsync: { model: "gpt-6-luna", effort: "high" },
    },
  },
  claude: {
    fallbacks: [],
    roles: {
      generator: { model: "claude-sonnet-5-5", effort: "high" },
      "checker-b": { model: "claude-sonnet-5-5", effort: "high" },
      "checker-c": { model: "claude-opus-5-5", effort: "high" },
      tiebreaker: { model: "claude-sonnet-5-5", effort: "high" },
      adjudicator: { model: "claude-opus-5-5", effort: "high" },
      groupsync: { model: "claude-sonnet-5-5", effort: "high" },
    },
  },
};

// Checker A is always the Claude checker, so that two model families vote in a Codex run.
const CHECKER_A = { runner: "claude", model: "claude-sonnet-5-5", effort: "high", fallbacks: [] };

const MAIN_ATTEMPTS = 3;

const CREDIT_PATTERNS = {
  codex: /out of credits/i,
  claude: /usage limit|limit reached|out of extra usage|credit balance is too low/i,
};
const TRANSIENT_PATTERNS = {
  codex: /at capacity|flagged for possible/i,
  claude: /overloaded|rate limit|\b(529|503)\b|ECONNRESET|timed out/i,
};

/** Returns { runner, model, effort, fallbacks } for a role. lake is a non-empty string for lake species. */
export function resolveModel({ runner, role, lake }) {
  if (!RUNNERS.includes(runner)) throw new Error(`unknown runner: ${runner}`);
  if (role === "checker-a") return { ...CHECKER_A, fallbacks: [...CHECKER_A.fallbacks] };
  const key = /^tiebreaker-\d+$/.test(role) ? "tiebreaker" : role;
  const entry = MODEL_TABLE[runner].roles[key];
  if (!entry) throw new Error(`unknown role: ${role}`);
  const rule = entry.lake ? (lake ? entry.lake : entry.other) : entry;
  return { runner, model: rule.model, effort: rule.effort, fallbacks: [...MODEL_TABLE[runner].fallbacks] };
}

/** Returns { command, args } of the agent CLI call. The prompt is passed on stdin. */
export function buildCommand({ runner, model, effort, lastMessagePath }) {
  if (runner === "codex") {
    return {
      command: "codex",
      args: [
        "exec", "-m", model,
        "-c", `model_reasoning_effort=${effort}`,
        "-s", "workspace-write",
        "-c", "sandbox_workspace_write.network_access=true",
        "-c", "web_search=live",
        "--ephemeral", "--color", "never",
        "-o", lastMessagePath,
        "-",
      ],
    };
  }
  if (runner === "claude") {
    // The container is the isolation boundary here, as the sandbox workspace-write with network
    // access is for Codex. Agents fetch pages only through archive-fetch.mjs and use no external
    // connectors, so WebFetch is left out and --strict-mcp-config without an MCP config hides the
    // MCP tools of user plugins and claude.ai connectors (browser control, uploads).
    return {
      command: "claude",
      args: [
        "-p", "--model", model,
        "--effort", effort,
        "--permission-mode", "bypassPermissions",
        "--tools", "Bash,Read,Write,Edit,Glob,Grep,WebSearch",
        "--strict-mcp-config",
        "--no-session-persistence",
        "--output-format", "text",
      ],
    };
  }
  throw new Error(`unknown runner: ${runner}`);
}

/**
 * Classifies a finished run as "ok", "retry" or "out-of-credits". Only an unfinished run (empty
 * last message or exit code other than 0) is matched, against the log and the last message
 * together because claude prints some errors to stdout. An unfinished run without a known
 * pattern is "ok"; the caller validates the output file of the role.
 */
export function classifyOutput({ runner, log, lastMessage, exitCode }) {
  const unfinished = lastMessage.trim() === "" || exitCode !== 0;
  if (!unfinished) return "ok";
  const text = `${log}\n${lastMessage}`;
  if (CREDIT_PATTERNS[runner].test(text)) return "out-of-credits";
  if (TRANSIENT_PATTERNS[runner].test(text)) return "retry";
  return "ok";
}

// Variables of a parent Claude Code session. A child agent that inherits them attaches to that
// session (messaging socket, session id, effort) instead of running on its own.
const SESSION_VARIABLES = [
  "CLAUDECODE", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_MESSAGING_SOCKET",
  "CLAUDE_CODE_MESSAGING_TOKEN", "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDE_CODE_CHILD_SESSION",
  "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_PID", "CLAUDE_EFFORT",
];

/** Returns a copy of env without the session variables of a parent Claude Code session. */
export function childEnv(env) {
  const copy = { ...env };
  for (const key of SESSION_VARIABLES) delete copy[key];
  return copy;
}

// Waits ms milliseconds; an abort ends the wait early.
const defaultSleep = (ms, abortSignal) =>
  new Promise((done) => {
    const timer = setTimeout(done, ms);
    abortSignal?.addEventListener("abort", () => {
      clearTimeout(timer);
      done();
    }, { once: true });
  });

function readOrEmpty(path) {
  try {
    return readFileSync(path, "utf8");
  } catch {
    return "";
  }
}

// Runs the child once and resolves with its output. A spawn error counts as exit code -1, and so
// does a child ended by a signal. On timeout or abort the child gets SIGTERM and, when it has not
// closed after killGraceMs, SIGKILL. When its output streams stay open after SIGKILL (a
// descendant holds them), the run ends after one more grace period.
function runChild({ command, args, cwd, env, prompt, timeoutMs, killGraceMs, spawnImpl, abortSignal }) {
  return new Promise((resolve) => {
    let stdout = "";
    let stderr = "";
    let combined = "";
    let settled = false;
    let stopping = false;
    let timedOut = false;
    const timers = [];
    const child = spawnImpl(command, args, { cwd, env: childEnv(env), stdio: ["pipe", "pipe", "pipe"] });
    const finish = (exitCode, signal = null) => {
      if (settled) return;
      settled = true;
      for (const t of timers) clearTimeout(t);
      abortSignal?.removeEventListener("abort", stop);
      resolve({ stdout, stderr, combined, exitCode, signal, timedOut });
    };
    const stop = () => {
      if (settled || stopping) return;
      stopping = true;
      child.kill("SIGTERM");
      timers.push(setTimeout(() => {
        child.kill("SIGKILL");
        timers.push(setTimeout(() => finish(-1, "SIGKILL"), killGraceMs));
      }, killGraceMs));
    };
    abortSignal?.addEventListener("abort", stop, { once: true });
    timers.push(setTimeout(() => {
      timedOut = true;
      stop();
    }, timeoutMs));
    // Decoding in the stream keeps a multibyte character that arrives in two chunks intact.
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      stdout += chunk;
      combined += chunk;
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk;
      combined += chunk;
    });
    child.on("error", (error) => {
      stderr += `${error.message}\n`;
      combined += `${error.message}\n`;
      finish(-1);
    });
    child.on("close", (code, signal) => finish(code ?? -1, signal ?? null));
    child.stdin.on("error", () => {});
    child.stdin.end(prompt);
  });
}

// Closing line of an attempt in the role log: exit code, signal and timeout.
function exitLine({ exitCode, signal, timedOut }, timeoutMs) {
  return `=== exit ${exitCode}${signal ? ` signal ${signal}` : ""}${timedOut ? ` timeout after ${timeoutMs} ms` : ""}\n`;
}

/**
 * Runs a role with the runner that resolveModel gives for it (checker-a uses Claude in every run)
 * and returns { status: "ok" | "out-of-credits" | "failed" | "aborted", model, attempts }.
 * "failed" means that every attempt was a transient failure. "aborted" means that abortSignal
 * fired: the running child is ended (SIGTERM, then SIGKILL after killGraceMs) and no further
 * attempt starts. A failed agent never throws.
 */
export async function runAgent({
  runner, role, lake, promptPath, logDir, cwd,
  timeoutMs = 3_600_000, retryDelayMs = 60_000, killGraceMs = 10_000,
  spawnImpl = spawn, sleep = defaultSleep, env = process.env, now = () => new Date(), abortSignal,
}) {
  const resolved = resolveModel({ runner, role, lake });
  const models = [...Array(MAIN_ATTEMPTS).fill(resolved.model), ...resolved.fallbacks];
  mkdirSync(logDir, { recursive: true });
  const logPath = join(logDir, `${role}.log`);
  const lastPath = join(logDir, `${role}.last`);
  const prompt = readFileSync(promptPath, "utf8");

  let attempts = 0;
  let usedModel = resolved.model;
  for (const model of models) {
    if (attempts > 0) await sleep(retryDelayMs, abortSignal);
    if (abortSignal?.aborted) return { status: "aborted", model: usedModel, attempts };
    attempts += 1;
    usedModel = model;
    rmSync(lastPath, { force: true });
    const { command, args } = buildCommand({ runner: resolved.runner, model, effort: resolved.effort, lastMessagePath: lastPath });
    const startedAt = now().toISOString();
    const result = await runChild({ command, args, cwd, env, prompt, timeoutMs, killGraceMs, spawnImpl, abortSignal });
    // Every attempt is appended to the log under its own header; the classification reads the
    // output of this attempt only.
    const output = resolved.runner === "codex" ? result.combined : result.stderr;
    appendFileSync(logPath, `=== attempt ${attempts} ${model} ${startedAt}\n${output}${output.endsWith("\n") || output === "" ? "" : "\n"}${exitLine(result, timeoutMs)}`);
    let lastMessage;
    if (resolved.runner === "codex") {
      lastMessage = readOrEmpty(lastPath);
    } else {
      writeFileSync(lastPath, result.stdout);
      lastMessage = result.stdout;
    }
    if (abortSignal?.aborted) return { status: "aborted", model: usedModel, attempts };
    const verdict = classifyOutput({ runner: resolved.runner, log: result.combined, lastMessage, exitCode: result.exitCode });
    if (verdict === "ok") return { status: "ok", model: usedModel, attempts };
    if (verdict === "out-of-credits") return { status: "out-of-credits", model: usedModel, attempts };
  }
  return { status: "failed", model: usedModel, attempts };
}
