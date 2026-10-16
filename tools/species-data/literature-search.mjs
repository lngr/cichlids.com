#!/usr/bin/env node
// Searches scientific literature for the research agents without the web search tool of the agent
// session. Prints per work the year, title, DOI, an open-access link where one exists and the
// abstract, so an agent can decide what to read and cite.
//
//   node tools/species-data/literature-search.mjs "<query>" [limit]
//
// OpenAlex answers when an API key is set (environment variable OPENALEX_API_KEY or the file
// ~/.config/cichlids/openalex.key); requests without a key share a small daily budget per IP
// address. Europe PMC answers without a key, Crossref is the last fallback. Calls of
// all processes keep a minimum interval, and rate-limited requests (HTTP 429) are retried after a
// pause, because many agents search in parallel. Each printed result is cached for 30 days in
// data/species/work/.literature/search/<sha1 of query and limit>.txt; a cached query needs no network.
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, rmdirSync, statSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { safeMkdir, safeWriteFile } from './safe-write.mjs';

const [query, limitArg] = process.argv.slice(2);
if (!query) {
  console.error('Usage: node tools/species-data/literature-search.mjs "<query>" [limit]');
  process.exit(2);
}
const LOCK_DIR = join(tmpdir(), 'cichlids-literature.lock');
const LAST_CALL_FILE = join(tmpdir(), 'cichlids-literature.last');
const MIN_INTERVAL_MS = 1500;
const RETRY_PAUSES_MS = [10000, 30000, 60000];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Waits for the shared lock and the minimum interval since the last call of any process.
async function throttled(fn) {
  for (;;) {
    try {
      mkdirSync(LOCK_DIR);
      break;
    } catch {
      try {
        if (Date.now() - statSync(LOCK_DIR).mtimeMs > 60000) rmdirSync(LOCK_DIR);
      } catch {}
      await sleep(200);
    }
  }
  try {
    const last = existsSync(LAST_CALL_FILE) ? Number(readFileSync(LAST_CALL_FILE, 'utf8')) : 0;
    const wait = last + MIN_INTERVAL_MS - Date.now();
    if (wait > 0) await sleep(wait);
    writeFileSync(LAST_CALL_FILE, String(Date.now()));
  } finally {
    rmdirSync(LOCK_DIR);
  }
  return fn();
}

// Fetches JSON through the throttle and retries rate-limited requests.
async function getJson(url, service) {
  for (let attempt = 0; ; attempt++) {
    const res = await throttled(() => fetch(url, { signal: AbortSignal.timeout(30000) }));
    if (res.status === 429 && attempt < RETRY_PAUSES_MS.length) {
      console.error(`literature-search: ${service} rate limit, retrying in ${RETRY_PAUSES_MS[attempt] / 1000} s`);
      await sleep(RETRY_PAUSES_MS[attempt]);
      continue;
    }
    if (!res.ok) throw new Error(`${service} answered HTTP ${res.status}`);
    return res.json();
  }
}

const limit = Math.min(Number.parseInt(limitArg ?? '8', 10) || 8, 25);

// The base of these writes is the plain path <repo>/data/species/work. The tool runs inside the
// agent cage, where that path leads to the work directory that the cage binds after its
// validation. Outside the cage nothing validates the path (in a worktree it is a symlink that an
// agent can retarget), so a manual run there needs a check of the link first.
const CACHE_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'data', 'species', 'work', '.literature', 'search');
const CACHE_MAX_AGE_MS = 30 * 24 * 3600 * 1000;
const cacheFile = join(CACHE_DIR, `${createHash('sha1').update(`${query}\n${limit}`).digest('hex')}.txt`);
if (existsSync(cacheFile) && Date.now() - statSync(cacheFile).mtimeMs < CACHE_MAX_AGE_MS) {
  process.stdout.write(readFileSync(cacheFile, 'utf8'));
  process.exit(0);
}

// OpenAlex stores abstracts as an inverted index of word positions.
function abstractText(index) {
  if (!index) return '';
  const words = [];
  for (const [word, positions] of Object.entries(index)) for (const p of positions) words[p] = word;
  return words.filter(Boolean).join(' ');
}

function openAlexKey() {
  if (process.env.OPENALEX_API_KEY) return process.env.OPENALEX_API_KEY.trim();
  const file = join(homedir(), '.config', 'cichlids', 'openalex.key');
  return existsSync(file) ? readFileSync(file, 'utf8').trim() : null;
}

async function openAlex() {
  const key = openAlexKey();
  if (!key) throw new Error('no OpenAlex API key');
  const url = new URL('https://api.openalex.org/works');
  url.searchParams.set('api_key', key);
  url.searchParams.set('search', query);
  url.searchParams.set('per-page', String(limit));
  url.searchParams.set('select', 'doi,title,publication_year,open_access,abstract_inverted_index,primary_location');
  const data = await getJson(url, 'OpenAlex');
  return data.results.map((w) => ({
    year: w.publication_year,
    title: w.title,
    venue: w.primary_location?.source?.display_name ?? '',
    doi: w.doi ?? '',
    openAccess: w.open_access?.oa_url ?? '',
    abstract: abstractText(w.abstract_inverted_index),
  }));
}

// Turns entity-encoded markup of titles and abstracts into plain text.
function plain(text) {
  return String(text ?? '')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&')
    .replace(/<[^>]+>/g, '')
    .trim();
}

async function europePmc() {
  const url = new URL('https://www.ebi.ac.uk/europepmc/webservices/rest/search');
  // Every word of the query must appear in title or abstract; a plain query also matches full texts
  // that mention the words in passing.
  const words = query.split(/\s+/).map((w) => w.replace(/[^\p{L}\p{N}-]/gu, '')).filter(Boolean);
  url.searchParams.set('query', words.map((w) => `(TITLE:${w} OR ABSTRACT:${w})`).join(' AND '));
  url.searchParams.set('format', 'json');
  url.searchParams.set('resultType', 'core');
  url.searchParams.set('pageSize', String(limit));
  const data = await getJson(url, 'Europe PMC');
  return (data.resultList?.result ?? []).map((w) => ({
    year: w.pubYear ?? '',
    title: plain(w.title),
    venue: w.journalInfo?.journal?.title ?? w.bookOrReportDetails?.publisher ?? '',
    doi: w.doi ? `https://doi.org/${w.doi}` : '',
    openAccess: (w.fullTextUrlList?.fullTextUrl ?? []).find((u) => u.availabilityCode === 'OA')?.url ?? '',
    abstract: plain(w.abstractText),
  }));
}

async function crossref() {
  const url = new URL('https://api.crossref.org/works');
  url.searchParams.set('query', query);
  url.searchParams.set('rows', String(limit));
  url.searchParams.set('select', 'DOI,title,issued,container-title,abstract');
  const data = await getJson(url, 'Crossref');
  return data.message.items.map((w) => ({
    year: w.issued?.['date-parts']?.[0]?.[0] ?? '',
    title: w.title?.[0] ?? '',
    venue: w['container-title']?.[0] ?? '',
    doi: w.DOI ? `https://doi.org/${w.DOI}` : '',
    openAccess: '',
    abstract: plain(w.abstract),
  }));
}

let works = [];
for (const [name, search] of [['OpenAlex', openAlex], ['Europe PMC', europePmc], ['Crossref', crossref]]) {
  try {
    works = await search();
    if (works.length > 0) break;
  } catch (e) {
    if (name !== 'OpenAlex' || openAlexKey()) console.error(`literature-search: ${name}: ${e.message}`);
  }
}
if (works.length === 0) console.log('No works found.');
const lines = [];
for (const w of works) {
  lines.push(`\n== ${w.year} ${w.title}${w.venue ? ` (${w.venue})` : ''}`);
  if (w.doi) lines.push(`   doi: ${w.doi}`);
  if (w.openAccess) lines.push(`   open access: ${w.openAccess}`);
  if (w.abstract) lines.push(`   abstract: ${w.abstract.length > 900 ? `${w.abstract.slice(0, 900)} ...` : w.abstract}`);
}
if (lines.length > 0) {
  const printed = `${lines.join('\n')}\n`;
  process.stdout.write(printed);
  // data/species/work is the trusted base of the cache writes; a symlink below it is refused.
  const workDir = dirname(dirname(CACHE_DIR));
  safeMkdir(CACHE_DIR, workDir);
  safeWriteFile(cacheFile, printed, workDir);
}
