// Tests for the species profile validator. They run on the built-in Node test runner with small
// inline fixture profiles.
import { test } from "node:test";
import assert from "node:assert/strict";

import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { hybridGroupIssues, validateFiles, validateProfile, validateProfiles } from "./validate.mjs";

const SOURCE = { kind: "literature", title: "Example source", url: "https://fishbase.se/a", accessed: "2026-01-02" };
const SOURCE_2 = { kind: "database", title: "Second source", url: "https://example.org/b", accessed: "2026-01-02" };

function fact(value, extra = {}) {
  return { value, confidence: "high", rationale: "Because.", sources: [SOURCE], ...extra };
}

function hard(value, extra = {}) {
  return fact(value, {
    sources: [SOURCE, SOURCE_2],
    evidence: [
      { source: SOURCE, value },
      { source: SOURCE_2, value },
    ],
    verification: { method: "unanimous", votes: { agree: 3, total: 3 } },
    ...extra,
  });
}

// Gives each water feature a tolerance one unit wider than its natural range on both sides.
function addTolerances(profile) {
  for (const name of ["temperatureC", "ph", "ghDgh", "khDkh"]) {
    const { min, max } = profile.features[name].value;
    const value = { min: min - 1, max: max + 1 };
    profile.features[name].tolerance = hard(value);
  }
  return profile;
}

const LAKE = {
  id: "lake-malawi",
  name: "Lake Malawi",
  aliases: ["Lake Nyasa"],
  temperatureC: { min: 24, max: 29 },
  ph: { min: 7.7, max: 8.6 },
  ghDgh: { min: 3, max: 5 },
  khDkh: { min: 6, max: 8 },
  rationale: "Measured values.",
  sources: [SOURCE],
};
const WATERBODIES = new Map([[LAKE.id, LAKE]]);

// A profile from Lake Malawi whose water values equal the table.
function lakeProfile() {
  const profile = makeProfile();
  profile.features.origin = fact({ continent: "Africa", region: "Lake Malawi", waterbody: "Lake Nyasa" });
  for (const name of ["temperatureC", "ph", "ghDgh"]) {
    profile.features[name] = hard({ ...LAKE[name] }, { verification: { method: "waterbody", waterbody: LAKE.id } });
  }
  profile.features.khDkh = fact({ ...LAKE.khDkh });
  return addTolerances(profile);
}

function makeProfile(overrides = {}) {
  return addTolerances({
    schemaVersion: 1,
    id: "tropheus-moorii",
    legacyIds: [1],
    status: "described",
    taxonomy: { validName: "Tropheus moorii", genus: "Tropheus", species: "moorii", source: SOURCE },
    features: {
      origin: fact({ continent: "Africa", region: "Lake Tanganyika" }),
      temperatureC: hard({ min: 24, max: 28 }),
      ph: hard({ min: 8, max: 9 }),
      ghDgh: hard({ min: 10, max: 20 }),
      khDkh: fact({ min: 8, max: 14 }),
      maxSizeCm: fact({ male: 14, female: 12 }),
      predation: hard(0),
      diet: hard({ type: "aufwuchs", bloatSensitive: true }),
      aggressionConspecific: fact(3),
      aggressionHeterospecific: fact(2),
      territory: fact({ size: "small", breedingOnly: false }),
      habitatZones: fact(["rock"]),
      social: fact({ structure: "group", minGroup: 10 }),
      speciesOnly: hard(false),
      hybridGroups: hard(["tropheus"]),
      coloration: fact({ baseColors: ["black"], pattern: "mixed" }),
      feedingSpeed: fact("normal"),
      minTank: fact({ volumeL: 400, lengthCm: 150, depthCm: 50 }),
      lifespanYears: fact({ min: 8, max: 12 }),
      breeding: fact("maternal-mouthbrooder"),
      beginnerSuitability: fact("demanding"),
    },
    provenance: { runs: [{ role: "generator", model: "m", date: "2026-01-02" }] },
    ...overrides,
  });
}

const GROUPS = new Set(["tropheus"]);
const options = { final: true, fileName: "tropheus-moorii.yaml", hybridGroupIds: GROUPS, waterbodies: WATERBODIES };

const errors = (issues) => issues.filter((i) => i.level === "error");
const warnings = (issues) => issues.filter((i) => i.level === "warning");

test("a valid minimal profile passes without issues", () => {
  assert.deepEqual(validateProfile(makeProfile(), options), []);
});

test("a fact without sources fails", () => {
  const profile = makeProfile();
  delete profile.features.khDkh.sources;
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/khDkh" && /sources/.test(i.message)), JSON.stringify(found));
});

test("a range with min greater than max fails", () => {
  const profile = makeProfile();
  profile.features.ph.value = { min: 9, max: 8 };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/value" && /min/.test(i.message)), JSON.stringify(found));
});

test("an implausible maximum size fails", () => {
  const profile = makeProfile();
  profile.features.maxSizeCm.value = { male: 200, female: 12 };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/maxSizeCm/value/male"), JSON.stringify(found));
});

test("a hard feature without verification fails once", () => {
  const profile = makeProfile();
  delete profile.features.predation.verification;
  const found = errors(validateProfile(profile, options));
  const hits = found.filter((i) => i.pointer === "/features/predation");
  assert.equal(hits.length, 1, JSON.stringify(found));
  assert.match(hits[0].message, /verification/);
});

test("a verification with an unknown method fails", () => {
  const profile = makeProfile();
  profile.features.predation.verification = { method: "approved" };
  assert.ok(errors(validateProfile(profile, options)).length > 0);
});

test("a vote based verification without votes fails", () => {
  const profile = makeProfile();
  profile.features.predation.verification = { method: "unanimous" };
  assert.ok(errors(validateProfile(profile, options)).some((i) => /votes/.test(i.message)));
});

test("a waterbody verification without a waterbody fails", () => {
  const profile = makeProfile();
  profile.features.ph.verification = { method: "waterbody" };
  assert.ok(errors(validateProfile(profile, options)).some((i) => /waterbody/.test(i.message)));
});

test("a verification with zero votes fails", () => {
  const profile = makeProfile();
  profile.features.predation.verification = { method: "unanimous", votes: { agree: 0, total: 3 } };
  assert.ok(errors(validateProfile(profile, options)).length > 0);
});

test("a lake profile with table values and waterbody verification passes", () => {
  assert.deepEqual(validateProfile(lakeProfile(), options), []);
});

test("a lake species verified by votes fails", () => {
  const profile = lakeProfile();
  profile.features.ph.verification = { method: "unanimous", votes: { agree: 3, total: 3 } };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/verification"), JSON.stringify(found));
});

test("a lake species verified against a different waterbody fails", () => {
  const profile = lakeProfile();
  profile.features.ghDgh.verification = { method: "waterbody", waterbody: "lake-other" };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ghDgh/verification"), JSON.stringify(found));
  assert.ok(found.some((i) => i.pointer === "/features/ghDgh/verification/waterbody"), JSON.stringify(found));
});

test("a lake species whose water value deviates from the table fails", () => {
  for (const name of ["temperatureC", "ph", "ghDgh", "khDkh"]) {
    const profile = lakeProfile();
    profile.features[name].value = { min: LAKE[name].min, max: LAKE[name].max + 1 };
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/value`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("a waterbody verification with an unknown id fails on a species without a table waterbody", () => {
  const profile = makeProfile();
  profile.features.ph.verification = { method: "waterbody", waterbody: "nowhere" };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/verification/waterbody"), JSON.stringify(found));
});

test("a waterbody verification whose values deviate from the table fails", () => {
  const profile = makeProfile();
  profile.features.ph.verification = { method: "waterbody", waterbody: LAKE.id };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/value"), JSON.stringify(found));
});

test("the waterbody rule is skipped without a table", () => {
  const profile = lakeProfile();
  profile.features.ph.value = { min: 1, max: 2 };
  profile.features.ph.tolerance.value = { min: 0, max: 3 };
  profile.features.ph.verification = { method: "unanimous", votes: { agree: 3, total: 3 } };
  assert.deepEqual(validateProfile(profile, { ...options, waterbodies: null }), []);
});

test("a species from a water body outside the table is not bound to it", () => {
  const profile = makeProfile();
  profile.features.origin = fact({ continent: "Africa", region: "Lake Tanganyika", waterbody: "Lake Tanganyika" });
  assert.deepEqual(validateProfile(profile, options), []);
});

test("hard features accept one source and one high-trust evidence entry", () => {
  for (const name of ["predation", "diet", "speciesOnly", "hybridGroups", "temperatureC", "ph", "ghDgh"]) {
    const profile = makeProfile();
    const feature = profile.features[name];
    feature.sources = [SOURCE];
    feature.evidence = [{ source: SOURCE, value: feature.value }];
    assert.deepEqual(errors(validateProfile(profile, options)), [], name);
  }
});

test("duplicate source citations do not invalidate a value", () => {
  const profile = makeProfile();
  profile.features.predation.sources = [SOURCE, { ...SOURCE }];
  assert.deepEqual(errors(validateProfile(profile, options)), []);
});

test("a conservative value with one source passes at confidence low", () => {
  const profile = makeProfile();
  Object.assign(profile.features.predation, {
    confidence: "low",
    sources: [SOURCE],
    verification: { method: "conservative", votes: { agree: 2, total: 5 } },
  });
  assert.deepEqual(validateProfile(profile, options), []);
});

test("a conservative value above confidence low fails", () => {
  const profile = makeProfile();
  Object.assign(profile.features.predation, {
    confidence: "medium",
    verification: { method: "conservative", votes: { agree: 2, total: 5 } },
  });
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/predation/confidence"), JSON.stringify(found));
});

test("the review property is rejected by the schema", () => {
  const profile = makeProfile();
  profile.features.predation.review = { status: "approved" };
  assert.ok(errors(validateProfile(profile, options)).length > 0);
});

test("a file name that differs from the id fails", () => {
  const found = errors(validateProfile(makeProfile(), { ...options, fileName: "other.yaml" }));
  assert.ok(found.some((i) => i.pointer === "/id"), JSON.stringify(found));
});

test("a duplicate legacyId across two profiles fails", () => {
  const a = makeProfile();
  const b = makeProfile({ id: "tropheus-duboisi", legacyIds: [1, 2] });
  const issues = validateProfiles(
    [
      { file: "tropheus-moorii.yaml", data: a },
      { file: "tropheus-duboisi.yaml", data: b },
    ],
    { final: true, hybridGroupIds: GROUPS },
  );
  const found = errors(issues).filter((i) => /legacyId/.test(i.message));
  assert.ok(found.length >= 1, JSON.stringify(issues));
  assert.ok(found.some((i) => i.file === "tropheus-duboisi.yaml"));
});

test("a duplicate id across two profiles fails", () => {
  const issues = validateProfiles(
    [
      { file: "a/tropheus-moorii.yaml", data: makeProfile() },
      { file: "b/tropheus-moorii.yaml", data: makeProfile({ legacyIds: [5] }) },
    ],
    { final: true, hybridGroupIds: GROUPS },
  );
  assert.ok(errors(issues).some((i) => /duplicate id/i.test(i.message)), JSON.stringify(issues));
});

test("an unknown hybrid group yields a warning only", () => {
  const profile = makeProfile();
  profile.features.hybridGroups.value = ["tropheus", "new-group"];
  const issues = validateProfile(profile, options);
  assert.equal(errors(issues).length, 0, JSON.stringify(issues));
  assert.equal(warnings(issues).length, 1);
  assert.equal(warnings(issues)[0].pointer, "/features/hybridGroups/value/1");
});

test("a list of several hybrid groups is accepted", () => {
  const profile = makeProfile();
  profile.features.hybridGroups.value = ["tropheus", "other"];
  const issues = validateProfile(profile, { ...options, hybridGroupIds: new Set(["tropheus", "other"]) });
  assert.deepEqual(issues, []);
});

test("an empty hybrid group list is accepted", () => {
  const profile = makeProfile();
  profile.features.hybridGroups.value = [];
  assert.deepEqual(validateProfile(profile, options), []);
});

test("duplicate hybrid group ids and a non-list value are rejected", () => {
  for (const value of [["tropheus", "tropheus"], "tropheus", null]) {
    const profile = makeProfile();
    profile.features.hybridGroups.value = value;
    assert.ok(errors(validateProfile(profile, options)).length > 0, JSON.stringify(value));
  }
});

test("a profile missing a group that names its species yields a warning", () => {
  const profile = makeProfile();
  const hybridGroups = [
    { id: "tropheus", members: ["Tropheus moorii", "Tropheus duboisi"] },
    { id: "tropheus-moorii-ikola", members: ["Tropheus moorii", "Tropheus sp. 'Ikola'"] },
  ];
  const found = warnings(validateProfile(profile, { ...options, hybridGroupIds: new Set(["tropheus", "tropheus-moorii-ikola"]), hybridGroups }));
  assert.equal(found.length, 1, JSON.stringify(found));
  assert.equal(found[0].pointer, "/features/hybridGroups/value");
  assert.match(found[0].message, /tropheus-moorii-ikola/);
});

test("a profile listing a group that does not name its species yields a warning", () => {
  const profile = makeProfile();
  const hybridGroups = [{ id: "tropheus", members: ["Tropheus duboisi", "Tropheus brichardi"] }];
  const found = warnings(validateProfile(profile, { ...options, hybridGroups }));
  assert.equal(found.length, 1, JSON.stringify(found));
  assert.equal(found[0].pointer, "/features/hybridGroups/value/0");
});

test("the feature hybridGroup is rejected by the schema", () => {
  const profile = makeProfile();
  profile.features.hybridGroup = profile.features.hybridGroups;
  delete profile.features.hybridGroups;
  const found = errors(validateProfile(profile, options));
  assert.ok(found.length > 0, JSON.stringify(found));
});

test("an archive source with legacy thread id and matching url passes", () => {
  const profile = makeProfile();
  profile.features.khDkh.sources.push({ kind: "archive", title: "cichlids.com forum thread 13697", url: "https://www.cichlids.com/disc/read.php?2,13697" });
  assert.deepEqual(errors(validateProfile(profile, options)), []);
});

test("an archive source without url or with a mismatching url fails", () => {
  const profile = makeProfile();
  profile.features.khDkh.sources.push({ kind: "archive", title: "cichlids.com forum thread 13697" });
  profile.features.ph.sources.push({ kind: "archive", title: "cichlids.com forum thread 13697", url: "https://www.cichlids.com/disc/read.php?2,99" });
  const found = errors(validateProfile(profile, options)).filter((i) => /archive source/.test(i.message));
  assert.deepEqual(found.map((i) => i.pointer).sort(), ["/features/khDkh/sources/1", "/features/ph/sources/2"]);
});

test("a hard feature without evidence fails", () => {
  for (const name of ["predation", "diet", "speciesOnly", "hybridGroups", "temperatureC", "ph", "ghDgh"]) {
    const profile = makeProfile();
    delete profile.features[name].evidence;
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/evidence`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("hard features with only low-trust evidence accept medium and low but reject high", () => {
  for (const name of ["predation", "diet", "speciesOnly", "hybridGroups", "temperatureC", "ph", "ghDgh"]) {
    for (const confidence of ["high", "medium", "low"]) {
      const profile = makeProfile();
      const feature = profile.features[name];
      // Kind does not override an unknown URL, even when sources lists a trusted source.
      feature.evidence = [{ source: SOURCE_2, value: feature.value }];
      feature.confidence = confidence;
      const found = errors(validateProfile(profile, options));
      assert.deepEqual(found.map((i) => i.pointer), confidence === "high" ? [`/features/${name}/confidence`] : [], name);
    }
  }
});

test("waterbody verified hard features need evidence", () => {
  const profile = lakeProfile();
  for (const name of ["temperatureC", "ph", "ghDgh"]) {
    delete profile.features[name].evidence;
  }
  const found = errors(validateProfile(profile, options));
  for (const name of ["temperatureC", "ph", "ghDgh"]) {
    assert.ok(found.some((i) => i.pointer === `/features/${name}/evidence`));
  }
});

test("a fact may carry evidence with a note, a malformed entry fails the schema", () => {
  const profile = makeProfile();
  profile.features.khDkh.evidence = [{ source: SOURCE, value: { min: 8, max: 14 }, note: "aquarium recommendation" }];
  assert.deepEqual(validateProfile(profile, options), []);
  profile.features.khDkh.evidence = [{ source: SOURCE }];
  assert.ok(errors(validateProfile(profile, options)).length > 0);
});

test("a conservative value needs one evidence entry of any kind", () => {
  const profile = makeProfile();
  profile.features.predation = hard(2, {
    confidence: "low",
    verification: { method: "conservative", votes: { agree: 2, total: 5 } },
    evidence: [{ source: { kind: "forum", title: "A forum post" }, value: 2 }],
  });
  assert.deepEqual(errors(validateProfile(profile, options)), []);
  profile.features.predation.evidence = [];
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/predation/evidence"), JSON.stringify(found));
});

const MEDIAN = { method: "median", votes: { agree: 2, total: 5 } };

test("a median water value passes at confidence low with one evidence entry of any kind", () => {
  for (const name of ["temperatureC", "ph", "ghDgh", "khDkh"]) {
    const profile = makeProfile();
    const value = profile.features[name].value;
    profile.features[name] = hard(value, {
      confidence: "low",
      verification: MEDIAN,
      evidence: [{ source: { kind: "forum", title: "A forum post" }, value }],
      tolerance: profile.features[name].tolerance,
    });
    assert.deepEqual(errors(validateProfile(profile, options)), [], name);
    profile.features[name].evidence = [];
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/evidence`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("a median water value above confidence low fails", () => {
  const profile = makeProfile();
  profile.features.ph.verification = MEDIAN;
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/confidence"), JSON.stringify(found));
});

test("method median is rejected on predation, diet, speciesOnly and hybridGroups", () => {
  for (const name of ["predation", "diet", "speciesOnly", "hybridGroups"]) {
    const profile = makeProfile();
    profile.features[name].confidence = "low";
    profile.features[name].verification = MEDIAN;
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/verification/method`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("method conservative is rejected on a tolerance", () => {
  const profile = makeProfile();
  Object.assign(profile.features.ph.tolerance, { confidence: "low", verification: { method: "conservative", votes: { agree: 2, total: 5 } } });
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/tolerance/verification/method"), JSON.stringify(found));
});

const WATER = ["temperatureC", "ph", "ghDgh", "khDkh"];

const ADJUDICATED = {
  method: "adjudicated",
  votes: { agree: 2, total: 5 },
  replaced: { min: 8, max: 10 },
  replacedMethod: "median",
};

test("an adjudicated water value or tolerance passes at confidence low or medium with one evidence entry of any kind", () => {
  for (const name of WATER) {
    for (const isTolerance of [false, true]) {
      for (const confidence of ["low", "medium"]) {
        const profile = makeProfile();
        const feature = isTolerance ? profile.features[name].tolerance : profile.features[name];
        Object.assign(feature, {
          confidence,
          sources: [SOURCE],
          verification: ADJUDICATED,
          evidence: [{ source: { kind: "forum", title: "A forum post" }, value: feature.value }],
        });
        assert.deepEqual(errors(validateProfile(profile, options)), [], `${name}, tolerance ${isTolerance}, ${confidence}`);
      }
    }
  }
});

test("an adjudicated water value or tolerance at confidence high fails", () => {
  for (const name of WATER) {
    for (const isTolerance of [false, true]) {
      const profile = makeProfile();
      const feature = isTolerance ? profile.features[name].tolerance : profile.features[name];
      feature.verification = ADJUDICATED;
      const at = `/features/${name}${isTolerance ? "/tolerance" : ""}`;
      const found = errors(validateProfile(profile, options));
      assert.ok(found.some((i) => i.pointer === `${at}/confidence` && /adjudicated/.test(i.message)), JSON.stringify(found));
    }
  }
});

test("an adjudicated water value or tolerance without evidence fails", () => {
  for (const name of WATER) {
    for (const isTolerance of [false, true]) {
      for (const evidence of [undefined, []]) {
        const profile = makeProfile();
        const feature = isTolerance ? profile.features[name].tolerance : profile.features[name];
        Object.assign(feature, { confidence: "low", verification: ADJUDICATED, evidence });
        const at = `/features/${name}${isTolerance ? "/tolerance" : ""}`;
        const found = errors(validateProfile(profile, options));
        assert.ok(found.some((i) => i.pointer === `${at}/evidence` && /adjudicated.*at least one/.test(i.message)), JSON.stringify(found));
      }
    }
  }
});

test("method adjudicated is rejected on every non-water feature and its tolerance", () => {
  const names = Object.keys(makeProfile().features).filter((name) => !WATER.includes(name));
  for (const name of names) {
    for (const isTolerance of [false, true]) {
      const profile = makeProfile();
      if (isTolerance) profile.features[name].tolerance = hard({ min: 0, max: 1 });
      const feature = isTolerance ? profile.features[name].tolerance : profile.features[name];
      Object.assign(feature, { confidence: "low", verification: ADJUDICATED });
      const at = `/features/${name}${isTolerance ? "/tolerance" : ""}`;
      const found = errors(validateProfile(profile, options));
      assert.ok(found.some((i) => i.pointer === `${at}/verification/method` && /adjudicated/.test(i.message)), `${name}: ${JSON.stringify(found)}`);
    }
  }
});

test("an adjudicated verification requires votes, replaced and replacedMethod", () => {
  for (const isTolerance of [false, true]) {
    for (const field of ["votes", "replaced", "replacedMethod"]) {
      const profile = makeProfile();
      const verification = { ...ADJUDICATED };
      delete verification[field];
      const feature = isTolerance ? profile.features.ph.tolerance : profile.features.ph;
      Object.assign(feature, { confidence: "low", verification });
      const at = `/features/ph${isTolerance ? "/tolerance" : ""}/verification`;
      const found = errors(validateProfile(profile));
      assert.ok(found.some((i) => i.pointer === at && i.message.includes(field)), JSON.stringify(found));
    }
  }
});

test("an adjudicated verification accepts each tally replacement method", () => {
  for (const replacedMethod of ["unanimous", "qualified-majority", "conservative", "median"]) {
    const profile = makeProfile();
    Object.assign(profile.features.ph, { confidence: "low", verification: { ...ADJUDICATED, replacedMethod } });
    assert.deepEqual(errors(validateProfile(profile, options)), [], replacedMethod);
  }
});

test("an adjudicated verification rejects replacement methods outside the tally methods", () => {
  for (const replacedMethod of ["waterbody", "adjudicated", "approved"]) {
    const profile = makeProfile();
    profile.features.ph.verification = { ...ADJUDICATED, replacedMethod };
    const found = errors(validateProfile(profile));
    assert.ok(found.some((i) => i.pointer === "/features/ph/verification/replacedMethod"), JSON.stringify(found));
  }
});

test("the replaced tally decision is a range with numeric min and max", () => {
  for (const replaced of [null, 8, { min: 8 }, { max: 10 }, { min: "8", max: 10 }, { min: 8, max: "10" }]) {
    const profile = makeProfile();
    profile.features.ph.verification = { ...ADJUDICATED, replaced };
    const found = errors(validateProfile(profile));
    assert.ok(found.some((i) => i.pointer.startsWith("/features/ph/verification/replaced")), JSON.stringify(found));
  }
});

test("an adjudicated tolerance still contains the natural range", () => {
  const profile = makeProfile();
  Object.assign(profile.features.ph.tolerance, { confidence: "low", verification: ADJUDICATED, value: { min: 8.2, max: 8.8 } });
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/tolerance/value" && /must contain/.test(i.message)), JSON.stringify(found));
});

test("a named lake keeps waterbody verification for temperatureC, ph and ghDgh when adjudication is attempted", () => {
  for (const name of ["temperatureC", "ph", "ghDgh"]) {
    const profile = lakeProfile();
    Object.assign(profile.features[name], { confidence: "low", verification: ADJUDICATED });
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/verification` && /requires verification method waterbody/.test(i.message)), JSON.stringify(found));
  }
});

test("a named lake accepts adjudicated tolerances and keeps its natural table ranges", () => {
  const profile = lakeProfile();
  for (const name of WATER) {
    Object.assign(profile.features[name].tolerance, { confidence: "medium", verification: ADJUDICATED, evidence: [{ source: SOURCE, value: profile.features[name].tolerance.value }] });
  }
  assert.deepEqual(errors(validateProfile(profile, options)), []);
});

test("a water feature without tolerance fails", () => {
  for (const name of WATER) {
    const profile = makeProfile();
    delete profile.features[name].tolerance;
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/tolerance`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("a tolerance that does not contain the natural range fails", () => {
  for (const name of WATER) {
    for (const value of [{ min: 100, max: 200 }, { min: -100, max: 0 }]) {
      const profile = makeProfile();
      profile.features[name].tolerance.value = value;
      const found = errors(validateProfile(profile, options));
      assert.ok(found.some((i) => i.pointer === `/features/${name}/tolerance/value`), `${name}: ${JSON.stringify(found)}`);
    }
  }
});

test("a tolerance equal to the natural range passes", () => {
  const profile = makeProfile();
  profile.features.ph.tolerance.value = { ...profile.features.ph.value };
  assert.deepEqual(validateProfile(profile, options), []);
});

test("a tolerance without verification or with method waterbody fails", () => {
  const profile = makeProfile();
  delete profile.features.ph.tolerance.verification;
  profile.features.ghDgh.tolerance.verification = { method: "waterbody", waterbody: LAKE.id };
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/tolerance"), JSON.stringify(found));
  assert.ok(found.some((i) => i.pointer === "/features/ghDgh/tolerance/verification/method"), JSON.stringify(found));
});

test("a lake species still needs a tolerance that is voted on", () => {
  const profile = lakeProfile();
  assert.deepEqual(validateProfile(profile, options), []);
  delete profile.features.ph.tolerance;
  assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === "/features/ph/tolerance"));
});

test("every tolerance accepts one evidence entry and requires high trust for confidence high", () => {
  for (const name of WATER) {
    for (const source of [SOURCE, SOURCE_2, { kind: "website", title: "Association", url: "https://ciklid.org/species" }]) {
      for (const confidence of ["high", "medium", "low"]) {
        const profile = makeProfile();
        const tolerance = profile.features[name].tolerance;
        Object.assign(tolerance, { confidence, sources: [source], evidence: [{ source, value: tolerance.value }] });
        const found = errors(validateProfile(profile, options));
        assert.deepEqual(found.map((i) => i.pointer), confidence === "high" && source !== SOURCE ? [`/features/${name}/tolerance/confidence`] : []);
      }
    }
    for (const evidence of [undefined, []]) {
      const profile = makeProfile();
      Object.assign(profile.features[name].tolerance, { confidence: "medium", evidence });
      assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === `/features/${name}/tolerance/evidence`));
    }
  }
});

test("every median tolerance accepts confidence low and one evidence entry of any kind", () => {
  for (const name of WATER) {
    const profile = makeProfile();
    const tolerance = profile.features[name].tolerance;
    Object.assign(tolerance, {
      confidence: "low",
      verification: MEDIAN,
      evidence: [{ source: { kind: "forum", title: "Forum" }, value: tolerance.value }],
    });
    assert.deepEqual(errors(validateProfile(profile, options)), [], name);
    tolerance.confidence = "medium";
    assert.deepEqual(errors(validateProfile(profile, options)).map((i) => i.pointer), [`/features/${name}/tolerance/confidence`], name);
    tolerance.confidence = "low";
    for (const evidence of [[], undefined]) {
      tolerance.evidence = evidence;
      assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === `/features/${name}/tolerance/evidence`), name);
    }
  }
});

test("a tolerance on a non-water feature fails", () => {
  const profile = makeProfile();
  profile.features.predation.tolerance = profile.features.ph.tolerance;
  assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === "/features/predation/tolerance"));
});

test("source kind does not cap the confidence of a soft feature", () => {
  const profile = makeProfile();
  profile.features.feedingSpeed = fact("hasty", {
    sources: [{ kind: "archive", title: "cichlids.com forum thread 13697", url: "https://www.cichlids.com/disc/read.php?2,13697" }],
  });
  assert.deepEqual(errors(validateProfile(profile, options)), []);
});

test("URL-free literature and archived high-trust URLs support high confidence", () => {
  for (const source of [
    { kind: "literature", title: "A monograph" },
    { kind: "website", title: "Archived study", url: "https://web.archive.org/web/20260102/https://fishbase.se/a" },
  ]) {
    const profile = makeProfile();
    profile.features.predation.evidence = [{ source, value: 0 }];
    assert.deepEqual(errors(validateProfile(profile, options)), []);
  }
});

function group(extra = {}) {
  return {
    id: "tropheus",
    description: "All Tropheus species.",
    members: ["Tropheus moorii", "Tropheus duboisi"],
    sources: [SOURCE],
    basis: "documented",
    userNote: "Wild hybrids of these species are reported.",
    ...extra,
  };
}

test("a well-formed hybrid group file has no issues", () => {
  assert.deepEqual(hybridGroupIssues({ groups: [group(), group({ id: "aulonocara", basis: "presumed" })] }, "g.yaml"), []);
});

test("a hybrid group without basis is an error", () => {
  const broken = group();
  delete broken.basis;
  const found = errors(hybridGroupIssues({ groups: [broken] }, "g.yaml"));
  assert.equal(found.length, 1, JSON.stringify(found));
  assert.equal(found[0].file, "g.yaml");
  assert.equal(found[0].pointer, "/groups/0/basis");
});

test("a hybrid group with an unknown basis is an error", () => {
  const found = errors(hybridGroupIssues({ groups: [group({ basis: "likely" })] }, "g.yaml"));
  assert.equal(found.length, 1, JSON.stringify(found));
  assert.equal(found[0].pointer, "/groups/0/basis");
});

test("a duplicate hybrid group id is an error", () => {
  const found = errors(hybridGroupIssues({ groups: [group(), group()] }, "g.yaml"));
  assert.ok(found.some((i) => i.pointer === "/groups/1/id" && /duplicate/.test(i.message)), JSON.stringify(found));
});

test("a hybrid group without a user note is an error", () => {
  const broken = group();
  delete broken.userNote;
  const found = errors(hybridGroupIssues({ groups: [broken] }, "g.yaml"));
  assert.equal(found.length, 1, JSON.stringify(found));
  assert.equal(found[0].file, "g.yaml");
  assert.equal(found[0].pointer, "/groups/0/userNote");
});

test("a hybrid group with an empty or non-string user note is an error", () => {
  for (const userNote of ["", "   ", 5, null]) {
    const found = errors(hybridGroupIssues({ groups: [group({ userNote })] }, "g.yaml"));
    assert.equal(found.length, 1, JSON.stringify({ userNote, found }));
    assert.equal(found[0].pointer, "/groups/0/userNote");
  }
});

test("a hybrid group with one member is an error", () => {
  const found = errors(hybridGroupIssues({ groups: [group({ members: ["Tropheus moorii"] })] }, "g.yaml"));
  assert.ok(found.some((i) => i.pointer === "/groups/0/members"), JSON.stringify(found));
});

test("a hybrid group without description or sources is an error", () => {
  const found = errors(hybridGroupIssues({ groups: [group({ description: "", sources: [] })] }, "g.yaml"));
  assert.deepEqual(found.map((i) => i.pointer).sort(), ["/groups/0/description", "/groups/0/sources"]);
});

test("a broken hybrid group file fails validateFiles", () => {
  const dir = mkdtempSync(join(tmpdir(), "hybrid-groups-"));
  try {
    const path = join(dir, "hybrid-groups.yaml");
    writeFileSync(path, "groups:\n  - id: x\n    description: d\n    members: [A a, B b]\n    userNote: n\n    sources: [{kind: literature, title: t}]\n");
    const found = errors(validateFiles([], { hybridGroupsPath: path }));
    assert.ok(found.some((i) => i.pointer === "/groups/0/basis" && i.file.endsWith("hybrid-groups.yaml")), JSON.stringify(found));
  } finally {
    rmSync(dir, { recursive: true });
  }
});

test('Sources accept archived references and offline flags; malformed references fail', () => {
  const p = makeProfile();
  p.taxonomy.source = { ...SOURCE, archived: 'fishbase.se/a/2026-10-14T093000Z', offline: true };
  assert.equal(validateProfile(p, { archiveRoot: '/nonexistent-cichlids-test-root' }).length, 0);
  for (const archived of ['../bad', 'fishbase.se/a/2026-99-99T000000Z', '/fishbase.se/a/2026-10-14T093000Z']) {
    p.taxonomy.source.archived = archived;
    assert.ok(validateProfile(p).some(i => i.level === 'error' && i.pointer.includes('archived')), archived);
  }
});
test('Missing archive snapshots warn only when the archive root exists', () => {
  const root = mkdtempSync(join(tmpdir(), 'cichlids-validation-archive-'));
  try {
    const p = makeProfile();
    p.taxonomy.source = { ...SOURCE, archived: 'fishbase.se/a/2026-10-14T093000Z' };
    assert.ok(validateProfile(p, { archiveRoot: root }).some(i => i.level === 'warning' && i.pointer === '/taxonomy/source/archived'));
    assert.equal(validateProfile(p, { archiveRoot: join(root, 'absent') }).length, 0);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('An existing snapshot directory satisfies reference validation', () => {
  const root = mkdtempSync(join(tmpdir(), 'cichlids-validation-archive-'));
  try {
    const p = makeProfile();
    p.taxonomy.source = { ...SOURCE, archived: 'fishbase.se/a/2026-10-14T093000Z', offline: false };
    mkdirSync(join(root, p.taxonomy.source.archived), { recursive: true });
    assert.equal(validateProfile(p, { archiveRoot: root }).length, 0);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('The file validator checks archive references in shared waterbody and hybrid-group sources', () => {
  const root = mkdtempSync(join(tmpdir(), 'cichlids-validation-tables-'));
  try {
    const waterbodiesPath = join(root, 'waterbodies.yaml');
    const hybridGroupsPath = join(root, 'hybrid-groups.yaml');
    writeFileSync(waterbodiesPath, 'waterbodies:\n  - sources:\n      - kind: literature\n        title: Lake measurements\n        archived: ../invalid\n');
    writeFileSync(hybridGroupsPath, 'groups:\n  - id: group\n    description: Crosses\n    members: [A, B]\n    basis: documented\n    userNote: Observed crosses.\n    sources:\n      - kind: literature\n        title: Crosses\n        archived: example.org/crosses/2026-10-14T093000Z\n');
    const issues = validateFiles([], { waterbodiesPath, hybridGroupsPath, archiveRoot: root });
    assert.ok(issues.some(i => i.level === 'error' && i.pointer === '/waterbodies/0/sources/0/archived'));
    assert.ok(issues.some(i => i.level === 'warning' && i.pointer === '/groups/0/sources/0/archived'));
  } finally { rmSync(root, { recursive: true, force: true }); }
});
