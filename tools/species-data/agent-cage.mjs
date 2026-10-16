// The bubblewrap cage of the Claude agents. An agent reads untrusted web pages and runs with
// bypassed permissions, so it runs in a cage that shows it the repository read-only, a few
// directories read-write (the write roots), some directories read-only (the read directories) and
// nothing else of the user: no home directory, no other checkout, no docker socket, no host
// display, no credential. The cage has a network namespace of its own with a loopback interface
// only. A per-call directory outside the cage holds three unix sockets and is mounted read-only at
// /run/cage: the credential proxy (credential-proxy.mjs) behind the Anthropic API, where the agent
// holds only a placeholder credential, the egress proxy (egress-proxy.mjs) behind the rest of the
// internet, and the archive search service (archive-service.mjs) behind the forum archive. The init
// program of the cage (cage-init.mjs) relays two loopback ports to the first two sockets; the
// archive client (archive-search.mjs) connects to the third one directly.
//
// cageArgs builds the bwrap arguments from resolved real paths and touches no file. planCage
// resolves and validates the paths. cageEnv builds the environment of the cage from an allowlist.
import { execFileSync } from "node:child_process";
import {
  accessSync, constants, lstatSync, mkdtempSync, readlinkSync, realpathSync, statSync,
} from "node:fs";
import { homedir, tmpdir } from "node:os";
import { basename, delimiter, dirname, join, resolve, sep } from "node:path";
import { archiveRoot } from "./source-archive.mjs";

// The credentials that the cage holds. The proxy replaces them by the real ones.
export const PLACEHOLDER_API_KEY = "sk-ant-api03-placeholder-proxy";
export const PLACEHOLDER_OAUTH_TOKEN = "placeholder-oauth";

// Variables of the parent environment that the cage keeps when they are set.
const PASSED_VARIABLES = ["PATH", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "TZ"];

// The loopback ports that cage-init.mjs opens in the cage, the mount point of the per-call socket
// directory, the names of the sockets in it and the variable that names the archive socket.
export const API_RELAY_PORT = 18080;
export const EGRESS_RELAY_PORT = 18081;
export const CAGE_RUN_DIR = "/run/cage";
export const API_SOCKET = "api.sock";
export const EGRESS_SOCKET = "egress.sock";
export const ARCHIVE_SOCKET = "archive.sock";
export const ARCHIVE_SOCKET_VARIABLE = "CICHLIDS_ARCHIVE_SEARCH_SOCKET";

// Paths of the container that the cage hides when they exist: the host X display and the file
// systems of other containers.
const HIDDEN_PATHS = ["/X11-unix", "/var/lib/docker"];

// Size limits of the writable tmpfs mounts (bytes). The mounts that only hide a path (/run,
// /workspaces and the hidden paths) get a small one.
export const TMP_SIZE = 2 * 1024 ** 3;
export const HOME_SIZE = 512 * 1024 ** 2;
export const HIDDEN_SIZE = 1024 ** 2;

// Directories whose content the cage replaces by an empty tmpfs; a program there needs its own
// bind.
const HIDDEN_PREFIXES = ["/tmp", "/run", "/workspaces"];

// Directory names that no write root may lie in: git hooks and Claude Code settings run later,
// outside the cage.
const FORBIDDEN_SEGMENTS = [".git", ".claude"];

const MAX_LINKS = 40;

/** True when path is root or lies below it. */
export const isInside = (path, root) =>
  path === root || path.startsWith(root.endsWith(sep) ? root : root + sep);

/**
 * Returns the real path of the nearest existing ancestor of path plus the remaining segments, so
 * that a symlink cannot lead out of a root. A dangling symlink is followed to its target.
 */
export function realPathOf(path, links = 0) {
  if (links > MAX_LINKS) throw new Error(`too many symbolic links: ${path}`);
  const rest = [];
  let current = resolve(path);
  for (;;) {
    try {
      return join(realpathSync(current), ...rest);
    } catch (error) {
      if (error.code !== "ENOENT" && error.code !== "ENOTDIR") throw error;
    }
    let target = null;
    try {
      if (lstatSync(current).isSymbolicLink()) {
        target = resolve(dirname(current), readlinkSync(current));
      }
    } catch {
      target = null;
    }
    if (target !== null) return join(realPathOf(target, links + 1), ...rest);
    const parent = dirname(current);
    if (parent === current) return join(current, ...rest);
    rest.unshift(basename(current));
    current = parent;
  }
}

/**
 * Returns the real path of the executable name on the PATH value, or null when there is none. The
 * real path is what runs inside the cage, so a symlink in a bin directory leads to its target.
 */
export function resolveOnPath(name, pathValue) {
  for (const dir of String(pathValue ?? "").split(delimiter).filter(Boolean)) {
    const candidate = join(dir, name);
    try {
      if (!statSync(candidate).isFile()) continue;
      accessSync(candidate, constants.X_OK);
      return realpathSync(candidate);
    } catch {
      continue;
    }
  }
  return null;
}

/**
 * Returns { commonDir, gitDir } of the repository at root as real paths; an entry is null when git
 * fails (no repository). The main checkout is the parent of the common git directory.
 */
export function gitInfo(root, exec = execFileSync) {
  const ask = (flag) => {
    try {
      const out = exec("git", ["rev-parse", flag], {
        cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
      }).trim();
      return out ? realPathOf(resolve(root, out)) : null;
    } catch {
      return null;
    }
  };
  return { commonDir: ask("--git-common-dir"), gitDir: ask("--git-dir") };
}

/**
 * Returns the real path of <root>/data/species/work, or null when it does not exist. The path is
 * either a real directory of this checkout or a symlink into the main checkout; any other target
 * (an agent retargeted the symlink) throws. git is the result of gitInfo.
 */
export function validateWorkDir(root, git) {
  const link = join(root, "data", "species", "work");
  let real;
  try {
    real = realpathSync(link);
  } catch (error) {
    if (error.code === "ENOENT") return null;
    throw error;
  }
  const allowed = [join(realpathSync(root), "data", "species", "work")];
  if (git.commonDir) {
    const main = join(dirname(git.commonDir), "data", "species", "work");
    allowed.push(main);
    try {
      allowed.push(realpathSync(main));
    } catch {
      // The main checkout has no work directory; the plain path stays allowed.
    }
  }
  if (!allowed.includes(real)) {
    throw new Error(`${link} resolves to ${real}; it must be ${allowed.join(" or ")}`);
  }
  if (!statSync(real).isDirectory()) throw new Error(`${link} is not a directory`);
  return real;
}

/**
 * Returns the reason when the real path of a directory must not be a write root, else null. A write
 * root is not the repository, a directory that contains it, the home directory or a directory that
 * contains it, a git directory or a directory that contains or lies inside one, /tmp itself, the
 * Claude Code scratch directory /tmp/claude-<uid> itself, nor a path with a .git or .claude
 * segment (below the repository root when it lies inside the repository). confined limits the
 * root to the repository and /tmp. The same rules apply to a read directory.
 */
export function writeRootError(real, { root, home, gitDirs, confined = false }) {
  if (real === root) return "the repository root";
  if (isInside(root, real)) return "a directory that contains the repository";
  if (isInside(home, real)) {
    return real === home ? "the home directory" : "a directory that contains the home directory";
  }
  if (gitDirs.some((g) => isInside(g, real) || isInside(real, g))) {
    return "a git directory or a directory that holds one";
  }
  if (real === "/tmp" || /^\/tmp\/claude-\d+$/.test(real)) {
    return "/tmp or the Claude Code scratch directory in it";
  }
  const below = isInside(real, root) ? real.slice(root.length) : real;
  const forbidden = below.split(sep).find((segment) => FORBIDDEN_SEGMENTS.includes(segment));
  if (forbidden) return `inside ${forbidden}`;
  if (confined && !isInside(real, root) && !isInside(real, "/tmp")) {
    return "outside the repository and /tmp";
  }
  return null;
}

// Resolves an extra directory (a write root or a read directory): a path (relative to root) of an
// existing directory inside the repository or /tmp. Returns its real path; throws an Error that
// names label otherwise.
function resolveExtraRoot(path, { root, home, gitDirs }, label) {
  const full = resolve(root, path);
  let real;
  try {
    real = realpathSync(full);
    if (!statSync(real).isDirectory()) throw new Error("not a directory");
  } catch (error) {
    const why = error.code ?? error.message;
    throw new Error(`${label} ${full} is not allowed: not an existing directory (${why})`);
  }
  const problem = writeRootError(real, { root, home, gitDirs, confined: true });
  if (problem) throw new Error(`${label} ${full} is not allowed: ${problem}`);
  return real;
}

/**
 * Resolves an extra write root: a path (relative to root) of an existing directory inside the
 * repository or /tmp. Returns its real path; throws an Error with the reason otherwise. root, home
 * and gitDirs are real paths (see writeRootsOf). A write root can be a code directory such as
 * tools/; what the agent changes there must be read and approved before it runs.
 */
export const resolveWriteRoot = (path, options) => resolveExtraRoot(path, options, "write root");

/** Resolves a read directory with the rules of resolveWriteRoot. The cage binds it read-only. */
export const resolveReadRoot = (path, options) => resolveExtraRoot(path, options, "read directory");

// Returns the reason when the source archive root (a real path) must not be a write root.
function archiveError(archive, { root, home }) {
  if (isInside(archive, home)) return "inside the home directory";
  if (isInside(archive, root)) return "inside the repository";
  return null;
}

/**
 * Returns { root, home, gitDirs, work, archive, writable, readable } with real paths: the write
 * roots <root>/data, the validated real path of <root>/data/species/work, the source archive root
 * (each when it exists) and the extra roots (see resolveWriteRoot), without duplicates, and the
 * read directories (see resolveReadRoot). Throws an Error with a clear message when a path is not
 * allowed: the source archive root lies inside the home directory or the repository, or a read
 * directory lies inside a write root.
 */
export function writeRootsOf({
  root, env = process.env, home = homedir(), extraWriteRoots = [], extraReadRoots = [],
  exec = execFileSync,
}) {
  const realRoot = realpathSync(root);
  const realHome = realPathOf(home);
  const git = gitInfo(realRoot, exec);
  const gitDirs = [git.commonDir, git.gitDir].filter(Boolean);
  const check = (real, label) => {
    const problem = writeRootError(real, { root: realRoot, home: realHome, gitDirs });
    if (problem) throw new Error(`write root ${label} is not allowed: ${problem}`);
    return real;
  };

  const writable = [];
  const dataDir = join(realRoot, "data");
  if (statExists(dataDir)) {
    const real = realpathSync(dataDir);
    if (real !== dataDir) {
      throw new Error(`${dataDir} resolves to ${real}; it must be a real directory`);
    }
    writable.push(check(real, dataDir));
  }
  const work = validateWorkDir(realRoot, git);
  if (work) writable.push(check(work, work));
  const archive = archiveRoot(env);
  const archiveProblem = archiveError(realPathOf(archive), { root: realRoot, home: realHome });
  if (archiveProblem) {
    throw new Error(`CICHLIDS_SOURCE_ARCHIVE ${archive} is not allowed: ${archiveProblem}`);
  }
  const realArchive = statExists(archive) ? realpathSync(archive) : null;
  if (realArchive) writable.push(check(realArchive, archive));
  const options = { root: realRoot, home: realHome, gitDirs };
  for (const extra of extraWriteRoots) writable.push(resolveWriteRoot(extra, options));
  const readable = [...new Set(extraReadRoots.map((dir) => resolveReadRoot(dir, options)))];
  for (const dir of readable) {
    const holder = writable.find((w) => isInside(dir, w));
    if (holder) throw new Error(`read directory ${dir} lies inside the write root ${holder}`);
  }
  return {
    root: realRoot, home: realHome, gitDirs, work, archive: realArchive,
    writable: [...new Set(writable)], readable,
  };
}

/**
 * Returns the features of the installed bwrap that the cage uses, { disableUserns, size }, read
 * from "bwrap --help". Without an exec argument the answer is computed once and cached; a missing
 * or failing bwrap gives no feature. exec is injectable for tests.
 */
let cachedFeatures = null;
export function bwrapFeatures(exec) {
  const probe = (run) => {
    let text = "";
    try {
      text = String(run("bwrap", ["--help"], {
        encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
      }));
    } catch (error) {
      text = String(error?.stdout ?? "");
    }
    return { disableUserns: /--disable-userns\b/.test(text), size: /--size\b/.test(text) };
  };
  if (exec) return probe(exec);
  cachedFeatures ??= probe(execFileSync);
  return cachedFeatures;
}

/**
 * Resolves and validates everything the cage needs and returns { root, home, chdir, claudeBinary,
 * claudeBind, nodeBinary, readOnly, writable, scanRoots, archive, hidden, features } with real
 * paths. writable is the result of writeRootsOf; readOnly lists { source, target } pairs (the
 * repository, the programs, the home items and the read directories). cwd must lie in the
 * repository, a write root or a read directory. scanRoots are the write roots that the audit scans
 * (those inside the repository, the work directory and the source archive root). hidden lists the
 * paths of the container that the cage replaces by an empty tmpfs (exists is injectable), and
 * features is the result of bwrapFeatures (injectable). Throws an Error with a clear message when a
 * path is not allowed.
 */
export function planCage({
  root, cwd = root, env = process.env, home = homedir(), extraWriteRoots = [], extraReadRoots = [],
  exec = execFileSync, exists = statExists, features = bwrapFeatures(),
}) {
  const roots = writeRootsOf({ root, env, home, extraWriteRoots, extraReadRoots, exec });
  const { root: realRoot, home: realHome, writable, readable, work } = roots;

  const chdir = realPathOf(cwd);
  if (!isInside(chdir, realRoot) && ![...writable, ...readable].some((r) => isInside(chdir, r))) {
    throw new Error(
      `the working directory ${chdir} lies outside the repository and the write roots`,
    );
  }

  const claudeBinary = resolveOnPath("claude", env.PATH);
  if (!claudeBinary) throw new Error("claude is not on the PATH");
  const claudeDir = dirname(claudeBinary);
  // A directory that is the home directory, the file system root or holds the repository is not
  // bound whole; the binary alone is.
  const claudeBind = claudeDir === realHome || claudeDir === sep || isInside(realRoot, claudeDir)
    ? claudeBinary : claudeDir;

  const readOnly = [
    { source: realRoot, target: realRoot }, { source: claudeBind, target: claudeBind },
  ];
  // The init program runs with the node of the host. It lies on the read-only root unless a tmpfs
  // of the cage hides it, and then the binary alone is bound.
  const nodeBinary = realpathSync(process.execPath);
  if ([realHome, ...HIDDEN_PREFIXES].some((hidden) => isInside(nodeBinary, hidden))) {
    readOnly.push({ source: nodeBinary, target: nodeBinary });
  }
  for (const rel of [[".cache", "ms-playwright"], [".config", "cichlids", "openalex.key"]]) {
    const target = join(realHome, ...rel);
    if (statExists(target)) readOnly.push({ source: realpathSync(target), target });
  }
  for (const dir of readable) readOnly.push({ source: dir, target: dir });

  return {
    root: realRoot, home: realHome, chdir, claudeBinary, claudeBind, nodeBinary, readOnly, writable,
    scanRoots: [...new Set([
      ...writable.filter((r) => isInside(r, realRoot) || r === work),
      ...(roots.archive ? [roots.archive] : []),
    ])],
    archive: roots.archive, hidden: HIDDEN_PATHS.filter(exists), features,
  };
}

function statExists(path) {
  try {
    statSync(path);
    return true;
  } catch {
    return false;
  }
}

/**
 * Returns the bwrap arguments (without "bwrap") that run inner (a command and its arguments) in
 * the cage. plan is the result of planCage and runDir the per-call directory with the proxy
 * sockets. The order matters, because a later mount lies on top of an earlier one: the whole file
 * system read-only, then fresh /dev, /proc, /tmp, /run, the home directory and /workspaces (the
 * checkouts and backups of the host stay hidden) and the hidden paths of the plan, then runDir
 * read-only at /run/cage (connecting to a socket needs no write access to the mount), the
 * read-only and the read-write binds on top. The cage gets its own pid, ipc and network namespace
 * (the loopback interface only), and, when bwrap supports it, a size limit on every tmpfs and no
 * user namespaces of its own.
 */
export function cageArgs({ plan, inner, runDir }) {
  const sized = (size, path) =>
    (plan.features.size ? ["--size", String(size), "--tmpfs", path] : ["--tmpfs", path]);
  const args = [
    "--ro-bind", "/", "/",
    "--dev", "/dev",
    "--proc", "/proc",
    ...sized(TMP_SIZE, "/tmp"),
    ...sized(HIDDEN_SIZE, "/run"),
    ...sized(HOME_SIZE, plan.home),
    ...sized(HIDDEN_SIZE, "/workspaces"),
  ];
  for (const path of plan.hidden) args.push(...sized(HIDDEN_SIZE, path));
  args.push(
    "--unshare-pid", "--unshare-ipc", "--unshare-net", "--new-session", "--die-with-parent",
  );
  if (plan.features.disableUserns) args.push("--unshare-user", "--disable-userns");
  args.push("--ro-bind", runDir, CAGE_RUN_DIR);
  for (const { source, target } of plan.readOnly) args.push("--ro-bind", source, target);
  for (const path of plan.writable) args.push("--bind", path, path);
  args.push("--chdir", plan.chdir, ...inner);
  return args;
}

/** The relay specification for cage-init.mjs: the loopback ports and the sockets they relay to. */
export const relaySpec = () =>
  `${API_RELAY_PORT}=${CAGE_RUN_DIR}/${API_SOCKET},` +
  `${EGRESS_RELAY_PORT}=${CAGE_RUN_DIR}/${EGRESS_SOCKET}`;

/**
 * Returns the command inside the cage for the program argv (a command and its arguments): the host
 * node runs the init program of the repository, which opens the relays and starts argv.
 */
export const cageCommand = ({ plan, argv }) => [
  plan.nodeBinary, join(plan.root, "tools", "species-data", "cage-init.mjs"), relaySpec(), "--",
  ...argv,
];

/**
 * Creates the per-call directory (mode 700) for the sockets of the proxies and the archive search
 * service in tmp and returns its real path.
 */
export function createRunDir(tmp = tmpdir()) {
  return realpathSync(mkdtempSync(join(tmp, "cage-")));
}

/**
 * Returns the environment of the cage: the allowlisted variables of env that are set, HOME, the
 * Claude config directory below it, the source archive (its real path when the plan has one), the
 * addresses of the relays (the API relay as ANTHROPIC_BASE_URL, the egress relay as the HTTP and
 * HTTPS proxy of curl and node, which skip loopback addresses), the path of the archive search
 * socket in the cage and the placeholder credential of the billing. Nothing else passes.
 */
export function cageEnv({ env, plan, billing }) {
  const out = {};
  for (const name of PASSED_VARIABLES) if (env[name] !== undefined) out[name] = env[name];
  out.HOME = plan.home;
  out.CLAUDE_CONFIG_DIR = join(plan.home, ".claude");
  if (env.CICHLIDS_SOURCE_ARCHIVE !== undefined) {
    out.CICHLIDS_SOURCE_ARCHIVE = plan.archive ?? env.CICHLIDS_SOURCE_ARCHIVE;
  }
  const egress = `http://127.0.0.1:${EGRESS_RELAY_PORT}`;
  for (const name of ["HTTPS_PROXY", "HTTP_PROXY", "https_proxy", "http_proxy"]) out[name] = egress;
  for (const name of ["NO_PROXY", "no_proxy"]) out[name] = "127.0.0.1,localhost";
  out.NODE_USE_ENV_PROXY = "1";
  out.ANTHROPIC_BASE_URL = `http://127.0.0.1:${API_RELAY_PORT}`;
  out[ARCHIVE_SOCKET_VARIABLE] = `${CAGE_RUN_DIR}/${ARCHIVE_SOCKET}`;
  if (billing === "api-key") out.ANTHROPIC_API_KEY = PLACEHOLDER_API_KEY;
  else if (billing === "subscription") out.CLAUDE_CODE_OAUTH_TOKEN = PLACEHOLDER_OAUTH_TOKEN;
  else throw new Error("billing must be one of: api-key, subscription");
  return out;
}
