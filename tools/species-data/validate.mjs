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
import { readFileSync, readdirSync, existsSync, statSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { parse } from "yaml";
import { findWaterbody, loadWaterbodies } from "./waterbodies.mjs";
import { archiveRoot, validArchiveRef } from "./source-archive.mjs";
import { loadSourceTrust, trustOf } from "./source-trust.mjs";

const SOURCE_TRUST = loadSourceTrust();

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
// Hard features outside the water values carry conservative confidence checks.
const CONSERVATIVE_FEATURES = ["predation", "diet", "speciesOnly", "hybridGroups"];
// Water features carry a tolerance next to the natural-habitat value.
const TOLERANCE_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh"];
// Natural water values and their tolerances may be decided by the median of the votes.
const MEDIAN_FEATURES = ["temperatureC", "ph", "ghDgh", "khDkh"];
const TOLERANCE_METHODS = ["unanimous", "qualified-majority", "median", "adjudicated"];
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

// An adjudicated water range replaces an implausible tally decision; its evidence supports the
// replacement, with confidence low or medium.
function adjudicatedIssues(features, file) {
  const issues = [];
  for (const [name, feature] of Object.entries(features)) {
    for (const [pointer, fact] of [[`/features/${name}`, feature], [`/features/${name}/tolerance`, feature?.tolerance]]) {
      if (!isObject(fact) || fact.verification?.method !== "adjudicated") continue;
      if (!TOLERANCE_FEATURES.includes(name)) {
        issues.push(error(`${pointer}/verification/method`, `method adjudicated applies only to ${TOLERANCE_FEATURES.join(", ")} and their tolerances`, file));
        continue;
      }
      if (!["low", "medium"].includes(fact.confidence)) {
        issues.push(error(`${pointer}/confidence`, `adjudicated range of "${name}" must have confidence low or medium`, file));
      }
      if (!Array.isArray(fact.evidence) || fact.evidence.length === 0) {
        issues.push(error(`${pointer}/evidence`, `adjudicated range of "${name}" needs at least one evidence entry`, file));
      }
    }
  }
  return issues;
}

function evidenceIssues(features, file) {
  const issues = [];
  for (const name of new Set([...HARD_FEATURES, ...TOLERANCE_FEATURES])) {
    const feature = features[name];
    const facts = [];
    if (HARD_FEATURES.includes(name)) facts.push([`/features/${name}`, feature]);
    if (TOLERANCE_FEATURES.includes(name)) facts.push([`/features/${name}/tolerance`, feature?.tolerance]);
    for (const [pointer, fact] of facts) {
      if (!isObject(fact)) continue;
      const method = fact.verification?.method;
      // Median and adjudicated ranges have their own evidence checks.
      const checkedElsewhere = ["median", "adjudicated"].includes(method);
      if (!checkedElsewhere && (!Array.isArray(fact.evidence) || fact.evidence.length === 0)) {
        const label = method === "conservative" ? "conservative value of " : "";
        issues.push(error(`${pointer}/evidence`, `${label}"${name}" needs at least one evidence entry`, file));
      }
      if (fact.confidence === "high" &&
          !(Array.isArray(fact.evidence) && fact.evidence.some((entry) => trustOf(entry?.source, SOURCE_TRUST) === "high"))) {
        issues.push(error(`${pointer}/confidence`, "confidence high needs at least one high-trust evidence source", file));
      }
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
    // Adjudicated tolerances have their evidence and confidence checked in adjudicatedIssues.
    if (method === "adjudicated") continue;
    if (method === "median") {
      if (tolerance.confidence !== "low") {
        issues.push(error(`${at}/confidence`, `median tolerance of "${name}" must have confidence low`, file));
      }
      if (!Array.isArray(tolerance.evidence) || tolerance.evidence.length === 0) {
        issues.push(error(`${at}/evidence`, `median tolerance of "${name}" needs at least one evidence entry`, file));
      }
    }
  }
  return issues;
}

function semanticIssues(profile, { fileName, hybridGroupIds, hybridGroups, waterbodies }, file) {
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

  for (const name of CONSERVATIVE_FEATURES) {
    const feature = features[name];
    if (!isObject(feature) || !isObject(feature.verification)) continue;
    if (feature.verification.method === "conservative") {
      if (feature.confidence !== "low") {
        issues.push(error(`/features/${name}/confidence`, `conservative value of "${name}" must have confidence low`, file));
      }
    }
  }

  issues.push(...evidenceIssues(features, file));
  issues.push(...medianIssues(features, file));
  issues.push(...adjudicatedIssues(features, file));
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
  if (Array.isArray(groups) && Array.isArray(hybridGroups)) {
    issues.push(...groupMembershipIssues(profile.taxonomy?.validName, groups, hybridGroups, file));
  }
  return issues;
}

// A group and the profiles of its members name each other: a profile lists every group whose
// members include its valid name and only such groups.
function groupMembershipIssues(validName, groups, hybridGroups, file) {
  if (typeof validName !== "string") return [];
  const issues = [];
  const byId = new Map(hybridGroups.filter(isObject).map((g) => [g.id, g]));
  groups.forEach((id, i) => {
    const members = byId.get(id)?.members;
    if (Array.isArray(members) && !members.includes(validName)) {
      issues.push(warning(`/features/hybridGroups/value/${i}`, `hybrid group "${id}" does not list ${validName} as a member`, file));
    }
  });
  const missing = hybridGroups
    .filter((g) => isObject(g) && Array.isArray(g.members) && g.members.includes(validName) && !groups.includes(g.id))
    .map((g) => g.id);
  if (missing.length > 0) {
    issues.push(warning("/features/hybridGroups/value", `hybrid groups naming ${validName} are missing: ${missing.join(", ")}`, file));
  }
  return issues;
}

/** Checks local snapshot references in source entries, including evidence and taxonomy. */
export function snapshotSourceIssues(node, file, root = archiveRoot(), pointer = "") {
  const issues = [];
  if (Array.isArray(node)) {
    node.forEach((value, i) => issues.push(...snapshotSourceIssues(value, file, root, `${pointer}/${i}`)));
  } else if (isObject(node)) {
    if ("kind" in node && "archived" in node) {
      const at = `${pointer}/archived`;
      if (!validArchiveRef(node.archived)) issues.push(error(at, "invalid source archive reference", file));
      else if (existsSync(root) && (!existsSync(join(root, node.archived)) || !statSync(join(root, node.archived)).isDirectory())) {
        issues.push(warning(at, `source archive snapshot not found: ${node.archived}`, file));
      }
    }
    for (const [key, value] of Object.entries(node)) issues.push(...snapshotSourceIssues(value, file, root, `${pointer}/${key.replace(/~/g, "~0").replace(/\//g, "~1")}`));
  }
  return issues;
}

/**
 * Validates one profile object. With options.final, the final-profile checks run as well.
 * Options: final (boolean), fileName (base name, checked against the id), hybridGroupIds (Set),
 * hybridGroups (the parsed group list, for the membership check in both directions), waterbodies (Map from loadWaterbodies, or null to skip the waterbody rule), file (label put
 * into the issues).
 */
export function validateProfile(profile, options = {}) {
  const file = options.file;
  const issues = schemaIssues(profile, file);
  issues.push(...snapshotSourceIssues(profile, file, options.archiveRoot));
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

const HYBRID_GROUP_BASES = ["documented", "presumed"];

/**
 * Errors for a parsed hybrid group file: every group has a unique id, a description, at least two
 * member names, at least one source, a basis of documented or presumed and a non-empty user note.
 */
export function hybridGroupIssues(doc, file) {
  if (!isObject(doc) || !Array.isArray(doc.groups)) {
    return [error("/groups", "the hybrid group file needs a list \"groups\"", file)];
  }
  const issues = [];
  const ids = new Set();
  doc.groups.forEach((group, i) => {
    const at = `/groups/${i}`;
    if (!isObject(group)) {
      issues.push(error(at, "a hybrid group must be an object", file));
      return;
    }
    if (typeof group.id !== "string" || group.id.trim() === "") {
      issues.push(error(`${at}/id`, "a hybrid group needs an id", file));
    } else if (ids.has(group.id)) {
      issues.push(error(`${at}/id`, `duplicate hybrid group id "${group.id}"`, file));
    } else {
      ids.add(group.id);
    }
    if (typeof group.description !== "string" || group.description.trim() === "") {
      issues.push(error(`${at}/description`, "a hybrid group needs a description", file));
    }
    if (typeof group.userNote !== "string" || group.userNote.trim() === "") {
      issues.push(error(`${at}/userNote`, "a hybrid group needs a userNote", file));
    }
    const members = Array.isArray(group.members) ? group.members : [];
    if (members.length < 2 || !members.every((m) => typeof m === "string" && m.trim() !== "")) {
      issues.push(error(`${at}/members`, "a hybrid group needs at least two members, each a valid name string", file));
    }
    if (!Array.isArray(group.sources) || group.sources.length === 0) {
      issues.push(error(`${at}/sources`, "a hybrid group needs at least one source", file));
    }
    if (!HYBRID_GROUP_BASES.includes(group.basis)) {
      issues.push(error(`${at}/basis`, `a hybrid group needs a basis, one of ${HYBRID_GROUP_BASES.join(", ")}`, file));
    }
  });
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
 * Reads and validates YAML files; final and intermediate files may be mixed. The hybrid group file
 * is validated as well. The hybrid group ids and the waterbody table can be injected
 * (waterbodies: Map or null) or read from the given paths. A missing table file skips the
 * waterbody rule with a warning.
 */
export function validateFiles(
  paths,
  { hybridGroupIds, hybridGroups, hybridGroupsPath = HYBRID_GROUPS_PATH, waterbodiesPath = WATERBODIES_PATH, waterbodies, archiveRoot: sourceRoot = archiveRoot() } = {},
) {
  const issues = [];
  if (existsSync(hybridGroupsPath)) {
    const groupsFile = relative(process.cwd(), hybridGroupsPath) || hybridGroupsPath;
    try {
      const doc = parse(readFileSync(hybridGroupsPath, "utf8"));
      issues.push(...hybridGroupIssues(doc, groupsFile));
      issues.push(...snapshotSourceIssues(doc, groupsFile, sourceRoot));
      hybridGroupIds ??= new Set((Array.isArray(doc?.groups) ? doc.groups : []).map((g) => g?.id));
      hybridGroups ??= Array.isArray(doc?.groups) ? doc.groups : undefined;
    } catch (e) {
      issues.push(error("", `cannot read or parse YAML: ${e.message}`, groupsFile));
    }
  }
  if (existsSync(waterbodiesPath)) {
    const tableFile = relative(process.cwd(), waterbodiesPath) || waterbodiesPath;
    try {
      issues.push(...snapshotSourceIssues(parse(readFileSync(waterbodiesPath, "utf8")), tableFile, sourceRoot));
    } catch (e) {
      issues.push(error("", `cannot read or parse YAML: ${e.message}`, tableFile));
    }
  }
  hybridGroupIds ??= new Set();
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
  issues.push(...validateProfiles(finals, { final: true, hybridGroupIds, hybridGroups, waterbodies, archiveRoot: sourceRoot }));
  issues.push(...validateProfiles(others, { final: false, hybridGroupIds, waterbodies, archiveRoot: sourceRoot }));
  return issues;
}

// Directory checks apply to the full catalog independently of the selected profile paths.
export function directoryIssues({directory, forms, mappings, legacy, profiles}) {
  const issues=[];
  const files={directory:'data/species/directory.yaml',forms:'data/species/trade-forms.yaml',mappings:'data/species/legacy-mapping.yaml',legacy:'data/species/legacy-catalog.json'};
  const lists={directory:directory?.species,forms:forms?.forms,mappings:mappings?.mappings,legacy};
  for (const [key,list] of Object.entries(lists)) {
    if (!Array.isArray(list)) issues.push(error('',`${key} needs an array (${key==='directory' ? 'species' : key})`,files[key]));
  }
  if (issues.length) return issues;
  function idIndex(entries,file) {
    const index=new Map();
    entries.forEach((entry,i)=>{
      const id=entry?.id;
      if (typeof id!=='string' || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(id)) issues.push(error(`/${i}/id`,'id must be well formed kebab-case',file));
      if (index.has(id)) issues.push(error(`/${i}/id`,`duplicate id ${id}`,file));
      index.set(id,entry);
    });
    return index;
  }
  const species=idIndex(lists.directory,files.directory);
  const trade=idIndex(lists.forms,files.forms);
  for (const id of trade.keys()) if (species.has(id)) issues.push(error('/forms',`id ${id} occurs in directory and trade forms`,files.forms));
  const names=new Map(lists.directory.map(entry=>[entry?.name,entry]));
  const legacyIds=new Set(lists.legacy.map(row=>row?.legacyId));
  const index=new Map();
  const kinds=['profile','exact','synonym','correction','trade-form','hybrid','domestic-form','open'];
  let open=0;
  lists.mappings.forEach((row,i)=>{
    const at=`/mappings/${i}`;
    if (!legacyIds.has(row?.legacyId)) issues.push(error(`${at}/legacyId`,`unknown legacyId ${row?.legacyId}`,files.mappings));
    if (!index.has(row?.legacyId)) index.set(row?.legacyId,[]);
    index.get(row?.legacyId).push(row);
    if (!kinds.includes(row?.match)) issues.push(error(`${at}/match`,`match must be one of ${kinds.join(', ')}`,files.mappings));
    if (row?.match==='open') {
      open++;
      if (row.target!==null) issues.push(error(`${at}/target`,'open mapping target must be null',files.mappings));
    } else if (!species.has(row?.target) && !trade.has(row?.target)) {
      issues.push(error(`${at}/target`,`target ${row?.target} does not exist`,files.mappings));
    }
    if (row?.match==='correction' && (typeof row.note!=='string' || !row.note.trim())) issues.push(error(`${at}/note`,'correction needs a reason in note',files.mappings));
  });
  for (const id of legacyIds) if (index.get(id)?.length!==1) issues.push(error('/mappings',`legacyId ${id} must appear exactly once`,files.mappings));
  if (open) issues.push(warning('/mappings',`${open} open legacy entries`,files.mappings));
  for (const {data:profile,file} of profiles) {
    const entry=names.get(profile?.taxonomy?.validName);
    if (profile?.status==='described' && !entry) issues.push(warning('/taxonomy/validName',`profile ${profile.id}: ${profile.taxonomy?.validName} is absent from directory`,file));
    if (profile?.status!=='described' && !trade.has(profile?.id)) issues.push(error('/id',`profile ${profile?.id} needs a trade-forms entry`,file));
    const targets=new Set([profile?.id, ...(entry ? [entry.id] : [])]);
    if (profile?.legacyIds !== undefined && !Array.isArray(profile.legacyIds)) issues.push(error('/legacyIds','legacyIds must be an array',file));
    for (const id of Array.isArray(profile?.legacyIds) ? profile.legacyIds : []) {
      const rows=index.get(id) ?? [];
      if (rows.length!==1 || rows[0]?.match==='open' || !targets.has(rows[0]?.target)) issues.push(error('/legacyIds',`legacyId ${id} contradicts profile ${profile.id}, target must be ${[...targets].join(' or ')}`,file));
    }
  }
  return issues;
}

export function validateDirectoryFiles(speciesDir=SPECIES_DIR) {
  const issues=[];
  function read(name,json=false) {
    const path=join(speciesDir,name);
    try { return json ? JSON.parse(readFileSync(path,'utf8')) : parse(readFileSync(path,'utf8')); }
    catch (e) { issues.push(error('',`cannot read or parse directory data: ${e.message}`,path)); return null; }
  }
  const directory=read('directory.yaml');
  const forms=read('trade-forms.yaml');
  const mappings=read('legacy-mapping.yaml');
  const legacy=read('legacy-catalog.json',true);
  const profiles=[];
  const profileDir=join(speciesDir,'profiles');
  if (existsSync(profileDir)) {
    for (const name of readdirSync(profileDir).filter(x=>x.endsWith('.yaml')).sort()) {
      const data=read(`profiles/${name}`);
      if (data) profiles.push({data,file:join(profileDir,name)});
    }
  }
  if (!issues.length) issues.push(...directoryIssues({directory,forms,mappings,legacy,profiles}));
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
  const issues = [...validateFiles(paths), ...validateDirectoryFiles()];
  console.log(formatReport(issues, paths.length));
  process.exit(issues.some((i) => i.level === "error") ? 1 : 0);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
