// Tests for the vote tally. Fixtures are small in-memory feature sets per agent file.
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

import { tally } from "./tally.mjs";

const lit = (n) => ({ kind: "literature", title: `Literature ${n}`, url: `https://fishbase.se/l${n}` });
const db = (n) => ({ kind: "database", title: `Database ${n}`, url: `https://ciklid.org/d${n}` });
const forum = (n) => ({ kind: "forum", title: `Forum ${n}`, url: `https://example.org/f${n}` });
const TWO = [lit(1), db(1)];

function file(name, features) {
  const out = {};
  for (const [feature, spec] of Object.entries(features)) {
    out[feature] = Array.isArray(spec) ? { value: spec[0], sources: spec[1] ?? TWO } : spec;
  }
  return { name, data: { features: out } };
}

const NAMES = ["generator.yaml", "checker-a.yaml", "checker-b.yaml", "tiebreaker-1.yaml", "tiebreaker-2.yaml"];

function voteFiles(feature, values, sources = TWO) {
  return values.map((v, i) => file(NAMES[i], { [feature]: [v, sources] }));
}

const decide = (feature, values, sources) => tally(voteFiles(feature, values, sources)).features[feature];

const LAKE = {
  id: "lake-malawi",
  name: "Lake Malawi",
  aliases: ["Lake Nyasa"],
  temperatureC: { min: 24, max: 29 },
  ph: { min: 7.7, max: 8.6 },
  ghDgh: { min: 3, max: 5 },
  khDkh: { min: 6, max: 8 },
  rationale: "Measured values.",
  sources: [lit(9)],
};

test("three equal votes with two distinct sources including a trusted source are unanimous", () => {
  const f = decide("predation", [1, 1, 1]);
  assert.equal(f.decision, 1);
  assert.deepEqual(f.verification, { method: "unanimous", votes: { agree: 3, total: 3 } });
  assert.equal(f.confidence, "medium");
  assert.equal(f.needsTiebreaker, undefined);
});

test("three or more distinct sources including high trust give high confidence", () => {
  const f = decide("predation", [1, 1, 1], [lit(1), db(1), lit(2)]);
  assert.equal(f.confidence, "high");
  assert.equal(f.sources.length, 3);
});

test("a disagreement below five votes asks for a tie-breaker", () => {
  const result = tally(voteFiles("predation", [1, 1, 2]));
  const f = result.features.predation;
  assert.equal(f.needsTiebreaker, true);
  assert.equal(f.reason, "disagreement");
  assert.equal(f.decision, null);
  assert.deepEqual(f.proposals.map((p) => p.value), [1, 1, 2]);
  assert.ok(result.pendingTiebreakers.includes("predation"));
});

test("agreeing votes with fewer than two distinct sources including a trusted source ask for a tie-breaker", () => {
  const f = decide("speciesOnly", [true, true, true], [lit(1)]);
  assert.equal(f.needsTiebreaker, true);
  assert.equal(f.reason, "insufficient-sources");
});

test("sources are counted once by url", () => {
  const f = decide("speciesOnly", [true, true, true], [lit(1), lit(1)]);
  assert.equal(f.reason, "insufficient-sources");
});

test("four of five agreeing votes are a qualified majority", () => {
  const f = decide("predation", [1, 1, 1, 1, 3]);
  assert.equal(f.decision, 1);
  assert.deepEqual(f.verification, { method: "qualified-majority", votes: { agree: 4, total: 5 } });
});

test("five equal votes are a qualified majority", () => {
  const f = decide("hybridGroups", Array(5).fill(["tropheus"]));
  assert.equal(f.verification.method, "qualified-majority");
  assert.equal(f.verification.votes.agree, 5);
});

test("empty hybrid group lists agree with each other", () => {
  const f = decide("hybridGroups", [[], [], []]);
  assert.deepEqual(f.decision, []);
  assert.equal(f.verification.method, "unanimous");
});

test("hybrid group lists agree as sets regardless of order", () => {
  const f = decide("hybridGroups", [["a", "b"], ["b", "a"], ["a", "b"]]);
  assert.deepEqual(f.decision, ["a", "b"]);
  assert.deepEqual(f.verification, { method: "unanimous", votes: { agree: 3, total: 3 } });
});

test("a different set of hybrid groups is a disagreement", () => {
  const f = decide("hybridGroups", [["a", "b"], ["a"], ["a", "b"]]);
  assert.equal(f.needsTiebreaker, true);
  assert.equal(f.reason, "disagreement");
});

test("a decided hybrid group list is sorted", () => {
  const f = decide("hybridGroups", [["b", "a"], ["b", "a"], ["a", "b"]]);
  assert.deepEqual(f.decision, ["a", "b"]);
});

test("work files with the legacy hybridGroup value are read as lists", () => {
  const files = [
    file("generator.yaml", { hybridGroup: ["tropheus", TWO] }),
    file("checker-a.yaml", { hybridGroups: [["tropheus"], TWO] }),
    file("checker-b.yaml", { hybridGroup: ["tropheus", TWO] }),
  ];
  const f = tally(files).features.hybridGroups;
  assert.deepEqual(f.decision, ["tropheus"]);
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.proposals.map((p) => p.value), [["tropheus"], ["tropheus"], ["tropheus"]]);
});

test("a legacy null hybridGroup is read as an empty list", () => {
  const files = ["generator.yaml", "checker-a.yaml", "checker-b.yaml"].map((n) => file(n, { hybridGroup: [null, TWO] }));
  const f = tally(files).features.hybridGroups;
  assert.deepEqual(f.decision, []);
  assert.equal(f.verification.method, "unanimous");
  assert.equal(tally(files).features.hybridGroup, undefined);
});

test("a 3 to 2 split of five votes is conservative", () => {
  const f = decide("predation", [1, 1, 1, 2, 3]);
  assert.equal(f.decision, 3);
  assert.deepEqual(f.verification, { method: "conservative", votes: { agree: 3, total: 5 } });
  assert.equal(f.confidence, "low");
});

test("conservative predation is the maximum level", () => {
  assert.equal(decide("predation", [0, 0, 1, 2, 2]).decision, 2);
});

test("conservative speciesOnly is true when any vote says true", () => {
  const f = decide("speciesOnly", [false, false, false, true, true]);
  assert.equal(f.decision, true);
  assert.equal(f.verification.method, "conservative");
});

test("conservative hybridGroups is the union of the groups named by two or more votes", () => {
  const f = decide("hybridGroups", [["a", "b"], ["b", "c"], ["a"], [], ["d"]]);
  assert.deepEqual(f.decision, ["a", "b"]);
  assert.equal(f.verification.method, "conservative");
  assert.equal(f.confidence, "low");
});

test("conservative hybridGroups falls back to the most named group when none reaches two votes", () => {
  const f = decide("hybridGroups", [[], ["a"], ["b"], ["c"], []]);
  assert.deepEqual(f.decision, ["a"]);
  assert.equal(f.verification.method, "conservative");
  assert.deepEqual(decide("hybridGroups", [[], [], [], ["g"], ["h"]]).decision, ["g"]);
});

test("conservative hybridGroups is empty when no vote names a group", () => {
  const f = decide("hybridGroups", [[], [], [], [], []]);
  assert.deepEqual(f.decision, []);
  assert.equal(f.verification.method, "qualified-majority");
});

test("conservative diet takes the strictest type and any bloat sensitivity", () => {
  const diet = (type, bloatSensitive) => ({ type, bloatSensitive });
  const f = decide("diet", [diet("omnivore", false), diet("invertivore", false), diet("piscivore", true), diet("omnivore", false), diet("invertivore", true)]);
  assert.deepEqual(f.decision, { type: "piscivore", bloatSensitive: true });
  assert.equal(f.verification.method, "conservative");
  const g = decide("diet", [diet("omnivore", false), diet("invertivore", false), diet("aufwuchs", false), diet("omnivore", false), diet("invertivore", false)]);
  assert.equal(g.decision.type, "aufwuchs");
});

test("diet votes agree on type and bloat sensitivity only", () => {
  const f = decide("diet", [
    { type: "omnivore", bloatSensitive: false, foods: ["a"] },
    { type: "omnivore", bloatSensitive: false, foods: ["b"] },
    { type: "omnivore", bloatSensitive: false },
  ]);
  assert.equal(f.verification.method, "unanimous");
  const g = decide("diet", [
    { type: "omnivore", bloatSensitive: false },
    { type: "omnivore", bloatSensitive: true },
    { type: "omnivore", bloatSensitive: false },
  ]);
  assert.equal(g.reason, "disagreement");
});

test("a natural range without a qualified majority is the median range of all votes", () => {
  const f = decide("temperatureC", [{ min: 23, max: 30 }, { min: 24, max: 30 }, { min: 19.5, max: 32.4 }, { min: 23, max: 36 }, { min: 23, max: 36 }]);
  assert.deepEqual(f.verification, { method: "median", votes: { agree: 2, total: 5 } });
  assert.equal(f.confidence, "low");
  assert.deepEqual(f.decision, { min: 23, max: 32 });
});

test("the median range of six votes is rounded to the feature precision", () => {
  const votes = [{ min: 23, max: 30 }, { min: 24, max: 30 }, { min: 19.5, max: 32.4 }, { min: 23, max: 36 }, { min: 23, max: 36 }, { min: 28.5, max: 32.4 }];
  const f = decide("temperatureC", votes);
  assert.equal(f.verification.method, "median");
  assert.deepEqual(f.decision, { min: 23, max: 32 });
  const g = decide("ph", [{ min: 6.5, max: 7.5 }, { min: 7, max: 8 }, { min: 7.5, max: 9 }, { min: 8, max: 9.5 }, { min: 6, max: 7 }]);
  assert.equal(g.verification.method, "median");
  assert.deepEqual(g.decision, { min: 7, max: 8 });
});

test("a natural range with a qualified majority is decided by that majority", () => {
  const f = decide("ghDgh", [{ min: 3, max: 5 }, { min: 4, max: 6 }, { min: 3, max: 5 }, { min: 4, max: 6 }, { min: 20, max: 30 }]);
  assert.equal(f.verification.method, "qualified-majority");
  assert.deepEqual(f.decision, { min: 4, max: 6 });
});

test("ranges within the tolerance agree and the decision is the median", () => {
  const f = decide("temperatureC", [{ min: 24, max: 28 }, { min: 25, max: 29 }, { min: 24, max: 30 }]);
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.decision, { min: 24, max: 29 });
});

test("ranges beyond the tolerance disagree", () => {
  const f = decide("temperatureC", [{ min: 24, max: 28 }, { min: 24, max: 28 }, { min: 22, max: 28 }]);
  assert.equal(f.reason, "disagreement");
  assert.equal(f.proposals.length, 3);
});

test("the pH tolerance is 0.3 and the median is rounded to one decimal", () => {
  const f = decide("ph", [{ min: 7.7, max: 8.6 }, { min: 8.0, max: 8.9 }, { min: 7.8, max: 8.7 }]);
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.decision, { min: 7.8, max: 8.7 });
  const g = decide("ph", [{ min: 7.7, max: 8.6 }, { min: 8.1, max: 8.6 }, { min: 7.7, max: 8.6 }]);
  assert.equal(g.reason, "disagreement");
});

test("the median of an even number of votes is rounded to an integer", () => {
  const f = decide("ghDgh", [{ min: 3, max: 5 }, { min: 4, max: 6 }, { min: 3, max: 5 }, { min: 4, max: 6 }]);
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.decision, { min: 4, max: 6 });
});

test("a tie between candidates goes to the earlier file", () => {
  const f = decide("predation", [2, 1, 1, 2, 3]);
  assert.equal(f.verification.method, "conservative");
  const g = decide("predation", [2, 1, 1, 2]);
  assert.equal(g.needsTiebreaker, true);
  assert.deepEqual(g.proposals.map((p) => p.file), NAMES.slice(0, 4));
});

test("files are ordered generator, checkers, then tie-breakers by number", () => {
  const files = [NAMES[4], NAMES[3], NAMES[2], NAMES[1], NAMES[0]].map((name, i) => file(name, { predation: [i, TWO] }));
  const result = tally(files);
  assert.deepEqual(result.files, NAMES);
  assert.deepEqual(result.features.predation.proposals.map((p) => p.file), NAMES);
});

test("checker-c votes after checker-b and before the tie-breakers", () => {
  const order = ["generator.yaml", "checker-a.yaml", "checker-b.yaml", "checker-c.yaml", "tiebreaker-1.yaml"];
  const files = [...order].reverse().map((name) => file(name, { predation: [1, TWO] }));
  assert.deepEqual(tally(files).files, order);
});

test("four agreeing votes with checker-c are unanimous", () => {
  const names = ["generator.yaml", "checker-a.yaml", "checker-b.yaml", "checker-c.yaml"];
  const f = tally(names.map((name) => file(name, { predation: [1, TWO] }))).features.predation;
  assert.deepEqual(f.verification, { method: "unanimous", votes: { agree: 4, total: 4 } });
});

test("with checker-c one tie-breaker reaches a qualified majority", () => {
  const votes = { "generator.yaml": 1, "checker-a.yaml": 1, "checker-b.yaml": 2, "checker-c.yaml": 1 };
  const files = Object.entries(votes).map(([name, v]) => file(name, { predation: [v, TWO] }));
  assert.equal(tally(files).features.predation.needsTiebreaker, true);
  const f = tally([...files, file("tiebreaker-1.yaml", { predation: [1, TWO] })]).features.predation;
  assert.deepEqual(f.verification, { method: "qualified-majority", votes: { agree: 4, total: 5 } });
  assert.deepEqual(f.proposals.map((p) => p.file), [...Object.keys(votes), "tiebreaker-1.yaml"]);
});

test("a tie-breaker file with a subset of features votes only on those", () => {
  const files = [
    ...voteFiles("predation", [1, 1, 2]),
    file("tiebreaker-1.yaml", { predation: [1, TWO] }),
    file("tiebreaker-2.yaml", { predation: [1, TWO] }),
  ];
  const f = tally(files).features.predation;
  assert.deepEqual(f.verification, { method: "qualified-majority", votes: { agree: 4, total: 5 } });
  assert.equal(tally(files).features.diet.reason, "no-votes");
});

test("water values of a lake species come from the table without votes", () => {
  const origin = { value: { continent: "Africa", region: "Lake Malawi", waterbody: "Lake Nyasa" } };
  const files = NAMES.slice(0, 3).map((name) => file(name, { origin, temperatureC: [{ min: 20, max: 30 }], ph: [{ min: 7, max: 9 }] }));
  const result = tally(files, new Map([[LAKE.id, LAKE]]));
  assert.equal(result.waterbody, "lake-malawi");
  for (const name of ["temperatureC", "ph", "ghDgh", "khDkh"]) {
    const f = result.features[name];
    assert.deepEqual(f.decision, { min: LAKE[name].min, max: LAKE[name].max }, name);
    assert.deepEqual(f.verification, { method: "waterbody", waterbody: "lake-malawi" }, name);
    assert.equal(f.confidence, "high");
    assert.deepEqual(f.sources, LAKE.sources);
  }
  assert.ok(!result.pendingTiebreakers.includes("ph"));
});

test("the most common waterbody decides and an unknown one falls back to votes", () => {
  const origin = (waterbody) => ({ value: { continent: "Africa", region: "R", waterbody } });
  const lake = [file(NAMES[0], { origin: origin("Lake Malawi") }), file(NAMES[1], { origin: origin("lake-malawi") }), file(NAMES[2], { origin: origin("Other") })];
  assert.equal(tally(lake, new Map([[LAKE.id, LAKE]])).waterbody, "lake-malawi");
  const other = [file(NAMES[0], { origin: origin("Lake Malawi") }), file(NAMES[1], { origin: origin("Other") }), file(NAMES[2], { origin: origin("Other") })];
  const result = tally(other, new Map([[LAKE.id, LAKE]]));
  assert.equal(result.waterbody, null);
  assert.equal(result.features.khDkh.reason, "no-votes");
  assert.equal(result.features.ph.reason, "no-votes");
});

test("the result carries no YAML anchors", () => {
  const origin = { value: { continent: "Africa", region: "R", waterbody: "Lake Malawi" } };
  const result = tally(NAMES.slice(0, 3).map((n) => file(n, { origin })), new Map([[LAKE.id, LAKE]]));
  assert.notEqual(result.features.ph.sources, result.features.ghDgh.sources);
});

test("evidence of all votes is collected once per source and value", () => {
  const ev = (source, value, note) => ({ source, value, ...(note ? { note } : {}) });
  const files = [
    file("generator.yaml", { predation: { value: 1, sources: TWO, evidence: [ev(lit(1), 1), ev(db(1), 2)] } }),
    file("checker-a.yaml", { predation: { value: 1, sources: TWO, evidence: [ev(lit(1), 1, "again"), ev(db(1), 1)] } }),
    file("checker-b.yaml", { predation: { value: 1, sources: TWO } }),
  ];
  const f = tally(files).features.predation;
  assert.deepEqual(f.evidence, [ev(lit(1), 1), ev(db(1), 2), ev(db(1), 1)]);
});

test("evidence is present on open features and absent for waterbody decisions", () => {
  const entry = { source: lit(1), value: 1 };
  const open = tally([file("generator.yaml", { predation: { value: 1, sources: TWO, evidence: [entry] } }), file("checker-a.yaml", { predation: { value: 2, sources: TWO } })]);
  assert.deepEqual(open.features.predation.evidence, [entry]);
  assert.deepEqual(open.features.diet.evidence, []);
  const origin = { value: { continent: "Africa", region: "R", waterbody: "Lake Malawi" } };
  const lake = tally(NAMES.slice(0, 3).map((n) => file(n, { origin, ph: { value: { min: 7, max: 9 }, sources: TWO, evidence: [{ source: lit(1), value: { min: 7, max: 9 } }] } })), new Map([[LAKE.id, LAKE]]));
  assert.equal(lake.features.ph.evidence, undefined);
});

const rangeFile = (name, natural, tolerance) =>
  file(name, { ghDgh: { value: natural, sources: TWO, tolerance: { value: tolerance, sources: TWO, evidence: [{ source: lit(1), value: tolerance }] } } });

test("tolerance ranges are voted as their own entry", () => {
  const files = NAMES.slice(0, 3).map((n, i) => rangeFile(n, { min: 10, max: 20 }, { min: 8 + i, max: 24 }));
  const result = tally(files);
  const f = result.features["ghDgh.tolerance"];
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.decision, { min: 9, max: 24 });
  assert.deepEqual(f.evidence.length, 3);
  assert.equal(result.features.ghDgh.verification.method, "unanimous");
});

test("tolerance votes beyond the threshold ask for a tie-breaker and are listed as pending", () => {
  const files = NAMES.slice(0, 3).map((n, i) => rangeFile(n, { min: 10, max: 20 }, { min: 8 + 3 * i, max: 24 }));
  const result = tally(files);
  assert.equal(result.features["ghDgh.tolerance"].reason, "disagreement");
  assert.ok(result.pendingTiebreakers.includes("ghDgh.tolerance"));
});

test("khDkh tolerance agrees within 2", () => {
  const khFile = (n, min) => file(n, { khDkh: { value: { min: 6, max: 8 }, sources: TWO, tolerance: { value: { min, max: 12 }, sources: TWO } } });
  const f = tally([khFile(NAMES[0], 4), khFile(NAMES[1], 6), khFile(NAMES[2], 5)]).features["khDkh.tolerance"];
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.decision, { min: 5, max: 12 });
});

test("a median tolerance uses all minima and maxima and contains the natural range", () => {
  const spread = [{ min: 1, max: 40 }, { min: 4, max: 36 }, { min: 7, max: 32 }, { min: 10, max: 28 }, { min: 13, max: 24 }];
  const files = NAMES.map((n, i) => rangeFile(n, { min: 15, max: 20 }, spread[i]));
  let f = tally(files).features["ghDgh.tolerance"];
  assert.equal(f.verification.method, "median");
  assert.deepEqual(f.decision, { min: 7, max: 32 });
  assert.equal(f.confidence, "low");
  const narrow = NAMES.map((n, i) => rangeFile(n, { min: 5, max: 35 }, spread[i]));
  f = tally(narrow).features["ghDgh.tolerance"];
  assert.deepEqual(f.decision, { min: 5, max: 35 });
});

test("a median tolerance with disjoint votes is widened to contain the natural range", () => {
  const spread = [{ min: 3, max: 4 }, { min: 10, max: 12 }, { min: 20, max: 24 }, { min: 30, max: 32 }, { min: 40, max: 44 }];
  const files = NAMES.map((n, i) => rangeFile(n, { min: 18, max: 26 }, spread[i]));
  const f = tally(files).features["ghDgh.tolerance"];
  assert.equal(f.verification.method, "median");
  assert.deepEqual(f.decision, { min: 18, max: 26 });
});

test("median tolerances round both bounds with the water feature precision", () => {
  for (const name of ["temperatureC", "ph", "ghDgh", "khDkh"]) {
    const ph = name === "ph";
    const ranges = ph
      ? [5, 6, 7, 7.1, 8, 9].map((min) => ({ min, max: min + 1 }))
      : [1, 4, 7, 8, 11, 14].map((min) => ({ min, max: min + 10 }));
    const natural = ph ? { min: 7.2, max: 7.8 } : { min: 10, max: 12 };
    const files = [...NAMES, "tiebreaker-3.yaml"].map((n, i) => file(n, {
      [name]: { value: natural, sources: TWO, tolerance: { value: ranges[i], sources: TWO } },
    }));
    const f = tally(files).features[`${name}.tolerance`];
    assert.deepEqual(f.verification, { method: "median", votes: { agree: 2, total: 6 } }, name);
    assert.equal(f.confidence, "low", name);
    assert.deepEqual(f.decision, ph ? { min: 7.1, max: 8.1 } : { min: 8, max: 18 }, name);
  }
});

test("a decided tolerance of any method is widened to contain the natural range", () => {
  // Three files vote the tolerance unanimously; tie-breakers vote only the natural range, whose
  // median ends above the tolerance.
  const naturals = [{ min: 3, max: 10 }, { min: 3, max: 13 }, { min: 3, max: 16 }];
  const files = naturals.map((nat, i) => rangeFile(NAMES[i], nat, { min: 3, max: 15 }));
  files.push(file(NAMES[3], { ghDgh: [{ min: 4, max: 16 }, TWO] }), file(NAMES[4], { ghDgh: [{ min: 4, max: 24 }, TWO] }));
  const result = tally(files);
  assert.deepEqual(result.features.ghDgh.decision, { min: 3, max: 16 });
  const f = result.features["ghDgh.tolerance"];
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.decision, { min: 3, max: 16 });
});

test("lake species vote the tolerance while the table decides the natural value", () => {
  const origin = { value: { continent: "Africa", region: "R", waterbody: "Lake Malawi" } };
  const tol = { min: 2, max: 7 };
  const files = NAMES.slice(0, 3).map((n) => file(n, { origin, ghDgh: { value: { min: 3, max: 5 }, sources: TWO, tolerance: { value: tol, sources: TWO, evidence: [{ source: db(1), value: tol }] } } }));
  const result = tally(files, new Map([[LAKE.id, LAKE]]));
  assert.equal(result.features.ghDgh.verification.method, "waterbody");
  assert.equal(result.features["ghDgh.tolerance"].verification.method, "unanimous");
  assert.deepEqual(result.features["ghDgh.tolerance"].decision, tol);
  assert.equal(result.features["ph.tolerance"].reason, "no-votes");
});

test("fewer than three agreeing votes ask for a tie-breaker", () => {
  for (const values of [[1], [1, 1]]) {
    const f = decide("predation", values);
    assert.equal(f.needsTiebreaker, true);
    assert.equal(f.reason, "too-few-votes");
    assert.equal(f.decision, null);
  }
});

test("the confidence does not exceed the median confidence of the agreeing agents", () => {
  const files = ["low", "medium", "medium"].map((c, i) => ({
    name: NAMES[i],
    data: { features: { predation: { value: 1, confidence: c, sources: [lit(1), db(1), lit(2)] } } },
  }));
  const f = tally(files).features.predation;
  assert.equal(f.verification.method, "unanimous");
  assert.equal(f.confidence, "medium");
});


test("medium and low trust sources can settle unanimous and qualified votes", () => {
  for (const values of [[true, true, true], [true, true, true, true, false]]) {
    const sources = [db(1), forum(1)];
    const f = decide("speciesOnly", values, sources);
    assert.equal(f.decision, true);
    assert.equal(f.verification.method, values.length === 3 ? "unanimous" : "qualified-majority");
    assert.equal(f.confidence, "medium");
    assert.deepEqual(f.sources, sources);
  }
});

test("any number of low-trust sources alone cannot settle votes", () => {
  for (const values of [[1, 1, 1], [1, 1, 1, 1, 1]]) {
    const sources = [forum(1), forum(2), forum(3)];
    const f = decide("predation", values, sources);
    if (values.length === 3) assert.equal(f.reason, "insufficient-sources");
    else assert.equal(f.verification.method, "conservative");
    assert.deepEqual(f.sources, sources);
  }
});

test("three medium or low-trust sources suggest medium confidence", () => {
  assert.equal(decide("predation", [1, 1, 1], [db(1), db(2), forum(1)]).confidence, "medium");
  assert.equal(decide("predation", [1, 1, 1], [lit(1), forum(1), forum(2)]).confidence, "high");
});

test("dissenting sources stay cited but do not support agreement or confidence", () => {
  const files = voteFiles("predation", [1, 1, 1, 1, 2], [forum(1), forum(2)]);
  files[4].data.features.predation.sources = [lit(1)];
  let f = tally(files).features.predation;
  assert.equal(f.verification.method, "conservative");
  assert.deepEqual(f.sources, [forum(1), forum(2), lit(1)]);
  for (const entry of files.slice(0, 4)) entry.data.features.predation.sources = [db(1), forum(1), forum(2)];
  f = tally(files).features.predation;
  assert.equal(f.verification.method, "qualified-majority");
  assert.equal(f.confidence, "medium");
  assert.deepEqual(f.sources, [db(1), forum(1), forum(2), lit(1)]);
});

test("sources without URLs are distinct by title", () => {
  const sources = [{ kind: "literature", title: "Book A" }, { kind: "website", title: "Site B" }];
  const f = decide("predation", [1, 1, 1], [...sources, { ...sources[0] }]);
  assert.equal(f.verification.method, "unanimous");
  assert.deepEqual(f.sources, sources);
});

test("tolerance votes use trust for source support and confidence", () => {
  for (const [sources, method, confidence] of [
    [[db(1), forum(1)], "unanimous", "medium"],
    [[db(1), db(2), forum(1)], "unanimous", "medium"],
    [[lit(1), forum(1), forum(2)], "unanimous", "high"],
    [[forum(1), forum(2), forum(3)], undefined, undefined],
  ]) {
    const files = NAMES.slice(0, 3).map((name) => file(name, {
      ph: { value: { min: 7, max: 8 }, sources: TWO, tolerance: { value: { min: 6, max: 9 }, sources } },
    }));
    const f = tally(files).features["ph.tolerance"];
    assert.equal(f.verification?.method, method);
    assert.equal(f.confidence, confidence);
    if (!method) assert.equal(f.reason, "insufficient-sources");
    assert.deepEqual(f.sources, sources);
  }
});

test("the command refuses a tally.yaml or a work directory that is a symlink and writes nothing behind it", () => {
  const dir = realpathSync(mkdtempSync(join(tmpdir(), "tally-safe-")));
  const elsewhere = join(dir, "elsewhere");
  mkdirSync(elsewhere);
  const script = fileURLToPath(new URL("./tally.mjs", import.meta.url));
  const seed = (wd) => {
    mkdirSync(wd, { recursive: true });
    for (const name of ["generator", "checker-a", "checker-b"]) writeFileSync(join(wd, `${name}.yaml`), "id: x\n");
  };
  const tallyIn = (wd) => spawnSync(process.execPath, [script, wd], { encoding: "utf8" });
  const wd = join(dir, "work", "legacy-1");
  seed(wd);
  assert.equal(tallyIn(wd).status, 0);
  assert.ok(existsSync(join(wd, "tally.yaml")));
  rmSync(join(wd, "tally.yaml"));
  writeFileSync(join(elsewhere, "target.yaml"), "keep");
  symlinkSync(join(elsewhere, "target.yaml"), join(wd, "tally.yaml"));
  const linked = tallyIn(wd);
  assert.equal(linked.status, 1);
  assert.match(linked.stderr, /^tally: refusing to write through the symlink .*tally\.yaml/);
  assert.equal(readFileSync(join(elsewhere, "target.yaml"), "utf8"), "keep");
  seed(elsewhere);
  symlinkSync(elsewhere, join(dir, "work", "legacy-2"));
  assert.equal(tallyIn(join(dir, "work", "legacy-2")).status, 1);
  assert.ok(!existsSync(join(elsewhere, "tally.yaml")));
});
