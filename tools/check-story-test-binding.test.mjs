// Tests fuer den Story<->Test-Bindungs-Check (ADR-0015; Definition of Done aus ADR-0003).
// Laeuft ohne externe Abhaengigkeiten ueber den eingebauten Node-Test-Runner.
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { parseFrontmatter, checkAll } from "./check-story-test-binding.mjs";

function makeRepo() {
  const root = mkdtempSync(join(tmpdir(), "story-test-binding-"));
  mkdirSync(join(root, "backlog", "tasks"), { recursive: true });
  mkdirSync(join(root, "e2e"), { recursive: true });
  return root;
}

function writeStory(root, file, { id, status, references = [] }) {
  const refs =
    references.length === 0
      ? "references: []"
      : "references:\n" + references.map((r) => `  - ${r}`).join("\n");
  const body = `---\nid: ${id}\ntitle: '${id} story'\nstatus: ${status}\nlabels: []\ndependencies: []\n${refs}\n---\n\n## Description\n\nbeispiel\n`;
  writeFileSync(join(root, "backlog", "tasks", file), body);
}

test("parseFrontmatter liest status und references als Liste", () => {
  const fm = parseFrontmatter(
    "---\nid: TASK-7\nstatus: Done\nreferences:\n  - e2e/a.yaml\n  - e2e/b.yaml\n---\nrumpf",
  );
  assert.equal(fm.id, "TASK-7");
  assert.equal(fm.status, "Done");
  assert.deepEqual(fm.references, ["e2e/a.yaml", "e2e/b.yaml"]);
});

test("Done-Story mit gueltiger bidirektionaler Bindung besteht", () => {
  const root = makeRepo();
  try {
    writeFileSync(join(root, "e2e", "ok.yaml"), "appId: x\ntags:\n  - TASK-1\n---\n- launchApp\n");
    writeStory(root, "task-1 - ok.md", { id: "TASK-1", status: "Done", references: ["e2e/ok.yaml"] });
    const { failures } = checkAll({ root });
    assert.deepEqual(failures, []);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("Done-Story ohne References schlaegt fehl", () => {
  const root = makeRepo();
  try {
    writeStory(root, "task-2 - noref.md", { id: "TASK-2", status: "Done", references: [] });
    const { failures } = checkAll({ root });
    assert.equal(failures.length, 1);
    assert.match(failures[0], /TASK-2/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("Done-Story mit nicht existierender Testdatei schlaegt fehl", () => {
  const root = makeRepo();
  try {
    writeStory(root, "task-3 - missing.md", { id: "TASK-3", status: "Done", references: ["e2e/fehlt.yaml"] });
    const { failures } = checkAll({ root });
    assert.equal(failures.length, 1);
    assert.match(failures[0], /TASK-3/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("Done-Story mit Test ohne Rueck-Tag schlaegt fehl", () => {
  const root = makeRepo();
  try {
    writeFileSync(join(root, "e2e", "untagged.yaml"), "appId: x\n---\n- launchApp\n");
    writeStory(root, "task-4 - untagged.md", { id: "TASK-4", status: "Done", references: ["e2e/untagged.yaml"] });
    const { failures } = checkAll({ root });
    assert.equal(failures.length, 1);
    assert.match(failures[0], /TASK-4/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("Nicht-Done-Story wird nicht erzwungen", () => {
  const root = makeRepo();
  try {
    writeStory(root, "task-5 - wip.md", { id: "TASK-5", status: "In Progress", references: [] });
    const { failures } = checkAll({ root });
    assert.deepEqual(failures, []);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
