#!/usr/bin/env node
// Validates knowledge articles (data/knowledge/articles/<id>.md): YAML frontmatter plus a Markdown
// body whose claims cite the frontmatter sources as footnote references such as [^key].
//
// Usage: node tools/species-data/validate-articles.mjs [paths...]
// Without paths, every data/knowledge/articles/*.md is validated.
//
// Issues have the shape { level: "error" | "warning", file, pointer, message }; pointer is a JSON
// pointer into the frontmatter or "line N" for the body. Errors fail the run (exit code 1).
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { parse } from "yaml";
import { archiveSourceIssues, error, warning } from "./validate.mjs";
import { loadWaterbodies } from "./waterbodies.mjs";

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const ARTICLES_DIR = join(REPO_ROOT, "data", "knowledge", "articles");
const ARTICLE_SCHEMA_PATH = join(REPO_ROOT, "data", "knowledge", "schema", "article.schema.json");
const SPECIES_SCHEMA_PATH = join(REPO_ROOT, "data", "species", "schema", "species-profile.schema.json");
const PROFILES_DIR = join(REPO_ROOT, "data", "species", "profiles");
const WATERBODIES_PATH = join(REPO_ROOT, "data", "species", "waterbodies.yaml");

const CITATION = /\[\^([^\]\s]+)\]/g;
const LIST_ITEM = /^\s*(?:[-*+]|\d+[.)])\s+/;
const LIST_MARKER_ONLY = /^\s*(?:[-*+]|\d+[.)])\s*$/;
const FENCE = /^\s*(```|~~~)/;
const HEADING = /^\s{0,3}#{1,6}(\s|$)/;
const TABLE_ROW = /^\s*\|/;
const DASHES = /[\u2013\u2014]/;
const isObject = (x) => x !== null && typeof x === "object" && !Array.isArray(x);

let compiled;
function schemaValidator() {
  if (!compiled) {
    const ajv = new Ajv2020({ allErrors: true, strictTypes: false });
    addFormats(ajv);
    ajv.addSchema(JSON.parse(readFileSync(SPECIES_SCHEMA_PATH, "utf8")));
    compiled = ajv.compile(JSON.parse(readFileSync(ARTICLE_SCHEMA_PATH, "utf8")));
  }
  return compiled;
}

/** Splits an article into frontmatter text and body; body lines keep their file line numbers. */
export function splitArticle(text) {
  const lines = text.replace(/\r\n/g, "\n").split("\n");
  if (lines[0] !== "---") return null;
  const end = lines.indexOf("---", 1);
  if (end < 0) return null;
  return { frontmatter: lines.slice(1, end).join("\n"), body: lines.slice(end + 1), firstBodyLine: end + 2 };
}

/**
 * Returns the text blocks of a body that need a citation: plain paragraphs and single list items.
 * Headings, tables, code blocks, blank lines and lines holding only a list marker are skipped.
 * Each block is { line, text } with the 1-based line of its first line.
 */
export function citationBlocks(bodyLines, firstLine = 1) {
  const blocks = [];
  let current = null;
  let inFence = false;
  const close = () => {
    if (current) blocks.push(current);
    current = null;
  };
  bodyLines.forEach((raw, i) => {
    const line = firstLine + i;
    if (FENCE.test(raw)) {
      close();
      inFence = !inFence;
      return;
    }
    if (inFence || raw.trim() === "" || HEADING.test(raw) || TABLE_ROW.test(raw) || LIST_MARKER_ONLY.test(raw)) {
      close();
      return;
    }
    if (LIST_ITEM.test(raw)) {
      close();
      current = { line, text: raw };
    } else if (current) {
      current.text += `\n${raw}`;
    } else {
      current = { line, text: raw };
    }
  });
  close();
  return blocks;
}

function sentenceCount(text) {
  return (text.trim().match(/[.!?](?=\s|$)/g) ?? []).length;
}

/**
 * Validates the text of one article. Options: fileName (base name, checked against the id),
 * waterbodies (Map from loadWaterbodies, or null to skip), profileIds (Set of profile ids, or
 * null to skip), file (label put into the issues).
 */
export function validateArticle(text, options = {}) {
  const file = options.file;
  const issues = [];
  const parts = splitArticle(text);
  if (!parts) return [error("", "article needs YAML frontmatter delimited by --- lines", file)];

  let meta;
  try {
    meta = parse(parts.frontmatter, { maxAliasCount: -1 });
  } catch (e) {
    return [error("", `cannot parse frontmatter YAML: ${e.message}`, file)];
  }
  if (!isObject(meta)) return [error("", "frontmatter must be a mapping", file)];

  const validate = schemaValidator();
  if (!validate(meta)) {
    for (const e of validate.errors) {
      const extra = e.keyword === "additionalProperties" ? ` (${e.params.additionalProperty})` : "";
      issues.push(error(e.instancePath, `${e.message}${extra}`, file));
    }
  }
  if (typeof meta.id === "string" && options.fileName !== undefined && options.fileName !== `${meta.id}.md`) {
    issues.push(error("/id", `file name "${options.fileName}" must be "${meta.id}.md"`, file));
  }
  if (typeof meta.summary === "string") {
    const n = sentenceCount(meta.summary);
    if (n < 1 || n > 3) issues.push(error("/summary", `summary has ${n} sentences, expected 1 to 3`, file));
  }

  const sources = isObject(meta.sources) ? meta.sources : {};
  const cited = new Set();
  const blocks = citationBlocks(parts.body, parts.firstBodyLine);
  for (const block of blocks) {
    const keys = [...block.text.matchAll(CITATION)].map((m) => m[1]);
    if (keys.length === 0) {
      // A methodology article describes the procedure of cichlids.com itself; its paragraphs about
      // that procedure have no external source, so a missing citation is a warning there.
      const report = meta.kind === "methodology" ? warning : error;
      issues.push(report(`line ${block.line}`, "paragraph or list item has no citation [^key]", file));
    }
    for (const key of keys) cited.add(key);
  }
  // Citations inside headings or tables still count as uses of a source and must resolve.
  parts.body.forEach((raw, i) => {
    for (const m of raw.matchAll(CITATION)) {
      cited.add(m[1]);
      if (!(m[1] in sources)) issues.push(error(`line ${parts.firstBodyLine + i}`, `citation [^${m[1]}] has no entry in sources`, file));
    }
  });
  for (const key of Object.keys(sources)) {
    if (!cited.has(key)) issues.push(error(`/sources/${key}`, `source "${key}" is never cited in the body`, file));
  }

  issues.push(...archiveSourceIssues(sources, file, "/sources"));

  if (options.waterbodies && Array.isArray(meta.waterbodies)) {
    meta.waterbodies.forEach((id, i) => {
      if (!options.waterbodies.has(id)) issues.push(error(`/waterbodies/${i}`, `unknown waterbody "${id}"`, file));
    });
  }
  if (options.profileIds && Array.isArray(meta.species)) {
    meta.species.forEach((id, i) => {
      if (!options.profileIds.has(id)) issues.push(warning(`/species/${i}`, `no profile file for species "${id}"`, file));
    });
  }

  parts.body.forEach((raw, i) => {
    if (DASHES.test(raw)) issues.push(warning(`line ${parts.firstBodyLine + i}`, "body contains an em or en dash", file));
  });
  return issues;
}

function loadProfileIds(dir = PROFILES_DIR) {
  if (!existsSync(dir)) return new Set();
  return new Set(readdirSync(dir).filter((f) => f.endsWith(".yaml")).map((f) => f.slice(0, -".yaml".length)));
}

/**
 * Reads and validates article files. The waterbody table and the profile ids can be injected
 * (waterbodies: Map or null, profileIds: Set) or are read from the repository. A missing table
 * file skips the waterbody check with a warning.
 */
export function validateArticleFiles(paths, options = {}) {
  const issues = [];
  let { waterbodies } = options;
  const { profileIds = loadProfileIds(), waterbodiesPath = WATERBODIES_PATH } = options;
  if (waterbodies === undefined && paths.length > 0) {
    waterbodies = null;
    const tableFile = relative(process.cwd(), waterbodiesPath) || waterbodiesPath;
    if (existsSync(waterbodiesPath)) {
      try {
        waterbodies = loadWaterbodies(waterbodiesPath);
      } catch (e) {
        issues.push(error("", e.message, tableFile));
      }
    } else {
      issues.push(warning("", "waterbody table not found, waterbody check skipped", tableFile));
    }
  }
  for (const path of paths) {
    const file = relative(process.cwd(), resolve(path)) || path;
    let text;
    try {
      text = readFileSync(path, "utf8");
    } catch (e) {
      issues.push(error("", `cannot read file: ${e.message}`, file));
      continue;
    }
    issues.push(...validateArticle(text, { file, fileName: basename(path), waterbodies, profileIds }));
  }
  return issues;
}

function formatReport(issues, fileCount) {
  const lines = issues.map((i) => `${i.level === "error" ? "ERROR  " : "WARNING"} ${i.file ?? "-"} ${i.pointer || "/"}: ${i.message}`);
  const errorCount = issues.filter((i) => i.level === "error").length;
  lines.push(`${fileCount} file(s), ${errorCount} error(s), ${issues.length - errorCount} warning(s)`);
  return lines.join("\n");
}

function main() {
  let paths = process.argv.slice(2);
  if (paths.length === 0) {
    paths = existsSync(ARTICLES_DIR)
      ? readdirSync(ARTICLES_DIR).filter((f) => f.endsWith(".md")).sort().map((f) => join(ARTICLES_DIR, f))
      : [];
  }
  const issues = validateArticleFiles(paths);
  console.log(formatReport(issues, paths.length));
  process.exit(issues.some((i) => i.level === "error") ? 1 : 0);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
