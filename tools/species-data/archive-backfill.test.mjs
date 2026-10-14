import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { parse } from 'yaml';
import { collectSourceUrls, insertArchiveRefs, backfill } from './archive-backfill.mjs';
const ref = 'example.org/fish/2026-10-14T093000Z';
const refs = new Map([['https://example.org/fish', ref]]);
const cases = [
  ['plain', 'sources:\n  - kind: database\n    url: https://example.org/fish # keep\n    title: Fish\n', `sources:\n  - kind: database\n    url: https://example.org/fish # keep\n    archived: ${ref}\n    title: Fish\n`],
  ['quoted', 'source:\n  kind: website\n  url: "https://example.org/fish"\n  title: Fish\n', `source:\n  kind: website\n  url: "https://example.org/fish"\n  archived: ${ref}\n  title: Fish\n`],
  ['continued', 'sources:\n  - kind: website\n    url: "https://example.org/\\\n      fish" # keep\n    title: Fish\n', `sources:\n  - kind: website\n    url: "https://example.org/\\\n      fish" # keep\n    archived: ${ref}\n    title: Fish\n`],
  ['URL first in list', 'sources:\n  - url: https://example.org/fish\n    kind: database\n', `sources:\n  - url: https://example.org/fish\n    archived: ${ref}\n    kind: database\n`],
  ['flow', 'sources:\n  - {kind: database, url: "https://example.org/fish", title: "Fish } reef"} # keep\n', `sources:\n  - {kind: database, url: "https://example.org/fish", title: "Fish } reef", archived: ${ref}} # keep\n`],
  ['multiline flow URL', 'source: {kind: database, url: "https://example.org/\\\n  fish", title: Fish}\n', `source: {kind: database, url: "https://example.org/\\\n  fish", title: Fish, archived: ${ref}}\n`],
  ['EOF without newline', 'source:\n  kind: website\n  url: https://example.org/fish', `source:\n  kind: website\n  url: https://example.org/fish\n  archived: ${ref}`],
];
for (const [name, input, expected] of cases) test(`Given ${name} YAML, insertion preserves every existing byte`, () => {
  assert.equal(insertArchiveRefs(input, refs), expected);
  assert.deepEqual(collectSourceUrls(input), ['https://example.org/fish']);
  assert.equal(insertArchiveRefs(expected, refs), expected);
  assert.ok(JSON.stringify(parse(expected)).includes(ref));
});
test('Existing archived entries and unrelated URL maps remain untouched', () => {
  const input = 'sources:\n  - {kind: database, url: https://example.org/fish, archived: keep}\n  - kind: database\n    archived: keep-too\n    url: https://example.org/fish\nother:\n  url: https://example.org/fish\n';
  assert.equal(insertArchiveRefs(input, refs), input);
});
test('CRLF, single quotes, comments and multiple source occurrences retain their formatting', () => {
  const input = "source:\r\n  kind: database\r\n  url: 'https://example.org/fish' # comment\r\nsource2: {kind: website, url: https://example.org/fish}\r\n";
  const expected = input.replace(" # comment\r\n", ` # comment\r\n  archived: ${ref}\r\n`).replace('fish}', `fish, archived: ${ref}}`);
  assert.equal(insertArchiveRefs(input, refs), expected);
  assert.deepEqual(collectSourceUrls(input), ['https://example.org/fish']);
});
test('Given profile and shared-table fixtures, dry run lists unique URLs and backfill archives each once', async t => {
  const speciesDir = mkdtempSync(join(tmpdir(), 'cichlids-backfill-test-'));
  t.after(() => rmSync(speciesDir, { recursive: true, force: true }));
  mkdirSync(join(speciesDir, 'profiles'));
  const input = cases[0][1];
  const files = ['profiles/fish.yaml', 'hybrid-groups.yaml', 'waterbodies.yaml'];
  for (const f of files) writeFileSync(join(speciesDir, f), input);
  const root = join(speciesDir, 'archive');
  let calls = 0;
  const archive = async url => { calls++; assert.equal(url, 'https://example.org/fish'); return { ref }; };
  const dry = await backfill({ speciesDir, root, dryRun: true, archive });
  assert.deepEqual(dry.urls, ['https://example.org/fish']);
  assert.equal(calls, 0);
  assert.equal(existsSync(root), false);
  for (const f of files) assert.equal(readFileSync(join(speciesDir, f), 'utf8'), input);
  await backfill({ speciesDir, root, archive });
  assert.equal(calls, 1);
  for (const f of files) assert.equal(readFileSync(join(speciesDir, f), 'utf8'), cases[0][2]);
});
test('Given failed sources, backfill preserves their YAML and reports failures', async t => {
  const speciesDir = mkdtempSync(join(tmpdir(), 'cichlids-backfill-test-'));
  t.after(() => rmSync(speciesDir, { recursive: true, force: true }));
  mkdirSync(join(speciesDir, 'profiles'));
  writeFileSync(join(speciesDir, 'profiles/fish.yaml'), cases[0][1]);
  const result = await backfill({ speciesDir, archive: async () => { throw new Error('HTTP 404'); } });
  assert.equal(result.failures.length, 1);
  assert.equal(readFileSync(join(speciesDir, 'profiles/fish.yaml'), 'utf8'), cases[0][1]);
});

test('Flow mappings with trailing commas retain the comma and stay valid', () => {
  const input = 'source: {kind: website, url: https://example.org/fish, }\n';
  const result = insertArchiveRefs(input, refs);
  assert.equal(result, `source: {kind: website, url: https://example.org/fish,  archived: ${ref}}\n`);
  assert.equal(parse(result).source.archived, ref);
});
