// Tests for the archive search service with an injected query function: protocol, validation, the
// concurrency cap, the timeout, the output cap and the error paths. No test starts docker.
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, statSync } from "node:fs";
import net from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { ArchiveError, formatResults } from "./archive-query.mjs";
import { startArchiveService } from "./archive-service.mjs";

const RAW = "12\tcichlids\t1\tfirst post\n12\tcichlids\t2\tsecond post\n40\tafrican\t7\tthird post\n";

const socketPath = () => join(mkdtempSync(join(tmpdir(), "archive-service-")), "archive.sock");

// Sends text (nothing when null) and resolves with the whole answer until the service closes.
// halfClose ends the sending side after the text.
const ask = (path, text, { halfClose = false } = {}) => new Promise((resolve, reject) => {
  const socket = net.connect(path);
  const chunks = [];
  socket.on("data", (chunk) => chunks.push(chunk));
  socket.on("error", reject);
  socket.on("close", () => resolve(Buffer.concat(chunks).toString("utf8")));
  socket.on("connect", () => {
    if (text !== null) socket.write(text);
    if (halfClose) socket.end();
  });
});

const request = (path, body, options) => ask(path, `${JSON.stringify(body)}\n`, options);

// Starts a service with a query that records its calls; the options override the defaults.
async function start({ query, ...options } = {}) {
  const calls = [];
  const service = await startArchiveService({
    socketPath: socketPath(),
    query: async (args) => {
      calls.push(args);
      return (query ?? (async () => RAW))(args);
    },
    ...options,
  });
  return { service, calls, path: service.socketPath };
}

// A query that waits until the test releases it.
function gate() {
  const waiting = [];
  return {
    waiting,
    query: (args) => new Promise((resolve, reject) => waiting.push({ args, resolve, reject })),
  };
}

const until = async (condition) => {
  for (let i = 0; i < 200 && !condition(); i += 1) await new Promise((done) => setTimeout(done, 5));
  assert.ok(condition(), "condition not reached in time");
};

test("a request gets the text of the search and the connection closes", async () => {
  const { service, calls, path } = await start();
  try {
    assert.equal(await request(path, { term: "Altolamprologus", limit: 5 }), formatResults(RAW));
    assert.equal(calls.length, 1);
    assert.equal(calls[0].term, "Altolamprologus");
    assert.equal(calls[0].limit, 5);
    assert.equal(calls[0].signal.aborted, false);
  } finally {
    await service.close();
  }
});

test("the limit defaults to 30, a client may half-close after the line, a request without newline counts at its end", async () => {
  const { service, calls, path } = await start({ query: async () => "" });
  try {
    assert.equal(await request(path, { term: "ab" }), "No posts found.\n");
    assert.equal(calls[0].limit, 30);
    assert.equal(await request(path, { term: "cd", limit: 2 }, { halfClose: true }), "No posts found.\n");
    assert.equal(await ask(path, JSON.stringify({ term: "ef" }), { halfClose: true }), "No posts found.\n");
    assert.deepEqual(calls.map((c) => c.term), ["ab", "cd", "ef"]);
  } finally {
    await service.close();
  }
});

test("only the first line counts; a second line is not a second request", async () => {
  const { service, calls, path } = await start();
  try {
    await ask(path, `${JSON.stringify({ term: "first" })}\n${JSON.stringify({ term: "second" })}\n`);
    assert.deepEqual(calls.map((c) => c.term), ["first"]);
  } finally {
    await service.close();
  }
});

test("an invalid request answers one error line and runs no query", async () => {
  const { service, calls, path } = await start();
  try {
    const errorOf = async (text) => {
      const answer = await ask(path, text);
      assert.match(answer, /^error: [^\n]+\n$/);
      return answer;
    };
    assert.match(await errorOf("not json\n"), /not one JSON line/);
    assert.match(await errorOf("\n"), /not one JSON line/);
    assert.match(await errorOf("[]\n"), /must be a JSON object/);
    assert.match(await errorOf('"ab"\n'), /must be a JSON object/);
    assert.match(await errorOf('{"term":"a"}\n'), /term must be a string of 2 to 100 characters/);
    assert.match(await errorOf(`{"term":"${"x".repeat(101)}"}\n`), /term must be a string/);
    assert.match(await errorOf('{"term":"a\\nb"}\n'), /without control characters/);
    assert.match(await errorOf('{"term":"a\\u0000b"}\n'), /without control characters/);
    assert.match(await errorOf('{"term":7}\n'), /term must be a string/);
    assert.match(await errorOf('{"term":"ab","limit":0}\n'), /limit must be an integer from 1 to 100/);
    assert.match(await errorOf('{"term":"ab","limit":101}\n'), /limit must be an integer/);
    assert.match(await errorOf('{"term":"ab","limit":2.5}\n'), /limit must be an integer/);
    assert.match(await errorOf('{"term":"ab","limit":"3"}\n'), /limit must be an integer/);
    assert.match(await errorOf('{"term":"ab","sql":"x"}\n'), /other than term and limit/);
    assert.equal(calls.length, 0);
  } finally {
    await service.close();
  }
});

test("a request line beyond 4096 bytes and a silent client answer an error line", async () => {
  const { service, calls, path } = await start({ requestTimeoutMs: 50 });
  try {
    assert.equal(await ask(path, "x".repeat(5000)), "error: the request line is too long\n");
    assert.equal(await ask(path, null), "error: no request line received\n");
    assert.equal(calls.length, 0);
  } finally {
    await service.close();
  }
});

test("at most the given number of queries run at once; the next request is busy until one ends", async () => {
  const held = gate();
  const { service, path } = await start({ query: held.query, maxQueries: 4 });
  try {
    const answers = [0, 1, 2, 3].map((i) => request(path, { term: `term${i}` }));
    await until(() => held.waiting.length === 4);
    assert.equal(await request(path, { term: "fifth" }), "error: too many archive queries at once, retry shortly\n");
    assert.equal(held.waiting.length, 4);
    held.waiting[0].resolve(RAW);
    assert.equal(await answers[0], formatResults(RAW));
    const sixth = request(path, { term: "sixth" });
    await until(() => held.waiting.length === 5);
    for (const entry of held.waiting.slice(1)) entry.resolve("");
    assert.deepEqual(await Promise.all(answers.slice(1)), Array(3).fill("No posts found.\n"));
    held.waiting[4].resolve("");
    assert.equal(await sixth, "No posts found.\n");
  } finally {
    await service.close();
  }
});

test("a failed query frees its place", async () => {
  const held = gate();
  const { service, path } = await start({ query: held.query, maxQueries: 1 });
  try {
    const first = request(path, { term: "first" });
    await until(() => held.waiting.length === 1);
    held.waiting[0].reject(new ArchiveError("failed"));
    assert.equal(await first, "error: the archive query failed\n");
    const second = request(path, { term: "second" });
    await until(() => held.waiting.length === 2);
    held.waiting[1].resolve("");
    assert.equal(await second, "No posts found.\n");
  } finally {
    await service.close();
  }
});

test("a query beyond the timeout answers the timeout error and its signal aborts", async () => {
  const held = gate();
  const { service, path } = await start({ query: held.query, timeoutMs: 50, maxQueries: 1 });
  try {
    assert.equal(await request(path, { term: "slow" }), "error: the archive query timed out after 30 s\n");
    assert.equal(held.waiting[0].args.signal.aborted, true);
    // The query ignores its signal, so it keeps its place until it ends.
    assert.equal(await request(path, { term: "next" }), "error: too many archive queries at once, retry shortly\n");
    held.waiting[0].reject(new Error("killed"));
    const next = request(path, { term: "next" });
    await until(() => held.waiting.length === 2);
    held.waiting[1].resolve("");
    assert.equal(await next, "No posts found.\n");
  } finally {
    await service.close();
  }
});

test("an answer beyond the output cap is cut at a line and says so", async () => {
  const raw = Array.from({ length: 100 }, (_, i) => `${i}\tcichlids\t${i}\t${"x".repeat(580)}`).join("\n");
  const { service, path } = await start({ query: async () => raw, maxOutputBytes: 10 * 1024 });
  try {
    const answer = await request(path, { term: "many", limit: 100 });
    assert.ok(Buffer.byteLength(answer) <= 10 * 1024);
    assert.match(answer, /\n\[output cut at 10 KB\]\n$/);
    assert.match(answer, /== cichlids.com forum thread 0\n/);
    assert.ok(!answer.includes("thread 99\n"));
    for (const line of answer.split("\n")) assert.ok(!/^   - x+$/.test(line) || line.length === 3 + 2 + 580);
  } finally {
    await service.close();
  }
});

test("the default cap is 200 KB", async () => {
  const raw = Array.from({ length: 100 }, (_, i) => `${i}\tcichlids\t${i}\t${"\u{1F41F}".repeat(600)}`).join("\n");
  const { service, path } = await start({ query: async () => raw });
  try {
    const answer = await request(path, { term: "many", limit: 100 });
    assert.ok(Buffer.byteLength(answer) <= 200 * 1024);
    assert.match(answer, /\n\[output cut at 200 KB\]\n$/);
  } finally {
    await service.close();
  }
});

test("errors of the query answer their message; an unknown error answers the generic one without its text", async () => {
  const outcomes = [
    new ArchiveError("unavailable"),
    new ArchiveError("timeout"),
    new Error("password authentication failed for user cichlids with term secret-term"),
    "not an error",
  ];
  const { service, path } = await start({ query: async () => { throw outcomes.shift(); } });
  try {
    assert.match(await request(path, { term: "ab" }), /^error: the archive database is not available \(container stack-postgres-1 is not running/);
    assert.equal(await request(path, { term: "ab" }), "error: the archive query timed out after 30 s\n");
    const generic = await request(path, { term: "secret-term" });
    assert.equal(generic, "error: the archive query failed\n");
    assert.equal(await request(path, { term: "ab" }), "error: the archive query failed\n");
  } finally {
    await service.close();
  }
});

test("a query that throws synchronously and a result of the wrong type answer the generic error", async () => {
  let mode = 0;
  const { service, path } = await start({
    query: () => {
      mode += 1;
      if (mode === 1) throw new Error("boom");
      return { not: "text" };
    },
  });
  try {
    assert.equal(await request(path, { term: "ab" }), "error: the archive query failed\n");
    assert.equal(await request(path, { term: "ab" }), "error: the archive query failed\n");
  } finally {
    await service.close();
  }
});

test("the service neither writes to stdout or stderr nor keeps terms: requests leave no output", async () => {
  const written = [];
  const original = { out: process.stdout.write, err: process.stderr.write };
  const { service, path } = await start();
  process.stdout.write = (chunk, ...rest) => { written.push(String(chunk)); return original.out.call(process.stdout, "", ...rest.filter((x) => typeof x === "function")); };
  process.stderr.write = (chunk, ...rest) => { written.push(String(chunk)); return original.err.call(process.stderr, "", ...rest.filter((x) => typeof x === "function")); };
  try {
    await request(path, { term: "needle-term" });
    await ask(path, "needle-term garbage\n");
    await request(path, { term: "x" });
  } finally {
    process.stdout.write = original.out;
    process.stderr.write = original.err;
    await service.close();
  }
  assert.deepEqual(written.filter((text) => /needle-term|first post/.test(text)), []);
});

test("close ends open connections, aborts running queries and removes the socket; a socket path that cannot be bound fails to start", async () => {
  const held = gate();
  const { service, path } = await start({ query: held.query });
  assert.equal(statSync(path).isSocket(), true);
  const answer = request(path, { term: "running" });
  await until(() => held.waiting.length === 1);
  await service.close();
  assert.equal(await answer, "");
  await until(() => held.waiting[0].args.signal.aborted);
  assert.ok(!existsSync(path));
  held.waiting[0].resolve("");
  await assert.rejects(startArchiveService({ socketPath: join(tmpdir(), "archive-service-absent", "archive.sock"), query: async () => "" }), /ENOENT|EACCES/);
});
