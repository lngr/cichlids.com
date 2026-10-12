// Tests for the knowledge article validator. Articles are inline strings.
import { test } from "node:test";
import assert from "node:assert/strict";

import { citationBlocks, validateArticle } from "./validate-articles.mjs";

const WATERBODIES = new Map([["lake-malawi", { id: "lake-malawi" }]]);
const PROFILES = new Set(["tropheus-moorii"]);

const FRONT = `schemaVersion: 1
id: malawi-water
title: The water of Lake Malawi
language: en
summary: Lake Malawi water is hard and alkaline. Care sheets differ because they describe different tanks.
topics: [water]
species: [tropheus-moorii]
waterbodies: [lake-malawi]
sources:
  alpha:
    kind: literature
    title: Alpha paper
    url: https://example.org/alpha
  beta:
    kind: database
    title: Beta table
provenance:
  runs:
    - {role: generator, model: m, date: 2026-10-12}
`;

const BODY = `# Water

The lake is alkaline.[^alpha]

- Hardness is moderate.[^beta]
- Temperature is stable.[^alpha]

| Value | Reading |
| ----- | ------- |
| pH | 8 |

\`\`\`
uncited code
\`\`\`
`;

const options = { fileName: "malawi-water.md", waterbodies: WATERBODIES, profileIds: PROFILES };
const article = (front = FRONT, body = BODY) => `---\n${front}---\n\n${body}`;
const check = (text, opts = options) => validateArticle(text, opts);
const errors = (issues) => issues.filter((i) => i.level === "error");
const warnings = (issues) => issues.filter((i) => i.level === "warning");

test("a valid article passes without issues", () => {
  assert.deepEqual(check(article()), []);
});

test("missing frontmatter fails", () => {
  assert.equal(errors(check("# Only a body\n")).length, 1);
});

test("frontmatter is checked against the schema", () => {
  const found = errors(check(article(FRONT.replace("language: en", "language: de"))));
  assert.ok(found.some((i) => i.pointer === "/language"), JSON.stringify(found));
  const extra = errors(check(article(`${FRONT}extra: 1\n`)));
  assert.ok(extra.some((i) => /extra/.test(i.message)), JSON.stringify(extra));
  const key = errors(check(article(FRONT.replace("  alpha:", "  Alpha_:")).replace("[^alpha]", "[^Alpha_]")));
  assert.ok(key.length > 0);
});

test("a source of the wrong shape fails the schema", () => {
  const found = errors(check(article(FRONT.replace("    kind: database\n", "    kind: blog\n"))));
  assert.ok(found.length > 0);
});

test("the file name must equal the id", () => {
  const found = errors(check(article(), { ...options, fileName: "other.md" }));
  assert.ok(found.some((i) => i.pointer === "/id"), JSON.stringify(found));
});

test("a summary of four sentences fails", () => {
  const found = errors(check(article(FRONT.replace(/summary: .*/, "summary: One. Two. Three. Four."))));
  assert.ok(found.some((i) => i.pointer === "/summary"), JSON.stringify(found));
});

test("a citation without a source fails", () => {
  const found = errors(check(article(FRONT, BODY.replace("[^beta]", "[^gamma]"))));
  assert.ok(found.some((i) => /gamma/.test(i.message)), JSON.stringify(found));
});

test("a source that is never cited fails", () => {
  const found = errors(check(article(FRONT, BODY.replace("[^beta]", "[^alpha]"))));
  assert.ok(found.some((i) => i.pointer === "/sources/beta"), JSON.stringify(found));
});

test("a paragraph without citation fails and reports its line", () => {
  const found = errors(check(article(FRONT, `${BODY}\nAn uncited claim.\n`)));
  assert.equal(found.length, 1, JSON.stringify(found));
  assert.match(found[0].pointer, /^line \d+$/);
});

test("every list item needs its own citation", () => {
  const found = errors(check(article(FRONT, BODY.replace("- Temperature is stable.[^alpha]", "- Temperature is stable."))));
  assert.equal(found.length, 1, JSON.stringify(found));
});

test("a citation on a continuation line covers the paragraph", () => {
  const body = BODY.replace("The lake is alkaline.[^alpha]", "The lake is alkaline and\nthe water is hard.[^alpha]");
  assert.deepEqual(check(article(FRONT, body)), []);
});

test("headings, tables, code blocks and bare list markers need no citation", () => {
  const blocks = citationBlocks(["# Heading", "", "| a | b |", "", "-", "", "```", "code", "```", "", "text"], 1);
  assert.deepEqual(blocks, [{ line: 11, text: "text" }]);
});

test("an unknown waterbody fails and an unknown species only warns", () => {
  const front = FRONT.replace("waterbodies: [lake-malawi]", "waterbodies: [lake-x]").replace("species: [tropheus-moorii]", "species: [nothing-here]");
  const issues = check(article(front));
  assert.ok(errors(issues).some((i) => i.pointer === "/waterbodies/0"), JSON.stringify(issues));
  assert.ok(warnings(issues).some((i) => i.pointer === "/species/0"), JSON.stringify(issues));
});

test("the waterbody check is skipped without a table", () => {
  const front = FRONT.replace("waterbodies: [lake-malawi]", "waterbodies: [lake-x]");
  assert.deepEqual(check(article(front), { ...options, waterbodies: null }), []);
});

test("archive sources follow the thread title and url rule", () => {
  const archive = (title, url) => `  gamma:\n    kind: archive\n    title: ${title}\n${url ? `    url: ${url}\n` : ""}`;
  const body = `${BODY}\nA forum remark.[^gamma]\n`;
  const ok = archive("cichlids.com forum thread 13697", "https://www.cichlids.com/disc/read.php?2,13697");
  assert.deepEqual(check(article(FRONT.replace("provenance:", `${ok}provenance:`), body)), []);
  const bad = archive("cichlids.com forum thread 13697", "https://www.cichlids.com/disc/read.php?2,99");
  const found = errors(check(article(FRONT.replace("provenance:", `${bad}provenance:`), body)));
  assert.ok(found.some((i) => i.pointer === "/sources/gamma" && /archive/.test(i.message)), JSON.stringify(found));
});

test("em and en dashes in the body warn", () => {
  const issues = check(article(FRONT, BODY.replace("is alkaline.", "is — alkaline.")));
  assert.equal(errors(issues).length, 0);
  assert.equal(warnings(issues).length, 1);
});

test("a methodology article reports an uncited paragraph as a warning", () => {
  const issues = check(article(`${FRONT}kind: methodology\n`, `${BODY}\nThe planner compares every pair of species.\n`));
  assert.deepEqual(errors(issues), []);
  assert.ok(warnings(issues).some((i) => /no citation/.test(i.message)), JSON.stringify(issues));
});
