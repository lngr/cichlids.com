// The forum archive query that archive-search.mjs and the archive search service share: the
// validation of a search request, the fixed SQL query and its run through "docker exec", the
// formatting of the answer and its size cap. The query reads the local dev database read-only and
// selects post text only, never member names. The term reaches psql as a variable (-v), never as
// part of the SQL text.
import { spawn } from "node:child_process";

export const CONTAINER = "stack-postgres-1";
export const MIN_TERM_LENGTH = 2;
export const MAX_TERM_LENGTH = 100;
export const DEFAULT_LIMIT = 30;
export const MAX_LIMIT = 100;
export const QUERY_TIMEOUT_MS = 30_000;
export const MAX_OUTPUT_BYTES = 200 * 1024;

// Upper bound of the raw psql output; 100 excerpts of 600 characters stay far below it.
const MAX_PSQL_BYTES = 1024 ** 2;
const MAX_STDERR_BYTES = 8 * 1024;

// What docker reports when the container is missing or stopped, or when the daemon is out of reach.
const NOT_REACHABLE = new RegExp(
  "No such container|is not running|Cannot connect to the Docker daemon|" +
  "failed to connect to the docker API|permission denied", "i",
);

// Forum ids of the legacy Phorum installation, as resolved by the legacy redirects of the API.
const FORUM_ID = { cichlids: 1, african: 2, market_place: 3 };

// Variables of the parent environment that docker needs to reach its daemon.
const DOCKER_VARIABLES = [
  "PATH", "HOME", "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG", "DOCKER_TLS_VERIFY",
  "DOCKER_CERT_PATH", "XDG_CONFIG_HOME",
];

export const SQL = `
SELECT t.legacy_id, t.category, p.id, replace(replace(left(p.body, 600), E'\\n', ' '), E'\\t', ' ')
FROM discussion_post p JOIN discussion_thread t ON t.id = p.thread_id
WHERE p.body ILIKE '%' || :'term' || '%' AND t.legacy_id IS NOT NULL
ORDER BY t.legacy_id, p.id
LIMIT :limit`;

// The messages that reach the caller. An error of any other kind answers the "failed" message, so
// nothing of a database or process error leaves the service.
const MESSAGES = {
  unavailable:
    `the archive database is not available (container ${CONTAINER} is not running or docker is ` +
    "not reachable)",
  timeout: `the archive query timed out after ${QUERY_TIMEOUT_MS / 1000} s`,
  busy: "too many archive queries at once, retry shortly",
  failed: "the archive query failed",
};

/** An error of a search with a code (invalid, unavailable, timeout, busy or failed) and a message. */
export class ArchiveError extends Error {
  constructor(code, message = MESSAGES[code] ?? MESSAGES.failed) {
    super(message);
    this.code = code;
  }
}

/** The message that a caller may see for any error. */
export const publicMessage = (error) =>
  (error instanceof ArchiveError ? error.message : MESSAGES.failed);

const invalid = (message) => new ArchiveError("invalid", message);

/**
 * Validates a search request, { term, limit }, and returns it with the default limit filled in.
 * The term is a string of 2 to 100 characters without control characters; the limit is an integer
 * from 1 to 100 (default 30). Throws an ArchiveError with code "invalid" otherwise.
 */
export function validateRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)) {
    throw invalid("the request must be a JSON object with term and limit");
  }
  if (Object.keys(request).some((key) => key !== "term" && key !== "limit")) {
    throw invalid("the request has a field other than term and limit");
  }
  const { term, limit = DEFAULT_LIMIT } = request;
  const length = typeof term === "string" ? [...term].length : 0;
  if (length < MIN_TERM_LENGTH || length > MAX_TERM_LENGTH || /\p{Cc}/u.test(term)) {
    throw invalid(
      `term must be a string of ${MIN_TERM_LENGTH} to ${MAX_TERM_LENGTH} characters without ` +
      "control characters",
    );
  }
  if (!Number.isInteger(limit) || limit < 1 || limit > MAX_LIMIT) {
    throw invalid(`limit must be an integer from 1 to ${MAX_LIMIT}`);
  }
  return { term, limit };
}

/** Returns the variables of env that docker needs, for the environment of a query. */
export function dockerEnv(env) {
  const out = {};
  for (const name of DOCKER_VARIABLES) if (env[name] !== undefined) out[name] = env[name];
  return out;
}

/**
 * Returns the query function that runs the fixed SQL through "docker exec" in container, with env
 * as the whole environment of the docker process. The function takes { term, limit, signal },
 * resolves with the raw psql output (tab separated rows) and rejects with an ArchiveError:
 * "unavailable" when docker or the container is missing or not reachable, "timeout" when signal
 * aborts or the database cancels the statement after timeoutMs, "failed" otherwise. spawnImpl is
 * injectable.
 */
export function createDockerQuery({
  env = {}, container = CONTAINER, timeoutMs = QUERY_TIMEOUT_MS, spawnImpl = spawn,
} = {}) {
  return ({ term, limit, signal }) => new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new ArchiveError("timeout"));
      return;
    }
    const child = spawnImpl("docker", [
      "exec", "-i", "-e", `PGOPTIONS=-c statement_timeout=${timeoutMs}`, container,
      "psql", "-U", "cichlids", "-d", "cichlids", "-AtF", "\t",
      "-v", "ON_ERROR_STOP=1", "-v", `term=${term}`, "-v", `limit=${limit}`,
    ], { env, stdio: ["pipe", "pipe", "pipe"] });
    const out = [];
    let outBytes = 0;
    let stderr = "";
    let verdict = null;
    const stop = () => {
      verdict ??= new ArchiveError("timeout");
      child.kill("SIGKILL");
    };
    signal?.addEventListener("abort", stop, { once: true });
    child.stdout.on("data", (chunk) => {
      outBytes += chunk.length;
      if (outBytes > MAX_PSQL_BYTES) {
        verdict ??= new ArchiveError("failed");
        child.kill("SIGKILL");
        return;
      }
      out.push(chunk);
    });
    child.stderr.setEncoding("utf8");
    child.stderr.on("data", (chunk) => {
      if (stderr.length < MAX_STDERR_BYTES) stderr += chunk;
    });
    child.stdin.on("error", () => {});
    child.on("error", (error) => {
      signal?.removeEventListener("abort", stop);
      reject(new ArchiveError(error.code === "ENOENT" ? "unavailable" : "failed"));
    });
    child.on("close", (code) => {
      signal?.removeEventListener("abort", stop);
      if (verdict) reject(verdict);
      else if (code === 0) resolve(Buffer.concat(out).toString("utf8"));
      else if (/statement timeout/i.test(stderr)) reject(new ArchiveError("timeout"));
      else if (NOT_REACHABLE.test(stderr)) reject(new ArchiveError("unavailable"));
      else reject(new ArchiveError("failed"));
    });
    child.stdin.end(SQL);
  });
}

/**
 * Formats the raw psql output as the text of archive-search.mjs: each matching thread with its
 * legacy thread id, its legacy forum URL and the excerpts of its posts, or "No posts found.".
 */
export function formatResults(raw) {
  const rows = raw.split("\n").filter(Boolean).map((line) => line.split("\t"));
  const lines = [];
  if (rows.length === 0) lines.push("No posts found.");
  let current = null;
  for (const [legacyId, category, , excerpt] of rows) {
    if (legacyId !== current) {
      current = legacyId;
      lines.push(
        `\n== cichlids.com forum thread ${legacyId}`,
        `   url: https://www.cichlids.com/disc/read.php?${FORUM_ID[category] ?? 1},${legacyId}`,
      );
    }
    lines.push(`   - ${excerpt}`);
  }
  return `${lines.join("\n")}\n`;
}

/**
 * Returns text unchanged when it fits in maxBytes (UTF-8), else its leading complete lines plus a
 * closing line that names the cut, within maxBytes together.
 */
export function capOutput(text, maxBytes = MAX_OUTPUT_BYTES) {
  if (Buffer.byteLength(text) <= maxBytes) return text;
  const marker = `[output cut at ${Math.floor(maxBytes / 1024)} KB]\n`;
  const room = Buffer.from(text).subarray(0, Math.max(0, maxBytes - Buffer.byteLength(marker)));
  const kept = room.toString("utf8").replace(/\uFFFD$/, "");
  return `${kept.slice(0, kept.lastIndexOf("\n") + 1)}${marker}`;
}
