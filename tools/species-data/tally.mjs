#!/usr/bin/env node
// Counts the votes of the agents of one species work directory and decides the hard features
// that the votes settle. Features that stay open are listed for tie-breaker agents.
//
// Usage: node tools/species-data/tally.mjs <workdir>
// Reads generator.yaml, checker-a.yaml, checker-b.yaml, the optional checker-c.yaml and every
// tiebreaker-N.yaml in the work directory and writes tally.yaml next to them. Exit code 2 when a
// required input file is missing.
//
// Every file that contains a hard feature casts one vote. Water values of a species from a lake
// of data/species/waterbodies.yaml are taken from that table and are not voted on.
// The tolerance range of each water feature is voted on as its own entry named
// "<feature>.tolerance", also for lake species.
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parse, stringify } from "yaml";
import { findWaterbody, loadWaterbodies } from "./waterbodies.mjs";
import { safeWriteFile } from "./safe-write.mjs";
import { loadSourceTrust, trustOf } from "./source-trust.mjs";

const SOURCE_TRUST = loadSourceTrust();

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const WATERBODIES_PATH = join(REPO_ROOT, "data", "species", "waterbodies.yaml");

const WATERBODY_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh"];
// khDkh is voted as well, so that a decided tolerance always contains the natural carbonate hardness.
const VOTED_FEATURES = ["predation", "diet", "speciesOnly", "hybridGroups", "temperatureC", "ph", "ghDgh", "khDkh"];
// Agreement thresholds and rounding of the water features; their tolerance entries use the same.
const RANGE_TOLERANCE = { temperatureC: 1, ph: 0.3, ghDgh: 2, khDkh: 2 };
const RANGE_DECIMALS = { temperatureC: 0, ph: 1, ghDgh: 0, khDkh: 0 };
const TOLERANCE_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh"];
// Diet types from strictest to least strict requirement on tank mates and food.
const DIET_STRICTNESS = ["aufwuchs", "piscivore", "invertivore", "omnivore"];
const MIN_SOURCES = 2;
// A unanimous decision needs at least this many independent votes.
const UNANIMOUS_MIN_VOTES = 3;
const QUALIFIED_TOTAL = 5;
const QUALIFIED_AGREE = 4;
const EPSILON = 1e-9;

const baseName = (name) => name.replace(/\.tolerance$/, "");
const isRange = (name) => baseName(name) in RANGE_TOLERANCE;

/** Position of a file in the vote order: generator, checker-a, checker-b, checker-c, tiebreaker-1, ... */
function rank(name) {
  if (name === "generator.yaml") return 0;
  if (name === "checker-a.yaml") return 1;
  if (name === "checker-b.yaml") return 2;
  if (name === "checker-c.yaml") return 3;
  const m = /^tiebreaker-(\d+)\.yaml$/.exec(name);
  return m ? 3 + Number(m[1]) : Number.MAX_SAFE_INTEGER;
}

function agrees(name, a, b) {
  if (isRange(name)) {
    const tolerance = RANGE_TOLERANCE[baseName(name)] + EPSILON;
    return Math.abs(a.min - b.min) <= tolerance && Math.abs(a.max - b.max) <= tolerance;
  }
  if (name === "diet") return a.type === b.type && a.bloatSensitive === b.bloatSensitive;
  if (name === "hybridGroups") return sameSet(a, b);
  return (a ?? null) === (b ?? null);
}

function sameSet(a, b) {
  const left = new Set(a);
  const right = new Set(b);
  return left.size === right.size && [...left].every((v) => right.has(v));
}

function median(numbers) {
  const sorted = [...numbers].sort((x, y) => x - y);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

function round(x, decimals) {
  const factor = 10 ** decimals;
  return Math.round(x * factor) / factor;
}

function medianRange(name, values) {
  const decimals = RANGE_DECIMALS[baseName(name)];
  return {
    min: round(median(values.map((v) => v.min)), decimals),
    max: round(median(values.map((v) => v.max)), decimals),
  };
}

function collectSources(votes) {
  const found = new Map();
  for (const vote of votes) {
    for (const source of vote.sources) {
      if (!source) continue;
      const key = source.url ?? source.title;
      if (!found.has(key)) found.set(key, source);
    }
  }
  return [...found.values()];
}

// Evidence entries of the given votes, once per source (url, else title) and JSON-equal value.
function collectEvidence(votes) {
  const found = new Map();
  for (const vote of votes) {
    for (const entry of vote.evidence) {
      if (!entry?.source) continue;
      const key = JSON.stringify([entry.source.url ?? entry.source.title, entry.value]);
      if (!found.has(key)) found.set(key, entry);
    }
  }
  return [...found.values()];
}

function mostCommon(values) {
  const counts = new Map();
  for (const v of values) counts.set(v, (counts.get(v) ?? 0) + 1);
  let best;
  let bestCount = 0;
  for (const [v, n] of counts) {
    if (n > bestCount) {
      best = v;
      bestCount = n;
    }
  }
  return best;
}

// Every group named by at least two votes; when no group reaches two votes, the group named first.
function conservativeGroups(values) {
  const counts = new Map();
  for (const list of values) for (const group of new Set(list)) counts.set(group, (counts.get(group) ?? 0) + 1);
  const repeated = [...counts].filter(([, n]) => n >= 2).map(([group]) => group);
  if (repeated.length > 0) return repeated.sort();
  return counts.size > 0 ? [mostCommon(values.flat())] : [];
}

function conservativeValue(name, votes) {
  const values = votes.map((v) => v.value);
  switch (name) {
    case "predation":
      return Math.max(...values);
    case "speciesOnly":
      return values.some((v) => v === true);
    case "hybridGroups":
      return conservativeGroups(values);
    case "diet":
      return {
        type: DIET_STRICTNESS.find((t) => values.some((v) => v.type === t)) ?? values[0].type,
        bloatSensitive: values.some((v) => v.bloatSensitive === true),
      };
    default:
      throw new Error(`no conservative rule for ${name}`);
  }
}

const CONFIDENCE_ORDER = ["low", "medium", "high"];

function medianConfidence(values) {
  const ranks = values.map((c) => CONFIDENCE_ORDER.indexOf(c)).filter((r) => r >= 0).sort((a, b) => a - b);
  return ranks.length === 0 ? null : CONFIDENCE_ORDER[ranks[Math.floor((ranks.length - 1) / 2)]];
}

function lowerConfidence(a, b) {
  if (b === null) return a;
  return CONFIDENCE_ORDER[Math.min(CONFIDENCE_ORDER.indexOf(a), CONFIDENCE_ORDER.indexOf(b))];
}

function decideVoted(name, votes) {
  const proposals = votes.map((v) => ({ file: v.file, value: v.value }));
  if (votes.length === 0) {
    return { decision: null, verification: null, needsTiebreaker: true, reason: "no-votes", proposals, sources: [], evidence: [] };
  }

  let top = null;
  for (const candidate of votes) {
    const agreeing = votes.filter((v) => agrees(name, candidate.value, v.value));
    if (!top || agreeing.length > top.agreeing.length) top = { candidate, agreeing };
  }
  const total = votes.length;
  const agree = top.agreeing.length;
  const agreeingSources = collectSources(top.agreeing);
  const sources = collectSources(votes);
  const enoughSources = agreeingSources.length >= MIN_SOURCES &&
    agreeingSources.some((source) => ["medium", "high"].includes(trustOf(source, SOURCE_TRUST)));
  const evidence = collectEvidence(votes);
  let decidedValue = top.candidate.value;
  if (isRange(name)) decidedValue = medianRange(name, top.agreeing.map((v) => v.value));
  else if (name === "hybridGroups") decidedValue = [...new Set(decidedValue)].sort();
  const decided = (method, confidence) => ({
    decision: decidedValue,
    verification: { method, votes: { agree, total } },
    confidence,
    proposals,
    sources,
    evidence,
  });
  // Confidence combines source count and trust among agreeing votes with the median confidence
  // the agreeing agents gave themselves.
  const sourceStrength = agreeingSources.length >= 3 &&
    agreeingSources.some((source) => trustOf(source, SOURCE_TRUST) === "high") ? "high" : "medium";
  const strength = lowerConfidence(sourceStrength, medianConfidence(top.agreeing.map((v) => v.confidence)));

  if (total < QUALIFIED_TOTAL) {
    if (total >= UNANIMOUS_MIN_VOTES && agree === total && enoughSources) {
      return decided("unanimous", strength);
    }
    return {
      decision: null,
      verification: null,
      needsTiebreaker: true,
      reason: agree < total ? "disagreement" : total < UNANIMOUS_MIN_VOTES ? "too-few-votes" : "insufficient-sources",
      proposals,
      sources,
      evidence,
    };
  }
  if (agree >= QUALIFIED_AGREE && enoughSources) return decided("qualified-majority", strength);
  // Natural ranges and tolerances without a qualified majority use the median range.
  // For tolerances, an intersection lets the narrowest proposal set the limit and produces water
  // conflicts absent from aquarium practice; the median reflects what the sources recommend.
  if (isRange(name)) {
    return {
      decision: medianRange(name, votes.map((v) => v.value)),
      verification: { method: "median", votes: { agree, total } },
      confidence: "low",
      proposals,
      sources,
      evidence,
    };
  }
  return {
    decision: conservativeValue(name, votes),
    verification: { method: "conservative", votes: { agree, total } },
    confidence: "low",
    proposals,
    sources,
    evidence,
  };
}

function decideFromTable(name, entry, files) {
  return {
    decision: { min: entry[name].min, max: entry[name].max },
    verification: { method: "waterbody", waterbody: entry.id },
    confidence: "high",
    proposals: files.filter((f) => f.data?.features?.[name]?.value !== undefined).map((f) => ({ file: f.name, value: f.data.features[name].value })),
    sources: entry.sources,
  };
}

function chooseWaterbody(files, waterbodies) {
  const keys = [];
  for (const f of files) {
    const named = f.data?.features?.origin?.value?.waterbody;
    if (typeof named !== "string" || named.trim() === "") continue;
    keys.push(findWaterbody(waterbodies, named)?.id ?? named.trim().toLowerCase());
  }
  const key = mostCommon(keys);
  return key !== undefined && waterbodies.has(key) ? waterbodies.get(key) : null;
}

// Work files that predate the list value carry hybridGroup as one id or null.
function legacyHybridGroups(feature) {
  if (!feature || feature.value === undefined) return undefined;
  return { ...feature, value: typeof feature.value === "string" ? [feature.value] : [] };
}

/**
 * Tallies the votes. files: [{ name, data }] with the parsed content of generator.yaml,
 * checker-a.yaml, checker-b.yaml, checker-c.yaml and tiebreaker-N.yaml (any order); waterbodies: Map from
 * loadWaterbodies. Returns the content of tally.yaml.
 */
export function tally(files, waterbodies = new Map()) {
  const ordered = [...files].sort((a, b) => rank(a.name) - rank(b.name));
  const entry = chooseWaterbody(ordered, waterbodies);
  const features = {};

  if (entry) for (const name of WATERBODY_FEATURES) features[name] = decideFromTable(name, entry, ordered);

  const castVotes = (name, pick) => {
    const votes = [];
    for (const f of ordered) {
      const feature = pick(f.data?.features?.[name] ?? (name === "hybridGroups" ? legacyHybridGroups(f.data?.features?.hybridGroup) : undefined));
      if (feature && feature.value !== undefined) {
        votes.push({
          file: f.name,
          value: feature.value,
          sources: Array.isArray(feature.sources) ? feature.sources : [],
          evidence: Array.isArray(feature.evidence) ? feature.evidence : [],
          confidence: feature.confidence,
        });
      }
    }
    return votes;
  };

  for (const name of VOTED_FEATURES) {
    if (features[name]) continue;
    features[name] = decideVoted(name, castVotes(name, (feature) => feature));
  }
  // The tolerance is voted for every water feature, also where a water body decides the natural value.
  for (const name of TOLERANCE_FEATURES) {
    const natural = features[name]?.decision ?? undefined;
    const tolerance = decideVoted(`${name}.tolerance`, castVotes(name, (feature) => feature?.tolerance));
    // A tolerance contains the natural range, whatever method decided either of them.
    if (tolerance.decision && natural) {
      tolerance.decision = {
        min: Math.min(tolerance.decision.min, natural.min),
        max: Math.max(tolerance.decision.max, natural.max),
      };
    }
    features[`${name}.tolerance`] = tolerance;
  }

  const result = {
    files: ordered.map((f) => f.name),
    waterbody: entry ? entry.id : null,
    features,
    pendingTiebreakers: Object.keys(features).filter((n) => features[n].needsTiebreaker),
  };
  // A JSON round trip detaches shared objects, so the YAML output carries no anchors.
  return JSON.parse(JSON.stringify(result));
}

function readWorkdir(dir) {
  const names = ["generator.yaml", "checker-a.yaml", "checker-b.yaml"];
  const missing = names.filter((n) => !existsSync(join(dir, n)));
  if (missing.length > 0) throw new Error(`missing input file(s) in ${dir}: ${missing.join(", ")}`);
  const optional = existsSync(join(dir, "checker-c.yaml")) ? ["checker-c.yaml"] : [];
  const tiebreakers = readdirSync(dir).filter((n) => /^tiebreaker-\d+\.yaml$/.test(n));
  return [...names, ...optional, ...tiebreakers].map((name) => ({
    name,
    data: parse(readFileSync(join(dir, name), "utf8"), { maxAliasCount: -1 }),
  }));
}

function summary(result) {
  const lines = Object.entries(result.features).map(([name, f]) => {
    if (f.needsTiebreaker) return `${name}: open (${f.reason})`;
    const v = f.verification;
    const how = v.method === "waterbody" ? `waterbody ${v.waterbody}` : `${v.method} ${v.votes.agree}/${v.votes.total}`;
    return `${name}: ${JSON.stringify(f.decision)} (${how}, confidence ${f.confidence})`;
  });
  lines.push(result.pendingTiebreakers.length > 0 ? `pending tie-breakers: ${result.pendingTiebreakers.join(", ")}` : "no tie-breaker needed");
  return lines.join("\n");
}

function main() {
  const dir = process.argv[2];
  if (!dir) {
    console.error("Usage: node tools/species-data/tally.mjs <workdir>");
    process.exit(2);
  }
  let files;
  let waterbodies = new Map();
  try {
    files = readWorkdir(resolve(dir));
    if (existsSync(WATERBODIES_PATH)) waterbodies = loadWaterbodies(WATERBODIES_PATH);
    else console.error(`waterbody table ${WATERBODIES_PATH} not found, all water values are voted on`);
  } catch (e) {
    console.error(e.message);
    process.exit(2);
  }
  const result = tally(files, waterbodies);
  // The work directory lies in a tree that agents write; its parent is the trusted base.
  try {
    safeWriteFile(join(resolve(dir), "tally.yaml"), stringify(result, { lineWidth: 100 }), dirname(resolve(dir)));
  } catch (e) {
    console.error(`tally: ${e.message}`);
    process.exit(1);
  }
  console.log(summary(result));
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
