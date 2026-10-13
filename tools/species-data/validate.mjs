#!/usr/bin/env node
// Validates species profile files against data/species/schema/species-profile.schema.json and
// applies the cross-field checks that JSON Schema cannot express.
//
// Usage: node tools/species-data/validate.mjs [paths...]
// Without paths, every data/species/profiles/*.yaml is validated. Files below a directory named
// "profiles" are final profiles and get the additional checks; other files (pipeline
// intermediates under data/species/work) are checked against the schema only.
//
// Issues have the shape { level: "error" | "warning", file, pointer, message }. Errors fail the
// run (exit code 1), warnings are reported only.
import { readFileSync, readdirSync, existsSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { parse } from "yaml";
import { findWaterbody, loadWaterbodies } from "./waterbodies.mjs";

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const SPECIES_DIR = join(REPO_ROOT, "data", "species");
const SCHEMA_PATH = join(SPECIES_DIR, "schema", "species-profile.schema.json");
const PROFILES_DIR = join(SPECIES_DIR, "profiles");
const HYBRID_GROUPS_PATH = join(SPECIES_DIR, "hybrid-groups.yaml");
const WATERBODIES_PATH = join(SPECIES_DIR, "waterbodies.yaml");

// Features whose values lead to hard compatibility verdicts; each carries a verification object
// that records how the value was verified automatically.
export const HARD_FEATURES = ["temperatureC", "ph", "ghDgh", "predation", "diet", "speciesOnly", "hybridGroups"];
// Water values that a lake table decides; the first three are hard features, khDkh is a plain fact.
const WATERBODY_VERIFIED_FEATURES = ["temperatureC", "ph", "ghDgh"];
const WATERBODY_VALUE_FEATURES = [...WATERBODY_VERIFIED_FEATURES, "khDkh"];
// Hard features outside the water values need independent sources unless the value is conservative.
const SOURCE_RULE_FEATURES = ["predation", "diet", "speciesOnly", "hybridGroups"];
const EXTERNAL_SOURCE_KINDS = ["catalog-of-fishes", "literature", "database"];
const MIN_EXTERNAL_SOURCES = 2;
// Water features carry a tolerance next to the natural-habitat value.
const TOLERANCE_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh"];
// Natural water values may be decided by the median of the votes; no other fact may be.
const MEDIAN_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh"];
const TOLERANCE_METHODS = ["unanimous", "qualified-majority", "conservative"];
const RANGE_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh", "lifespanYears"];
const MAX_PLAUSIBLE_SIZE_CM = 150;

let compiled;
function schemaValidator() {
  if (!compiled) {
    const ajv = new Ajv2020({ allErrors: true, strictTypes: false });
    addFormats(ajv);
    compiled = ajv.compile(JSON.parse(readFileSync(SCHEMA_PATH, "utf8")));
  }
  return compiled;
}

export function error(pointer, message, file) {
  return { level: "error", file, pointer, message };
}

export function warning(pointer, message, file) {
  return { level: "warning", file, pointer, message };
}

function schemaIssues(profile, file) {
  const validate = schemaValidator();
  if (validate(profile)) return [];
  return validate.errors.map((e) => {
    const extra = e.keyword === "additionalProperties" ? ` (${e.params.additionalProperty})` : "";
    return error(e.instancePath, `${e.message}${extra}`, file);
  });
}

const isNumber = (x) => typeof x === "number" && Number.isFinite(x);
const isObject = (x) => x !== null && typeof x === "object" && !Array.isArray(x);

// Checks that need the final-profile context. They tolerate malformed data, since the schema
// check reports structural problems separately.
function sameRange(a, b) {
  return isObject(a) && isObject(b) && a.min === b.min && a.max === b.max;
}

function externalSourceCount(sources) {
  if (!Array.isArray(sources)) return 0;
  const keys = new Set();
  for (const s of sources) {
    if (isObject(s) && EXTERNAL_SOURCE_KINDS.includes(s.kind)) keys.add(s.url ?? s.title);
  }
  return keys.size;
}

function externalEvidenceCount(evidence) {
  if (!Array.isArray(evidence)) return 0;
  return externalSourceCount(evidence.map((e) => (isObject(e) ? e.source : undefined)));
}

// A median value exists because the votes did not settle; like a conservative value it has
// confidence low and needs at least one evidence entry of any kind.
function medianIssues(features, file) {
  const issues = [];
  for (const [name, feature] of Object.entries(features)) {
    if (!isObject(feature) || feature.verification?.method !== "median") continue;
    if (!MEDIAN_FEATURES.includes(name)) {
      issues.push(error(`/features/${name}/verification/method`, `method median applies only to ${MEDIAN_FEATURES.join(", ")}`, file));
      continue;
    }
    if (feature.confidence !== "low") {
      issues.push(error(`/features/${name}/confidence`, `median value of "${name}" must have confidence low`, file));
    }
    if (!Array.isArray(feature.evidence) || feature.evidence.length === 0) {
      issues.push(error(`/features/${name}/evidence`, `median value of "${name}" needs at least one evidence entry`, file));
    }
  }
  return issues;
}

function evidenceIssues(features, file) {
  const issues = [];
  for (const name of [...SOURCE_RULE_FEATURES, ...WATERBODY_VERIFIED_FEATURES]) {
    const feature = features[name];
    if (!isObject(feature)) continue;
    if (WATERBODY_VERIFIED_FEATURES.includes(name) && feature.verification?.method === "waterbody") continue;
    // The evidence of a median value is checked in medianIssues.
    if (feature.verification?.method === "median") continue;
    // A conservative value exists because the sources are too few or disagree; its evidence shows
    // what the sources state, so it needs at least one entry of any kind.
    if (feature.verification?.method === "conservative") {
      if (!Array.isArray(feature.evidence) || feature.evidence.length === 0) {
        issues.push(error(`/features/${name}/evidence`, `conservative value of "${name}" needs at least one evidence entry`, file));
      }
      continue;
    }
    if (externalEvidenceCount(feature.evidence) < MIN_EXTERNAL_SOURCES) {
      issues.push(
        error(`/features/${name}/evidence`, `"${name}" needs evidence entries from at least ${MIN_EXTERNAL_SOURCES} distinct sources of kind ${EXTERNAL_SOURCE_KINDS.join(", ")}`, file),
      );
    }
  }
  return issues;
}

function waterbodyIssues(features, waterbodies, file) {
  const issues = [];
  const origin = features.origin?.value;
  const named = isObject(origin) && typeof origin.waterbody === "string" ? findWaterbody(waterbodies, origin.waterbody) : null;

  if (named) {
    for (const name of WATERBODY_VERIFIED_FEATURES) {
      const verification = features[name]?.verification;
      if (isObject(features[name]) && !(isObject(verification) && verification.method === "waterbody" && verification.waterbody === named.id)) {
        issues.push(error(`/features/${name}/verification`, `origin waterbody "${named.id}" requires verification method waterbody with waterbody "${named.id}"`, file));
      }
    }
    for (const name of WATERBODY_VALUE_FEATURES) {
      const feature = features[name];
      if (isObject(feature) && !sameRange(feature.value, named[name])) {
        issues.push(error(`/features/${name}/value`, `value must equal the range of waterbody "${named.id}" (${named[name].min} to ${named[name].max})`, file));
      }
    }
  }

  for (const name of HARD_FEATURES) {
    const verification = features[name]?.verification;
    if (!isObject(verification) || verification.method !== "waterbody") continue;
    const entry = typeof verification.waterbody === "string" ? waterbodies.get(verification.waterbody) : undefined;
    if (!entry) {
      issues.push(error(`/features/${name}/verification/waterbody`, `unknown waterbody "${verification.waterbody}"`, file));
    } else if (!WATERBODY_VERIFIED_FEATURES.includes(name)) {
      issues.push(error(`/features/${name}/verification/method`, `method waterbody applies only to ${WATERBODY_VERIFIED_FEATURES.join(", ")}`, file));
    } else if (!(named && named.id === entry.id) && !sameRange(features[name].value, entry[name])) {
      issues.push(error(`/features/${name}/value`, `value must equal the range of waterbody "${entry.id}" (${entry[name].min} to ${entry[name].max})`, file));
    }
  }
  return issues;
}

// Archive sources cite the legacy thread id, which is stable across imports, and the legacy forum
// URL, which the platform redirects to the thread.
const ARCHIVE_TITLE = /^cichlids\.com forum thread (\d+)$/;
const ARCHIVE_URL = /^https:\/\/www\.cichlids\.com\/disc\/read\.php\?[123],(\d+)$/;

export function* archiveSources(node, pointer = "") {
  if (Array.isArray(node)) {
    for (let i = 0; i < node.length; i++) yield* archiveSources(node[i], `${pointer}/${i}`);
  } else if (isObject(node)) {
    if (node.kind === "archive" && "title" in node) {
      yield { pointer, source: node };
      return;
    }
    for (const [key, value] of Object.entries(node)) yield* archiveSources(value, `${pointer}/${key}`);
  }
}

/** Errors for archive sources below node that lack the thread title or the matching legacy url. */
export function archiveSourceIssues(node, file, prefix = "") {
  const issues = [];
  for (const { pointer, source } of archiveSources(node)) {
    const match = ARCHIVE_TITLE.exec(source.title ?? "");
    const urlMatch = ARCHIVE_URL.exec(source.url ?? "");
    if (!match || !urlMatch || urlMatch[1] !== match[1]) {
      issues.push(error(prefix + pointer, "archive source needs the title \"cichlids.com forum thread <legacy id>\" and the matching legacy forum url", file));
    }
  }
  return issues;
}

function toleranceIssues(features, file) {
  const issues = [];
  for (const name of Object.keys(features)) {
    if (isObject(features[name]) && "tolerance" in features[name] && !TOLERANCE_FEATURES.includes(name)) {
      issues.push(error(`/features/${name}/tolerance`, `only ${TOLERANCE_FEATURES.join(", ")} carry a tolerance`, file));
    }
  }
  for (const name of TOLERANCE_FEATURES) {
    const feature = features[name];
    if (!isObject(feature)) continue;
    const at = `/features/${name}/tolerance`;
    const tolerance = feature.tolerance;
    if (!isObject(tolerance)) {
      issues.push(error(at, `water feature "${name}" has no tolerance`, file));
      continue;
    }
    const value = tolerance.value;
    const natural = feature.value;
    if (isObject(value) && isNumber(value.min) && isNumber(value.max)) {
      if (value.min > value.max) {
        issues.push(error(`${at}/value`, `min (${value.min}) is greater than max (${value.max})`, file));
      } else if (isObject(natural) && isNumber(natural.min) && isNumber(natural.max) && !(value.min <= natural.min && value.max >= natural.max)) {
        issues.push(error(`${at}/value`, `tolerance (${value.min} to ${value.max}) must contain the natural range (${natural.min} to ${natural.max})`, file));
      }
    }
    const method = tolerance.verification?.method;
    if (!isObject(tolerance.verification)) {
      issues.push(error(at, `tolerance of "${name}" has no verification object`, file));
    } else if (!TOLERANCE_METHODS.includes(method)) {
      issues.push(error(`${at}/verification/method`, `tolerance verification method must be one of ${TOLERANCE_METHODS.join(", ")}`, file));
    }
    if (method === "conservative") {
      if (tolerance.confidence !== "low") {
        issues.push(error(`${at}/confidence`, `conservative tolerance of "${name}" must have confidence low`, file));
      }
      if (!Array.isArray(tolerance.evidence) || tolerance.evidence.length === 0) {
        issues.push(error(`${at}/evidence`, `conservative tolerance of "${name}" needs at least one evidence entry`, file));
      }
    } else if (externalEvidenceCount(tolerance.evidence) < MIN_EXTERNAL_SOURCES) {
      issues.push(
        error(`${at}/evidence`, `tolerance of "${name}" needs evidence entries from at least ${MIN_EXTERNAL_SOURCES} distinct sources of kind ${EXTERNAL_SOURCE_KINDS.join(", ")}`, file),
      );
    }
  }
  return issues;
}

function semanticIssues(profile, { fileName, hybridGroupIds, waterbodies }, file) {
  const issues = [];
  if (fileName !== undefined && typeof profile.id === "string" && fileName !== `${profile.id}.yaml`) {
    issues.push(error("/id", `file name "${fileName}" must be "${profile.id}.yaml"`, file));
  }
  const features = isObject(profile.features) ? profile.features : {};

  for (const name of RANGE_FEATURES) {
    const value = features[name]?.value;
    if (isObject(value) && isNumber(value.min) && isNumber(value.max) && value.min > value.max) {
      issues.push(error(`/features/${name}/value`, `min (${value.min}) is greater than max (${value.max})`, file));
    }
  }

  const size = features.maxSizeCm?.value;
  if (isObject(size)) {
    for (const sex of ["male", "female"]) {
      const x = size[sex];
      if (isNumber(x) && !(x > 0 && x <= MAX_PLAUSIBLE_SIZE_CM)) {
        issues.push(error(`/features/maxSizeCm/value/${sex}`, `${x} cm is not plausible (expected > 0 and <= ${MAX_PLAUSIBLE_SIZE_CM})`, file));
      }
    }
  }

  issues.push(...archiveSourceIssues(profile, file));

  for (const name of HARD_FEATURES) {
    const feature = features[name];
    if (isObject(feature) && !isObject(feature.verification)) {
      issues.push(error(`/features/${name}`, `hard feature "${name}" has no verification object`, file));
    }
  }

  // Forum, archive and model knowledge carry very low weight: a value resting on them alone has
  // confidence low.
  for (const [name, feature] of Object.entries(features)) {
    for (const [pointer, fact] of [[`/features/${name}`, feature], [`/features/${name}/tolerance`, feature?.tolerance]]) {
      if (isObject(fact) && Array.isArray(fact.sources) && externalSourceCount(fact.sources) === 0 && fact.confidence !== "low") {
        issues.push(error(`${pointer}/confidence`, "a value without a source of kind catalog-of-fishes, literature or database must have confidence low", file));
      }
    }
  }

  for (const name of SOURCE_RULE_FEATURES) {
    const feature = features[name];
    if (!isObject(feature) || !isObject(feature.verification)) continue;
    if (feature.verification.method === "conservative") {
      if (feature.confidence !== "low") {
        issues.push(error(`/features/${name}/confidence`, `conservative value of "${name}" must have confidence low`, file));
      }
    } else if (externalSourceCount(feature.sources) < MIN_EXTERNAL_SOURCES) {
      issues.push(
        error(`/features/${name}/sources`, `"${name}" needs at least ${MIN_EXTERNAL_SOURCES} sources of kind ${EXTERNAL_SOURCE_KINDS.join(", ")} unless the verification method is conservative`, file),
      );
    }
  }

  issues.push(...evidenceIssues(features, file));
  issues.push(...medianIssues(features, file));
  issues.push(...toleranceIssues(features, file));
  if (waterbodies) issues.push(...waterbodyIssues(features, waterbodies, file));

  const groups = features.hybridGroups?.value;
  if (Array.isArray(groups) && hybridGroupIds) {
    groups.forEach((group, i) => {
      if (typeof group === "string" && !hybridGroupIds.has(group)) {
        issues.push(warning(`/features/hybridGroups/value/${i}`, `hybrid group "${group}" is not listed in hybrid-groups.yaml`, file));
      }
    });
  }
  return issues;
}

/**
 * Validates one profile object. With options.final, the final-profile checks run as well.
 * Options: final (boolean), fileName (base name, checked against the id), hybridGroupIds (Set),
 * waterbodies (Map from loadWaterbodies, or null to skip the waterbody rule), file (label put
 * into the issues).
 */
export function validateProfile(profile, options = {}) {
  const file = options.file;
  const issues = schemaIssues(profile, file);
  if (options.final && isObject(profile)) {
    issues.push(...semanticIssues(profile, options, file));
  }
  return issues;
}

/**
 * Validates a set of { file, data } entries, including uniqueness of id and legacyIds across
 * entries when options.final is set. The base name of file is checked against the id.
 */
export function validateProfiles(entries, options = {}) {
  const issues = [];
  const ids = new Map();
  const legacyIds = new Map();
  for (const { file, data } of entries) {
    issues.push(...validateProfile(data, { ...options, file, fileName: options.final ? basename(file) : undefined }));
    if (!options.final || !isObject(data)) continue;
    if (typeof data.id === "string") {
      if (ids.has(data.id)) {
        issues.push(error("/id", `duplicate id "${data.id}" (also in ${ids.get(data.id)})`, file));
      } else {
        ids.set(data.id, file);
      }
    }
    if (Array.isArray(data.legacyIds)) {
      for (const legacyId of data.legacyIds) {
        if (legacyIds.has(legacyId)) {
          issues.push(error("/legacyIds", `duplicate legacyId ${legacyId} (also in ${legacyIds.get(legacyId)})`, file));
        } else {
          legacyIds.set(legacyId, file);
        }
      }
    }
  }
  return issues;
}

export function loadHybridGroupIds(path = HYBRID_GROUPS_PATH) {
  if (!existsSync(path)) return new Set();
  const doc = parse(readFileSync(path, "utf8"));
  return new Set((doc?.groups ?? []).map((g) => g.id));
}

function isFinalProfilePath(path) {
  return basename(dirname(resolve(path))) === "profiles";
}

/**
 * Reads and validates YAML files; final and intermediate files may be mixed. The hybrid group ids
 * and the waterbody table can be injected (waterbodies: Map or null) or read from the given
 * paths. A missing table file skips the waterbody rule with a warning.
 */
export function validateFiles(
  paths,
  { hybridGroupIds = loadHybridGroupIds(), waterbodiesPath = WATERBODIES_PATH, waterbodies } = {},
) {
  const issues = [];
  const finals = [];
  const others = [];
  for (const path of paths) {
    const file = relative(process.cwd(), resolve(path)) || path;
    let data;
    try {
      data = parse(readFileSync(path, "utf8"));
    } catch (e) {
      issues.push(error("", `cannot read or parse YAML: ${e.message}`, file));
      continue;
    }
    (isFinalProfilePath(path) ? finals : others).push({ file, data });
  }
  if (waterbodies === undefined && finals.length > 0) {
    const tableFile = relative(process.cwd(), waterbodiesPath) || waterbodiesPath;
    waterbodies = null;
    if (existsSync(waterbodiesPath)) {
      try {
        waterbodies = loadWaterbodies(waterbodiesPath);
      } catch (e) {
        issues.push(error("", e.message, tableFile));
      }
    } else {
      issues.push(warning("", "waterbody table not found, waterbody rule skipped", tableFile));
    }
  }
  issues.push(...validateProfiles(finals, { final: true, hybridGroupIds, waterbodies }));
  issues.push(...validateProfiles(others, { final: false, hybridGroupIds, waterbodies }));
  return issues;
}

export function formatReport(issues, fileCount) {
  const lines = issues.map((i) => `${i.level === "error" ? "ERROR  " : "WARNING"} ${i.file ?? "-"} ${i.pointer || "/"}: ${i.message}`);
  const errorCount = issues.filter((i) => i.level === "error").length;
  lines.push(`${fileCount} file(s), ${errorCount} error(s), ${issues.length - errorCount} warning(s)`);
  return lines.join("\n");
}

function main() {
  let paths = process.argv.slice(2);
  if (paths.length === 0) {
    paths = existsSync(PROFILES_DIR)
      ? readdirSync(PROFILES_DIR).filter((f) => f.endsWith(".yaml")).sort().map((f) => join(PROFILES_DIR, f))
      : [];
  }
  const issues = validateFiles(paths);
  console.log(formatReport(issues, paths.length));
  process.exit(issues.some((i) => i.level === "error") ? 1 : 0);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
