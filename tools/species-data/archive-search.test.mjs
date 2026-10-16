// Tests for archive-search.mjs: the command line, the direct mode with an injected query function
// and the client mode against a real archive search service on a temporary socket. No test starts
// docker.
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import net from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { ArchiveError, formatResults } from "./archive-query.mjs";
import { startArchiveService } from "./archive-service.mjs";
import { askService, main } from "./archive-search.mjs";

const RAW = "12\tcichlids\t1\tfirst post\n12\tcichlids\t2\tsecond post\n40\tafrican\t7\tthird post\n";

// Runs main with captured streams; resolves { code, out, err }.
async function run(argv, options) {
  let out = "";
  let err = "";
  const code = await main(argv, {
    stdout: { write: (text) => { out += text; } }, stderr: { write: (text) => { err += text; } },
    ...options,
  });
  return { code, out, err };
}

const socketPath = () => join(mkdtempSync(join(tmpdir(), "archive-search-")), "archive.sock");

test("direct mode: without the socket variable the query function runs and the text is printed", async () => {
  const calls = [];
  const query = async (args) => { calls.push(args); return RAW; };
  const result = await run(["Altolamprologus", "5"], { env: {}, query });
  assert.deepEqual({ ...result, out: undefined }, { code: 0, out: undefined, err: "" });
  assert.equal(result.out, formatResults(RAW));
  assert.equal(calls[0].term, "Altolamprologus");
  assert.equal(calls[0].limit, 5);
  assert.equal(calls[0].signal.aborted, false);
  assert.equal((await run(["ab"], { env: {}, query: async () => "" })).out, "No posts found.\n");
  await run(["ab"], { env: {}, query });
  assert.equal(calls[1].limit, 30);
});

test("direct mode: an empty term, a bad term and a bad limit are refused before any query", async () => {
  const query = async () => assert.fail("no query expected");
  assert.deepEqual(await run([], { env: {}, query }), {
    code: 2, out: "", err: 'Usage: node tools/species-data/archive-search.mjs "<term>" [limit]\n',
  });
  assert.equal((await run([""], { env: {}, query })).code, 2);
  for (const argv of [["a"], ["ab", "0"], ["ab", "101"], ["ab", "x"], ["ab", "1.5"], ["ab", "-3"], ["ab", ""], ["a\nb"]]) {
    const result = await run(argv, { env: {}, query });
    assert.equal(result.code, 2, argv.join(" "));
    assert.equal(result.out, "");
    assert.match(result.err, /^error: (term must be a string of 2 to 100 characters|limit must be an integer from 1 to 100)/);
  }
});

test("direct mode: a failed query prints its error line to stderr and exits with 1", async () => {
  const unavailable = await run(["ab"], { env: {}, query: async () => { throw new ArchiveError("unavailable"); } });
  assert.equal(unavailable.code, 1);
  assert.equal(unavailable.out, "");
  assert.match(unavailable.err, /^error: the archive database is not available \(container stack-postgres-1 is not running/);
});

test("client mode: the request goes to the socket, the answer is printed unchanged and no query runs", async () => {
  const seen = [];
  const service = await startArchiveService({
    socketPath: socketPath(), query: async (args) => { seen.push(args); return RAW; },
  });
  try {
    const query = async () => assert.fail("the direct query must not run");
    const env = { CICHLIDS_ARCHIVE_SEARCH_SOCKET: service.socketPath };
    const result = await run(["Altolamprologus", "7"], { env, query });
    assert.deepEqual(result, { code: 0, out: formatResults(RAW), err: "" });
    assert.deepEqual([seen[0].term, seen[0].limit], ["Altolamprologus", 7]);
    await run(["ab"], { env, query });
    assert.equal(seen[1].limit, 30);
    // A bad request is refused by the client already.
    assert.equal((await run(["a"], { env, query })).code, 2);
    assert.equal(seen.length, 2);
  } finally {
    await service.close();
  }
});

test("client mode: an error line of the service goes to stderr with exit code 1", async () => {
  const service = await startArchiveService({
    socketPath: socketPath(), query: async () => { throw new ArchiveError("unavailable"); },
  });
  try {
    const result = await run(["ab"], { env: { CICHLIDS_ARCHIVE_SEARCH_SOCKET: service.socketPath } });
    assert.equal(result.code, 1);
    assert.equal(result.out, "");
    assert.match(result.err, /^error: the archive database is not available \(container stack-postgres-1 is not running[^\n]*\)\n$/);
  } finally {
    await service.close();
  }
});

test("client mode: a socket that cannot be reached and a service that closes without an answer", async () => {
  const absent = join(mkdtempSync(join(tmpdir(), "archive-search-")), "absent.sock");
  const missing = await run(["ab"], { env: { CICHLIDS_ARCHIVE_SEARCH_SOCKET: absent } });
  assert.equal(missing.code, 1);
  assert.match(missing.err, /^error: cannot reach the archive search service \(connect ENOENT /);
  const path = socketPath();
  const silent = net.createServer((socket) => {
    socket.resume();
    socket.end();
  });
  await new Promise((done) => silent.listen(path, done));
  try {
    const result = await run(["ab"], { env: { CICHLIDS_ARCHIVE_SEARCH_SOCKET: path } });
    assert.equal(result.code, 1);
    assert.match(result.err, /closed the connection without an answer/);
  } finally {
    await new Promise((done) => silent.close(done));
  }
});

test("askService: sends one JSON line and gives up on a service that stays silent", async () => {
  const path = socketPath();
  const lines = [];
  const server = net.createServer((socket) => {
    socket.on("data", (chunk) => lines.push(String(chunk)));
    socket.on("error", () => {});
  });
  await new Promise((done) => server.listen(path, done));
  try {
    await assert.rejects(askService(path, { term: "ab", limit: 3 }, 50), /did not answer in time/);
    assert.deepEqual(lines, ['{"term":"ab","limit":3}\n']);
  } finally {
    server.close();
  }
});
