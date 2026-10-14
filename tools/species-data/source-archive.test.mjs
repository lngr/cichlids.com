import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { archivePath, archiveUrl, storeSnapshot, findSnapshot, validArchiveRef, contentIssue, USER_AGENT } from './source-archive.mjs';
const run = promisify(execFile);
const prose = 'Cichlids occupy rocky habitats and feed on invertebrates. '.repeat(12);
const html = `<html><title>Fish ecology</title><script>secret()</script><style>hidden</style><body><article><p>${prose}</p></article></body></html>`;
function sandbox(t) {
  const root = mkdtempSync(join(tmpdir(), 'cichlids-archive-test-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  return root;
}
async function server(t, handler) {
  const s = createServer(handler);
  await new Promise(r => s.listen(0, '127.0.0.1', r));
  t.after(() => new Promise(r => s.close(r)));
  return `http://127.0.0.1:${s.address().port}`;
}

test('Given a URL, its archive layout is readable, bounded and traversal safe', () => {
  assert.equal(archivePath('https://www.seriouslyfish.com/species/Neolamprologus-Leleupi/'), 'seriouslyfish.com/species/neolamprologus-leleupi');
  assert.equal(archivePath('https://example.org/'), 'example.org/_root');
  const query = 'species=leleupi&lang=en';
  assert.equal(archivePath(`https://example.org/?${query}`), `example.org/_root/q-${createHash('sha256').update(query).digest('hex').slice(0, 8)}`);
  assert.equal(archivePath('https://example.org/a%2Fb/%2E%2E%2Fx'), 'example.org/a_b/.._x');
  assert.ok(archivePath(`https://example.org/${'a'.repeat(200)}`).split('/').every(s => s.length <= 80));
  assert.throws(() => archivePath('file:///etc/passwd'), /http/i);
  assert.throws(() => archivePath('https://user:password@example.org/'), /credential/i);
});

test('Given successful content, a snapshot keeps exact bytes, text, metadata and an index line', async t => {
  const root = sandbox(t);
  const url = 'https://www.example.org/fish';
  const snap = await storeSnapshot({ url, finalUrl: 'https://example.org/fish', body: Buffer.from(html), contentType: 'text/html; charset=utf-8', httpStatus: 200, fetchedAt: '2026-10-14T09:30:00.000Z', tool: 'archive-fetch', version: '1.0' }, { root });
  assert.equal(snap.ref, 'example.org/fish/2026-10-14T093000Z');
  assert.equal(readFileSync(join(root, snap.ref, 'original.html'), 'utf8'), html);
  assert.equal(readFileSync(snap.textPath, 'utf8'), prose.trim());
  const meta = JSON.parse(readFileSync(join(root, snap.ref, 'meta.json')));
  assert.deepEqual(meta, { url, finalUrl: 'https://example.org/fish', fetchedAt: '2026-10-14T09:30:00.000Z', httpStatus: 200, contentType: 'text/html; charset=utf-8', sha256: createHash('sha256').update(html).digest('hex'), bytes: Buffer.byteLength(html), tool: 'archive-fetch', version: '1.0' });
  assert.deepEqual(JSON.parse(readFileSync(join(root, 'index.jsonl'), 'utf8')), { ref: snap.ref, url, fetchedAt: meta.fetchedAt, httpStatus: 200 });
  assert.equal((await findSnapshot(url, { root })).ref, snap.ref);
});

test('Given colliding timestamps or sanitized paths, snapshots do not overwrite each other', async t => {
  const root = sandbox(t);
  const a = await storeSnapshot({ url: 'https://example.org/FISH', body: html, contentType: 'text/html', fetchedAt: '2026-01-01T00:00:00Z' }, { root });
  const b = await storeSnapshot({ url: 'https://example.org/fish', body: html + ' more', contentType: 'text/html', fetchedAt: '2026-01-01T00:00:00Z' }, { root });
  assert.notEqual(a.ref, b.ref);
  assert.equal((await findSnapshot('https://example.org/FISH', { root })).ref, a.ref);
  assert.equal(readFileSync(join(root, a.ref, 'original.html'), 'utf8'), html);
});

for (const [name, input, reason] of [
  ['HTTP error', { httpStatus: 404, text: prose }, /HTTP 404/],
  ['challenge', { body: `<title>Just a moment...</title>${prose}`, text: prose }, /bot|challenge/i],
  ['captcha', { body: `<title>Captcha verification</title>${prose}`, text: prose }, /captcha|login/i],
  ['login', { body: `<title>Sign in</title>${prose}`, text: prose }, /login|paywall/i],
  ['paywall', { body: `<title>Purchase this article</title>${prose}`, text: prose }, /login|paywall/i],
  ['certificate error', { body: `<title>Privacy error</title><body>NET::ERR_CERT_AUTHORITY_INVALID ${prose}</body>`, text: prose }, /certificate|error page/i],
  ['browser error', { body: `<title>This site can’t be reached</title>${prose}`, text: prose }, /error page/i],
  ['short page', { body: '<p>Fish.</p>', text: 'Fish.' }, /200|short/i],
]) {
  test(`Given a ${name}, no snapshot is stored and a failure is logged`, async t => {
    const root = sandbox(t);
    await assert.rejects(storeSnapshot({ url: 'https://example.org/paper', contentType: 'text/html', body: html, ...input }, { root }), reason);
    assert.equal(existsSync(join(root, 'index.jsonl')), false);
    const failure = JSON.parse(readFileSync(join(root, 'failures.jsonl'), 'utf8'));
    assert.equal(failure.url, 'https://example.org/paper');
    assert.ok(failure.time);
    assert.match(failure.reason, reason);
  });
}

test('Access checks permit an article mentioning certificates, login and subscriptions', () => {
  assert.equal(contentIssue({ url: 'https://example.org/article', contentType: 'text/html', body: `<title>Certificate analysis in fisheries</title><article>${prose.repeat(5)} login subscription certificate</article>`, text: prose.repeat(5) }), null);
});

test('XML and binary originals use their content types and readable extraction', async t => {
  const root = sandbox(t);
  for (const [type, ext, body] of [['application/xml', 'xml', `<article><p>${prose}</p></article>`], ['text/plain', 'bin', prose]]) {
    const s = await storeSnapshot({ url: `https://example.org/${ext}`, contentType: type, body }, { root });
    assert.equal(readFileSync(join(root, s.ref, `original.${ext}`), 'utf8'), body);
    assert.match(readFileSync(s.textPath, 'utf8'), /Cichlids/);
  }
});

test('Given a broken PDF, extraction fails without a snapshot', async t => {
  const root = sandbox(t);
  await assert.rejects(storeSnapshot({ url: 'https://example.org/p.pdf', contentType: 'application/pdf', body: '%PDF-broken' }, { root }));
  assert.equal(existsSync(join(root, 'index.jsonl')), false);
  assert.equal(existsSync(join(root, 'failures.jsonl')), true);
});

test('Given a network or certificate failure, it is logged without a snapshot', async t => {
  const root = sandbox(t);
  for (const msg of ['network unavailable', 'certificate verification failed']) {
    await assert.rejects(archiveUrl('https://example.org/fail', { root, fetchImpl: async () => { throw new Error(msg); } }), new RegExp(msg));
  }
  assert.equal(readFileSync(join(root, 'failures.jsonl'), 'utf8').trim().split('\n').length, 2);
});

test('Given a hanging request, timeout logs a failure', async t => {
  const root = sandbox(t);
  const url = await server(t, () => {});
  await assert.rejects(archiveUrl(url, { root, timeoutMs: 50 }), /timeout|aborted/i);
  assert.ok(existsSync(join(root, 'failures.jsonl')));
});

test('Given a public URL, the CLI archives redirects, reuses without requests and refreshes', async t => {
  const root = sandbox(t);
  let requests = 0;
  const url = await server(t, (req, res) => {
    requests++;
    assert.equal(req.headers['user-agent'], USER_AGENT);
    if (req.url === '/start') { res.writeHead(302, { location: '/article' }); res.end(); }
    else { res.setHeader('content-type', 'text/html'); res.end(html); }
  });
  const env = { ...process.env, CICHLIDS_SOURCE_ARCHIVE: root };
  const cli = fileURLToPath(new URL('./archive-fetch.mjs', import.meta.url));
  const first = await run(process.execPath, [cli, `${url}/start`], { env });
  const ref = first.stdout.split('\n')[0];
  assert.equal(validArchiveRef(ref), true);
  assert.ok(first.stdout.includes(join(root, ref, 'text.txt')));
  assert.ok(first.stdout.includes(prose.trim()));
  assert.equal(JSON.parse(readFileSync(join(root, ref, 'meta.json'))).finalUrl, `${url}/article`);
  assert.equal(requests, 2);
  await run(process.execPath, [cli, `${url}/start`], { env });
  assert.equal(requests, 2);
  await run(process.execPath, [cli, `${url}/start`, '--refresh'], { env });
  assert.equal(requests, 4);
  assert.equal(readFileSync(join(root, 'index.jsonl'), 'utf8').trim().split('\n').length, 2);
});

test('Given parallel CLI processes, same-host request starts are separated by two seconds', async t => {
  const root = sandbox(t);
  const times = [];
  const url = await server(t, (req, res) => { times.push(Date.now()); res.setHeader('content-type', 'text/html'); res.end(html); });
  const cli = fileURLToPath(new URL('./archive-fetch.mjs', import.meta.url));
  await Promise.all(['/a', '/b'].map(path => run(process.execPath, [cli, url + path], { env: { ...process.env, CICHLIDS_SOURCE_ARCHIVE: root } })));
  assert.equal(times.length, 2);
  assert.ok(times[1] - times[0] >= 1950, String(times));
});

test('Given an error response, the CLI exits non-zero and creates only a failure record', async t => {
  const root = sandbox(t);
  const url = await server(t, (req, res) => { res.writeHead(500); res.end(html); });
  await assert.rejects(run(process.execPath, [fileURLToPath(new URL('./archive-fetch.mjs', import.meta.url)), url], { env: { ...process.env, CICHLIDS_SOURCE_ARCHIVE: root } }));
  assert.equal(existsSync(join(root, 'index.jsonl')), false);
  assert.match(readFileSync(join(root, 'failures.jsonl'), 'utf8'), /HTTP 500/);
});

test('Archive references reject traversal, absolute paths, invalid timestamps and overlong segments', () => {
  for (const ref of ['example.org/_root/2026-10-14T093000Z', 'doi.org/10.1234_fish/2026-01-01T000000Z']) assert.equal(validArchiveRef(ref), true);
  for (const ref of ['../foo/2026-10-14T093000Z', '/example.org/x/2026-10-14T093000Z', 'example.org/../2026-10-14T093000Z', 'example.org/x/2026-99-99T999999Z', 'example.org/x', `example.org/${'a'.repeat(81)}/2026-10-14T093000Z`]) assert.equal(validArchiveRef(ref), false, ref);
});

// A minimal PDF fixture with a standard text operator and a complete cross-reference table.
function pdfFixture(text) {
  const lines = text.match(/.{1,70}(?:\s|$)/g) ?? [text];
  const stream = `BT /F1 12 Tf 72 720 Td 16 TL ${lines.map(line => `(${line}) Tj T*`).join(' ')} ET`;
  const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>', '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>', '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>', `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`];
  let data = '%PDF-1.4\n';
  const offsets = [0];
  objects.forEach((object, i) => { offsets.push(Buffer.byteLength(data)); data += `${i + 1} 0 obj\n${object}\nendobj\n`; });
  const xref = Buffer.byteLength(data);
  data += `xref\n0 6\n0000000000 65535 f \n${offsets.slice(1).map(n => `${String(n).padStart(10, '0')} 00000 n \n`).join('')}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(data);
}
test('Given a public PDF, the original bytes and PDF text are archived', async t => {
  const root = sandbox(t);
  const body = pdfFixture(prose);
  const snap = await storeSnapshot({ url: 'https://example.org/fish.pdf', contentType: 'application/pdf', body }, { root });
  assert.deepEqual(readFileSync(join(root, snap.ref, 'original.pdf')), body);
  assert.ok(readFileSync(snap.textPath, 'utf8').includes('Cichlids occupy rocky habitats'));
});
test('Given a PDF content type containing HTML error markup, even supplied text is rejected', async t => {
  const root = sandbox(t);
  await assert.rejects(storeSnapshot({ url: 'https://example.org/fake.pdf', contentType: 'application/pdf', body: `<title>Privacy error</title>NET::ERR_CERT_AUTHORITY_INVALID ${prose}`, text: prose }, { root }), /PDF|certificate|error page/i);
});
test('Given a web.archive.org copy, metadata retains the requested archive URL', async t => {
  const root = sandbox(t);
  const url = 'https://web.archive.org/web/20200101/https://example.org/fish';
  const snap = await storeSnapshot({ url, body: html, contentType: 'text/html' }, { root });
  assert.equal(snap.meta.url, url);
  assert.match(snap.ref, /^web\.archive\.org\/web\/20200101\/https_\/example\.org\/fish\//);
});
test('Given simultaneous requests for the same URL, one snapshot and one request suffice', async t => {
  const root = sandbox(t);
  let requests = 0;
  const url = await server(t, (req, res) => { requests++; res.setHeader('content-type', 'text/html'); res.end(html); });
  const cli = fileURLToPath(new URL('./archive-fetch.mjs', import.meta.url));
  const got = await Promise.all([1, 2].map(() => run(process.execPath, [cli, url], { env: { ...process.env, CICHLIDS_SOURCE_ARCHIVE: root } })));
  assert.equal(requests, 1);
  assert.equal(got[0].stdout, got[1].stdout);
});
