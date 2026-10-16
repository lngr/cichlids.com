// Tests for the audit of the write roots after a caged attempt.
import { test } from "node:test";
import assert from "node:assert/strict";
import { chmodSync, mkdirSync, mkdtempSync, realpathSync, symlinkSync, unlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { MAX_ENTRIES, WATCHED_NAMES, auditRoots, snapshotWatched } from "./agent-audit.mjs";

// A data root, a second write root and a directory outside both.
function tree() {
  const base = realpathSync(mkdtempSync(join(tmpdir(), "agent-audit-")));
  const [data, work, outside] = ["data", "work", "outside"].map((n) => join(base, n));
  for (const d of [join(data, "species", "profiles"), join(work, "legacy-1", "logs"), outside]) mkdirSync(d, { recursive: true });
  return { base, data, work, outside, scanRoots: [data, work], allowedRoots: [data, work] };
}

const run = (t, before = new Set(), extra = {}) =>
  auditRoots({ scanRoots: t.scanRoots, allowedRoots: t.allowedRoots, before, ...extra });
const audit = (t, before, extra) => run(t, before, extra).hits;

test("a clean tree has no hits", () => {
  const t = tree();
  writeFileSync(join(t.data, "species", "profiles", "a.yaml"), "x");
  assert.deepEqual(audit(t), []);
});

test("WATCHED_NAMES lists the entries that persist code or instructions", () => {
  assert.deepEqual([...WATCHED_NAMES].sort(), [
    ".agents", ".claude", ".codex", ".envrc", ".git", ".gitattributes", ".mcp.json", "AGENTS.md", "AGENTS.override.md",
    "CLAUDE.local.md", "CLAUDE.md",
  ].sort());
});

test("a symlink with a target outside the write roots is a hit, also a relative and a dangling one", () => {
  const t = tree();
  symlinkSync(t.outside, join(t.data, "out"));
  symlinkSync("../../outside", join(t.work, "legacy-1", "rel"));
  symlinkSync(join(t.base, "nowhere", "x"), join(t.data, "dangling"));
  assert.deepEqual(audit(t).sort(), [
    `${join(t.data, "dangling")} (symlink to ${join(t.base, "nowhere", "x")})`,
    `${join(t.data, "out")} (symlink to ${t.outside})`,
    `${join(t.work, "legacy-1", "rel")} (symlink to ${t.outside})`,
  ].sort());
});

test("a symlink out of the roots that existed with the same target before is a warning, a retargeted one a hit", () => {
  const t = tree();
  const link = join(t.work, "legacy-1", "shared");
  symlinkSync(t.outside, link);
  const before = snapshotWatched(t.scanRoots);
  assert.deepEqual(run(t, before), { hits: [], warnings: [`${link} (symlink to ${t.outside})`] });
  unlinkSync(link);
  symlinkSync(t.base, link);
  assert.deepEqual(run(t, before), { hits: [`${link} (symlink to ${t.base})`], warnings: [] });
  // A symlink that did not exist before, also one that points where an older one did, is a hit.
  const fresh = join(t.work, "legacy-1", "fresh");
  symlinkSync(t.base, fresh);
  assert.deepEqual(audit(t, before).sort(), [`${fresh} (symlink to ${t.base})`, `${link} (symlink to ${t.base})`].sort());
});

test("a symlink whose target lies inside a write root is fine, also from one root into another", () => {
  const t = tree();
  symlinkSync(join(t.data, "species"), join(t.data, "alias"));
  symlinkSync(t.work, join(t.data, "species", "work"));
  symlinkSync(join(t.data, "species"), join(t.work, "legacy-1", "back"));
  assert.deepEqual(audit(t), []);
});

test("a symlink to a parent of a write root is a hit", () => {
  const t = tree();
  symlinkSync(t.base, join(t.data, "up"));
  assert.deepEqual(audit(t), [`${join(t.data, "up")} (symlink to ${t.base})`]);
});

test("a watched name that did not exist before is a hit, an existing one is ignored", () => {
  const t = tree();
  mkdirSync(join(t.data, "species", "AGENTS.md"));
  const before = snapshotWatched(t.scanRoots);
  assert.deepEqual(audit(t, before), []);
  const planted = [
    join(t.data, ".claude"), join(t.work, "legacy-1", ".git"), join(t.data, ".codex"), join(t.work, ".agents"),
    join(t.data, "species", "profiles", "CLAUDE.md"), join(t.work, "CLAUDE.local.md"), join(t.data, ".mcp.json"),
    join(t.data, "AGENTS.override.md"), join(t.work, "legacy-1", ".gitattributes"), join(t.data, "species", ".envrc"),
  ];
  for (const dir of planted.slice(0, 4)) mkdirSync(dir);
  for (const f of planted.slice(4)) writeFileSync(f, "x");
  assert.deepEqual(audit(t, before).sort(), planted.map((p) => `${p} (created)`).sort());
});

test("node_modules is scanned for symlinks and not for watched names", () => {
  const t = tree();
  mkdirSync(join(t.data, "node_modules", "pkg"), { recursive: true });
  mkdirSync(join(t.data, "node_modules", "pkg", ".claude"));
  writeFileSync(join(t.data, "node_modules", "pkg", "AGENTS.md"), "x");
  symlinkSync(join(t.data, "species"), join(t.data, "node_modules", "pkg", "inside"));
  assert.deepEqual(audit(t), []);
  const link = join(t.data, "node_modules", "pkg", "link");
  symlinkSync(t.outside, link);
  assert.deepEqual(audit(t), [`${link} (symlink to ${t.outside})`]);
  // A node_modules directory that appears during the attempt follows the same rule.
  const before = snapshotWatched(t.scanRoots);
  mkdirSync(join(t.work, "node_modules", ".bin"), { recursive: true });
  symlinkSync(t.base, join(t.work, "node_modules", ".bin", "tool"));
  assert.deepEqual(run(t, before), {
    hits: [`${join(t.work, "node_modules", ".bin", "tool")} (symlink to ${t.base})`],
    warnings: [`${link} (symlink to ${t.outside})`],
  });
});

test("watched directories are not entered, and a symlink is not followed", () => {
  const t = tree();
  mkdirSync(join(t.data, ".claude", "hooks"), { recursive: true });
  symlinkSync(t.outside, join(t.data, ".claude", "hooks", "x"));
  assert.deepEqual(audit(t, new Set([join(t.data, ".claude")])), []);
  mkdirSync(join(t.outside, ".claude"));
  symlinkSync(t.outside, join(t.data, "follow"));
  assert.deepEqual(audit(t, new Set([join(t.data, ".claude")])), [`${join(t.data, "follow")} (symlink to ${t.outside})`]);
});

test("a scan root that does not exist is skipped", () => {
  const t = tree();
  assert.deepEqual(auditRoots({ scanRoots: [join(t.base, "absent")], allowedRoots: [], before: new Set() }), { hits: [], warnings: [] });
  assert.equal(snapshotWatched([join(t.base, "absent")]).size, 0);
});

test("a directory that cannot be read is a hit, a missing one is not", { skip: process.getuid?.() === 0 }, () => {
  const t = tree();
  const locked = join(t.data, "species", "locked");
  mkdirSync(locked);
  chmodSync(locked, 0);
  try {
    assert.deepEqual(audit(t), [`${locked} (unreadable: EACCES)`]);
  } finally {
    chmodSync(locked, 0o755);
  }
  assert.deepEqual(audit(t), []);
});

test("a symlink that cannot be resolved is a hit", () => {
  const t = tree();
  symlinkSync(join(t.data, "loop-b"), join(t.data, "loop-a"));
  symlinkSync(join(t.data, "loop-a"), join(t.data, "loop-b"));
  assert.deepEqual(audit(t).sort(), [
    `${join(t.data, "loop-a")} (unreadable symlink: ELOOP)`, `${join(t.data, "loop-b")} (unreadable symlink: ELOOP)`,
  ]);
  assert.equal(snapshotWatched(t.scanRoots).size, 0);
});

test("the walk stops at the entry limit and reports it", () => {
  const t = tree();
  for (let i = 0; i < 5; i++) writeFileSync(join(t.data, `f${i}`), "x");
  assert.deepEqual(audit(t, new Set(), { limit: 100 }), []);
  assert.deepEqual(audit(t, new Set(), { limit: 3 }), ["the walk stopped after 3 entries"]);
  assert.ok(snapshotWatched(t.scanRoots, { limit: 3 }) instanceof Set);
  assert.equal(MAX_ENTRIES, 200_000);
});

test("every scan root is audited, so the source archive root is too", () => {
  const t = tree();
  const archive = join(t.base, "archive");
  mkdirSync(join(archive, "host", "page"), { recursive: true });
  const before = snapshotWatched([...t.scanRoots, archive]);
  symlinkSync(t.outside, join(archive, "host", "page", "out"));
  mkdirSync(join(archive, ".claude"));
  const result = auditRoots({ scanRoots: [...t.scanRoots, archive], allowedRoots: [...t.allowedRoots, archive], before });
  assert.deepEqual(result.hits.sort(), [`${join(archive, ".claude")} (created)`, `${join(archive, "host", "page", "out")} (symlink to ${t.outside})`].sort());
});
