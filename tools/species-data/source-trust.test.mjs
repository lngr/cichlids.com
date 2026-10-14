import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { stringify } from "yaml";
import { loadSourceTrust, trustOf } from "./source-trust.mjs";

const list = {
  default: "low",
  byKind: { literature: "high", database: "medium", website: "low" },
  hosts: [
    { host: "example.org", path: "/studies", level: "high" },
    { host: "example.org", level: "medium" },
    { host: "sub.example.org", level: "low" },
  ],
};

const source = (url, kind = "literature") => ({ title: "Source", kind, url });

test("host and parent domains match, paths use prefixes and the first match wins", () => {
  for (const url of ["https://example.org/studies/a", "https://www.example.org/studies/a", "https://SUB.EXAMPLE.ORG/studies-extra"]) {
    assert.equal(trustOf(source(url), list), "high", url);
  }
  assert.equal(trustOf(source("https://sub.example.org/other"), list), "medium");
  for (const url of ["https://notexample.org/studies", "https://example.org.evil.com/studies", "https://unknown.org", "bad url"]) {
    assert.equal(trustOf(source(url), list), "low", url);
  }
});

test("only sources without a URL use byKind; unmatched sources use the default", () => {
  assert.equal(trustOf({ kind: "literature" }, list), "high");
  assert.equal(trustOf({ kind: "database" }, list), "medium");
  assert.equal(trustOf({ kind: "other" }, list), "low");
  assert.equal(trustOf(source("https://unknown.org"), list), "low");
  assert.equal(trustOf(undefined, list), "low");
  assert.equal(trustOf(source("https://unknown.org"), { ...list, default: "medium" }), "medium");
});

test("archive copies use the original host and path on both archive hosts", () => {
  for (const host of ["web.archive.org", "archive.org"]) {
    for (const stamp of ["20260102030405", "20260102030405id_", "20260102im_"]) {
      assert.equal(trustOf(source(`https://${host}/web/${stamp}/https://sub.example.org/studies/a`), list), "high");
      assert.equal(trustOf(source(`https://${host}/web/${stamp}/http://example.org/other`), list), "medium");
      assert.equal(trustOf(source(`https://${host}/web/${stamp}/https://unknown.org/a`), list), "low");
    }
  }
  assert.equal(trustOf(source("https://archive.org/details/book"), list), "low");
  assert.equal(trustOf(source("https://web.archive.org/web/20260102/invalid"), list), "low");
});

function withList(doc, check) {
  const dir = mkdtempSync(join(tmpdir(), "source-trust-"));
  const path = join(dir, "trust.yaml");
  try {
    writeFileSync(path, stringify(doc));
    check(path);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test("the loader reads the project list and a supplied path", () => {
  const project = loadSourceTrust();
  assert.equal(trustOf(source("https://www.fishbase.se/a"), project), "high");
  assert.equal(trustOf(source("https://cichlid-forum.com/articles/a"), project), "medium");
  assert.equal(trustOf(source("https://cichlid-forum.com/phpBB/a"), project), "low");
  withList(list, (path) => assert.deepEqual(loadSourceTrust(path), list));
});

test("duplicate host and path pairs fail, distinct paths are allowed", () => {
  withList({ ...list, hosts: [...list.hosts, { host: "EXAMPLE.ORG", level: "high" }] }, (path) => {
    assert.throws(() => loadSourceTrust(path), /duplicate.*host.*path/i);
  });
  withList({ ...list, hosts: [...list.hosts, { host: "example.org", path: "", level: "high" }] }, (path) => {
    assert.throws(() => loadSourceTrust(path), /duplicate/i);
  });
});

test("invalid trust levels fail in hosts, byKind and default", () => {
  for (const doc of [
    { ...list, default: "trusted" },
    { ...list, byKind: { literature: "trusted" } },
    { ...list, hosts: [{ host: "example.org", level: "trusted" }] },
  ]) withList(doc, (path) => assert.throws(() => loadSourceTrust(path), /high.*medium.*low/));
});

test("malformed list structure, hosts and path prefixes fail", () => {
  for (const doc of [null, {}, { ...list, hosts: {} }, { ...list, byKind: [] },
    { ...list, hosts: [null] }, { ...list, hosts: [{ level: "high" }] },
    { ...list, hosts: [{ host: "https://example.org", level: "high" }] },
    { ...list, hosts: [{ host: "example.org", path: 12, level: "high" }] },
    { ...list, hosts: [{ host: "example.org", path: "studies", level: "high" }] },
  ]) withList(doc, (path) => assert.throws(() => loadSourceTrust(path)));
});
