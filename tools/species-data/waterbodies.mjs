// Loads the table of measured water values of lakes and other water bodies
// (data/species/waterbodies.yaml). The validator checks lake species against it and the tally
// script takes their water values from it. Source kinds follow the species profile schema, so
// table sources can be copied into profiles unchanged.
import { readFileSync } from "node:fs";
import { parse } from "yaml";

const RANGE_FIELDS = ["temperatureC", "ph", "ghDgh", "khDkh"];
const SOURCE_KINDS = ["catalog-of-fishes", "literature", "database", "forum", "archive", "model-knowledge"];

const isObject = (x) => x !== null && typeof x === "object" && !Array.isArray(x);
const isNumber = (x) => typeof x === "number" && Number.isFinite(x);

function checkEntry(entry, index, path) {
  const label = isObject(entry) && typeof entry.id === "string" ? `waterbody "${entry.id}"` : `waterbody #${index + 1}`;
  const fail = (message) => {
    throw new Error(`${path}: ${label}: ${message}`);
  };
  if (!isObject(entry)) fail("entry is not an object");
  for (const field of ["id", "name", "rationale"]) {
    if (typeof entry[field] !== "string" || entry[field].trim() === "") fail(`field "${field}" is missing or not a non-empty string`);
  }
  for (const field of RANGE_FIELDS) {
    const range = entry[field];
    if (!isObject(range) || !isNumber(range.min) || !isNumber(range.max)) fail(`field "${field}" needs numeric min and max`);
    if (range.min > range.max) fail(`field "${field}" has min (${range.min}) greater than max (${range.max})`);
  }
  if (!Array.isArray(entry.sources) || entry.sources.length === 0) fail('field "sources" needs at least one source');
  for (const source of entry.sources) {
    if (!isObject(source) || typeof source.title !== "string" || !SOURCE_KINDS.includes(source.kind)) {
      fail(`every source needs a title and a kind of ${SOURCE_KINDS.join(", ")}`);
    }
  }
  if (entry.aliases !== undefined && (!Array.isArray(entry.aliases) || entry.aliases.some((a) => typeof a !== "string"))) {
    fail('field "aliases" must be a list of strings');
  }
}

/** Reads and validates the table; returns a Map from id to entry. Throws a descriptive Error. */
export function loadWaterbodies(path) {
  const doc = parse(readFileSync(path, "utf8"), { maxAliasCount: -1 });
  if (!isObject(doc) || !Array.isArray(doc.waterbodies)) {
    throw new Error(`${path}: top-level key "waterbodies" must be a list`);
  }
  const map = new Map();
  doc.waterbodies.forEach((entry, index) => {
    checkEntry(entry, index, path);
    if (map.has(entry.id)) throw new Error(`${path}: duplicate waterbody id "${entry.id}"`);
    map.set(entry.id, entry);
  });
  return map;
}

/** Finds an entry by id, name or alias, case-insensitively; returns null when nothing matches. */
export function findWaterbody(map, name) {
  if (typeof name !== "string" || !map) return null;
  const wanted = name.trim().toLowerCase();
  if (wanted === "") return null;
  for (const entry of map.values()) {
    const names = [entry.id, entry.name, ...(entry.aliases ?? [])];
    if (names.some((n) => n.trim().toLowerCase() === wanted)) return entry;
  }
  return null;
}
