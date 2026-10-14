#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { archiveUrl } from './source-archive.mjs';

export async function main(argv) {
  const urls = argv.filter(a => a !== '--refresh');
  if (urls.length !== 1 || urls[0].startsWith('--')) {
    console.error('Usage: node tools/species-data/archive-fetch.mjs <url> [--refresh]');
    return 2;
  }
  try {
    const snapshot = await archiveUrl(urls[0], { refresh: argv.includes('--refresh') });
    console.log(snapshot.ref);
    console.log(snapshot.textPath);
    console.log(readFileSync(snapshot.textPath, 'utf8').slice(0, 1500));
    return 0;
  } catch (e) { console.error(`archive-fetch: ${e.message}`); return 1; }
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) process.exitCode = await main(process.argv.slice(2));
