#!/usr/bin/env node
// Reads the Cichlid Room Companion (cichlidae.com) for the species research agents.
// The site sits behind a Cloudflare bot check that blocks curl and headless browsers; a headed
// Chromium on a virtual display passes it. Every call starts its own browser, so parallel agents
// do not share state. Calls of all processes share a lock and a minimum interval, and page texts are
// cached, because the site rate-limits frequent requests (Cloudflare error 1015).
//
//   xvfb-run -a node tools/species-data/crc.mjs search "<scientific name>"
//   xvfb-run -a node tools/species-data/crc.mjs page "<cichlidae.com URL>"
//
// search prints the matching species pages as "name -> URL"; page prints the readable text of a
// page followed by its subpage links (pictures, map, bibliography, articles) and keeps a snapshot
// of the page in the source archive, whose reference it prints as "ARCHIVED: <ref>".
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmdirSync, statSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';
import { browserProxyOptions } from './browser-proxy.mjs';
import { safeMkdir, safeWriteFile } from './safe-write.mjs';
import { findSnapshot, storeSnapshot } from './source-archive.mjs';

const BASE = 'https://cichlidae.com';
const LOCK_DIR = join(tmpdir(), 'cichlids-crc.lock');
const LAST_CALL_FILE = join(tmpdir(), 'cichlids-crc.last');
const MIN_INTERVAL_MS = 15000;
const STALE_LOCK_MS = 180000;
// The base of these writes is the plain path <repo>/data/species/work. The tool runs inside the
// agent cage, where that path leads to the work directory that the cage binds after its
// validation. Outside the cage nothing validates the path (in a worktree it is a symlink that an
// agent can retarget), so a manual run there needs a check of the link first.
const CACHE_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'data', 'species', 'work', '.crc-cache');
const CACHE_MAX_AGE_MS = 30 * 24 * 3600 * 1000;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Serialises the browser sessions of all processes and keeps MIN_INTERVAL_MS between them.
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

function cachePath(command, arg) {
  return join(CACHE_DIR, createHash('sha1').update(`${command}\n${arg}`).digest('hex') + '.txt');
}

function readCache(command, arg) {
  const p = cachePath(command, arg);
  if (existsSync(p) && Date.now() - statSync(p).mtimeMs < CACHE_MAX_AGE_MS) return readFileSync(p, 'utf8');
  return null;
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

async function passCheck(page) {
  const title = await page.title().catch(() => '');
  const text = await page.innerText('body').catch(() => '');
  if (/1015|rate limited/i.test(title + text.slice(0, 500))) throw new RateLimited();
  await page.waitForFunction(
    () => !document.title.startsWith('Just a moment') && document.body && document.body.innerText.length > 200,
    null,
    { timeout: 45000 },
  );
}

class RateLimited extends Error {
  constructor() {
    super('the site rate-limits requests (Cloudflare error 1015)');
  }
}

async function search(page, query, out) {
  await page.goto(`${BASE}/search.php`, { waitUntil: 'domcontentloaded' });
  await passCheck(page);
  await page.fill('input[name=query]', query);
  await Promise.all([page.waitForNavigation(), page.press('input[name=query]', 'Enter')]);
  await passCheck(page);
  // Search hits carry the page title as link text; the sidebar links carry bare names.
  const hits = await page.$$eval('a[href*="species.php"], a[href*="genus.php"], a[href*="article.php"]', (as) =>
    as.map((a) => [a.textContent.trim(), a.href]).filter(([t]) => t.endsWith('| Cichlid Room Companion')),
  );
  const seen = new Set();
  for (const [title, href] of hits) {
    if (seen.has(href)) continue;
    seen.add(href);
    out.push(`${title.replace(/\s*\|\s*Cichlid Room Companion$/, '')} -> ${href}`);
  }
  if (seen.size === 0) out.push('No results.');
}

async function read(page, url, out, snapshot) {
  if (!/^https:\/\/(www\.)?cichlidae\.com\//.test(url)) throw new Error('page only reads cichlidae.com URLs.');
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await passCheck(page);
  snapshot.finalUrl = page.url();
  snapshot.html = await page.content();
  snapshot.text = await page.innerText('body');
  out.push(`URL: ${page.url()}`);
  out.push(`TITLE: ${await page.title()}\n`);
  out.push(await page.innerText('body'));
  const links = await page.$$eval('a', (as) =>
    as
      .filter((a) => /content=|article\.php/.test(a.href) && a.textContent.trim())
      .map((a) => `${a.textContent.trim()} -> ${a.href}`),
  );
  out.push('\nLINKS:');
  out.push([...new Set(links)].join('\n'));
}

const [command, arg] = process.argv.slice(2);
if (!['search', 'page'].includes(command) || !arg) {
  console.error('Usage: xvfb-run -a node tools/species-data/crc.mjs search "<name>" | page "<url>"');
  process.exit(2);
}

async function fetchOnce() {
  const browser = await chromium.launch({
    executablePath: chromiumPath(),
    headless: false,
    args: ['--disable-blink-features=AutomationControlled'],
    ...browserProxyOptions(),
  });
  try {
    const page = await browser.newPage();
    const out = [];
    if (command === 'search') await search(page, arg, out);
    else await read(page, arg, out, lastSnapshot);
    return out.join('\n');
  } finally {
    await browser.close();
  }
}

// The page that the last browser call read, kept for the source archive.
const lastSnapshot = {};

// A page served from the cache is archived with its cached text when no snapshot exists.
async function archive(cachedText) {
  if (command !== 'page') return;
  try {
    let found = await findSnapshot(arg);
    if (!found && lastSnapshot.html) {
      found = await storeSnapshot({ url: arg, finalUrl: lastSnapshot.finalUrl, contentType: 'text/html', body: lastSnapshot.html, text: lastSnapshot.text, tool: 'crc' });
    } else if (!found && cachedText) {
      found = await storeSnapshot({ url: arg, contentType: 'text/plain', body: cachedText, text: cachedText, tool: 'crc-cache' });
    }
    if (found) console.log(`ARCHIVED: ${found.ref}`);
  } catch (e) {
    console.error(`crc: archive: ${e.message}`);
  }
}

const cached = readCache(command, arg);
if (cached !== null) {
  console.log(cached);
  await archive(cached);
} else {
  // A rate limit lifts after a while; two retries with growing pauses cover short blocks.
  const pauses = [60000, 120000];
  for (let attempt = 0; ; attempt++) {
    try {
      const text = await withLock(fetchOnce);
      // data/species/work is the trusted base of the cache writes; a symlink below it is refused.
      safeMkdir(CACHE_DIR, dirname(CACHE_DIR));
      safeWriteFile(cachePath(command, arg), text, dirname(CACHE_DIR));
      console.log(text);
      await archive();
      break;
    } catch (e) {
      if (e instanceof RateLimited && attempt < pauses.length) {
        console.error(`crc: ${e.message}, retrying in ${pauses[attempt] / 1000} s`);
        await sleep(pauses[attempt]);
        continue;
      }
      console.error(`crc: ${e.message}`);
      process.exitCode = 1;
      break;
    }
  }
}
