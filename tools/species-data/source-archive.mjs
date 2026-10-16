// Internal immutable research snapshots. Public fetches share a per-host process lock. The archive
// root is a write root of the agent cage, so every write below it refuses a symlink (safe-write.mjs).
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { safeAppendFile, safeMkdir, safeWriteFile } from './safe-write.mjs';
import { extractHtmlText, looksBlocked, looksLikeLoginOrPaywall, pdfText, teiToText } from './source-content.mjs';

export const USER_AGENT = 'cichlids.com-species-research/1.0';
export const VERSION = '1.0';
export const DEFAULT_ARCHIVE_ROOT = '/workspaces/cichlids-source-archive';
export const archiveRoot = (env = process.env) => resolve(env.CICHLIDS_SOURCE_ARCHIVE || DEFAULT_ARCHIVE_ROOT);
const hash = data => createHash('sha256').update(data).digest('hex');
const sleep = ms => new Promise(r => setTimeout(r, ms));

function publicUrl(input) {
  const u = new URL(input);
  if (!['http:', 'https:'].includes(u.protocol)) throw new Error('Only HTTP and HTTPS URLs are supported');
  if (u.username || u.password) throw new Error('URL credentials are not permitted');
  return u;
}
function segment(s) {
  try { s = decodeURIComponent(s); } catch {}
  s = s.toLowerCase().replace(/[^a-z0-9._-]/g, '_').slice(0, 80);
  return !s || s === '.' || s === '..' ? '_root' : s;
}
export function archivePath(url, pathSegments) {
  const u = publicUrl(url);
  const segments = pathSegments ?? u.pathname.split('/').filter(Boolean);
  const out = [segment(u.hostname.replace(/^www\./, '')), ...(segments.length ? segments.map(segment) : ['_root'])];
  if (u.search) out.push(`q-${hash(u.search.slice(1)).slice(0, 8)}`);
  return out.join('/');
}
const timestamp = date => date.toISOString().replace(/:/g, '').replace(/\.\d{3}Z$/, 'Z');
export function validArchiveRef(ref) {
  if (typeof ref !== 'string') return false;
  const parts = ref.split('/');
  if (parts.length < 3 || !parts.slice(0, -1).every(p => /^[a-z0-9._-]{1,80}$/.test(p) && p !== '.' && p !== '..')) return false;
  const m = /^(\d{4}-\d{2}-\d{2})T(\d{2})(\d{2})(\d{2})Z$/.exec(parts.at(-1));
  if (!m) return false;
  const date = new Date(`${m[1]}T${m[2]}:${m[3]}:${m[4]}Z`);
  return Number.isFinite(+date) && timestamp(date) === parts.at(-1);
}

// Directory locks record the owner PID. Dead owners are recovered without taking a live lock.
async function withFileLock(name, fn) {
  const dir = join(tmpdir(), `cichlids-source-${hash(name)}.lock`);
  const owner = join(dir, 'owner');
  const deadline = Date.now() + 300000;
  for (;;) {
    try { mkdirSync(dir); writeFileSync(owner, String(process.pid)); break; }
    catch (e) {
      if (e.code !== 'EEXIST') throw e;
      let abandoned = false;
      try {
        const pid = Number(readFileSync(owner, 'utf8'));
        try { process.kill(pid, 0); } catch (err) { abandoned = err.code === 'ESRCH'; }
      } catch {
        try { abandoned = Date.now() - statSync(dir).mtimeMs > 10000; } catch { continue; }
      }
      if (abandoned) { rmSync(dir, { recursive: true, force: true }); continue; }
      if (Date.now() >= deadline) throw new Error('Timeout waiting for source archive lock');
      await sleep(50);
    }
  }
  try { return await fn(); } finally { rmSync(dir, { recursive: true, force: true }); }
}

// Redirects are separate public requests, each obeying the destination host's rate limit.
export async function publicFetch(url, { fetchImpl = fetch, timeoutMs = 60000, ...options } = {}) {
  let target = publicUrl(url).href;
  const signal = options.signal ?? AbortSignal.timeout(timeoutMs);
  for (let n = 0; n <= 10; n++) {
    if (signal.aborted) throw signal.reason;
    // Hostname, excluding port and www, shares a limit across tools and processes.
    const host = publicUrl(target).hostname.replace(/^www\./, '');
    const lastFile = join(tmpdir(), `cichlids-source-${hash(host)}.last`);
    const res = await withFileLock(`host:${host}`, async () => {
      const last = existsSync(lastFile) ? Number(readFileSync(lastFile, 'utf8')) : 0;
      const wait = last + 2000 - Date.now();
      if (wait > 0) await sleep(wait);
      if (signal.aborted) throw signal.reason;
      writeFileSync(lastFile, String(Date.now()));
      return fetchImpl(target, { ...options, headers: { ...options.headers, 'user-agent': USER_AGENT }, signal, redirect: 'manual' });
    });
    if (![301, 302, 303, 307, 308].includes(res.status) || !res.headers.get('location')) return res;
    const next = new URL(res.headers.get('location'), target).href;
    await res.body?.cancel();
    target = publicUrl(next).href;
  }
  throw new Error('Too many redirects');
}

// Check both original markup and readable text; browser error documents can have substantial text.
export function contentIssue({ url, finalUrl = url, httpStatus = 200, contentType = 'text/html', body = '', text = '' }) {
  if (httpStatus >= 400) return `HTTP ${httpStatus}`;
  const raw = Buffer.isBuffer(body) ? body.toString('utf8') : String(body);
  const { title } = extractHtmlText(raw);
  const isMarkup = /html|xml|text|json/i.test(contentType) || /<(?:!doctype|html|title|body)\b/i.test(raw);
  if (/pdf/i.test(contentType) && !raw.startsWith('%PDF-')) return 'Invalid PDF body or error page';
  if (/^(chrome|edge|about):/i.test(finalUrl ?? '') ||
      (isMarkup && (/\b(?:NET::ERR_[A-Z_]+|ERR_(?:CERT|SSL|CONNECTION|NAME_NOT_RESOLVED|TIMED_OUT)[A-Z_]*)\b/.test(raw) ||
       /^(privacy error|certificate error|your connection is not private|this site can.t be reached|service unavailable|internal server error|access denied|page not found|404 not found)\b/i.test(title)))) return 'Certificate or browser/server error page';
  if (looksBlocked(httpStatus, isMarkup ? 'text/html' : contentType, raw)) return 'Bot check, captcha or challenge page';
  if (looksLikeLoginOrPaywall(title, text, finalUrl)) return 'Login, paywall or captcha page';
  if (text.trim().length < 200) return 'Readable text is shorter than 200 characters';
  return null;
}

export function recordFailure(url, reason, { root = archiveRoot() } = {}) {
  safeMkdir(root, root);
  safeAppendFile(join(root, 'failures.jsonl'), `${JSON.stringify({ url, time: new Date().toISOString(), reason })}\n`, root);
}
function extension(type) {
  if (/pdf/i.test(type)) return 'pdf';
  if (/html/i.test(type)) return 'html';
  if (/xml/i.test(type)) return 'xml';
  return 'bin';
}
async function extractedText(body, type) {
  const ext = extension(type);
  if (ext === 'pdf') return pdfText(body);
  const raw = body.toString('utf8');
  if (ext === 'html') return extractHtmlText(raw).text;
  if (ext === 'xml') return /<TEI\b/i.test(raw) ? teiToText(raw) : extractHtmlText(raw).text;
  return raw;
}

export function resolveSnapshot(ref, { root = archiveRoot() } = {}) {
  if (!validArchiveRef(ref)) throw new Error('Invalid archive reference');
  const dir = join(root, ref);
  const meta = JSON.parse(readFileSync(join(dir, 'meta.json'), 'utf8'));
  return { ref, meta, textPath: join(dir, 'text.txt'), originalPath: join(dir, `original.${extension(meta.contentType)}`) };
}

function isParquet(body) {
  return Buffer.isBuffer(body) && body.length >= 12 &&
    body.subarray(0, 4).toString() === 'PAR1' && body.subarray(-4).toString() === 'PAR1';
}

export async function findSnapshot(url, { root = archiveRoot(), pathSegments } = {}) {
  const base = archivePath(url, pathSegments);
  const dir = join(root, base);
  const candidates = existsSync(dir) ? readdirSync(dir).sort().reverse().map(stamp => `${base}/${stamp}`) : [];
  // DOI full texts use a single DOI segment; ordinary page fetches can reuse them through the index.
  const index = join(root, 'index.jsonl');
  if (existsSync(index)) {
    for (const line of readFileSync(index, 'utf8').trim().split('\n').reverse()) {
      try {
        const entry = JSON.parse(line);
        if (entry.url === url) candidates.push(entry.ref);
      } catch {}
    }
  }
  for (const ref of new Set(candidates)) {
    if (!validArchiveRef(ref)) continue;
    try {
      const meta = JSON.parse(readFileSync(join(root, ref, 'meta.json'), 'utf8'));
      if (meta.url !== url || !existsSync(join(root, ref, 'text.txt')) || !existsSync(join(root, ref, `original.${extension(meta.contentType)}`))) continue;
      return { ref, textPath: join(root, ref, 'text.txt'), meta };
    } catch {}
  }
  return null;
}

export async function storeSnapshot(input, { root = archiveRoot(), pathSegments } = {}) {
  const { url, finalUrl = url, contentType = 'application/octet-stream', httpStatus = 200, tool = 'archive-fetch', version = VERSION } = input;
  try {
    const body = Buffer.isBuffer(input.body) ? input.body : Buffer.from(input.body ?? '');
    const parquet = isParquet(body);
    const text = parquet ? `Parquet binary dataset (${body.length} bytes).` : input.text ?? await extractedText(body, contentType);
    const reason = contentIssue({ url, finalUrl, contentType, httpStatus, body, text });
    if (reason && !(parquet && reason === 'Readable text is shorter than 200 characters')) throw new Error(reason);
    const fetchedAt = new Date(input.fetchedAt ?? Date.now()).toISOString();
    const base = archivePath(url, pathSegments);
    return await withFileLock(`snapshot:${resolve(root)}:${base}`, async () => {
      safeMkdir(join(root, base), root);
      let date = new Date(fetchedAt);
      while (existsSync(join(root, base, timestamp(date)))) date = new Date(+date + 1000);
      const ref = `${base}/${timestamp(date)}`;
      const temp = join(root, base, `.writing-${randomUUID()}`);
      const meta = { url, finalUrl, fetchedAt, httpStatus, contentType, sha256: hash(body), bytes: body.length, tool, version };
      safeMkdir(temp, root);
      try {
        safeWriteFile(join(temp, `original.${extension(contentType)}`), body, root);
        safeWriteFile(join(temp, 'text.txt'), text, root);
        safeWriteFile(join(temp, 'meta.json'), `${JSON.stringify(meta, null, 2)}\n`, root);
        renameSync(temp, join(root, ref));
        safeAppendFile(join(root, 'index.jsonl'), `${JSON.stringify({ ref, url, fetchedAt, httpStatus })}\n`, root);
      } finally { rmSync(temp, { recursive: true, force: true }); }
      return { ref, textPath: join(root, ref, 'text.txt'), meta };
    });
  } catch (e) {
    recordFailure(url, e.message, { root });
    e.archiveFailureRecorded = true;
    throw e;
  }
}

export async function archiveUrl(url, options = {}) {
  const { root = archiveRoot(), refresh = false } = options;
  try {
    publicUrl(url);
    // Serializing identical URLs prevents duplicate network work by simultaneous agents.
    return await withFileLock(`url:${resolve(root)}:${url}`, async () => {
      if (!refresh) {
        const cached = await findSnapshot(url, options);
        if (cached) return cached;
      }
      const res = await publicFetch(url, options);
      if (res.status >= 400) { await res.body?.cancel(); throw new Error(`HTTP ${res.status}`); }
      const body = Buffer.from(await res.arrayBuffer());
      return storeSnapshot({ url, finalUrl: res.url || url, httpStatus: res.status, contentType: res.headers.get('content-type') ?? 'application/octet-stream', body }, options);
    });
  } catch (e) {
    if (!e.archiveFailureRecorded) recordFailure(url, e.message, { root });
    throw e;
  }
}
