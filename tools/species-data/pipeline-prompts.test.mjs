import test from "node:test";
import assert from "node:assert/strict";
import { buildGroupsyncPrompt, buildPrompt, pendingFeatures } from "./pipeline-prompts.mjs";

const ROOT = "/repo/root";
const base = { root: ROOT, legacyId: 201, name: "Aulonocara stuartgranti", hint: "Malawi peacock", model: "m-1", today: "2026-01-02", runner: "claude" };
const out = (role) => `${ROOT}/data/species/work/legacy-201/${role}.yaml`;
const ROLES = ["generator", "checker-a", "checker-b", "checker-c", "tiebreaker-1", "adjudicator"];
const pending = [["ph", [{ min: 6.5, max: 7.5 }, { min: 7, max: 8 }]], ["predation", [1, 2]]];
const all = (extra = {}) => ROLES.map((role) => [role, buildPrompt({ ...base, role, pending, ...extra })]);

test("role sentences", () => {
  const p = Object.fromEntries(all());
  assert.match(p.generator, /^You are the generator of the species profile pipeline/);
  assert.match(p["checker-a"], /^You are independent checker A /);
  assert.match(p["checker-b"], /^You are independent checker B /);
  assert.match(p["checker-c"], /^You are independent checker C /);
  assert.match(p["tiebreaker-1"], /^You are tie-breaker 1 /);
  assert.match(p.adjudicator, /^You are the adjudicator /);
});

test("output path, model, today and root", () => {
  for (const role of ["generator", "checker-a", "checker-b", "checker-c", "tiebreaker-1"]) {
    const p = buildPrompt({ ...base, role, pending });
    assert.ok(p.includes(`\`${out(role)}\``), role);
  }
  for (const [, p] of all()) {
    assert.ok(p.includes("use model `m-1`"));
    assert.ok(p.includes("Today is 2026-01-02."));
    assert.ok(p.includes(`under ${ROOT}.`));
  }
  assert.ok(buildPrompt({ ...base, role: "adjudicator" }).includes(`${ROOT}/data/species/profiles/<id>.yaml`));
});

test("lake and field sentences", () => {
  const lake = buildPrompt({ ...base, role: "generator", lake: "Malawi" });
  assert.ok(lake.includes("Lake Malawi: the waterbody table decides"));
  assert.ok(!lake.includes("Not a lake of the waterbody table"));
  const field = buildPrompt({ ...base, role: "generator" });
  assert.ok(field.includes("Not a lake of the waterbody table: determine the natural water values"));
});

test("legacyIds default and override", () => {
  assert.ok(buildPrompt({ ...base, role: "generator" }).includes("legacyIds: [201]."));
  assert.ok(buildPrompt({ ...base, role: "generator", legacyIds: "201, 202" }).includes("legacyIds: [201, 202]."));
});

test("independence sentence only for checkers, methods per checker", () => {
  for (const [role, p] of all()) {
    assert.equal(p.includes("Work independently:"), role.startsWith("checker-"), role);
  }
  assert.ok(!buildPrompt({ ...base, role: "checker-a" }).includes("Method:"));
  assert.ok(buildPrompt({ ...base, role: "checker-b" }).includes("Method: field data first."));
  assert.ok(buildPrompt({ ...base, role: "checker-c" }).includes("Method: sources first."));
});

test("tools paragraph depends on the runner", () => {
  for (const [, p] of all({ runner: "codex" })) {
    assert.ok(p.includes("use your own web search tool"));
    assert.ok(!p.includes("Use WebSearch for discovery"));
  }
  for (const [, p] of all({ runner: "claude" })) {
    assert.ok(p.includes("Use WebSearch for discovery as common.md says."));
    assert.ok(p.includes("never with curl or WebFetch"));
    assert.ok(!p.includes("use your own web search tool"));
  }
  assert.throws(() => buildPrompt({ ...base, role: "generator", runner: "x" }), /unknown runner/);
});

test("rules carry the full-text, process and validator sentences", () => {
  for (const [role, p] of all()) {
    assert.ok(p.includes("Full texts only via `fetch-oa.mjs`; when it cannot get a full text, use the abstract and never download the paper another way."), role);
    assert.ok(p.includes("Never kill processes you did not start yourself, and leave no background processes running when you finish."), role);
    const validator = "The validator may report warnings in other files; only your own file has to validate.";
    assert.equal(p.includes(validator), !["adjudicator", "tiebreaker-1"].includes(role), role);
  }
});

test("note reaches the prompt", () => {
  assert.ok(buildPrompt({ ...base, role: "adjudicator", note: "ADJ-NOTE" }).includes("ADJ-NOTE"));
  assert.ok(buildPrompt({ ...base, role: "generator", note: "GEN-NOTE" }).includes("GEN-NOTE"));
});

test("adjudicator carries the PROFILE EXISTS rule", () => {
  const p = buildPrompt({ ...base, role: "adjudicator" });
  assert.ok(p.includes("start your report with the words PROFILE EXISTS"));
  assert.ok(p.includes(`${ROOT}/data/species/work/legacy-201/adjudicator-profile.yaml`));
});

test("tiebreaker renders the votes list and requires pending", () => {
  const p = buildPrompt({ ...base, role: "tiebreaker-1", pending });
  assert.ok(p.includes('- ph: votes {"min":6.5,"max":7.5}, {"min":7,"max":8}.\n- predation: votes 1, 2.'));
  assert.throws(() => buildPrompt({ ...base, role: "tiebreaker-1" }), /pending is required/);
});

test("no prompt mentions checker-a.done or a dash", () => {
  for (const [role, p] of all()) {
    assert.ok(!p.includes("checker-a.done"), role);
    assert.ok(!/[\u2013\u2014]/.test(p), role);
  }
});

test("unknown role throws", () => {
  assert.throws(() => buildPrompt({ ...base, role: "nope" }), /unknown role/);
});

test("pendingFeatures", () => {
  const tally = {
    pendingTiebreakers: ["ph", "predation"],
    features: { ph: { proposals: [{ by: "a", value: 1 }, { by: "b", value: 2 }] }, predation: {} },
  };
  assert.deepEqual(pendingFeatures(tally), [["ph", [1, 2]], ["predation", []]]);
  assert.deepEqual(pendingFeatures({ features: {} }), []);
});

test("groupsync replaces both placeholders", () => {
  const p = buildGroupsyncPrompt({ root: ROOT, list: "- profile x: group y" });
  assert.ok(p.startsWith(`Work in ${ROOT} (a git worktree`));
  assert.ok(p.includes("- profile x: group y"));
  assert.ok(!p.includes("{{"));
});
