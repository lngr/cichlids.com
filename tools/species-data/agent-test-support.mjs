// Fixtures of the agent tests: a fake claude binary on a PATH and a fake repository tree. No test
// starts a real claude, codex or bwrap process.
import { chmodSync, mkdirSync, mkdtempSync, realpathSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

/** Creates a directory with an executable file named claude. Returns { dir, bin } with real paths. */
export function fakeClaude() {
  const dir = realpathSync(mkdtempSync(join(tmpdir(), "agent-bin-")));
  const bin = join(dir, "claude");
  writeFileSync(bin, "#!/bin/sh\n");
  chmodSync(bin, 0o755);
  return { dir, bin };
}

/** Creates a repository tree with data/species and tools/species-data and returns its real path. */
export function fakeRepo() {
  const root = realpathSync(mkdtempSync(join(tmpdir(), "agent-repo-")));
  for (const d of ["data/species", "tools/species-data", "docs"]) mkdirSync(join(root, d), { recursive: true });
  return root;
}

/** Creates an empty home directory and returns its real path. */
export function fakeHome() {
  return realpathSync(mkdtempSync(join(tmpdir(), "agent-home-")));
}
