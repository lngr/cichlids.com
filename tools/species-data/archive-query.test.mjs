// Tests for the shared archive query: validation, formatting, the output cap and the docker call
// with a fake spawn function. No test starts docker.
import { test } from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { PassThrough } from "node:stream";

import {
  ArchiveError, SQL, capOutput, createDockerQuery, dockerEnv, formatResults, publicMessage, validateRequest,
} from "./archive-query.mjs";

const refuses = (request, pattern) =>
  assert.throws(() => validateRequest(request), (error) => error instanceof ArchiveError && error.code === "invalid" && pattern.test(error.message));

test("validateRequest: a term of 2 to 100 characters and a limit of 1 to 100, default 30", () => {
  assert.deepEqual(validateRequest({ term: "Altolamprologus" }), { term: "Altolamprologus", limit: 30 });
  assert.deepEqual(validateRequest({ term: "ab", limit: 1 }), { term: "ab", limit: 1 });
  assert.deepEqual(validateRequest({ term: "é".repeat(100), limit: 100 }), { term: "é".repeat(100), limit: 100 });
  assert.deepEqual(validateRequest({ term: "café 'quoted'; DROP TABLE x" }).term, "café 'quoted'; DROP TABLE x");
});

test("validateRequest: refuses a bad term, a bad limit and a bad shape", () => {
  for (const term of [undefined, null, 5, ["ab"], "", "a", "é", "x".repeat(101), "a\nb", "ab\u0000", "ab\t", "ab\u007f", "ab\u0085"]) {
    refuses({ term }, /^term must be a string of 2 to 100 characters without control characters$/);
  }
  for (const limit of [0, 101, -1, 1.5, "5", null, NaN, Infinity, true, [5]]) {
    refuses({ term: "ab", limit }, /^limit must be an integer from 1 to 100$/);
  }
  for (const request of [null, "ab", 5, ["ab"]]) refuses(request, /JSON object/);
  refuses({ term: "ab", extra: 1 }, /other than term and limit/);
});

test("formatResults: threads with id, url and excerpts, and the empty answer", () => {
  assert.equal(formatResults(""), "No posts found.\n");
  const raw = "12\tcichlids\t1\tfirst\n12\tcichlids\t2\tsecond\n40\tmarket_place\t7\tthird\n77\tother\t8\tfourth\n";
  assert.equal(formatResults(raw), [
    "",
    "== cichlids.com forum thread 12",
    "   url: https://www.cichlids.com/disc/read.php?1,12",
    "   - first",
    "   - second",
    "",
    "== cichlids.com forum thread 40",
    "   url: https://www.cichlids.com/disc/read.php?3,40",
    "   - third",
    "",
    "== cichlids.com forum thread 77",
    "   url: https://www.cichlids.com/disc/read.php?1,77",
    "   - fourth",
    "",
  ].join("\n"));
});

test("capOutput: keeps a short text, else whole lines and a closing line within the limit", () => {
  assert.equal(capOutput("a\nb\n", 100), "a\nb\n");
  const text = `${"line of text\n".repeat(200)}`;
  const capped = capOutput(text, 1024);
  assert.ok(Buffer.byteLength(capped) <= 1024);
  assert.match(capped, /^(line of text\n)+\[output cut at 1 KB\]\n$/);
  const multibyte = capOutput("é".repeat(2000), 1024);
  assert.ok(Buffer.byteLength(multibyte) <= 1024);
  assert.equal(multibyte, "[output cut at 1 KB]\n");
  assert.equal(capOutput(`${"é".repeat(300)}\n${"é".repeat(2000)}\n`, 1024).split("\n")[0], "é".repeat(300));
});

test("publicMessage: an ArchiveError keeps its message, any other error answers the generic one", () => {
  assert.match(publicMessage(new ArchiveError("unavailable")), /stack-postgres-1 is not running/);
  assert.equal(publicMessage(new Error("password=secret")), "the archive query failed");
});

test("dockerEnv: only the variables that docker needs", () => {
  assert.deepEqual(dockerEnv({ PATH: "/bin", HOME: "/h", DOCKER_HOST: "unix:///d", ANTHROPIC_API_KEY: "k", GITHUB_TOKEN: "t" }), {
    PATH: "/bin", HOME: "/h", DOCKER_HOST: "unix:///d",
  });
});

// A fake spawn function: records the call and lets the test drive the child.
function fakeSpawn() {
  const calls = [];
  const spawnImpl = (command, args, options) => {
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.killed = [];
    child.kill = (signal) => {
      child.killed.push(signal);
      setImmediate(() => child.emit("close", null));
    };
    let input = "";
    child.stdin.on("data", (chunk) => { input += chunk; });
    calls.push({ command, args, options, child, input: () => input });
    return child;
  };
  return { calls, spawnImpl };
}

test("createDockerQuery: the term is a psql variable, the SQL goes in on stdin, the output comes back", async () => {
  const fake = fakeSpawn();
  const query = createDockerQuery({ env: { PATH: "/bin" }, spawnImpl: fake.spawnImpl });
  const term = "x'; DROP TABLE discussion_post; --";
  const pending = query({ term, limit: 7 });
  const [call] = fake.calls;
  assert.equal(call.command, "docker");
  assert.deepEqual(call.options.env, { PATH: "/bin" });
  assert.deepEqual(call.args.slice(0, 7), ["exec", "-i", "-e", "PGOPTIONS=-c statement_timeout=30000", "stack-postgres-1", "psql", "-U"]);
  assert.ok(call.args.includes(`term=${term}`));
  assert.ok(call.args.includes("limit=7"));
  assert.ok(call.args.includes("ON_ERROR_STOP=1"));
  call.child.stdout.write("12\tcichlids\t1\tpost text\n");
  call.child.stdout.end();
  call.child.emit("close", 0);
  assert.equal(await pending, "12\tcichlids\t1\tpost text\n");
  assert.equal(call.input(), SQL);
  assert.ok(!SQL.includes(term));
  assert.ok(!/member|author|user_name|handle/i.test(SQL));
});

test("createDockerQuery: a missing container, a missing docker and an unreachable daemon are unavailable", async () => {
  for (const text of ["Error response from daemon: No such container: stack-postgres-1", "Error response from daemon: container x is not running", "Cannot connect to the Docker daemon at unix:///var/run/docker.sock", "failed to connect to the docker API at unix:///var/run/docker.sock; check if the daemon is running", "permission denied while trying to connect"]) {
    const fake = fakeSpawn();
    const pending = createDockerQuery({ spawnImpl: fake.spawnImpl })({ term: "ab", limit: 1 });
    fake.calls[0].child.stderr.write(text);
    fake.calls[0].child.emit("close", 1);
    await assert.rejects(pending, (error) => error.code === "unavailable");
  }
  const fake = fakeSpawn();
  const pending = createDockerQuery({ spawnImpl: fake.spawnImpl })({ term: "ab", limit: 1 });
  fake.calls[0].child.emit("error", Object.assign(new Error("spawn docker ENOENT"), { code: "ENOENT" }));
  await assert.rejects(pending, (error) => error.code === "unavailable");
});

test("createDockerQuery: a database error and a statement timeout", async () => {
  const fake = fakeSpawn();
  const query = createDockerQuery({ spawnImpl: fake.spawnImpl });
  const failed = query({ term: "ab", limit: 1 });
  fake.calls[0].child.stderr.write("ERROR: relation does not exist ab");
  fake.calls[0].child.emit("close", 3);
  await assert.rejects(failed, (error) => error.code === "failed" && !/ab/.test(error.message));
  const slow = query({ term: "ab", limit: 1 });
  fake.calls[1].child.stderr.write("ERROR:  canceling statement due to statement timeout");
  fake.calls[1].child.emit("close", 3);
  await assert.rejects(slow, (error) => error.code === "timeout");
});

test("createDockerQuery: an abort kills the child and rejects with the timeout; an aborted signal starts nothing", async () => {
  const fake = fakeSpawn();
  const query = createDockerQuery({ spawnImpl: fake.spawnImpl });
  const controller = new AbortController();
  const pending = query({ term: "ab", limit: 1, signal: controller.signal });
  controller.abort();
  await assert.rejects(pending, (error) => error.code === "timeout");
  assert.deepEqual(fake.calls[0].child.killed, ["SIGKILL"]);
  await assert.rejects(query({ term: "ab", limit: 1, signal: controller.signal }), (error) => error.code === "timeout");
  assert.equal(fake.calls.length, 1);
});

test("createDockerQuery: output beyond the bound kills the child and fails", async () => {
  const fake = fakeSpawn();
  const pending = createDockerQuery({ spawnImpl: fake.spawnImpl })({ term: "ab", limit: 1 });
  fake.calls[0].child.stdout.write(Buffer.alloc(1024 ** 2 + 1, "a"));
  await assert.rejects(pending, (error) => error.code === "failed");
  assert.deepEqual(fake.calls[0].child.killed, ["SIGKILL"]);
});
