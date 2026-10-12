// Tests for the waterbody table loader.
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { findWaterbody, loadWaterbodies } from "./waterbodies.mjs";

const ENTRY = `  - id: lake-malawi
    name: Lake Malawi
    aliases: [Lake Nyasa]
    temperatureC: {min: 24, max: 29}
    ph: {min: 7.7, max: 8.6}
    ghDgh: {min: 3, max: 5}
    khDkh: {min: 6, max: 8}
    rationale: Measured values.
    sources:
      - kind: literature
        title: Example
        url: https://example.org/a
        accessed: 2026-10-12
`;

function load(text) {
  const dir = mkdtempSync(join(tmpdir(), "waterbodies-"));
  const path = join(dir, "waterbodies.yaml");
  writeFileSync(path, text);
  return loadWaterbodies(path);
}

test("a valid table loads into a map by id", () => {
  const map = load(`waterbodies:\n${ENTRY}`);
  assert.deepEqual([...map.keys()], ["lake-malawi"]);
  assert.equal(map.get("lake-malawi").ph.max, 8.6);
});

test("an entry with min greater than max is rejected", () => {
  assert.throws(() => load(`waterbodies:\n${ENTRY.replace("{min: 3, max: 5}", "{min: 6, max: 5}")}`), /ghDgh.*greater than max/);
});

test("an entry without a required field is rejected", () => {
  assert.throws(() => load(`waterbodies:\n${ENTRY.replace(/    khDkh:.*\n/, "")}`), /khDkh/);
  assert.throws(() => load(`waterbodies:\n${ENTRY.replace(/    rationale:.*\n/, "")}`), /rationale/);
});

test("an entry with an unknown source kind is rejected", () => {
  assert.throws(() => load(`waterbodies:\n${ENTRY.replace("kind: literature", "kind: website")}`), /source/);
});

test("a duplicate id is rejected", () => {
  assert.throws(() => load(`waterbodies:\n${ENTRY}${ENTRY}`), /duplicate/);
});

test("findWaterbody matches id, name and alias case-insensitively", () => {
  const map = load(`waterbodies:\n${ENTRY}`);
  for (const name of ["lake-malawi", "LAKE MALAWI", "lake nyasa", " Lake Malawi "]) {
    assert.equal(findWaterbody(map, name)?.id, "lake-malawi", name);
  }
  assert.equal(findWaterbody(map, "Lake Victoria"), null);
  assert.equal(findWaterbody(map, null), null);
});
