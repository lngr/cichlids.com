// Tests for the run lock. A finished child process provides the pid of a dead process.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { LOCK_FILE, acquireLock, lockState, pidAlive, readLockPid, releaseLock, takeOverStaleLock } from "./pipeline-lock.mjs";

const tmp = () => mkdtempSync(join(tmpdir(), "pipeline-lock-"));
const deadPid = () => spawnSync(process.execPath, ["-e", ""]).pid;

test("pidAlive tells a live pid from a dead one", () => {
  assert.equal(pidAlive(process.pid), true);
  assert.equal(pidAlive(deadPid()), false);
});

test("acquireLock creates the lock with the pid and refuses while that pid lives", () => {
  const wd = join(tmp(), "legacy-7");
  assert.deepEqual(acquireLock(wd, process.pid), { acquired: true });
  assert.equal(readFileSync(join(wd, LOCK_FILE), "utf8"), `${process.pid}\n`);
  assert.equal(lockState(wd), "running");
  assert.deepEqual(acquireLock(wd, 12345), { acquired: false, pid: process.pid });
  assert.equal(readLockPid(wd), process.pid);
});

test("acquireLock replaces the lock of a dead pid and an unreadable lock", () => {
  const wd = tmp();
  const dead = deadPid();
  writeFileSync(join(wd, LOCK_FILE), `${dead}\n`);
  assert.equal(lockState(wd), "stale");
  assert.deepEqual(acquireLock(wd, process.pid), { acquired: true, replaced: dead });
  assert.equal(readLockPid(wd), process.pid);
  writeFileSync(join(wd, LOCK_FILE), "garbage");
  assert.equal(lockState(wd), "stale");
  assert.deepEqual(acquireLock(wd, process.pid), { acquired: true, replaced: null });
});

test("releaseLock removes only a lock that names the pid", () => {
  const wd = tmp();
  acquireLock(wd, process.pid);
  releaseLock(wd, 12345);
  assert.ok(existsSync(join(wd, LOCK_FILE)));
  releaseLock(wd, process.pid);
  assert.ok(!existsSync(join(wd, LOCK_FILE)));
  assert.equal(lockState(wd), null);
  releaseLock(wd, process.pid);
});

test("acquireLock leaves only run.lock in the directory", () => {
  const wd = tmp();
  acquireLock(wd, process.pid);
  assert.deepEqual(readdirSync(wd), [LOCK_FILE]);
  assert.deepEqual(acquireLock(wd, 4242), { acquired: false, pid: process.pid });
  assert.deepEqual(readdirSync(wd), [LOCK_FILE]);
});

test("acquireLock takes over an empty lock file, which no starting process leaves behind", () => {
  const wd = tmp();
  writeFileSync(join(wd, LOCK_FILE), "");
  assert.deepEqual(acquireLock(wd, 4242), { acquired: true, replaced: null });
  assert.equal(readFileSync(join(wd, LOCK_FILE), "utf8"), "4242\n");
  assert.deepEqual(readdirSync(wd), [LOCK_FILE]);
});

test("takeOverStaleLock puts back a lock that another process took between check and rename", () => {
  const wd = tmp();
  writeFileSync(join(wd, LOCK_FILE), `${process.pid}\n`);
  assert.equal(takeOverStaleLock(wd, deadPid(), 4242), false);
  assert.equal(readLockPid(wd), process.pid);
  assert.deepEqual(readdirSync(wd), [LOCK_FILE]);
});

test("takeOverStaleLock removes the lock of the expected dead pid", () => {
  const wd = tmp();
  const dead = deadPid();
  writeFileSync(join(wd, LOCK_FILE), `${dead}\n`);
  assert.equal(takeOverStaleLock(wd, dead, 4242), true);
  assert.deepEqual(readdirSync(wd), []);
  assert.equal(takeOverStaleLock(wd, dead, 4242), true);
});
