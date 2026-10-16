// Tests for the safe writes of the tools into directories that an agent can write.
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { safeAppendFile, safeMkdir, safeWriteFile } from "./safe-write.mjs";

const tempDir = () => realpathSync(mkdtempSync(join(tmpdir(), "safe-write-")));

test("safeWriteFile: creates and replaces a file and leaves no temp file", () => {
  const dir = tempDir();
  const file = join(dir, "run.json");
  safeWriteFile(file, "one", dir);
  safeWriteFile(file, "two", dir);
  assert.equal(readFileSync(file, "utf8"), "two");
  assert.deepEqual(readdirSync(dir), ["run.json"]);
  safeWriteFile(file, Buffer.from([1, 2, 3]), dir);
  assert.deepEqual([...readFileSync(file)], [1, 2, 3]);
});

test("safeWriteFile: refuses a symlink at the target and leaves what it points to alone", () => {
  const dir = tempDir();
  const secret = join(dir, "secret.txt");
  writeFileSync(secret, "keep");
  symlinkSync(secret, join(dir, "run.json"));
  assert.throws(() => safeWriteFile(join(dir, "run.json"), "x", dir), /refusing to write through the symlink .*run\.json/);
  symlinkSync(join(dir, "missing"), join(dir, "dangling.json"));
  assert.throws(() => safeWriteFile(join(dir, "dangling.json"), "x", dir), /refusing to write through the symlink/);
  assert.equal(readFileSync(secret, "utf8"), "keep");
  assert.deepEqual(readdirSync(dir).sort(), ["dangling.json", "run.json", "secret.txt"]);
});

test("safeWriteFile and safeAppendFile: refuse a symlink at any component below the base", () => {
  const dir = tempDir();
  const elsewhere = join(dir, "elsewhere");
  mkdirSync(join(elsewhere, "inner"), { recursive: true });
  mkdirSync(join(dir, "root"));
  symlinkSync(elsewhere, join(dir, "root", "logs"));
  const base = join(dir, "root");
  assert.throws(() => safeWriteFile(join(base, "logs", "status"), "x", base), /refusing to write through the symlink .*logs/);
  assert.throws(() => safeAppendFile(join(base, "logs", "status"), "x", base), /refusing to write through the symlink .*logs/);
  assert.throws(() => safeWriteFile(join(base, "logs", "inner", "status"), "x", base), /refusing to write through the symlink .*logs/);
  assert.deepEqual(readdirSync(elsewhere), ["inner"]);
  assert.deepEqual(readdirSync(join(elsewhere, "inner")), []);
});

test("the base itself may be a symlink and is trusted; components below it are not", () => {
  const dir = tempDir();
  mkdirSync(join(dir, "real", "sub"), { recursive: true });
  symlinkSync(join(dir, "real"), join(dir, "work"));
  safeWriteFile(join(dir, "work", "sub", "a.txt"), "ok", join(dir, "work"));
  assert.equal(readFileSync(join(dir, "real", "sub", "a.txt"), "utf8"), "ok");
  assert.throws(() => safeWriteFile(join(dir, "work", "sub", "a.txt"), "x", dir), /refusing to write through the symlink .*work/);
});

test("a path outside the base is refused", () => {
  const dir = tempDir();
  mkdirSync(join(dir, "base"));
  for (const path of [join(dir, "other.txt"), join(dir, "base", "..", "other.txt"), join(dir, "base-two", "x")]) {
    assert.throws(() => safeWriteFile(path, "x", join(dir, "base")), /is not inside/);
    assert.throws(() => safeAppendFile(path, "x", join(dir, "base")), /is not inside/);
    assert.throws(() => safeMkdir(path, join(dir, "base")), /is not inside/);
  }
});

test("safeWriteFile: a failing write removes its temp file", () => {
  const dir = tempDir();
  mkdirSync(join(dir, "target"));
  assert.throws(() => safeWriteFile(join(dir, "target"), "x", dir));
  assert.deepEqual(readdirSync(dir), ["target"]);
});

test("safeAppendFile: creates the file, appends and refuses a symlink at the target", () => {
  const dir = tempDir();
  const log = join(dir, "generator.log");
  safeAppendFile(log, "a\n", dir);
  safeAppendFile(log, "b\n", dir);
  assert.equal(readFileSync(log, "utf8"), "a\nb\n");
  const secret = join(dir, "secret.txt");
  writeFileSync(secret, "keep");
  symlinkSync(secret, join(dir, "linked.log"));
  assert.throws(() => safeAppendFile(join(dir, "linked.log"), "x", dir), /refusing to write through the symlink/);
  assert.equal(readFileSync(secret, "utf8"), "keep");
});

test("safeMkdir: creates missing directories below the base, accepts existing ones and creates a missing base", () => {
  const dir = tempDir();
  safeMkdir(join(dir, "a", "b", "c"), dir);
  safeMkdir(join(dir, "a", "b", "c"), dir);
  safeMkdir(dir, dir);
  assert.ok(readdirSync(join(dir, "a", "b")).includes("c"));
  safeMkdir(join(dir, "new-base", "x"), join(dir, "new-base"));
  assert.deepEqual(readdirSync(join(dir, "new-base")), ["x"]);
});

test("safeMkdir: refuses a symlink and a file on the way and creates nothing behind them", () => {
  const dir = tempDir();
  const elsewhere = join(dir, "elsewhere");
  mkdirSync(elsewhere);
  mkdirSync(join(dir, "root"));
  symlinkSync(elsewhere, join(dir, "root", "link"));
  writeFileSync(join(dir, "root", "file"), "x");
  assert.throws(() => safeMkdir(join(dir, "root", "link", "new"), join(dir, "root")), /refusing to write through the symlink .*link/);
  assert.throws(() => safeMkdir(join(dir, "root", "file", "new"), join(dir, "root")), /is not a directory/);
  assert.deepEqual(readdirSync(elsewhere), []);
});
