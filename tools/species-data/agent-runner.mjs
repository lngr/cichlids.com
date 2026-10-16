// Runs one pipeline role (generator, checker, tie-breaker, adjudicator, group sync) with an agent
// CLI: Codex via "codex exec" or Claude via "claude -p". The prompt goes in on stdin.
//
// runAgent tries the main model of the role three times, then each fallback once, and
// stops early on success, when the account is out of credits or when the credential is rejected.
// Every attempt of a role is appended to <logDir>/<role>.log and its last message is written to
// <logDir>/<role>.last.
//
// The billing of Claude children is the explicit choice "api-key" or "subscription" (see
// BILLINGS). "api-key" bills the key in the key file (see defaultApiKeyFile) and fails without a
// usable one; "subscription" bills the claude.ai login of the user (see defaultCredentialsFile).
// A Claude child runs in the bubblewrap cage of agent-cage.mjs, which has no network of its own:
// the Anthropic API is reachable through the credential proxy of credential-proxy.mjs and the rest
// of the internet through the egress proxy of egress-proxy.mjs, and the forum archive search
// through the service of archive-service.mjs, all on unix sockets in a per-call directory outside
// the cage. They run in a child process of the runner (proxy-process.mjs), so that a crash of a
// proxy cannot stop the runner before the audit. The
// write roots are audited right after each attempt (agent-audit.mjs). A Codex child runs with the
// sandbox of Codex.
import { fork, spawn } from "node:child_process";
import { readFileSync, rmSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { auditRoots, snapshotWatched } from "./agent-audit.mjs";
import {
  API_SOCKET, ARCHIVE_SOCKET, EGRESS_SOCKET, cageArgs, cageCommand, cageEnv, createRunDir, planCage,
} from "./agent-cage.mjs";
import { dockerEnv } from "./archive-query.mjs";
import { checkApiKeyFile, checkLoginFile } from "./credential-proxy.mjs";
import { startProxyProcess } from "./proxy-process.mjs";
import { safeAppendFile, safeMkdir, safeWriteFile } from "./safe-write.mjs";

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

const AUTH_PATTERN = /invalid x-api-key|Invalid API key|authentication_error/i;
const CREDIT_PATTERNS = {
  codex: /out of credits/i,
  claude: /usage limit|limit reached|out of extra usage|credit balance is too low/i,
};
const TRANSIENT_PATTERNS = {
  codex: /at capacity|flagged for possible/i,
  claude: /overloaded|rate limit|\b(529|503)\b|ECONNRESET|timed out/i,
};

/**
 * Returns { runner, model, effort, fallbacks } for a role. lake is a non-empty string for lake
 * species.
 */
export function resolveModel({ runner, role, lake }) {
  if (!RUNNERS.includes(runner)) throw new Error(`unknown runner: ${runner}`);
  if (role === "checker-a") return { ...CHECKER_A, fallbacks: [...CHECKER_A.fallbacks] };
  const key = /^tiebreaker-\d+$/.test(role) ? "tiebreaker" : role;
  const entry = MODEL_TABLE[runner].roles[key];
  if (!entry) throw new Error(`unknown role: ${role}`);
  const rule = entry.lake ? (lake ? entry.lake : entry.other) : entry;
  return {
    runner, model: rule.model, effort: rule.effort, fallbacks: [...MODEL_TABLE[runner].fallbacks],
  };
}

/**
 * Path of the API key file: CICHLIDS_ANTHROPIC_KEY_FILE when set, else
 * ~/.config/cichlids/anthropic.key.
 */
export function defaultApiKeyFile(env = process.env) {
  return env.CICHLIDS_ANTHROPIC_KEY_FILE || join(homedir(), ".config", "cichlids", "anthropic.key");
}

// The billing of a Claude child is an explicit choice of the operator.
export const BILLINGS = ["api-key", "subscription"];

/** Path of the claude.ai login file of the user: ~/.claude/.credentials.json. */
export const defaultCredentialsFile = (home = homedir()) =>
  join(home, ".claude", ".credentials.json");

// Managed settings cannot be skipped by the claude CLI.
export const MANAGED_SETTINGS_FILE = "/etc/claude-code/managed-settings.json";

// Environment entries of the managed settings that select credentials or the endpoint of the CLI.
const MANAGED_ENV_NAMES = [
  "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL",
  "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
];
const OAUTH_VARIABLE = "CLAUDE_CODE_OAUTH_TOKEN";

/**
 * Returns the reason when the managed settings file sets an apiKeyHelper or an environment entry
 * that selects credentials or the endpoint (the OAuth token too under "api-key"), else null. A
 * missing file is fine. A file that cannot be read or parsed is a problem, because its effect on
 * the billing cannot be checked.
 */
export function managedSettingsProblem(billing, file = MANAGED_SETTINGS_FILE) {
  let text;
  try {
    text = readFileSync(file, "utf8");
  } catch (error) {
    return error.code === "ENOENT" ? null : `cannot be read (${error.code ?? error.message})`;
  }
  let settings;
  try {
    settings = JSON.parse(text);
  } catch {
    return "is not valid JSON";
  }
  if (settings === null || typeof settings !== "object" || Array.isArray(settings)) {
    return "is not a JSON object";
  }
  if (settings.apiKeyHelper !== undefined) return "sets apiKeyHelper";
  const names = [...MANAGED_ENV_NAMES, ...(billing === "api-key" ? [OAUTH_VARIABLE] : [])];
  const found = names.find(
    (n) => settings.env && typeof settings.env === "object" && n in settings.env,
  );
  return found ? `sets ${found} in env` : null;
}

/**
 * Returns the usage error of the --billing option value, or null when the value is valid. The
 * billing "api-key" needs a usable key file (see checkApiKeyFile) and "subscription" a login file
 * with an access token that has not expired (see checkLoginFile); there is no fallback.
 */
export function billingError(
  billing, { apiKeyFile, credentialsFile, managedFile = MANAGED_SETTINGS_FILE },
) {
  if (billing === undefined) return `missing --billing (${BILLINGS.join(" or ")})`;
  if (!BILLINGS.includes(billing)) return `--billing must be one of: ${BILLINGS.join(", ")}`;
  if (billing === "api-key") {
    const problem = checkApiKeyFile(apiKeyFile);
    if (problem) return `--billing api-key needs a usable key file: ${apiKeyFile} ${problem}`;
  } else {
    const problem = checkLoginFile(credentialsFile);
    if (problem) {
      return `--billing subscription needs a usable claude login: ${credentialsFile} ${problem}`;
    }
  }
  const managed = managedSettingsProblem(billing, managedFile);
  if (managed) {
    return `the managed settings ${managedFile} ${managed}, which would override ` +
      `--billing ${billing}`;
  }
  return null;
}

// Variables of a parent Claude Code session. A child that inherits them attaches to that session
// (messaging socket, session id, effort) instead of running on its own.
const SESSION_VARIABLES = [
  "CLAUDECODE", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_SESSION_ATTENDED",
  "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN", "CLAUDE_CODE_BRIDGE_SESSION_ID",
  "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_PID", "CLAUDE_EFFORT",
];

// Names of variables that hold credentials. The codex CLI reads its login from ~/.codex/auth.json.
const SECRET_NAME = /(TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|PRIVATE|API_?KEY)/i;

// Variables that lead to a credential agent, a password prompt or the display of the host; codex
// needs none of them.
const AGENT_VARIABLES = [
  "SSH_AUTH_SOCK", "GIT_ASKPASS", "SSH_ASKPASS", "GPG_AGENT_INFO", "DBUS_SESSION_BUS_ADDRESS",
  "XAUTHORITY", "DISPLAY",
];

/**
 * Returns the environment of a Codex child: env without the session variables of a parent Claude
 * Code session, without variables whose name marks a credential and without the variables of
 * credential agents, editor sessions and the display (SSH_AUTH_SOCK, GIT_ASKPASS, SSH_ASKPASS,
 * VSCODE_*, GPG_AGENT_INFO, DBUS_SESSION_BUS_ADDRESS, XAUTHORITY, DISPLAY). Codex runs in its own
 * sandbox outside the cage and gets the rest of the environment.
 */
export function codexEnv(env) {
  const dropped = (name) =>
    SESSION_VARIABLES.includes(name) || AGENT_VARIABLES.includes(name) ||
    name.startsWith("VSCODE_") || SECRET_NAME.test(name);
  return Object.fromEntries(Object.entries(env).filter(([name]) => !dropped(name)));
}

/**
 * Returns { command, args } of the agent CLI call. The prompt is passed on stdin. The claude call
 * is the command inside the cage; runAttempts wraps it with cageArgs.
 */
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
    // Agents fetch pages only through archive-fetch.mjs and use no external connectors, so
    // WebFetch is left out and --strict-mcp-config without an MCP config hides the MCP tools of
    // user plugins and claude.ai connectors (browser control, uploads). Settings files (user,
    // project, local) could change the billing and an agent could write one into the repository,
    // so --setting-sources is empty. A bare "--tools A B" would swallow the prompt argument, so
    // the tool list is one argument.
    return {
      command: "claude",
      args: [
        "-p", "--model", model,
        "--effort", effort,
        "--permission-mode", "bypassPermissions",
        "--tools=Bash,Read,Write,Edit,Glob,Grep,WebSearch",
        "--strict-mcp-config",
        "--no-session-persistence",
        "--output-format", "json",
        "--setting-sources", "",
      ],
    };
  }
  throw new Error(`unknown runner: ${runner}`);
}

/**
 * Classifies a finished run as "ok", "retry", "out-of-credits" or "auth-failed". Only an
 * unfinished run (empty last message, exit code other than 0 or isError) is matched, against the
 * log and the last message together because claude prints some errors to stdout. isError marks a
 * run that reports an error in its JSON output, whatever its exit code. "auth-failed" (claude
 * only) means a rejected credential: apiErrorStatus 401 or 403 from the JSON output, or the text
 * of such an error. An unfinished run without a known pattern is "ok";
 * the caller validates the output file of the role.
 */
export function classifyOutput({
  runner, log, lastMessage, exitCode, apiErrorStatus = null, isError = false,
}) {
  const unfinished = isError || lastMessage.trim() === "" || exitCode !== 0;
  if (!unfinished) return "ok";
  const text = `${log}\n${lastMessage}`;
  if (CREDIT_PATTERNS[runner].test(text)) return "out-of-credits";
  const rejected = apiErrorStatus === 401 || apiErrorStatus === 403 || AUTH_PATTERN.test(text);
  if (runner === "claude" && rejected) return "auth-failed";
  if (TRANSIENT_PATTERNS[runner].test(text)) return "retry";
  return "ok";
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

// Runs the child once and resolves with its output. A spawn error counts as exit code -1 (with
// spawnFailed set), and so does a child ended by a signal. On timeout or abort the child gets
// SIGTERM and, when it has not closed after killGraceMs, SIGKILL. When its output streams stay
// open after SIGKILL (a descendant holds them), the run ends after one more grace period.
function runChild({
  command, args, cwd, env, prompt, timeoutMs, killGraceMs, spawnImpl, abortSignal,
}) {
  return new Promise((resolve) => {
    let stdout = "";
    let stderr = "";
    let combined = "";
    let settled = false;
    let stopping = false;
    let timedOut = false;
    let spawnFailed = false;
    const timers = [];
    const child = spawnImpl(command, args, { cwd, env, stdio: ["pipe", "pipe", "pipe"] });
    const finish = (exitCode, signal = null) => {
      if (settled) return;
      settled = true;
      for (const t of timers) clearTimeout(t);
      abortSignal?.removeEventListener("abort", stop);
      resolve({ stdout, stderr, combined, exitCode, signal, timedOut, spawnFailed });
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
    if (abortSignal?.aborted) stop();
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
      spawnFailed = true;
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
  const how = signal ? ` signal ${signal}` : "";
  const timeout = timedOut ? ` timeout after ${timeoutMs} ms` : "";
  return `=== exit ${exitCode}${how}${timeout}\n`;
}

// Reads the JSON object that claude prints with --output-format json. Returns { valid, result,
// costUsd, apiErrorStatus, isError }; result is "" and valid false when stdout is not a JSON
// object.
function parseClaudeOutput(stdout) {
  let json;
  try {
    json = JSON.parse(stdout);
  } catch {
    json = null;
  }
  if (json === null || typeof json !== "object" || Array.isArray(json)) {
    return { valid: false, result: "", costUsd: null, apiErrorStatus: null, isError: false };
  }
  return {
    valid: true,
    result: typeof json.result === "string" ? json.result : "",
    costUsd: typeof json.total_cost_usd === "number" ? json.total_cost_usd : null,
    apiErrorStatus: typeof json.api_error_status === "number" ? json.api_error_status : null,
    isError: json.is_error === true,
  };
}

const firstLine = (error) => String(error?.message ?? error).split("\n")[0];

// Names the first hits of an audit in one line, without control characters.
function summarize(hits) {
  const shown = hits.slice(0, 5).map((f) => f.replace(/[^\x20-\x7e]/g, "?")).join(", ");
  return hits.length > 5 ? `${shown} and ${hits.length - 5} more` : shown;
}

// Runs the audit of the write roots of the plan with audit (auditRoots) and returns { hits,
// warnings }. An error of the audit itself is a hit.
function auditPlan(plan, before, audit) {
  try {
    return audit({ scanRoots: plan.scanRoots, allowedRoots: plan.writable, before });
  } catch (error) {
    return { hits: [`audit error: ${error.message}`], warnings: [] };
  }
}

/**
 * Runs one agent with an explicit { runner, model, effort, fallbacks }: the model three times,
 * then each fallback once. Writes <logDir>/<name>.log and <logDir>/<name>.last and returns
 * { status: "ok" | "out-of-credits" | "auth-failed" | "audit-failed" | "failed" | "aborted", model,
 * attempts, costUsd, billing }. "failed" means that every attempt was a transient failure, or that
 * the cage could not start (bwrap missing or failing; there is no run outside the cage).
 * "auth-failed" (rejected or expired credential) ends the run at once, because a retry cannot fix
 * it. "audit-failed" (claude only) means that the audit after an attempt found a symlink out of the
 * write roots, an added git, Claude Code, Codex or agent instruction entry, an unreadable
 * directory or a tree beyond the walk limit; the result then has detail, the first hits as one
 * line, and the full list is in the log. The audit runs right after the child ends, also when the
 * run is aborted or the child fails to start, and its failure wins over every other outcome (an
 * error of the run is then not thrown, and a log write that is refused counts as one more hit).
 * A proxy process that ends while an attempt runs ends the child of the attempt; the audit
 * also runs, and without a hit the result is "failed" with detail "the proxy process ended (<how>)"
 * and no further attempt. A symlink out of the write roots that existed unchanged
 * before the attempt is no hit; the log names it once per call as "=== audit warning". "aborted"
 * means that abortSignal fired: the running child is ended (SIGTERM, then SIGKILL after
 * killGraceMs) and no further attempt starts. billing is required for claude ("api-key" or
 * "subscription"; a missing or unknown value throws, and so does "api-key" without a usable key
 * file) and is null in the result for codex. A claude child runs in the cage of agent-cage.mjs for
 * the repository root (default cwd) with the write roots of planCage plus extraWriteRoots and the
 * read directories extraReadRoots; a path that planCage refuses (a retargeted work directory, a
 * forbidden root) throws before anything starts. features (the bwrap features, see bwrapFeatures),
 * tmp (the directory of the per-call socket directory), forkImpl (starts the proxy process, see
 * startProxyProcess) and auditImpl (default auditRoots) are injectable. One proxy process with a
 * credential proxy, an egress proxy and an archive search service serves all attempts of the call. The files of logDir are
 * written below
 * logBase (default logDir), a directory that the caller trusts: a symlink below it is refused.
 * costUsd sums the cost that claude reports per attempt. A failed agent never throws.
 */
export async function runAttempts({
  runner, model: mainModel, effort, fallbacks = [], name, promptPath, logDir, cwd,
  billing: chosenBilling,
  timeoutMs = 3_600_000, retryDelayMs = 60_000, killGraceMs = 10_000,
  spawnImpl = spawn, sleep = defaultSleep, env = process.env, now = () => new Date(), abortSignal,
  apiKeyFile = defaultApiKeyFile(env), home = homedir(),
  credentialsFile = defaultCredentialsFile(home),
  root = cwd, extraWriteRoots = [], extraReadRoots = [], gitExec, features, tmp, logBase = logDir,
  forkImpl = fork, auditImpl = auditRoots,
}) {
  const models = [...Array(MAIN_ATTEMPTS).fill(mainModel), ...fallbacks];
  safeMkdir(logDir, logBase);
  const logPath = join(logDir, `${name}.log`);
  const lastPath = join(logDir, `${name}.last`);
  const prompt = readFileSync(promptPath, "utf8");
  const caged = runner === "claude";
  if (caged && !BILLINGS.includes(chosenBilling)) {
    throw new Error(`billing must be one of: ${BILLINGS.join(", ")}`);
  }
  if (caged && chosenBilling === "api-key") {
    const problem = checkApiKeyFile(apiKeyFile);
    if (problem) {
      throw new Error(`api-key billing needs a usable key file: ${apiKeyFile} ${problem}`);
    }
  }
  const billing = caged ? chosenBilling : null;
  const plan = caged
    ? planCage({ root, cwd, env, home, extraWriteRoots, extraReadRoots, exec: gitExec, features })
    : null;
  const runDir = caged ? createRunDir(tmp) : null;
  let proxy = null;
  let proxyEnd = null;

  let attempts = 0;
  let costUsd = 0;
  let usedModel = mainModel;
  const outcome = (status, detail) =>
    ({ status, model: usedModel, attempts, costUsd, billing, ...(detail ? { detail } : {}) });
  const warned = new Set();
  try {
    if (caged) {
      proxy = await startProxyProcess({
        billing, apiKeyFile, credentialsFile, apiSocket: join(runDir, API_SOCKET),
        egressSocket: join(runDir, EGRESS_SOCKET), archiveSocket: join(runDir, ARCHIVE_SOCKET),
        archiveEnv: dockerEnv(env), forkImpl,
      });
      proxy.died.then((text) => {
        proxyEnd = text;
      });
    }
    for (const model of models) {
      if (attempts > 0) await sleep(retryDelayMs, abortSignal);
      if (abortSignal?.aborted) return outcome("aborted");
      attempts += 1;
      usedModel = model;
      rmSync(lastPath, { force: true });
      const built = buildCommand({ runner, model, effort, lastMessagePath: lastPath });
      const command = caged ? "bwrap" : built.command;
      const args = caged
        ? cageArgs({
          plan, runDir, inner: cageCommand({ plan, argv: [plan.claudeBinary, ...built.args] }),
        })
        : built.args;
      const childEnv = caged ? cageEnv({ env, plan, billing }) : codexEnv(env);
      if (proxyEnd !== null) {
        safeAppendFile(logPath, `=== proxy process ended (${proxyEnd})\n`, logBase);
        return outcome("failed", `the proxy process ended (${proxyEnd})`);
      }
      const watched = caged ? snapshotWatched(plan.scanRoots) : null;
      const startedAt = now().toISOString();
      // The end of the proxy process ends the child of the attempt, like an abort.
      const proxyStop = new AbortController();
      proxy?.died.then(() => proxyStop.abort());
      const stopSignal = abortSignal
        ? AbortSignal.any([abortSignal, proxyStop.signal]) : proxyStop.signal;
      let result;
      let childError = null;
      try {
        result = await runChild({
          command, args, cwd, env: childEnv, prompt, timeoutMs, killGraceMs, spawnImpl,
          abortSignal: stopSignal,
        });
      } catch (error) {
        childError = error;
        const text = `${error.message}\n`;
        result = {
          stdout: "", stderr: text, combined: text, exitCode: -1, signal: null, timedOut: false,
          spawnFailed: true,
        };
      }
      // The audit follows the end of the child at once, also after an abort or an error, and
      // decides before anything of the attempt is recorded as its result.
      const audit = caged ? auditPlan(plan, watched, auditImpl) : { hits: [], warnings: [] };
      // After a failed audit a refused log write is one more hit, so that the attempt always ends
      // as audit-failed and the hits are not lost.
      const record = (text) => {
        if (audit.hits.length === 0) {
          safeAppendFile(logPath, text, logBase);
          return;
        }
        try {
          safeAppendFile(logPath, text, logBase);
        } catch (error) {
          audit.hits.push(`${logPath} (log write refused: ${firstLine(error)})`);
        }
      };
      // Every attempt is appended to the log under its own header; the classification reads the
      // output of this attempt only. Stdout of claude is the JSON object; it goes to the log only
      // when it is not JSON, so that nothing of an unexpected output is lost. The classification
      // of a JSON output reads stderr and the result text, because numbers of the JSON (token
      // counts, durations) could match a pattern. The result text of an unfinished or failed
      // attempt goes to the log under "=== result", because the next attempt replaces the last
      // message. After a failed audit the last message stays unwritten.
      let output;
      let lastMessage;
      let classified = result.combined;
      let parsed = null;
      if (runner === "codex") {
        output = result.combined;
        lastMessage = readOrEmpty(lastPath);
      } else {
        parsed = parseClaudeOutput(result.stdout);
        output = parsed.valid ? result.stderr : result.combined;
        lastMessage = parsed.isError ? "" : parsed.result;
        if (parsed.valid) classified = `${result.stderr}\n${parsed.result}`;
        if (parsed.valid && parsed.result !== "" && (parsed.isError || result.exitCode !== 0)) {
          const gap = output === "" || output.endsWith("\n") ? "" : "\n";
          const end = parsed.result.endsWith("\n") ? "" : "\n";
          output += `${gap}=== result\n${parsed.result}${end}`;
        }
        if (audit.hits.length === 0) safeWriteFile(lastPath, lastMessage, logBase);
      }
      const billed = billing ? ` billing ${billing}` : "";
      const header = `=== attempt ${attempts} ${model} ${startedAt}${billed}\n`;
      const costLine = parsed?.costUsd == null ? "" : `=== cost ${parsed.costUsd} USD\n`;
      const newline = output.endsWith("\n") || output === "" ? "" : "\n";
      record(`${header}${output}${newline}${exitLine(result, timeoutMs)}${costLine}`);
      costUsd += parsed?.costUsd ?? 0;
      if (audit.hits.length > 0) {
        record(`=== audit failed\n${audit.hits.join("\n")}\n`);
        return outcome("audit-failed", summarize(audit.hits));
      }
      for (const line of audit.warnings.filter((w) => !warned.has(w))) {
        warned.add(line);
        record(`=== audit warning ${line}\n`);
      }
      if (proxyEnd !== null) {
        record(`=== proxy process ended (${proxyEnd})\n`);
        return outcome("failed", `the proxy process ended (${proxyEnd})`);
      }
      if (childError) throw childError;
      // A cage that does not start (bwrap fails, or cage-init cannot relay or start claude) ends
      // the run: a retry cannot fix it and nothing runs uncaged.
      const cageFailed = result.exitCode !== 0 && /^(bwrap|cage-init): /m.test(result.stderr);
      if (caged && (result.spawnFailed || cageFailed)) return outcome("failed");
      if (abortSignal?.aborted) return outcome("aborted");
      const verdict = classifyOutput({
        runner, log: classified, lastMessage, exitCode: result.exitCode,
        apiErrorStatus: parsed?.apiErrorStatus, isError: parsed?.isError,
      });
      if (verdict === "ok") return outcome("ok");
      if (verdict === "out-of-credits") return outcome("out-of-credits");
      if (verdict === "auth-failed") return outcome("auth-failed");
    }
    return outcome("failed");
  } finally {
    await proxy?.close();
    if (runDir) rmSync(runDir, { recursive: true, force: true });
  }
}

/**
 * Runs a role with the runner that resolveModel gives for it (checker-a uses Claude in every run)
 * and returns the result of runAttempts under the files <logDir>/<role>.log and <role>.last.
 */
export async function runAgent({ runner, role, lake, billing, ...options }) {
  const resolved = resolveModel({ runner, role, lake });
  return runAttempts({
    ...options, billing, name: role, runner: resolved.runner, model: resolved.model,
    effort: resolved.effort, fallbacks: resolved.fallbacks,
  });
}
