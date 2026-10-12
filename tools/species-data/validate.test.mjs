// Tests for the species profile validator. They run on the built-in Node test runner with small
// inline fixture profiles.
import { test } from "node:test";
import assert from "node:assert/strict";

import { validateProfile, validateProfiles } from "./validate.mjs";

const SOURCE = { kind: "literature", title: "Example source", url: "https://example.org/a", accessed: "2026-01-02" };
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
      hybridGroup: hard("tropheus"),
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

test("a hard feature with one external source fails unless it is conservative", () => {
  for (const name of ["predation", "diet", "speciesOnly", "hybridGroup"]) {
    const profile = makeProfile();
    profile.features[name].sources = [SOURCE, { kind: "forum", title: "Forum thread", url: "https://example.org/c" }];
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/sources`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("two entries of the same source do not count twice", () => {
  const profile = makeProfile();
  profile.features.predation.sources = [SOURCE, { ...SOURCE }];
  assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === "/features/predation/sources"));
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
  profile.features.hybridGroup.value = "new-group";
  const issues = validateProfile(profile, options);
  assert.equal(errors(issues).length, 0, JSON.stringify(issues));
  assert.equal(warnings(issues).length, 1);
  assert.equal(warnings(issues)[0].pointer, "/features/hybridGroup/value");
});

test("a null hybrid group is accepted", () => {
  const profile = makeProfile();
  profile.features.hybridGroup.value = null;
  assert.deepEqual(validateProfile(profile, options), []);
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
  for (const name of ["predation", "diet", "speciesOnly", "hybridGroup", "temperatureC", "ph", "ghDgh"]) {
    const profile = makeProfile();
    delete profile.features[name].evidence;
    const found = errors(validateProfile(profile, options));
    assert.ok(found.some((i) => i.pointer === `/features/${name}/evidence`), `${name}: ${JSON.stringify(found)}`);
  }
});

test("evidence from one distinct source or from a forum source fails", () => {
  const profile = makeProfile();
  profile.features.predation.evidence = [
    { source: SOURCE, value: 0 },
    { source: { ...SOURCE }, value: 0, note: "same source again" },
  ];
  profile.features.ph.evidence = [
    { source: SOURCE, value: { min: 8, max: 9 } },
    { source: { kind: "forum", title: "Forum thread", url: "https://example.org/c" }, value: { min: 8, max: 9 } },
  ];
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/predation/evidence"), JSON.stringify(found));
  assert.ok(found.some((i) => i.pointer === "/features/ph/evidence"), JSON.stringify(found));
});

test("a waterbody verified water value needs no evidence", () => {
  const profile = lakeProfile();
  for (const name of ["temperatureC", "ph", "ghDgh"]) delete profile.features[name].evidence;
  assert.deepEqual(validateProfile(profile, options), []);
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

const WATER = ["temperatureC", "ph", "ghDgh", "khDkh"];

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

test("a tolerance needs two distinct external evidence sources", () => {
  const profile = makeProfile();
  profile.features.ph.tolerance.evidence = [{ source: SOURCE, value: { min: 7, max: 10 } }];
  profile.features.ghDgh.tolerance.evidence = [
    { source: SOURCE, value: { min: 9, max: 21 } },
    { source: { kind: "forum", title: "Forum", url: "https://example.org/c" }, value: { min: 9, max: 21 } },
  ];
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/ph/tolerance/evidence"), JSON.stringify(found));
  assert.ok(found.some((i) => i.pointer === "/features/ghDgh/tolerance/evidence"), JSON.stringify(found));
});

test("a conservative tolerance needs confidence low and one evidence entry of any kind", () => {
  const profile = makeProfile();
  Object.assign(profile.features.ph.tolerance, {
    verification: { method: "conservative", votes: { agree: 2, total: 5 } },
    evidence: [{ source: { kind: "forum", title: "Forum", url: "https://example.org/c" }, value: { min: 7, max: 10 } }],
  });
  const found = errors(validateProfile(profile, options));
  assert.deepEqual(found.map((i) => i.pointer), ["/features/ph/tolerance/confidence"]);
  profile.features.ph.tolerance.confidence = "low";
  assert.deepEqual(validateProfile(profile, options), []);
  profile.features.ph.tolerance.evidence = [];
  assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === "/features/ph/tolerance/evidence"));
});

test("a tolerance on a non-water feature fails", () => {
  const profile = makeProfile();
  profile.features.predation.tolerance = profile.features.ph.tolerance;
  assert.ok(errors(validateProfile(profile, options)).some((i) => i.pointer === "/features/predation/tolerance"));
});

test("a value resting only on archive or forum sources must have confidence low", () => {
  const profile = makeProfile();
  profile.features.feedingSpeed = fact("hasty", {
    sources: [{ kind: "archive", title: "cichlids.com forum thread 13697", url: "https://www.cichlids.com/disc/read.php?2,13697" }],
  });
  const found = errors(validateProfile(profile, options));
  assert.ok(found.some((i) => i.pointer === "/features/feedingSpeed/confidence"), JSON.stringify(found));
  profile.features.feedingSpeed.confidence = "low";
  assert.deepEqual(errors(validateProfile(profile, options)), []);
});
