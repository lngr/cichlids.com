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
