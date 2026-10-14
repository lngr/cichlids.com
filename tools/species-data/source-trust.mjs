// Source trust is a confidence and conflict signal; every source remains citable.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { parse } from "yaml";

const DEFAULT_PATH = fileURLToPath(new URL("../../data/species/source-trust.yaml", import.meta.url));
const LEVELS = ["high", "medium", "low"];
const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

/** Reads and validates the ordered trust list. Throws a descriptive Error for invalid lists. */
export function loadSourceTrust(path = DEFAULT_PATH) {
  const list = parse(readFileSync(path, "utf8"));
  const fail = (message) => { throw new Error(`${path}: ${message}`); };
  const checkLevel = (level, label) => {
    if (!LEVELS.includes(level)) fail(`${label} must be one of ${LEVELS.join(", ")}`);
  };
  if (!isObject(list) || !Array.isArray(list.hosts) || !isObject(list.byKind)) {
    fail("trust list needs hosts (list), byKind (object) and default");
  }
  checkLevel(list.default, "default");
  for (const [kind, level] of Object.entries(list.byKind)) checkLevel(level, `byKind.${kind}`);
  const pairs = new Set();
  for (const entry of list.hosts) {
    if (!isObject(entry) || typeof entry.host !== "string" || !/^[a-z0-9]+(?:[a-z0-9.-]*[a-z0-9])?$/i.test(entry.host)) {
      fail("each host entry needs a domain host");
    }
    if (entry.path !== undefined && (typeof entry.path !== "string" || (entry.path !== "" && !entry.path.startsWith("/")))) {
      fail(`host ${entry.host}: path must be a URL path prefix`);
    }
    checkLevel(entry.level, `host ${entry.host} level`);
    const key = JSON.stringify([entry.host.toLowerCase(), entry.path ?? ""]);
    if (pairs.has(key)) fail(`duplicate host and path pair: ${key}`);
    pairs.add(key);
  }
  return list;
}

function originalUrl(value) {
  let url;
  try { url = new URL(value); } catch { return null; }
  // Each unwrapping shortens the URL, including nested archive copies.
  while (["web.archive.org", "archive.org"].includes(url.hostname)) {
    const match = /^\/web\/\d+(?:[a-z]+_)?\/(https?:\/\/.+)$/i.exec(url.pathname + url.search + url.hash);
    if (!match) break;
    try { url = new URL(match[1]); } catch { return null; }
  }
  return url;
}

/** First matching host or parent domain and optional path prefix wins. URL-free sources use kind. */
export function trustOf(source, list) {
  if (source?.url === undefined || source.url === null || source.url === "") {
    return list.byKind[source?.kind] ?? list.default;
  }
  const url = originalUrl(source.url);
  if (!url) return list.default;
  for (const entry of list.hosts) {
    const host = entry.host.toLowerCase();
    if ((url.hostname === host || url.hostname.endsWith(`.${host}`)) && url.pathname.startsWith(entry.path ?? "")) {
      return entry.level;
    }
  }
  return list.default;
}
