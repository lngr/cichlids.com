import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { parse } from "yaml";
import { expandAliases } from "./yaml-unalias.mjs";

const CLI = join(dirname(fileURLToPath(import.meta.url)), "yaml-unalias.mjs");

const ANCHORED = `sources:
  - &a
    url: https://example.org/a
    kind: paper
  - *a
  - *a
`;

test("an anchored list item and two aliases expand to three equal blocks", () => {
  const out = expandAliases(ANCHORED);
  const block = "  - url: https://example.org/a\n    kind: paper\n";
  assert.equal(out, `sources:\n${block}${block}${block}`);
  assert.deepEqual(parse(out), parse(ANCHORED));
});

test("a file without anchors is unchanged", () => {
  const text = "a: 1\nlist:\n  - x\n  - y: 2\n";
  assert.equal(expandAliases(text), text);
});

test("nested indentation inside the block is kept", () => {
  const text = `items:
    - &n
      name: x
      nested:
        - one
        - two: 2
          deep:
            k: v
    - *n
`;
  const out = expandAliases(text);
  const block = `    - name: x
      nested:
        - one
        - two: 2
          deep:
            k: v
`;
  assert.equal(out, `items:\n${block}${block}`);
  assert.deepEqual(parse(out), parse(text));
});

test("a mapping-value anchor is aliased once as a list item and once as a mapping value", () => {
  const text = `source: &cof
  kind: catalog
  title: Catalog
features:
  - name: a
    source: *cof
  - name: b
    sources:
      - *cof
`;
  const out = expandAliases(text);
  assert.equal(
    out,
    `source:
  kind: catalog
  title: Catalog
features:
  - name: a
    source:
      kind: catalog
      title: Catalog
  - name: b
    sources:
      - kind: catalog
        title: Catalog
`,
  );
  assert.deepEqual(parse(out), parse(text));
});

test("inline anchors, flow lists of aliases and same-column lists expand", () => {
  const text = `empty: &e []
names: &n
- x
- y
use:
  a: *e
  b: *n
  list: [*e, &g {kind: web}, *g]
`;
  const out = expandAliases(text);
  assert.doesNotMatch(out, /[&*]/);
  assert.deepEqual(parse(out), parse(text));
});

test("an alias without an anchor throws", () => {
  assert.throws(() => expandAliases("a:\n  - *missing\n"), /no anchor/);
});

test("the CLI rewrites a file, prints it and leaves clean files alone", () => {
  const dir = mkdtempSync(join(tmpdir(), "unalias-"));
  try {
    const anchored = join(dir, "a.yaml");
    const clean = join(dir, "b.yaml");
    writeFileSync(anchored, ANCHORED);
    writeFileSync(clean, "a: 1\n");
    const run = spawnSync("node", [CLI, anchored, clean], { encoding: "utf8" });
    assert.equal(run.status, 0);
    assert.equal(run.stdout.trim(), anchored);
    assert.equal(readFileSync(anchored, "utf8"), expandAliases(ANCHORED));
    assert.equal(readFileSync(clean, "utf8"), "a: 1\n");
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("an anchor name defined twice resolves each alias to the closest preceding anchor", () => {
  const text = "l:\n  - &a\n    k: 1\n  - *a\n  - &a\n    k: 2\n  - *a\n";
  const out = expandAliases(text);
  assert.deepEqual(parse(out), parse(text));
  assert.deepEqual(parse(out).l, [{ k: 1 }, { k: 1 }, { k: 2 }, { k: 2 }]);
});

test("the CLI leaves a file unchanged and exits 1 on a missing anchor", () => {
  const dir = mkdtempSync(join(tmpdir(), "unalias-"));
  try {
    const broken = "a:\n  - &x\n    k: v\n  - *zzz\n";
    const file = join(dir, "m.yaml");
    writeFileSync(file, broken);
    const run = spawnSync("node", [CLI, file], { encoding: "utf8" });
    assert.equal(run.status, 1);
    assert.equal(readFileSync(file, "utf8"), broken);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
