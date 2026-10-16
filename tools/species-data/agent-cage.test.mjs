// Tests for the cage: argument order, environment allowlist, path validation. Nothing starts bwrap.
import { test } from "node:test";
import assert from "node:assert/strict";
import { chmodSync, mkdirSync, mkdtempSync, realpathSync, statSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  API_RELAY_PORT, EGRESS_RELAY_PORT, HIDDEN_SIZE, HOME_SIZE, PLACEHOLDER_API_KEY,
  PLACEHOLDER_OAUTH_TOKEN, TMP_SIZE, bwrapFeatures,
  cageArgs, cageCommand, cageEnv, createRunDir, gitInfo, isInside, planCage, realPathOf, relaySpec, resolveOnPath,
  resolveReadRoot, resolveWriteRoot, validateWorkDir, writeRootError, writeRootsOf,
} from "./agent-cage.mjs";
import { fakeClaude, fakeHome, fakeRepo } from "./agent-test-support.mjs";

const plan = {
  root: "/w/repo", home: "/home/op", chdir: "/w/repo", claudeBinary: "/opt/claude/claude", claudeBind: "/opt/claude",
  nodeBinary: "/usr/bin/node", archive: "/w/archive", hidden: ["/X11-unix", "/var/lib/docker"],
  features: { disableUserns: true, size: true },
  readOnly: [
    { source: "/w/repo", target: "/w/repo" }, { source: "/opt/claude", target: "/opt/claude" },
    { source: "/real/ms-playwright", target: "/home/op/.cache/ms-playwright" },
    { source: "/tmp/claude-1000/s/briefs", target: "/tmp/claude-1000/s/briefs" },
  ],
  writable: ["/w/repo/data", "/w/main/data/species/work", "/w/archive"],
};
// The bwrap features that the planCage tests inject, so that no test starts bwrap.
const features = { disableUserns: true, size: true };

test("cageArgs: sized tmpfs mounts first, then the hidden paths, then the socket directory and the binds, then the command", () => {
  const hidden = String(HIDDEN_SIZE);
  assert.deepEqual(cageArgs({ plan, runDir: "/tmp/cage-x", inner: ["/opt/claude/claude", "-p"] }), [
    "--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc",
    "--size", String(TMP_SIZE), "--tmpfs", "/tmp", "--size", hidden, "--tmpfs", "/run",
    "--size", String(HOME_SIZE), "--tmpfs", "/home/op", "--size", hidden, "--tmpfs", "/workspaces",
    "--size", hidden, "--tmpfs", "/X11-unix", "--size", hidden, "--tmpfs", "/var/lib/docker",
    "--unshare-pid", "--unshare-ipc", "--unshare-net", "--new-session", "--die-with-parent",
    "--unshare-user", "--disable-userns",
    "--ro-bind", "/tmp/cage-x", "/run/cage",
    "--ro-bind", "/w/repo", "/w/repo", "--ro-bind", "/opt/claude", "/opt/claude",
    "--ro-bind", "/real/ms-playwright", "/home/op/.cache/ms-playwright",
    "--ro-bind", "/tmp/claude-1000/s/briefs", "/tmp/claude-1000/s/briefs",
    "--bind", "/w/repo/data", "/w/repo/data", "--bind", "/w/main/data/species/work", "/w/main/data/species/work",
    "--bind", "/w/archive", "/w/archive",
    "--chdir", "/w/repo", "/opt/claude/claude", "-p",
  ]);
});

test("cageArgs: the cage has a network namespace of its own", () => {
  assert.ok(cageArgs({ plan, runDir: "/r", inner: ["c"] }).includes("--unshare-net"));
  assert.ok(cageArgs({ plan: { ...plan, features: {} }, runDir: "/r", inner: ["c"] }).includes("--unshare-net"));
});

test("cageArgs: size limits and user namespace blocking only when bwrap supports them, hidden paths only when present", () => {
  const bare = cageArgs({ plan: { ...plan, features: { disableUserns: false, size: false }, hidden: [] }, runDir: "/r", inner: ["c"] });
  for (const flag of ["--size", "--disable-userns", "--unshare-user", "/X11-unix", "/var/lib/docker"]) assert.ok(!bare.includes(flag), flag);
  assert.deepEqual(bare.slice(0, 15), [
    "--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc", "--tmpfs", "/tmp", "--tmpfs", "/run", "--tmpfs", "/home/op",
    "--tmpfs", "/workspaces",
  ]);
  const sizeOnly = cageArgs({ plan: { ...plan, features: { size: true } }, runDir: "/r", inner: ["c"] });
  assert.ok(sizeOnly.includes("--size") && !sizeOnly.includes("--disable-userns"));
});

test("cageArgs: every tmpfs has a size limit when bwrap supports it, and the socket directory is read-only", () => {
  const args = cageArgs({ plan, runDir: "/r", inner: ["c"] });
  const tmpfs = args.flatMap((a, i) => (a === "--tmpfs" ? [i] : []));
  assert.equal(tmpfs.length, 6);
  for (const i of tmpfs) assert.equal(args[i - 2], "--size", args[i + 1]);
  const bind = args.indexOf("/run/cage");
  assert.equal(args[bind - 2], "--ro-bind");
  assert.deepEqual(args.slice(bind - 2, bind + 1), ["--ro-bind", "/r", "/run/cage"]);
  assert.ok(!args.some((a, i) => a === "--bind" && args[i + 2] === "/run/cage"));
});

test("cageArgs: every bind comes after the last tmpfs and /run/cage after the tmpfs of /run", () => {
  const args = cageArgs({ plan, runDir: "/r", inner: ["c"] });
  const lastTmpfs = args.lastIndexOf("--tmpfs");
  assert.ok(args.indexOf("--bind") > lastTmpfs);
  for (const flag of ["--ro-bind", "--bind"]) {
    for (let i = args.indexOf(flag, 4); i !== -1; i = args.indexOf(flag, i + 1)) assert.ok(i > lastTmpfs, `${flag} at ${i}`);
  }
  assert.ok(args.indexOf("/run/cage") > args.indexOf("/run"));
});

test("relaySpec and cageCommand: the host node runs the init program of the repository with the relay specification", () => {
  assert.equal(relaySpec(), `${API_RELAY_PORT}=/run/cage/api.sock,${EGRESS_RELAY_PORT}=/run/cage/egress.sock`);
  assert.deepEqual(cageCommand({ plan, argv: ["/opt/claude/claude", "-p", "--model", "m"] }), [
    "/usr/bin/node", "/w/repo/tools/species-data/cage-init.mjs", relaySpec(), "--", "/opt/claude/claude", "-p", "--model", "m",
  ]);
});

test("createRunDir: creates a directory with mode 700 under the given tmp, as a real path", () => {
  const tmp = realpathSync(mkdtempSync(join(tmpdir(), "cage-tmp-")));
  const a = createRunDir(tmp);
  const b = createRunDir(tmp);
  assert.notEqual(a, b);
  assert.equal(realpathSync(a), a);
  assert.ok(isInside(a, tmp));
  assert.equal(statSync(a).mode & 0o777, 0o700);
});

test("cageEnv: the allowlist, the relay addresses and one placeholder credential per billing", () => {
  const env = {
    PATH: "/bin", LANG: "C.UTF-8", LC_ALL: "C", LC_CTYPE: "C", TERM: "xterm", TZ: "UTC", CICHLIDS_SOURCE_ARCHIVE: "/link/archive",
    HOME: "/home/other", ANTHROPIC_API_KEY: "k", CLAUDE_CODE_OAUTH_TOKEN: "t", GITHUB_TOKEN: "g", DISPLAY: ":0", CLAUDECODE: "1",
    NuGetPackageSourceCredentials_x: "c", HTTPS_PROXY: "http://p", NODE_USE_ENV_PROXY: "0", ANTHROPIC_BASE_URL: "http://x",
    SSH_AUTH_SOCK: "/s", NO_PROXY: "*", CICHLIDS_ARCHIVE_SEARCH_SOCKET: "/host/elsewhere.sock",
  };
  const egress = `http://127.0.0.1:${EGRESS_RELAY_PORT}`;
  const common = {
    PATH: "/bin", LANG: "C.UTF-8", LC_ALL: "C", LC_CTYPE: "C", TERM: "xterm", TZ: "UTC", HOME: "/home/op", CLAUDE_CONFIG_DIR: "/home/op/.claude",
    CICHLIDS_SOURCE_ARCHIVE: "/w/archive", ANTHROPIC_BASE_URL: `http://127.0.0.1:${API_RELAY_PORT}`,
    CICHLIDS_ARCHIVE_SEARCH_SOCKET: "/run/cage/archive.sock",
    HTTPS_PROXY: egress, HTTP_PROXY: egress, https_proxy: egress, http_proxy: egress,
    NO_PROXY: "127.0.0.1,localhost", no_proxy: "127.0.0.1,localhost", NODE_USE_ENV_PROXY: "1",
  };
  assert.deepEqual(cageEnv({ env, plan, billing: "api-key" }), { ...common, ANTHROPIC_API_KEY: PLACEHOLDER_API_KEY });
  assert.deepEqual(cageEnv({ env, plan, billing: "subscription" }), { ...common, CLAUDE_CODE_OAUTH_TOKEN: PLACEHOLDER_OAUTH_TOKEN });
  assert.throws(() => cageEnv({ env, plan, billing: "both" }), /billing must be one of/);
});

test("cageEnv: variables that are not set stay out, and so does an unset source archive", () => {
  const out = cageEnv({ env: { PATH: "/bin" }, plan, billing: "subscription" });
  assert.deepEqual(Object.keys(out).sort(), [
    "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CONFIG_DIR", "HOME", "HTTPS_PROXY", "HTTP_PROXY", "NODE_USE_ENV_PROXY", "NO_PROXY", "PATH",
    "ANTHROPIC_BASE_URL", "CICHLIDS_ARCHIVE_SEARCH_SOCKET", "http_proxy", "https_proxy", "no_proxy",
  ].sort());
  assert.equal(cageEnv({ env: { PATH: "/bin", CICHLIDS_SOURCE_ARCHIVE: "/x" }, plan: { ...plan, archive: null }, billing: "api-key" }).CICHLIDS_SOURCE_ARCHIVE, "/x");
});

test("isInside and realPathOf", () => {
  assert.ok(isInside("/a/b", "/a"));
  assert.ok(isInside("/a", "/a"));
  assert.ok(!isInside("/ab", "/a"));
  assert.ok(isInside("/a", "/"));
  const dir = realpathSync(mkdtempSync(join(tmpdir(), "cage-real-")));
  symlinkSync(join(dir, "missing", "target"), join(dir, "dangling"));
  symlinkSync(dir, join(dir, "self"));
  assert.equal(realPathOf(join(dir, "self", "new", "file")), join(dir, "new", "file"));
  assert.equal(realPathOf(join(dir, "dangling", "x")), join(dir, "missing", "target", "x"));
  symlinkSync(join(dir, "loop2"), join(dir, "loop1"));
  symlinkSync(join(dir, "loop1"), join(dir, "loop2"));
  assert.throws(() => realPathOf(join(dir, "loop1")), /too many symbolic links/);
});

test("resolveOnPath: the real path of the first executable file on the PATH", () => {
  const first = fakeClaude();
  const second = fakeClaude();
  const link = join(mkdtempSync(join(tmpdir(), "cage-link-")), "claude");
  symlinkSync(second.bin, link);
  assert.equal(resolveOnPath("claude", `${first.dir}:${second.dir}`), first.bin);
  assert.equal(resolveOnPath("claude", `${join(link, "..")}`), second.bin);
  assert.equal(resolveOnPath("claude", "/nonexistent"), null);
  assert.equal(resolveOnPath("claude", undefined), null);
  const plain = join(mkdtempSync(join(tmpdir(), "cage-plain-")), "claude");
  writeFileSync(plain, "x");
  chmodSync(plain, 0o644);
  assert.equal(resolveOnPath("claude", join(plain, "..")), null);
});

// A git that answers rev-parse for a repository with the main checkout at main.
const gitAt = (main, gitDir) => (cmd, args, options) => {
  assert.equal(cmd, "git");
  assert.ok(options.cwd);
  return `${{ "--git-common-dir": join(main, ".git"), "--git-dir": gitDir }[args[1]]}\n`;
};
const noGit = () => {
  throw new Error("not a git repository");
};

test("gitInfo: real paths of the common git directory and the git directory, null when git fails", () => {
  const main = realpathSync(mkdtempSync(join(tmpdir(), "cage-main-")));
  mkdirSync(join(main, ".git", "worktrees", "w"), { recursive: true });
  assert.deepEqual(gitInfo(main, gitAt(main, join(main, ".git", "worktrees", "w"))), {
    commonDir: join(main, ".git"), gitDir: join(main, ".git", "worktrees", "w"),
  });
  assert.deepEqual(gitInfo(main, noGit), { commonDir: null, gitDir: null });
});

// A worktree at repo whose data/species/work is a symlink to target.
function worktree(target) {
  const main = realpathSync(mkdtempSync(join(tmpdir(), "cage-main-")));
  mkdirSync(join(main, ".git"));
  mkdirSync(join(main, "data", "species", "work"), { recursive: true });
  const root = fakeRepo();
  symlinkSync(target(main, root), join(root, "data", "species", "work"));
  return { main, root, git: gitInfo(root, gitAt(main, join(main, ".git", "worktrees", "w"))) };
}

test("validateWorkDir: a real directory, a missing one and the work directory of the main checkout pass", () => {
  const root = fakeRepo();
  assert.equal(validateWorkDir(root, { commonDir: null }), null);
  mkdirSync(join(root, "data", "species", "work"));
  assert.equal(validateWorkDir(root, { commonDir: null }), join(root, "data", "species", "work"));
  const linked = worktree((main) => join(main, "data", "species", "work"));
  assert.equal(validateWorkDir(linked.root, linked.git), join(linked.main, "data", "species", "work"));
});

test("validateWorkDir: a symlink retargeted to the home directory, a repository parent or the main checkout root fails", () => {
  const home = fakeHome();
  for (const target of [() => home, (main, root) => join(root, ".."), (main) => main, (main) => join(main, "data"), () => "/"]) {
    const linked = worktree(target);
    assert.throws(() => validateWorkDir(linked.root, linked.git), /data\/species\/work resolves to .* it must be /);
  }
  // Without git no main checkout is known, so the symlink into it fails too.
  const linked = worktree((main) => join(main, "data", "species", "work"));
  assert.throws(() => validateWorkDir(linked.root, { commonDir: null }), /it must be /);
});

test("validateWorkDir: a work path that is a file fails", () => {
  const root = fakeRepo();
  writeFileSync(join(root, "data", "species", "work"), "x");
  assert.throws(() => validateWorkDir(root, { commonDir: null }), /is not a directory|it must be/);
});

const checkRoot = { root: "/w/repo", home: "/home/op", gitDirs: ["/w/main/.git", "/w/main/.git/worktrees/w"] };

test("writeRootError: the repository, its parents, home, git directories, /tmp and forbidden segments", () => {
  const cases = [
    ["/w/repo", "the repository root"],
    ["/w", "a directory that contains the repository"],
    ["/", "a directory that contains the repository"],
    ["/home/op", "the home directory"],
    ["/home", "a directory that contains the home directory"],
    ["/w/main/.git", "a git directory or a directory that holds one"],
    ["/w/main/.git/hooks", "a git directory or a directory that holds one"],
    ["/w/main", "a git directory or a directory that holds one"],
    ["/tmp", "/tmp or the Claude Code scratch directory in it"],
    ["/tmp/claude-1000", "/tmp or the Claude Code scratch directory in it"],
    ["/w/repo/.claude/worktrees", "inside .claude"],
    ["/w/repo/docs/.git/x", "inside .git"],
    ["/tmp/x/.claude", "inside .claude"],
  ];
  for (const [path, reason] of cases) assert.equal(writeRootError(path, checkRoot), reason, path);
  for (const path of ["/w/repo/data", "/w/repo/tools/species-data", "/tmp/x", "/tmp/claude-1000/s/scratchpad/agents", "/w/archive"]) {
    assert.equal(writeRootError(path, checkRoot), null, path);
  }
});

test("writeRootError: a repository inside a .claude directory does not taint its own paths", () => {
  const nested = { root: "/w/main/.claude/worktrees/wt", home: "/home/op", gitDirs: [] };
  assert.equal(writeRootError("/w/main/.claude/worktrees/wt/data", nested), null);
  assert.equal(writeRootError("/w/main/.claude/worktrees/wt/.claude/x", nested), "inside .claude");
});

test("writeRootError: confined limits the root to the repository and /tmp", () => {
  const confined = { ...checkRoot, confined: true };
  assert.equal(writeRootError("/w/repo/docs", confined), null);
  assert.equal(writeRootError("/tmp/x", confined), null);
  assert.equal(writeRootError("/etc", confined), "outside the repository and /tmp");
  assert.equal(writeRootError("/w/archive", confined), "outside the repository and /tmp");
  assert.equal(writeRootError("/w/archive", checkRoot), null);
});

test("resolveWriteRoot: existing directories inside the repository or /tmp, resolved to real paths", () => {
  const root = fakeRepo();
  const home = fakeHome();
  const tmp = realpathSync(mkdtempSync(join(tmpdir(), "cage-write-")));
  const options = { root, home, gitDirs: [] };
  assert.equal(resolveWriteRoot("tools/species-data", options), join(root, "tools", "species-data"));
  assert.equal(resolveWriteRoot(tmp, options), tmp);
  symlinkSync(join(root, "docs"), join(tmp, "to-docs"));
  assert.equal(resolveWriteRoot(join(tmp, "to-docs"), options), join(root, "docs"));
});

test("resolveWriteRoot: a missing path, a file, a link out of the repository and a forbidden place fail", () => {
  const root = fakeRepo();
  const home = fakeHome();
  mkdirSync(join(root, ".claude"));
  mkdirSync(join(root, ".git", "hooks"), { recursive: true });
  writeFileSync(join(root, "README.md"), "x");
  symlinkSync(home, join(root, "docs-link"));
  const options = { root, home, gitDirs: [join(root, ".git")] };
  for (const [path, message] of [
    ["absent", /not an existing directory/], ["README.md", /not an existing directory/], [".", /the repository root/],
    ["..", /contains the repository/], ["docs-link", /the home directory/], [".claude", /inside \.claude/], [".git/hooks", /git directory/],
    ["/etc", /outside the repository and \/tmp/], ["/tmp", /contains the repository|scratch directory/],
  ]) {
    assert.throws(() => resolveWriteRoot(path, options), message, path);
  }
});

function worktreeParts() {
  const root = fakeRepo();
  const home = fakeHome();
  const archive = realpathSync(mkdtempSync(join(tmpdir(), "cage-archive-")));
  return { root, home, archive, env: { CICHLIDS_SOURCE_ARCHIVE: archive } };
}

test("writeRootsOf: data, the work directory, the archive and the extra roots as real paths without duplicates", () => {
  const p = worktreeParts();
  const linked = worktree((main) => join(main, "data", "species", "work"));
  const exec = gitAt(linked.main, join(linked.main, ".git", "worktrees", "w"));
  const roots = writeRootsOf({ root: linked.root, env: p.env, home: p.home, extraWriteRoots: ["tools/species-data", "data"], exec });
  assert.deepEqual(roots.writable, [
    join(linked.root, "data"), join(linked.main, "data", "species", "work"), p.archive, join(linked.root, "tools", "species-data"),
  ]);
  assert.equal(roots.work, join(linked.main, "data", "species", "work"));
  assert.equal(roots.archive, p.archive);
});

test("writeRootsOf: a missing data directory and a missing archive are skipped", () => {
  const root = realpathSync(mkdtempSync(join(tmpdir(), "cage-bare-")));
  const outside = realpathSync(mkdtempSync(join(tmpdir(), "cage-outside-")));
  const roots = writeRootsOf({ root, env: { CICHLIDS_SOURCE_ARCHIVE: join(outside, "no-archive") }, home: fakeHome(), exec: noGit });
  assert.deepEqual(roots.writable, []);
  assert.equal(roots.archive, null);
});

test("writeRootsOf: a source archive root inside the home directory or the repository is an error, also a missing one", () => {
  const root = fakeRepo();
  const home = fakeHome();
  const linked = join(mkdtempSync(join(tmpdir(), "cage-archive-link-")), "archive");
  symlinkSync(join(home, "archive"), linked);
  for (const archive of [join(home, "archive"), home, join(root, "archive"), join(root, "data", "archive"), linked]) {
    if (archive !== home && archive !== linked) mkdirSync(archive, { recursive: true });
    assert.throws(() => writeRootsOf({ root, env: { CICHLIDS_SOURCE_ARCHIVE: archive }, home, exec: noGit }), /CICHLIDS_SOURCE_ARCHIVE .* is not allowed: inside the (home directory|repository)/, archive);
  }
  assert.throws(() => writeRootsOf({ root, env: { CICHLIDS_SOURCE_ARCHIVE: join(home, "absent", "a") }, home, exec: noGit }), /inside the home directory/);
});

test("writeRootsOf: read directories are validated like write roots and may not lie inside a write root", () => {
  const p = worktreeParts();
  const scratch = realpathSync(mkdtempSync(join(tmpdir(), "cage-read-")));
  const roots = writeRootsOf({ root: p.root, env: p.env, home: p.home, extraReadRoots: [scratch, scratch, "docs"], exec: noGit });
  assert.deepEqual(roots.readable, [scratch, join(p.root, "docs")]);
  for (const [dir, message] of [
    [p.home, /the home directory/], ["/etc", /outside the repository and \/tmp/], ["/tmp", /contains the repository|scratch directory/],
    [join(p.root, "absent"), /not an existing directory/], [p.root, /the repository root/], ["data", /inside the write root/],
    [p.archive, /outside the repository and \/tmp|inside the write root/],
  ]) {
    assert.throws(() => writeRootsOf({ root: p.root, env: p.env, home: p.home, extraReadRoots: [dir], exec: noGit }), message, dir);
  }
  mkdirSync(join(p.root, ".claude"));
  assert.throws(() => writeRootsOf({ root: p.root, env: p.env, home: p.home, extraReadRoots: [".claude"], exec: noGit }), /inside \.claude/);
});

test("resolveReadRoot: the rules of resolveWriteRoot with the label of a read directory", () => {
  const root = fakeRepo();
  const options = { root, home: fakeHome(), gitDirs: [] };
  assert.equal(resolveReadRoot("docs", options), join(root, "docs"));
  assert.throws(() => resolveReadRoot("absent", options), /read directory .* is not allowed: not an existing directory/);
  assert.throws(() => resolveReadRoot("/etc", options), /read directory \/etc is not allowed: outside the repository and \/tmp/);
  assert.throws(() => resolveWriteRoot("/etc", options), /write root \/etc is not allowed/);
});

test("bwrapFeatures: reads --disable-userns and --size from the help text; a failing bwrap has none; the default is cached", () => {
  const help = (text) => () => text;
  assert.deepEqual(bwrapFeatures(help("  --unshare-user\n  --disable-userns   x\n  --size BYTES  y\n")), { disableUserns: true, size: true });
  assert.deepEqual(bwrapFeatures(help("  --unshare-user\n  --size BYTES\n")), { disableUserns: false, size: true });
  assert.deepEqual(bwrapFeatures(help("")), { disableUserns: false, size: false });
  assert.deepEqual(bwrapFeatures(() => { throw new Error("ENOENT"); }), { disableUserns: false, size: false });
  const failing = () => { throw Object.assign(new Error("exit 1"), { stdout: "--disable-userns" }); };
  assert.deepEqual(bwrapFeatures(failing), { disableUserns: true, size: false });
  let calls = 0;
  const counted = () => { calls += 1; return "--size"; };
  bwrapFeatures(counted);
  bwrapFeatures(counted);
  assert.equal(calls, 2);
});

test("planCage: the repository and the tools are read-only, the roots writable, home items bound when they exist", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  mkdirSync(join(p.home, ".cache", "ms-playwright"), { recursive: true });
  mkdirSync(join(p.home, ".config", "cichlids"), { recursive: true });
  writeFileSync(join(p.home, ".config", "cichlids", "openalex.key"), "dummy");
  const result = planCage({ root: p.root, env: { ...p.env, PATH: claude.dir }, home: p.home, exec: noGit, features });
  assert.deepEqual(result.readOnly, [
    { source: p.root, target: p.root }, { source: claude.dir, target: claude.dir },
    { source: join(p.home, ".cache", "ms-playwright"), target: join(p.home, ".cache", "ms-playwright") },
    { source: join(p.home, ".config", "cichlids", "openalex.key"), target: join(p.home, ".config", "cichlids", "openalex.key") },
  ]);
  assert.deepEqual(result.writable, [join(p.root, "data"), p.archive]);
  assert.deepEqual(result.scanRoots, [join(p.root, "data"), p.archive]);
  assert.equal(result.claudeBinary, claude.bin);
  assert.equal(result.chdir, p.root);
  assert.equal(result.home, p.home);
  // Without the home items nothing of the home directory is bound.
  const bare = planCage({ root: p.root, env: { ...p.env, PATH: claude.dir }, home: fakeHome(), exec: noGit, features });
  assert.equal(bare.readOnly.length, 2);
});

test("planCage: binds use real paths for a repository reached through a symlink and a claude symlink", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  const bin = mkdtempSync(join(tmpdir(), "cage-bin-"));
  symlinkSync(claude.bin, join(bin, "claude"));
  const link = join(mkdtempSync(join(tmpdir(), "cage-repo-link-")), "repo");
  symlinkSync(p.root, link);
  const result = planCage({ root: link, cwd: join(link, "tools"), env: { ...p.env, PATH: bin }, home: p.home, exec: noGit, features });
  assert.equal(result.root, p.root);
  assert.equal(result.chdir, join(p.root, "tools"));
  assert.equal(result.claudeBinary, claude.bin);
  for (const path of [...result.readOnly.flatMap((r) => [r.source, r.target]), ...result.writable]) assert.equal(path, realpathSync(path));
});

test("planCage: scanRoots hold the write roots inside the repository and the work directory", () => {
  const linked = worktree((main) => join(main, "data", "species", "work"));
  const claude = fakeClaude();
  const exec = gitAt(linked.main, join(linked.main, ".git", "worktrees", "w"));
  const result = planCage({
    root: linked.root, env: { CICHLIDS_SOURCE_ARCHIVE: join(linked.main, "no-archive"), PATH: claude.dir }, home: fakeHome(),
    extraWriteRoots: ["docs"], exec, features,
  });
  assert.deepEqual(result.scanRoots, [join(linked.root, "data"), join(linked.main, "data", "species", "work"), join(linked.root, "docs")]);
});

test("planCage: the source archive root and an extra root outside the repository are write roots, the archive is scanned", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  const extra = realpathSync(mkdtempSync(join(tmpdir(), "cage-extra-")));
  const result = planCage({ root: p.root, env: { ...p.env, PATH: claude.dir }, home: p.home, extraWriteRoots: [extra], exec: noGit, features });
  assert.deepEqual(result.writable, [join(p.root, "data"), p.archive, extra]);
  assert.deepEqual(result.scanRoots, [join(p.root, "data"), p.archive]);
  assert.equal(result.archive, p.archive);
});

test("planCage: read directories are bound read-only, never writable, and a working directory may lie in one", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  const briefs = realpathSync(mkdtempSync(join(tmpdir(), "cage-briefs-")));
  const result = planCage({
    root: p.root, cwd: briefs, env: { ...p.env, PATH: claude.dir }, home: p.home, extraReadRoots: [briefs], exec: noGit, features,
  });
  assert.deepEqual(result.readOnly.at(-1), { source: briefs, target: briefs });
  assert.ok(!result.writable.includes(briefs));
  assert.equal(result.chdir, briefs);
  assert.ok(cageArgs({ plan: result, runDir: "/r", inner: ["c"] }).join(" ").includes(`--ro-bind ${briefs} ${briefs}`));
});

test("planCage: the hidden paths are those of the container that exist; features come from the argument", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  const call = (exists) => planCage({ root: p.root, env: { ...p.env, PATH: claude.dir }, home: p.home, exec: noGit, features, exists });
  assert.deepEqual(call(() => true).hidden, ["/X11-unix", "/var/lib/docker"]);
  assert.deepEqual(call((path) => path === "/var/lib/docker").hidden, ["/var/lib/docker"]);
  assert.deepEqual(call(() => false).hidden, []);
  assert.deepEqual(call(() => false).features, features);
});

test("planCage: the host node is bound only when a tmpfs of the cage would hide it", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  const result = planCage({ root: p.root, env: { ...p.env, PATH: claude.dir }, home: p.home, exec: noGit, features });
  assert.equal(result.nodeBinary, realpathSync(process.execPath));
  const bound = result.readOnly.some((r) => r.source === result.nodeBinary);
  const hidden = ["/tmp", "/run", "/workspaces", p.home].some((d) => isInside(result.nodeBinary, d));
  assert.equal(bound, hidden);
});

test("planCage: claude directories that must not be bound whole are replaced by the binary", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  const result = planCage({ root: p.root, env: { ...p.env, PATH: claude.dir }, home: p.home, exec: noGit, features });
  assert.equal(result.claudeBind, claude.dir);
  // A claude directly in the home directory binds the file alone.
  const home = fakeHome();
  writeFileSync(join(home, "claude"), "#!/bin/sh\n");
  chmodSync(join(home, "claude"), 0o755);
  assert.equal(planCage({ root: p.root, env: { ...p.env, PATH: home }, home, exec: noGit, features }).claudeBind, join(home, "claude"));
});

test("planCage: a missing claude and a working directory outside the roots throw", () => {
  const p = worktreeParts();
  const claude = fakeClaude();
  assert.throws(() => planCage({ root: p.root, env: { ...p.env, PATH: "/nonexistent" }, home: p.home, exec: noGit, features }), /claude is not on the PATH/);
  assert.throws(() => planCage({ root: p.root, cwd: p.home, env: { ...p.env, PATH: claude.dir }, home: p.home, exec: noGit, features }), /lies outside the repository/);
  const result = planCage({ root: p.root, cwd: p.archive, env: { ...p.env, PATH: claude.dir }, home: p.home, exec: noGit, features });
  assert.equal(result.chdir, p.archive);
});
