#!/usr/bin/env node
// Fetches the full text of open-access scientific works for the research agents and keeps it in a
// local library, so that each work is downloaded once.
//
//   xvfb-run -a node tools/species-data/fetch-oa.mjs <doi or https://doi.org/... URL>
//
// The library is data/species/work/.literature/. Per work it holds <key>.json (metadata), the
// original (<key>.xml, <key>.pdf or <key>.html) and the extracted plain text <key>.txt; the key is
// the DOI with every character outside [a-z0-9._-] replaced by "_". A work with a text file is
// served from the library without network access. Otherwise OpenAlex must list the work as open
// access. Routes in trial order: the TEI XML copy hosted by OpenAlex, the PDF copy hosted by
// OpenAlex (each download costs $0.01 of the daily budget of the API key), then the open-access
// addresses of the publisher (PDF address first, then landing page) with a plain request and, when
// that is blocked, a headed Chromium on a virtual display. Pages that ask for a login, a
// subscription, a purchase or a captcha are not fetched. The metadata file names the route that
// delivered the text: openalex-tei, openalex-pdf, publisher-fetch or publisher-browser.
//
// Exit codes: 0 text available, 1 failure, 2 usage, 3 work is not open access, 4 blocked by login
// or paywall.
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmdirSync, statSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { BLOCK_PAGE_MARKERS, teiToText, looksBlocked, looksLikeLoginOrPaywall, extractHtmlText, pdfText } from './source-content.mjs';
export { teiToText, looksBlocked, looksLikeLoginOrPaywall, extractHtmlText } from './source-content.mjs';
import { USER_AGENT, publicFetch, storeSnapshot, findSnapshot, contentIssue, recordFailure } from './source-archive.mjs';

export const LIBRARY_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'data', 'species', 'work', '.literature');
const LOCK_DIR = join(tmpdir(), 'cichlids-oa.lock');
const LAST_CALL_FILE = join(tmpdir(), 'cichlids-oa.last');
const MIN_INTERVAL_MS = 10000;
const STALE_LOCK_MS = 180000;
const MIN_TEXT_LENGTH = 500;
const PREVIEW_LENGTH = 1500;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Reduces a bare DOI or a doi.org URL to the lowercase DOI.
export function normaliseDoi(input) {
  let doi = String(input ?? '').trim();
  try {
    doi = decodeURIComponent(doi);
  } catch {}
  doi = doi.replace(/^(https?:\/\/)?(dx\.)?doi\.org\//i, '').replace(/^doi:\s*/i, '');
  return doi.trim().toLowerCase();
}

// File name stem of a work in the library.
export function cacheKey(doi) {
  return normaliseDoi(doi).replace(/[^a-z0-9._-]/g, '_');
}

// Open-access addresses of an OpenAlex work in trial order: the best location first, then the other
// open-access locations; within a location the PDF address before the landing page.
export function pickCandidateUrls(work) {
  const locations = [];
  if (work?.best_oa_location) locations.push(work.best_oa_location);
  for (const l of work?.locations ?? []) if (l?.is_oa) locations.push(l);
  const out = [];
  const seen = new Set();
  const add = (url, kind) => {
    if (url && !seen.has(url)) {
      seen.add(url);
      out.push({ url, kind });
    }
  };
  for (const l of locations) {
    add(l.pdf_url, 'pdf');
    add(l.landing_page_url, 'html');
  }
  return out;
}

// Download routes of the copies hosted by OpenAlex, TEI XML before PDF. Only formats listed in
// has_content count.
export function pickOpenAlexContent(work) {
  const routes = [];
  const urls = work?.content_urls ?? {};
  if (work?.has_content?.grobid_xml && urls.grobid_xml) routes.push({ route: 'openalex-tei', format: 'xml', url: urls.grobid_xml });
  if (work?.has_content?.pdf && urls.pdf) routes.push({ route: 'openalex-pdf', format: 'pdf', url: urls.pdf });
  return routes;
}

// Decides from an OpenAlex work whether a download may start. Returns the exit code and message of
// a refusal, or the metadata and candidate addresses.
export function assessOpenAlexWork(work, doi) {
  if (!work?.open_access?.is_oa) {
    return { ok: false, exitCode: 3, message: 'not open access: use the abstract (literature-search) or another source' };
  }
  const candidates = pickCandidateUrls(work);
  if (candidates.length === 0) {
    return { ok: false, exitCode: 1, message: 'open access, but OpenAlex lists no address for the full text' };
  }
  const best = work.best_oa_location ?? {};
  return {
    ok: true,
    candidates,
    metadata: {
      doi,
      title: work.title ?? work.display_name ?? '',
      year: work.publication_year ?? null,
      license: best.license ?? work.open_access.license ?? null,
      oaStatus: work.open_access.oa_status ?? null,
    },
  };
}

// Waits for the shared lock and the minimum interval between browser sessions of all processes.
async function withLock(fn) {
  for (;;) {
    try {
      mkdirSync(LOCK_DIR);
      break;
    } catch {
      try {
        if (Date.now() - statSync(LOCK_DIR).mtimeMs > STALE_LOCK_MS) rmdirSync(LOCK_DIR);
      } catch {}
      await sleep(1000);
    }
  }
  try {
    const last = existsSync(LAST_CALL_FILE) ? Number(readFileSync(LAST_CALL_FILE, 'utf8')) : 0;
    const wait = last + MIN_INTERVAL_MS - Date.now();
    if (wait > 0) await sleep(wait);
    return await fn();
  } finally {
    writeFileSync(LAST_CALL_FILE, String(Date.now()));
    rmdirSync(LOCK_DIR);
  }
}

function chromiumPath() {
  if (process.env.CHROMIUM_PATH) return process.env.CHROMIUM_PATH;
  const cache = join(homedir(), '.cache', 'ms-playwright');
  const dirs = existsSync(cache) ? readdirSync(cache).filter((d) => /^chromium-\d+$/.test(d)).sort().reverse() : [];
  for (const d of dirs) {
    const exe = join(cache, d, 'chrome-linux64', 'chrome');
    if (existsSync(exe)) return exe;
  }
  throw new Error('No Chromium found; set CHROMIUM_PATH or run "npx playwright install chromium".');
}

function openAlexKey() {
  if (process.env.OPENALEX_API_KEY) return process.env.OPENALEX_API_KEY.trim();
  const file = join(homedir(), '.config', 'cichlids', 'openalex.key');
  return existsSync(file) ? readFileSync(file, 'utf8').trim() : null;
}

async function fetchOpenAlexWork(doi) {
  const url = new URL(`https://api.openalex.org/works/doi:${doi}`);
  const key = openAlexKey();
  if (key) url.searchParams.set('api_key', key);
  const pauses = [10000, 30000];
  for (let attempt = 0; ; attempt++) {
    const res = await publicFetch(url.href, { timeoutMs: 30000 });
    if (res.status === 429 && attempt < pauses.length) {
      await sleep(pauses[attempt]);
      continue;
    }
    if (res.status === 404) throw new Error('OpenAlex does not know this DOI');
    if (!res.ok) throw new Error(`OpenAlex answered HTTP ${res.status}`);
    return res.json();
  }
}

const isPdfBuffer = (buf) => buf.length > 4 && buf.subarray(0, 5).toString('latin1') === '%PDF-';

// Plain request with the research user agent. Returns the status, content type and body.
async function plainFetch(url) {
  const res = await publicFetch(url, {
    headers: { 'user-agent': USER_AGENT, accept: 'application/pdf,text/html;q=0.9,*/*;q=0.8', 'accept-language': 'en' },
    redirect: 'follow',
    signal: AbortSignal.timeout(60000),
  });
  const buffer = Buffer.from(await res.arrayBuffer());
  return { status: res.status, contentType: res.headers.get('content-type') ?? '', buffer, finalUrl: res.url || url };
}

// Loads the address in a headed Chromium. A PDF answer is captured from the response (or the
// download); an HTML answer is returned after a bot check has passed on its own.
async function browserFetch(url, kind) {
  const { chromium } = await import('playwright-core');
  return withLock(async () => {
    const browser = await chromium.launch({
      executablePath: chromiumPath(),
      headless: false,
      args: ['--disable-blink-features=AutomationControlled'],
    });
    try {
      const context = await browser.newContext({ acceptDownloads: true });
      const page = await context.newPage();
      let pdf = null;
      page.on('response', async (res) => {
        try {
          if (!pdf && /pdf/i.test(res.headers()['content-type'] ?? '') && res.status() === 200) {
            const body = await res.body();
            if (isPdfBuffer(body)) pdf = body;
          }
        } catch {}
      });
      page.on('download', async (download) => {
        try {
          const path = await download.path();
          if (path && !pdf) {
            const body = readFileSync(path);
            if (isPdfBuffer(body)) pdf = body;
          }
        } catch {}
      });
      const navigation = await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => null);
      const httpStatus = navigation?.status() ?? 200;
      const deadline = Date.now() + 45000;
      while (Date.now() < deadline) {
        if (pdf) return { pdf, finalUrl: page.url() };
        const title = await page.title().catch(() => '');
        const body = await page.innerText('body').catch(() => '');
        const challenge = /^just a moment|attention required/i.test(title) || BLOCK_PAGE_MARKERS.some((re) => re.test(body.slice(0, 1500)));
        if (!challenge && body.length > 200 && kind !== 'pdf') break;
        await sleep(1500);
      }
      if (pdf) return { pdf, finalUrl: page.url() };
      return {
        httpStatus,
        html: await page.content(),
        title: await page.title().catch(() => ''),
        finalUrl: page.url(),
      };
    } finally {
      await browser.close();
    }
  });
}

// Downloads one OpenAlex-hosted copy. Returns {format, data, text} or null after printing why the
// route is skipped (budget used up, rate limit, missing content). The API key is never printed.
async function openAlexContent({ route, format, url }) {
  const target = new URL(url);
  const key = openAlexKey();
  if (key) target.searchParams.set('api_key', key);
  let res;
  try {
    res = await publicFetch(target.href, { timeoutMs: 120000 });
  } catch (e) {
    console.error(`fetch-oa: ${route}: request failed (${e.name})`);
    return null;
  }
  if (!res.ok) {
    const body = (await res.text().catch(() => '')).slice(0, 300);
    if (res.status === 429 || res.status === 402 || /insufficient budget/i.test(body)) {
      console.error(`fetch-oa: OpenAlex content download refused (HTTP ${res.status}, rate limit or daily budget used up), using the publisher route`);
    } else {
      console.error(`fetch-oa: ${route}: OpenAlex answered HTTP ${res.status}, using the next route`);
    }
    return null;
  }
  const buffer = Buffer.from(await res.arrayBuffer());
  if (format === 'pdf') {
    if (!isPdfBuffer(buffer)) return null;
    const text = await pdfText(buffer);
    return text.trim().length >= MIN_TEXT_LENGTH ? { format, data: buffer, text, route, url: (res.url || url).replace(/[?&]api_key=[^&]*/, '') } : null;
  }
  const xml = buffer.toString('utf8');
  const text = teiToText(xml);
  return text.length >= MIN_TEXT_LENGTH ? { format, data: buffer, text, route, url: (res.url || url).replace(/[?&]api_key=[^&]*/, '') } : null;
}

class Refusal extends Error {
  constructor(exitCode, message) {
    super(message);
    this.exitCode = exitCode;
  }
}

// Tries one address. Returns {format: 'pdf'|'html', data, text, url} or null when the address gave
// no usable text. Throws Refusal(4) when the answer asks for a login or a payment.
async function tryCandidate({ url, kind }) {
  let answer = null;
  let fetched = null;
  try {
    fetched = await plainFetch(url);
  } catch (e) {
    console.error(`fetch-oa: plain request failed (${e.message})`);
  }
  if (fetched) {
    const asText = /html|text/i.test(fetched.contentType) ? fetched.buffer.toString('utf8') : '';
    if (fetched.status < 400 && isPdfBuffer(fetched.buffer)) {
      answer = { format: 'pdf', data: fetched.buffer, url: fetched.finalUrl, contentType: fetched.contentType, httpStatus: fetched.status, via: 'fetch' };
    } else if (!looksBlocked(fetched.status, fetched.contentType, asText, { expectPdf: kind === 'pdf' }) && fetched.status < 400) {
      answer = { format: 'html', data: fetched.buffer, url: fetched.finalUrl, contentType: fetched.contentType, httpStatus: fetched.status, via: 'fetch' };
    }
  }
  if (!answer) {
    console.error('fetch-oa: plain request blocked, retrying in a browser');
    const b = await browserFetch(url, kind);
    if (b.pdf) answer = { format: 'pdf', data: b.pdf, url: b.finalUrl, via: 'browser' };
    else answer = { format: 'html', data: b.html, url: b.finalUrl, httpStatus: b.httpStatus, via: 'browser' };
  }
  if (answer.format === 'pdf') {
    const text = await pdfText(answer.data);
    return text.trim().length >= MIN_TEXT_LENGTH ? { ...answer, text } : null;
  }
  const { title, text } = extractHtmlText(answer.data);
  if (looksLikeLoginOrPaywall(title, text, answer.url)) throw new Refusal(4, 'blocked by login or paywall: not fetched');
  const reason = contentIssue({ url, finalUrl: answer.url, httpStatus: answer.httpStatus ?? 200, contentType: 'text/html', body: answer.data, text });
  if (reason) { recordFailure(url, reason); return null; }
  return text.length >= MIN_TEXT_LENGTH ? { ...answer, text } : null;
}

function summary(meta) {
  return [
    `archived: ${meta.archived}`,
    `title: ${meta.title}`,
    `doi: ${meta.doi}`,
    `year: ${meta.year ?? 'unknown'}`,
    `license: ${meta.license ?? 'unknown'}`,
    `open access: ${meta.oaStatus ?? 'unknown'}`,
    `route: ${meta.route ?? 'unknown'}`,
    `source: ${(meta.url ?? 'unknown').replace(/[?&]api_key=[^&]*/, '')}`,
  ].join('\n');
}

function report(meta, txtPath) {
  const text = readFileSync(txtPath, 'utf8');
  console.log(summary(meta));
  console.log(`text file: ${txtPath}`);
  console.log(`\n${text.slice(0, PREVIEW_LENGTH)}${text.length > PREVIEW_LENGTH ? '\n[...]' : ''}`);
}

// DOI references stay independent of publisher and download route.
export async function archiveFullText(got, meta, options = {}) {
  const url = `https://doi.org/${normaliseDoi(meta.doi)}`;
  const contentType = got.contentType || ({ pdf: 'application/pdf', xml: 'application/xml', html: 'text/html' }[got.format] ?? 'application/octet-stream');
  const input = { url, finalUrl: got.url ?? meta.url ?? url, contentType, body: got.data, text: got.text, httpStatus: got.httpStatus ?? 200, fetchedAt: meta.fetchedAt, tool: 'fetch-oa', version: '1.0' };
  const reason = contentIssue(input);
  if (reason) { recordFailure(url, reason, options); throw new Error(reason); }
  const archiveOptions = { ...options, pathSegments: [cacheKey(meta.doi)] };
  const cached = await findSnapshot(url, archiveOptions);
  const sha256 = createHash('sha256').update(got.data).digest('hex');
  return cached?.meta.sha256 === sha256 ? cached : storeSnapshot(input, archiveOptions);
}

async function archiveLibraryEntry(base, meta, options) {
  const format = ['xml', 'pdf', 'html'].find(ext => existsSync(`${base}.${ext}`));
  if (!format) throw new Error('Library entry has no original full text');
  const got = { format, data: readFileSync(`${base}.${format}`), text: readFileSync(`${base}.txt`, 'utf8'), url: meta.url };
  const snapshot = await archiveFullText(got, meta, options);
  const updated = { ...meta, archived: snapshot.ref };
  writeFileSync(`${base}.json`, `${JSON.stringify(updated, null, 2)}\n`);
  return updated;
}

export async function migrateLibrary({ libraryDir = LIBRARY_DIR, ...options } = {}) {
  const snapshots = [];
  const failures = [];
  if (!existsSync(libraryDir)) return { snapshots, failures };
  for (const file of readdirSync(libraryDir).filter(f => /\.(xml|pdf|html)$/.test(f))) {
    const base = join(libraryDir, file.replace(/\.(xml|pdf|html)$/, ''));
    if (snapshots.some(s => s.base === base) || failures.some(f => f.base === base)) continue;
    try {
      const meta = existsSync(`${base}.json`) ? JSON.parse(readFileSync(`${base}.json`, 'utf8')) : {};
      meta.doi ??= file.replace(/\.(xml|pdf|html)$/, '').replace(/^(10\.\d{4,9})_/, '$1/');
      if (!/^10\.\d{4,9}\/\S+$/.test(meta.doi)) throw new Error('Library entry lacks a valid DOI');
      // Extraction from the original also supports entries without a text sidecar.
      if (!existsSync(`${base}.txt`)) {
        const format = file.split('.').at(-1);
        const data = readFileSync(join(libraryDir, file));
        const text = format === 'pdf' ? await pdfText(data) : format === 'xml' ? teiToText(data.toString('utf8')) : extractHtmlText(data.toString('utf8')).text;
        const snapshot = await archiveFullText({ format, data, text }, meta, options);
        const updated = { ...meta, archived: snapshot.ref };
        writeFileSync(`${base}.json`, `${JSON.stringify(updated, null, 2)}\n`);
        snapshots.push({ base, ref: snapshot.ref });
      } else {
        const updated = await archiveLibraryEntry(base, meta, options);
        snapshots.push({ base, ref: updated.archived });
      }
    } catch (e) { failures.push({ base, reason: e.message }); }
  }
  return { snapshots, failures };
}

export async function main(argv, { libraryDir = LIBRARY_DIR, ...archiveOptions } = {}) {
  if (argv[0] === '--migrate-library' && argv.length === 1) {
    const result = await migrateLibrary({ libraryDir, ...archiveOptions });
    for (const snapshot of result.snapshots) console.log(snapshot.ref);
    for (const failure of result.failures) console.error(`fetch-oa: ${failure.base}: ${failure.reason}`);
    return result.failures.length ? 1 : 0;
  }
  const input = argv[0];
  const doi = normaliseDoi(input);
  if (!/^10\.\d{4,9}\/\S+$/.test(doi)) {
    console.error('Usage: xvfb-run -a node tools/species-data/fetch-oa.mjs <doi or https://doi.org/... URL>');
    return 2;
  }
  const key = cacheKey(doi);
  const base = join(libraryDir, key);
  if (existsSync(`${base}.txt`)) {
    try {
      const meta = existsSync(`${base}.json`) ? JSON.parse(readFileSync(`${base}.json`, 'utf8')) : { doi };
      meta.doi ??= doi;
      const updated = await archiveLibraryEntry(base, meta, archiveOptions);
      report(updated, `${base}.txt`);
      return 0;
    } catch (e) { console.error(`fetch-oa: ${e.message}`); return 1; }
  }
  let work;
  try {
    work = await fetchOpenAlexWork(doi);
  } catch (e) {
    console.error(`fetch-oa: ${e.message}`);
    return 1;
  }
  const assessment = assessOpenAlexWork(work, doi);
  if (!assessment.ok) {
    console.error(assessment.message);
    return assessment.exitCode;
  }
  const store = async (got, url) => {
    const meta = { ...assessment.metadata, url, format: got.format, route: got.route, fetchedAt: new Date().toISOString() };
    const snapshot = await archiveFullText(got, meta, archiveOptions);
    meta.archived = snapshot.ref;
    mkdirSync(libraryDir, { recursive: true });
    writeFileSync(`${base}.${got.format}`, got.data);
    writeFileSync(`${base}.txt`, got.text);
    writeFileSync(`${base}.json`, `${JSON.stringify(meta, null, 2)}\n`);
    report(meta, `${base}.txt`);
  };
  for (const route of openAlexKey() ? pickOpenAlexContent(work) : []) {
    const got = await openAlexContent(route);
    if (got) {
      try { await store(got, route.url); }
      catch (e) { console.error(`fetch-oa: ${e.message}`); continue; }
      return 0;
    }
  }
  let loginSeen = false;
  for (const candidate of assessment.candidates) {
    try {
      const got = await tryCandidate(candidate);
      if (!got) continue;
      await store({ ...got, route: got.via === 'browser' ? 'publisher-browser' : 'publisher-fetch' }, candidate.url);
      return 0;
    } catch (e) {
      if (e instanceof Refusal) loginSeen = true;
      else console.error(`fetch-oa: ${candidate.url}: ${e.message}`);
    }
  }
  if (loginSeen) {
    console.error('blocked by login or paywall: not fetched');
    return 4;
  }
  console.error('fetch-oa: no open-access address delivered usable text');
  return 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await main(process.argv.slice(2));
}
