import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  assessOpenAlexWork,
  cacheKey,
  extractHtmlText,
  looksBlocked,
  looksLikeLoginOrPaywall,
  normaliseDoi,
  pickCandidateUrls,
  pickOpenAlexContent,
  teiToText,
} from './fetch-oa.mjs';

test('normaliseDoi accepts bare DOIs and doi.org URLs', () => {
  assert.equal(normaliseDoi('10.3390/Fishes11020106'), '10.3390/fishes11020106');
  assert.equal(normaliseDoi('https://doi.org/10.3390/fishes11020106'), '10.3390/fishes11020106');
  assert.equal(normaliseDoi('http://dx.doi.org/10.1111/J.1095-8649.2005.00718.X'), '10.1111/j.1095-8649.2005.00718.x');
  assert.equal(normaliseDoi('doi:10.1/abc'), '10.1/abc');
});

test('cacheKey replaces characters outside the safe set', () => {
  assert.equal(cacheKey('https://doi.org/10.3390/fishes11020106'), '10.3390_fishes11020106');
  assert.equal(cacheKey('10.1002/(SICI)1234<5>'), '10.1002__sici_1234_5_');
});

test('pickCandidateUrls orders best location first, PDF before landing page, skips closed locations', () => {
  const work = {
    best_oa_location: { is_oa: true, pdf_url: 'https://a/x.pdf', landing_page_url: 'https://a/x' },
    locations: [
      { is_oa: false, pdf_url: 'https://closed/x.pdf', landing_page_url: 'https://closed/x' },
      { is_oa: true, pdf_url: null, landing_page_url: 'https://b/x' },
      { is_oa: true, pdf_url: 'https://a/x.pdf', landing_page_url: 'https://a/x' },
    ],
  };
  assert.deepEqual(pickCandidateUrls(work), [
    { url: 'https://a/x.pdf', kind: 'pdf' },
    { url: 'https://a/x', kind: 'html' },
    { url: 'https://b/x', kind: 'html' },
  ]);
});

test('pickOpenAlexContent prefers TEI XML, then PDF, and only listed formats', () => {
  const both = {
    has_content: { pdf: true, grobid_xml: true },
    content_urls: { pdf: 'https://content.openalex.org/works/W1.pdf', grobid_xml: 'https://content.openalex.org/works/W1.grobid-xml' },
  };
  assert.deepEqual(pickOpenAlexContent(both).map((r) => r.route), ['openalex-tei', 'openalex-pdf']);
  assert.deepEqual(
    pickOpenAlexContent({ ...both, has_content: { pdf: true, grobid_xml: false } }).map((r) => r.route),
    ['openalex-pdf'],
  );
  assert.deepEqual(pickOpenAlexContent({ open_access: { is_oa: true } }), []);
});

test('teiToText extracts title, abstract, heads and paragraphs', () => {
  const tei = `<TEI><teiHeader><fileDesc><titleStmt><title level="a" type="main">Cichlid &amp; water</title></titleStmt></fileDesc>
    <profileDesc><abstract><div><p>Short abstract here.</p></div></abstract></profileDesc></teiHeader>
    <text><body><div><head n="1">Introduction</head><p>Fish live in <ref type="bibr">[1]</ref> lakes (see Fig. 1) .</p></div>
    <div><head>Methods</head><p>We measured pH.</p></div></body><back><p>Not body.</p></back></text></TEI>`;
  assert.equal(
    teiToText(tei),
    'Cichlid & water\n\nAbstract\n\nShort abstract here.\n\n## Introduction\n\nFish live in [1] lakes (see Fig. 1).\n\n## Methods\n\nWe measured pH.',
  );
});

test('looksBlocked recognises refusals and challenge pages', () => {
  assert.equal(looksBlocked(403, 'text/html', ''), true);
  assert.equal(looksBlocked(429, 'text/html', ''), true);
  assert.equal(looksBlocked(503, 'text/html', ''), true);
  assert.equal(looksBlocked(200, 'text/html', '<title>Just a moment...</title>'), true);
  assert.equal(looksBlocked(200, 'text/html', '<html>Please verify you are human</html>'), true);
  assert.equal(looksBlocked(200, 'text/html', '<html>an article</html>', { expectPdf: true }), true);
  assert.equal(looksBlocked(200, 'application/pdf', '', { expectPdf: true }), false);
  assert.equal(looksBlocked(200, 'text/html', '<html><body>An article</body></html>'), false);
});

test('looksLikeLoginOrPaywall flags login pages, paywalls and login hosts', () => {
  assert.equal(looksLikeLoginOrPaywall('Sign in to your account', 'x'.repeat(5000), 'https://a.org/x'), true);
  assert.equal(looksLikeLoginOrPaywall('Article', 'Purchase this article for $40. ' + 'x'.repeat(300), 'https://a.org/x'), true);
  assert.equal(looksLikeLoginOrPaywall('Article', 'x'.repeat(100), 'https://login.example.org/cas'), true);
  assert.equal(looksLikeLoginOrPaywall('Article', 'x'.repeat(100), 'https://a.org/login?next=1'), true);
  assert.equal(looksLikeLoginOrPaywall('Fish paper', 'Introduction. ' + 'x'.repeat(5000) + ' log in', 'https://a.org/paper'), false);
});

test('assessOpenAlexWork refuses works that are not open access', () => {
  const closed = { open_access: { is_oa: false }, locations: [{ is_oa: false, pdf_url: 'https://x/y.pdf' }] };
  const r = assessOpenAlexWork(closed, '10.1/x');
  assert.equal(r.ok, false);
  assert.equal(r.exitCode, 3);
  assert.equal(r.message, 'not open access: use the abstract (literature-search) or another source');
});

test('assessOpenAlexWork returns metadata and candidates for open-access works', () => {
  const work = {
    title: 'T',
    publication_year: 2026,
    open_access: { is_oa: true, oa_status: 'gold' },
    best_oa_location: { is_oa: true, license: 'cc-by', pdf_url: 'https://a/x.pdf' },
  };
  const r = assessOpenAlexWork(work, '10.1/x');
  assert.equal(r.ok, true);
  assert.deepEqual(r.metadata, { doi: '10.1/x', title: 'T', year: 2026, license: 'cc-by', oaStatus: 'gold' });
  assert.equal(r.candidates[0].url, 'https://a/x.pdf');
});

test('extractHtmlText keeps the article text and drops scripts and navigation', () => {
  const html = '<html><head><title>T &amp; U</title><script>var a=1;</script></head><body><nav>Menu</nav><article><h1>Head</h1><p>Para one.</p><p>Para two.</p></article><footer>Foot</footer></body></html>';
  const r = extractHtmlText(html);
  assert.equal(r.title, 'T & U');
  assert.equal(r.text, 'Head\n\nPara one.\n\nPara two.');
});

test('Full texts are archived by DOI and migration preserves fetch time and exact originals', async t => {
  const { mkdtempSync, writeFileSync, readFileSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { archiveFullText, migrateLibrary } = await import('./fetch-oa.mjs');
  const dir = mkdtempSync(join(tmpdir(), 'cichlids-oa-migrate-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const root = join(dir, 'archive');
  const doi = '10.1234/fish';
  const text = 'Field observations of cichlid ecology in rocky habitats. '.repeat(15);
  const data = `<article><p>${text}</p></article>`;
  const meta = { doi, url: 'https://publisher.org/paper', fetchedAt: '2020-01-02T03:04:05Z', format: 'html' };
  const snap = await archiveFullText({ data, text, format: 'html', url: meta.url }, meta, { root });
  assert.equal(snap.ref, 'doi.org/10.1234_fish/2020-01-02T030405Z');
  assert.equal(readFileSync(join(root, snap.ref, 'original.html'), 'utf8'), data);
  const key = cacheKey(doi);
  writeFileSync(join(dir, `${key}.json`), JSON.stringify(meta));
  writeFileSync(join(dir, `${key}.html`), data);
  writeFileSync(join(dir, `${key}.txt`), text);
  const result = await migrateLibrary({ libraryDir: dir, root });
  assert.equal(result.failures.length, 0);
  assert.equal(JSON.parse(readFileSync(join(dir, `${key}.json`))).archived, snap.ref);
  assert.equal(readFileSync(join(root, 'index.jsonl'), 'utf8').trim().split('\n').length, 1);
});

test('Full-text archiving refuses browser certificate-error pages even with long extracted text', async t => {
  const { archiveFullText } = await import('./fetch-oa.mjs');
  const { mkdtempSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const root = mkdtempSync(join(tmpdir(), 'cichlids-oa-error-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  await assert.rejects(archiveFullText({ format: 'html', data: '<title>Privacy error</title>NET::ERR_CERT_DATE_INVALID', text: 'error '.repeat(200) }, { doi: '10.1234/error' }, { root }), /certificate|error page/i);
});

test('Migration handles originals without text, rejects errors and continues with valid works', async t => {
  const { migrateLibrary } = await import('./fetch-oa.mjs');
  const { mkdtempSync, writeFileSync, readFileSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const libraryDir = mkdtempSync(join(tmpdir(), 'cichlids-oa-migrate-'));
  t.after(() => rmSync(libraryDir, { recursive: true, force: true }));
  for (const suffix of ['good', 'bad']) {
    const key = cacheKey(`10.1234/${suffix}`);
    writeFileSync(join(libraryDir, `${key}.json`), JSON.stringify({ doi: `10.1234/${suffix}`, fetchedAt: '2021-06-07T08:09:10Z' }));
    writeFileSync(join(libraryDir, `${key}.html`), suffix === 'good' ? `<article>${'Cichlid feeding ecology. '.repeat(50)}</article>` : `<title>Privacy error</title>NET::ERR_CERT_AUTHORITY_INVALID ${'error '.repeat(150)}`);
  }
  const result = await migrateLibrary({ libraryDir, root: join(libraryDir, 'archive') });
  assert.equal(result.snapshots.length, 1);
  assert.equal(result.failures.length, 1);
  assert.match(result.snapshots[0].ref, /2021-06-07T080910Z$/);
  assert.equal(JSON.parse(readFileSync(join(libraryDir, '10.1234_bad.json'))).archived, undefined);
});
test('Given a cached full text, fetch-oa prints and writes its archive reference without network access', async t => {
  const { main } = await import('./fetch-oa.mjs');
  const { mkdtempSync, writeFileSync, readFileSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const libraryDir = mkdtempSync(join(tmpdir(), 'cichlids-oa-cache-'));
  t.after(() => rmSync(libraryDir, { recursive: true, force: true }));
  const text = 'Cichlid feeding ecology. '.repeat(50);
  writeFileSync(join(libraryDir, '10.1234_cached.html'), `<article>${text}</article>`);
  writeFileSync(join(libraryDir, '10.1234_cached.txt'), text);
  writeFileSync(join(libraryDir, '10.1234_cached.json'), JSON.stringify({ doi: '10.1234/cached', fetchedAt: '2022-01-01T00:00:00Z' }));
  const lines = [];
  t.mock.method(console, 'log', x => lines.push(x));
  t.mock.method(globalThis, 'fetch', () => { throw new Error('network must not be used'); });
  assert.equal(await main(['10.1234/cached'], { libraryDir, root: join(libraryDir, 'archive') }), 0);
  const meta = JSON.parse(readFileSync(join(libraryDir, '10.1234_cached.json')));
  assert.equal(meta.archived, 'doi.org/10.1234_cached/2022-01-01T000000Z');
  assert.ok(lines.join('\n').includes(`archived: ${meta.archived}`));
});

test('A DOI full-text snapshot is reused by the page archive and differing full texts remain preserved', async t => {
  const { archiveFullText } = await import('./fetch-oa.mjs');
  const { archiveUrl } = await import('./source-archive.mjs');
  const { mkdtempSync, readFileSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const root = mkdtempSync(join(tmpdir(), 'cichlids-oa-reuse-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const meta = { doi: '10.1234/reuse', fetchedAt: '2020-01-01T00:00:00Z' };
  const text = 'Field observations of cichlid ecology. '.repeat(20);
  const first = await archiveFullText({ format: 'html', data: `<article>${text}</article>`, text }, meta, { root });
  const cached = await archiveUrl('https://doi.org/10.1234/reuse', { root, fetchImpl: async () => { throw new Error('must reuse DOI full text'); } });
  assert.equal(cached.ref, first.ref);
  const second = await archiveFullText({ format: 'html', data: `<article>${text}Additional findings.</article>`, text: `${text}Additional findings.` }, meta, { root });
  assert.notEqual(second.ref, first.ref);
  assert.equal(readFileSync(join(root, 'index.jsonl'), 'utf8').trim().split('\n').length, 2);
});

test('Given an OA publisher response, fetch-oa archives the exact full text before storing the library', async t => {
  const { main } = await import('./fetch-oa.mjs');
  const { USER_AGENT } = await import('./source-archive.mjs');
  const { mkdtempSync, readFileSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const libraryDir = mkdtempSync(join(tmpdir(), 'cichlids-oa-publisher-'));
  t.after(() => rmSync(libraryDir, { recursive: true, force: true }));
  const root = join(libraryDir, 'archive');
  const text = 'Field observations of cichlid ecology. '.repeat(20);
  const html = `<title>Fish ecology</title><article>${text}</article>`;
  const original = Buffer.concat([Buffer.from(html), Buffer.from([0x80, 0xff])]);
  const urls = [];
  t.mock.method(console, 'log', () => {});
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    assert.equal(options.headers['user-agent'], USER_AGENT);
    urls.push(url);
    if (url.startsWith('https://api.openalex.org/')) return Response.json({ title: 'Fish ecology', open_access: { is_oa: true }, best_oa_location: { is_oa: true, landing_page_url: 'https://publisher.example/paper' } });
    assert.equal(url, 'https://publisher.example/paper');
    return new Response(original, { headers: { 'content-type': 'text/html; charset=utf-8' } });
  });
  assert.equal(await main(['10.1234/publisher'], { libraryDir, root }), 0);
  assert.equal(urls.length, 2);
  const meta = JSON.parse(readFileSync(join(libraryDir, '10.1234_publisher.json')));
  assert.match(meta.archived, /^doi\.org\/10\.1234_publisher\//);
  assert.deepEqual(readFileSync(join(root, meta.archived, 'original.html')), original);
  const archiveMeta = JSON.parse(readFileSync(join(root, meta.archived, 'meta.json')));
  assert.equal(archiveMeta.contentType, 'text/html; charset=utf-8');
  assert.deepEqual(readFileSync(join(libraryDir, '10.1234_publisher.html')), original);
});

test('Given an agent-planted symlink in the library, fetch-oa refuses to write through it', async t => {
  const { main } = await import('./fetch-oa.mjs');
  const { mkdtempSync, mkdirSync, symlinkSync, writeFileSync, readFileSync, readdirSync, realpathSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const work = realpathSync(mkdtempSync(join(tmpdir(), 'cichlids-oa-link-')));
  t.after(() => rmSync(work, { recursive: true, force: true }));
  const elsewhere = join(work, 'elsewhere');
  mkdirSync(elsewhere);
  writeFileSync(join(elsewhere, 'meta.json'), 'keep');
  const libraryDir = join(work, '.literature');
  mkdirSync(libraryDir);
  const text = 'Cichlid feeding ecology. '.repeat(50);
  writeFileSync(join(libraryDir, '10.1234_linked.html'), `<article>${text}</article>`);
  writeFileSync(join(libraryDir, '10.1234_linked.txt'), text);
  symlinkSync(join(elsewhere, 'meta.json'), join(libraryDir, '10.1234_linked.json'));
  t.mock.method(console, 'error', () => {});
  t.mock.method(globalThis, 'fetch', () => { throw new Error('network must not be used'); });
  assert.equal(await main(['10.1234/linked'], { libraryDir, root: join(work, 'archive') }), 1);
  assert.equal(readFileSync(join(elsewhere, 'meta.json'), 'utf8'), 'keep');
  assert.deepEqual(readdirSync(elsewhere), ['meta.json']);
});
