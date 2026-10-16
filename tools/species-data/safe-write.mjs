// File writes of the tools into directories that an agent can write. An agent may replace a file or
// any directory on the way to it by a symlink, so every write checks each path component below a
// trusted base with lstat and refuses a symlink. The base is a directory that the caller trusts,
// normally the real path of the write root that contains the target. A full write goes to a temp
// file in the same directory and renames over the target, so a reader never sees half a file.
//
// The checks run before the write and use no directory handles, so an agent that runs at the same
// time can swap a component between the check and the write. The tools write while no agent
// of the same directory runs, except for the parallel roles of one pipeline run.
import { randomBytes } from "node:crypto";
import {
  closeSync, constants, lstatSync, mkdirSync, openSync, renameSync, rmSync, writeFileSync,
  writeSync,
} from "node:fs";
import { basename, dirname, isAbsolute, join, relative, resolve, sep } from "node:path";

const refusal = (path) => new Error(`refusing to write through the symlink ${path}`);

// Returns lstat of path, or null when it does not exist.
function lstatOrNull(path) {
  try {
    return lstatSync(path);
  } catch (error) {
    if (error.code === "ENOENT") return null;
    throw error;
  }
}

// Returns the paths from the first component below base down to path. Throws when path is not below
// base.
function componentsBelow(path, base) {
  const rel = relative(resolve(base), resolve(path));
  if (rel === "") return [];
  if (rel === ".." || rel.startsWith(`..${sep}`) || isAbsolute(rel)) {
    throw new Error(`${path} is not inside ${base}`);
  }
  const paths = [];
  let current = resolve(base);
  for (const segment of rel.split(sep)) {
    current = join(current, segment);
    paths.push(current);
  }
  return paths;
}

// Throws when a component of path below base is a symlink. Components that do not exist are fine.
function refuseSymlinks(path, base) {
  for (const component of componentsBelow(path, base)) {
    const stat = lstatOrNull(component);
    if (stat === null) return;
    if (stat.isSymbolicLink()) throw refusal(component);
  }
}

/**
 * Creates the directory path and its missing parents below base (base itself is created when it is
 * missing). Refuses a symlink or a file on the way.
 */
export function safeMkdir(path, base) {
  const components = componentsBelow(path, base);
  mkdirSync(base, { recursive: true });
  for (const component of components) {
    let stat = lstatOrNull(component);
    if (stat === null) {
      try {
        mkdirSync(component);
      } catch (error) {
        if (error.code !== "EEXIST") throw error;
      }
      stat = lstatSync(component);
    }
    if (stat.isSymbolicLink()) throw refusal(component);
    if (!stat.isDirectory()) throw new Error(`${component} is not a directory`);
  }
}

/**
 * Writes content (text or a buffer) to path through a temp file in the same directory. Refuses a
 * symlink at any component of path below base.
 */
export function safeWriteFile(path, content, base) {
  refuseSymlinks(path, base);
  const temp = join(dirname(path), `.${basename(path)}.${randomBytes(6).toString("hex")}.tmp`);
  try {
    writeFileSync(temp, content, { flag: "wx" });
    renameSync(temp, path);
  } catch (error) {
    rmSync(temp, { force: true });
    throw error;
  }
}

/**
 * Appends content to path. Refuses a symlink at any component of path below base, at the file also
 * when it appears after the check.
 */
export function safeAppendFile(path, content, base) {
  refuseSymlinks(path, base);
  const flags = constants.O_WRONLY | constants.O_APPEND | constants.O_CREAT | constants.O_NOFOLLOW;
  const fd = openSync(path, flags, 0o644);
  try {
    writeSync(fd, content);
  } finally {
    closeSync(fd);
  }
}
