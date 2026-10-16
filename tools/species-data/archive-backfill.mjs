#!/usr/bin/env node
// Source ranges permit insertions without reserializing or reformatting the YAML document.
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { isMap, isSeq, isScalar, parseDocument } from 'yaml';
import { safeWriteFile } from './safe-write.mjs';
import { archiveUrl, validArchiveRef } from './source-archive.mjs';
const SPECIES_DIR = resolve(dirname(fileURLToPath(import.meta.url)), '../../data/species');

function sourceMaps(yaml) {
  const doc = parseDocument(yaml, { keepSourceTokens: true });
  if (doc.errors.length) throw new Error(doc.errors.map(e => e.message).join('\n'));
  const maps = [];
  function walk(node) {
    if (isMap(node)) {
      if (node.has('kind') && isScalar(node.get('url', true)) && typeof node.get('url') === 'string') maps.push(node);
      for (const pair of node.items) walk(pair.value);
    } else if (isSeq(node)) for (const item of node.items) walk(item);
  }
  walk(doc.contents);
  return maps;
}
export function collectSourceUrls(yaml) {
  return [...new Set(sourceMaps(yaml).map(m => m.get('url')))];
}
export function insertArchiveRefs(yaml, refs) {
  const edits = [];
  for (const map of sourceMaps(yaml)) {
    if (map.has('archived')) continue;
    const ref = refs.get(map.get('url'));
    if (!ref) continue;
    if (!validArchiveRef(ref)) throw new Error(`Invalid source archive reference: ${ref}`);
    if (map.flow) {
      const end = map.srcToken.end.find(t => t.type === 'flow-map-end').offset;
      // Flow mappings may carry a trailing comma. Keep it and use it as the separator.
      const before = yaml.slice(map.range[0], end).trimEnd();
      edits.push({ at: end, text: `${before.endsWith(',') ? ' ' : ', '}archived: ${ref}` });
    } else {
      const pair = map.items.find(p => p.key.value === 'url');
      const scalarEnd = pair.value.range[1];
      const lineEnd = yaml.indexOf('\n', scalarEnd);
      const newline = yaml.includes('\r\n') ? '\r\n' : '\n';
      const lineStart = yaml.lastIndexOf('\n', pair.key.range[0] - 1) + 1;
      const indentation = ' '.repeat(pair.key.range[0] - lineStart);
      if (lineEnd === -1) edits.push({ at: yaml.length, text: `${newline}${indentation}archived: ${ref}` });
      else edits.push({ at: lineEnd + 1, text: `${indentation}archived: ${ref}${newline}` });
    }
  }
  for (const edit of edits.sort((a, b) => b.at - a.at)) yaml = yaml.slice(0, edit.at) + edit.text + yaml.slice(edit.at);
  return yaml;
}

export async function backfill({ speciesDir = SPECIES_DIR, root, dryRun = false, archive = archiveUrl } = {}) {
  const profiles = join(speciesDir, 'profiles');
  const files = existsSync(profiles) ? readdirSync(profiles).filter(f => f.endsWith('.yaml')).sort().map(f => join(profiles, f)) : [];
  files.push(...['hybrid-groups.yaml', 'waterbodies.yaml'].map(f => join(speciesDir, f)).filter(existsSync));
  const documents = files.map(file => ({ file, yaml: readFileSync(file, 'utf8') }));
  const urls = [...new Set(documents.flatMap(d => collectSourceUrls(d.yaml)))];
  const refs = new Map();
  const failures = [];
  if (dryRun) return { urls, refs, failures };
  for (const url of urls) {
    try { refs.set(url, (await archive(url, { root })).ref); }
    catch (e) { failures.push({ url, reason: e.message }); }
  }
  for (const { file, yaml } of documents) {
    const updated = insertArchiveRefs(yaml, refs);
    // The data directory above the species directory is the trusted base of the writes.
    if (updated !== yaml) safeWriteFile(file, updated, dirname(speciesDir));
  }
  return { urls, refs, failures };
}
export async function main(argv) {
  if (argv.some(a => a !== '--dry-run')) { console.error('Usage: node tools/species-data/archive-backfill.mjs [--dry-run]'); return 2; }
  try {
    const result = await backfill({ dryRun: argv.includes('--dry-run') });
    for (const url of result.urls) console.log(result.refs.has(url) ? `${url}\t${result.refs.get(url)}` : url);
    for (const failure of result.failures) console.error(`${failure.url}: ${failure.reason}`);
    return result.failures.length ? 1 : 0;
  } catch (e) { console.error(`archive-backfill: ${e.message}`); return 1; }
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) process.exitCode = await main(process.argv.slice(2));
