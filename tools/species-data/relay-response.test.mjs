// Tests for the forwarding of an upstream answer to a proxy client, with fake answers and
// responses.
import { test } from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { PassThrough } from "node:stream";

import { forwardableStatus, relayResponse } from "./relay-response.mjs";

// A fake upstream answer: a readable stream with a status that records destroy().
function fakeAnswer(statusCode, body = "") {
  const answer = new PassThrough();
  answer.statusCode = statusCode;
  answer.end(body);
  return answer;
}

// A fake response: a writable stream whose writeHead records its arguments or throws.
function fakeResponse({ refuse = false } = {}) {
  const res = new PassThrough();
  res.heads = [];
  res.writeHead = (...args) => {
    if (refuse) throw Object.assign(new Error("refused"), { code: "ERR_INVALID_CHAR" });
    res.heads.push(args);
    return res;
  };
  return res;
}

test("forwardableStatus: only final statuses from 200 to 599", () => {
  for (const status of [200, 204, 301, 404, 500, 599]) {
    assert.equal(forwardableStatus(status), true, status);
  }
  const refused = [0, 99, 100, 101, 199, 600, 999, 1000, -1, 200.5, NaN, undefined, null, "200"];
  for (const status of refused) {
    assert.equal(forwardableStatus(status), false, String(status));
  }
});

test("relayResponse: writes status and headers without a reason phrase and streams the body", async () => {
  const res = fakeResponse();
  const answer = fakeAnswer(201, "body");
  assert.equal(relayResponse(res, answer, { "x-a": "1" }), true);
  assert.deepEqual(res.heads, [[201, { "x-a": "1" }]]);
  const chunks = [];
  for await (const chunk of res) chunks.push(chunk);
  assert.equal(Buffer.concat(chunks).toString(), "body");
});

test("relayResponse: a status outside 200..599 writes nothing and destroys the answer", () => {
  for (const status of [0, 99, 101, 600, undefined]) {
    const res = fakeResponse();
    const answer = fakeAnswer(status);
    assert.equal(relayResponse(res, answer, {}), false, String(status));
    assert.deepEqual(res.heads, []);
    assert.equal(answer.destroyed, true);
  }
});

test("relayResponse: a refusal of writeHead gives false and destroys the answer", () => {
  const res = fakeResponse({ refuse: true });
  const answer = fakeAnswer(200);
  assert.equal(relayResponse(res, answer, { "x-bad": "a\u0001b" }), false);
  assert.equal(answer.destroyed, true);
});

test("relayResponse: an error of the answer destroys the response", async () => {
  const res = fakeResponse();
  const answer = fakeAnswer(200);
  relayResponse(res, answer, {});
  const closed = new EventEmitter();
  res.once("close", () => closed.emit("done"));
  answer.destroy(new Error("reset"));
  await new Promise((done) => closed.once("done", done));
  assert.equal(res.destroyed, true);
});
