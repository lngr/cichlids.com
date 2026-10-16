// Audit of the write roots after a caged agent attempt. The agent writes into the checkout and the
// archive, and code outside the cage reads those files later, so two things must not appear: a
// symlink whose real target lies outside the write roots and that did not exist with that target
// before the attempt (a later read or write outside the cage would follow it), and an entry that
// persists instructions or code for later tools (a git directory or attributes, Claude Code and
// Codex settings, agent instruction files, MCP configuration, an environment file for direnv) and
// did not exist before the attempt. The walk reports what it cannot read, and a tree beyond the
// entry limit fails, because an unchecked part of a write root is not audited.
import { readdirSync, realpathSync } from "node:fs";
import { join } from "node:path";
import { isInside, realPathOf } from "./agent-cage.mjs";

export const WATCHED_NAMES = [
  ".claude", ".git", "CLAUDE.md", "CLAUDE.local.md", "AGENTS.md", "AGENTS.override.md", ".mcp.json",
  ".codex", ".agents", ".gitattributes", ".envrc",
];

// Maximum number of entries that one walk over all scan roots visits.
export const MAX_ENTRIES = 200_000;

// Directories that the walk enters for symlinks only: installed packages hold names of their own
// that are not agent output.
const SYMLINK_ONLY_DIRECTORIES = ["node_modules"];

class LimitReached extends Error {}

// Calls visit(path, entry, symlinkOnly) for every entry below the scan roots and onError(path,
// error) for a directory that cannot be read (a missing one is skipped). Symlinks are visited and
// not followed. The walk does not enter watched directories; it enters SYMLINK_ONLY_DIRECTORIES
// with symlinkOnly set. Returns true when it stopped at the entry limit.
function walk(scanRoots, { visit, onError, limit }) {
  let count = 0;
  const enter = (dir, symlinkOnly) => {
    let entries;
    try {
      entries = readdirSync(dir, { withFileTypes: true });
    } catch (error) {
      if (error.code !== "ENOENT") onError(dir, error);
      return;
    }
    for (const entry of entries) {
      count += 1;
      if (count > limit) throw new LimitReached();
      const path = join(dir, entry.name);
      visit(path, entry, symlinkOnly);
      if (!entry.isDirectory() || (!symlinkOnly && WATCHED_NAMES.includes(entry.name))) continue;
      enter(path, symlinkOnly || SYMLINK_ONLY_DIRECTORIES.includes(entry.name));
    }
  };
  try {
    for (const scanRoot of scanRoots) enter(scanRoot, false);
  } catch (error) {
    if (error instanceof LimitReached) return true;
    throw error;
  }
  return false;
}

/** The snapshot key of a symlink at path with the real target target. */
export const linkKey = (path, target) => `symlink ${path} -> ${target}`;

// The real target of a symlink; a dangling link counts with the path it points to. Throws for an
// error other than a missing target (a loop, no permission).
function targetOf(path) {
  try {
    return realpathSync(path);
  } catch (error) {
    if (error.code !== "ENOENT" && error.code !== "ENOTDIR") throw error;
    return realPathOf(path);
  }
}

/**
 * Returns the state that auditRoots compares against: the paths below the scan roots whose name is
 * in WATCHED_NAMES (not inside node_modules), and for every symlink a key of its path and real
 * target (linkKey). Read errors are left to auditRoots.
 */
export function snapshotWatched(scanRoots, { limit = MAX_ENTRIES } = {}) {
  const found = new Set();
  walk(scanRoots, {
    limit,
    onError: () => {},
    visit: (path, entry, symlinkOnly) => {
      if (!symlinkOnly && WATCHED_NAMES.includes(entry.name)) found.add(path);
      if (entry.isSymbolicLink()) {
        try {
          found.add(linkKey(path, targetOf(path)));
        } catch {
          // A link that cannot be resolved has no key; the audit reports it.
        }
      }
    },
  });
  return found;
}

/**
 * Returns { hits, warnings }, both as text lines. A hit is a symlink below the scan roots whose
 * real target lies outside all allowedRoots and that before does not hold with that target, a
 * watched name below them that is not in before, a directory or symlink that cannot be read, and
 * the end of the walk at limit entries. before is the result of snapshotWatched taken before the
 * attempt. A warning is a symlink out of the write roots that existed with the same target before
 * the attempt: the audit reports it and does not fail on it.
 */
export function auditRoots({ scanRoots, allowedRoots, before, limit = MAX_ENTRIES }) {
  const hits = [];
  const warnings = [];
  const overflow = walk(scanRoots, {
    limit,
    onError: (path, error) => hits.push(`${path} (unreadable: ${error.code ?? error.message})`),
    visit: (path, entry, symlinkOnly) => {
      if (entry.isSymbolicLink()) {
        let target;
        try {
          target = targetOf(path);
        } catch (error) {
          hits.push(`${path} (unreadable symlink: ${error.code ?? error.message})`);
          return;
        }
        if (!allowedRoots.some((r) => isInside(target, r))) {
          const line = `${path} (symlink to ${target})`;
          if (before.has(linkKey(path, target))) warnings.push(line);
          else hits.push(line);
        }
      }
      if (!symlinkOnly && WATCHED_NAMES.includes(entry.name) && !before.has(path)) {
        hits.push(`${path} (created)`);
      }
    },
  });
  if (overflow) hits.push(`the walk stopped after ${limit} entries`);
  return { hits: [...new Set(hits)], warnings: [...new Set(warnings)] };
}
