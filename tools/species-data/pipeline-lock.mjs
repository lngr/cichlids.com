// Run lock of a pipeline work directory: <wd>/run.lock holds the pid of the process that runs the
// species. A lock whose pid is alive marks a running species; a lock of a dead pid is stale (the
// process ended without cleanup, for example by SIGKILL) and the next run replaces it. The work
// directory lies in a tree that agents write, so the lock creates the directory and its files with
// the symlink checks of safe-write.mjs.
import { randomUUID } from "node:crypto";
import { existsSync, linkSync, readFileSync, renameSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { safeMkdir, safeWriteFile } from "./safe-write.mjs";

export const LOCK_FILE = "run.lock";

/** True when a process with this pid exists. EPERM means it exists under another user. */
export function pidAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error.code === "EPERM";
  }
}

// The pid in a lock file, or null for a missing or unreadable file.
function readPidFile(path) {
  let text;
  try {
    text = readFileSync(path, "utf8").trim();
  } catch {
    return null;
  }
  return /^\d+$/.test(text) ? Number(text) : null;
}

/** The pid in the lock of wd, or null without a lock or with an unreadable one. */
export function readLockPid(wd) {
  return readPidFile(join(wd, LOCK_FILE));
}

/** "running" for a lock of a live pid, "stale" for any other lock, null without a lock. */
export function lockState(wd) {
  if (!existsSync(join(wd, LOCK_FILE))) return null;
  const pid = readLockPid(wd);
  return pid !== null && pidAlive(pid) ? "running" : "stale";
}

/**
 * Removes the stale lock of wd that names expected (a dead pid or null for an unreadable lock).
 * The lock is renamed to a unique name first and checked there: when another process replaced
 * the stale lock in the meantime, its lock goes back in place and the result is false.
 */
export function takeOverStaleLock(wd, expected, pid = process.pid) {
  const path = join(wd, LOCK_FILE);
  const aside = join(wd, `${LOCK_FILE}.stale.${pid}.${randomUUID()}`);
  try {
    renameSync(path, aside);
  } catch (error) {
    if (error.code === "ENOENT") return true;
    throw error;
  }
  const moved = readPidFile(aside);
  if (moved === expected) {
    rmSync(aside, { force: true });
    return true;
  }
  try {
    linkSync(aside, path);
  } catch {
    // A third process holds the lock; the moved lock is dropped.
  }
  rmSync(aside, { force: true });
  return false;
}

/**
 * Creates the lock of wd for pid. The pid goes to run.lock.<pid> first, and a hard link creates
 * run.lock with its content in one exclusive step, so no process sees an empty lock. Returns
 * { acquired: true, replaced } where replaced is the pid (or null for an unreadable lock) of a
 * stale lock that was removed, or { acquired: false, pid } when the lock names a live process.
 * A symlink at wd or below base (default: the directory of wd) is refused with an error.
 */
export function acquireLock(wd, pid = process.pid, base = dirname(wd)) {
  safeMkdir(wd, base);
  const path = join(wd, LOCK_FILE);
  const own = join(wd, `${LOCK_FILE}.${pid}`);
  safeWriteFile(own, `${pid}\n`, base);
  let replaced;
  try {
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        linkSync(own, path);
        return replaced === undefined ? { acquired: true } : { acquired: true, replaced };
      } catch (error) {
        if (error.code !== "EEXIST") throw error;
      }
      const owner = readLockPid(wd);
      if (owner !== null && pidAlive(owner)) return { acquired: false, pid: owner };
      if (takeOverStaleLock(wd, owner, pid)) replaced = owner;
    }
    return { acquired: false, pid: readLockPid(wd) };
  } finally {
    rmSync(own, { force: true });
  }
}

/** Removes the lock of wd when it names pid. */
export function releaseLock(wd, pid = process.pid) {
  if (readLockPid(wd) === pid) rmSync(join(wd, LOCK_FILE), { force: true });
}
