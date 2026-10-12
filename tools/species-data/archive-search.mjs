#!/usr/bin/env node
// Searches the cichlids.com forum archive for the species research agents and prints each matching
// thread with its citation: the legacy thread id, which is stable across imports, and the legacy
// forum URL, which the platform redirects to the thread. Reads the local dev database read-only
// and prints post text only, never member names.
//
//   node tools/species-data/archive-search.mjs "<term>" [limit]
import { execFileSync } from 'node:child_process';

// Forum ids of the legacy Phorum installation, as resolved by the legacy redirects of the API.
const FORUM_ID = { cichlids: 1, african: 2, market_place: 3 };

const [term, limitArg] = process.argv.slice(2);
if (!term) {
  console.error('Usage: node tools/species-data/archive-search.mjs "<term>" [limit]');
  process.exit(2);
}
const limit = Number.parseInt(limitArg ?? '30', 10);

const sql = `
SELECT t.legacy_id, t.category, p.id, replace(replace(left(p.body, 600), E'\\n', ' '), E'\\t', ' ')
FROM discussion_post p JOIN discussion_thread t ON t.id = p.thread_id
WHERE p.body ILIKE '%' || :'term' || '%' AND t.legacy_id IS NOT NULL
ORDER BY t.legacy_id, p.id
LIMIT ${limit}`;

const out = execFileSync(
  'docker',
  ['exec', '-i', 'stack-postgres-1', 'psql', '-U', 'cichlids', '-d', 'cichlids', '-AtF', '\t', '-v', `term=${term}`],
  { input: sql, encoding: 'utf8' },
);

const rows = out.split('\n').filter(Boolean).map((line) => line.split('\t'));
if (rows.length === 0) console.log('No posts found.');
let current = null;
for (const [legacyId, category, , excerpt] of rows) {
  if (legacyId !== current) {
    current = legacyId;
    console.log(`\n== cichlids.com forum thread ${legacyId}`);
    console.log(`   url: https://www.cichlids.com/disc/read.php?${FORUM_ID[category] ?? 1},${legacyId}`);
  }
  console.log(`   - ${excerpt}`);
}
