// Text extraction and access checks shared by research fetchers.
const LOGIN_HOSTS = [
  'login.', 'signin.', 'sso.', 'auth.', 'accounts.google.com', 'idp.', 'shibboleth', 'wayf.',
  'login.microsoftonline.com', 'secure.jbs.elsevierhealth.com',
];

// Plain text of a TEI document: title, abstract, then section heads and paragraphs of the body.
export function teiToText(xml) {
  const x = String(xml ?? '');
  const strip = (frag) =>
    decodeEntities(frag.replace(/<[^>]+>/g, ' '))
      .replace(/\s+/g, ' ')
      .replace(/ ([.,;:)\]])/g, '$1')
      .replace(/([(\[]) /g, '$1')
      .trim();
  const out = [];
  const title = x.match(/<titleStmt>[\s\S]*?<title\b[^>]*>([\s\S]*?)<\/title>/i)?.[1];
  if (title) out.push(strip(title));
  const abstract = x.match(/<abstract\b[^>]*>([\s\S]*?)<\/abstract>/i)?.[1];
  if (abstract) out.push('Abstract', ...[...abstract.matchAll(/<p\b[^>]*>([\s\S]*?)<\/p>/gi)].map((m) => strip(m[1])));
  const body = x.match(/<body\b[^>]*>([\s\S]*?)<\/body>/i)?.[1] ?? '';
  for (const m of body.matchAll(/<head\b[^>]*>([\s\S]*?)<\/head>|<p\b[^>]*>([\s\S]*?)<\/p>/gi)) {
    if (m[1] !== undefined) out.push(`## ${strip(m[1])}`);
    else out.push(strip(m[2]));
  }
  return out.filter(Boolean).join('\n\n');
}


export const BLOCK_PAGE_MARKERS = [
  /just a moment/i,
  /cf-chl|cf-browser-verification|challenge-platform/i,
  /attention required/i,
  /verify (that )?you are (a )?human/i,
  /enable javascript and cookies to continue/i,
  /checking your browser/i,
  /unusual traffic/i,
  /captcha verification|complete (?:the|this) captcha/i,
  /pardon our interruption/i,
  /making sure you.re not a bot/i,
  /anubis/i,
];

// True when a response is a refusal for plain clients: a status of 403, 429 or 503, a bot challenge
// page, or an HTML page where a PDF is expected (expectPdf).
export function looksBlocked(status, contentType, text, { expectPdf = false } = {}) {
  if ([403, 429, 503].includes(status)) return true;
  const isHtml = /html/i.test(contentType ?? '');
  if (expectPdf && !/pdf|octet-stream/i.test(contentType ?? '')) return true;
  if (isHtml) {
    const head = String(text ?? '').slice(0, 4000);
    return BLOCK_PAGE_MARKERS.some((re) => re.test(head));
  }
  return false;
}

const LOGIN_MARKERS =
  /\b(sign in|log in|login|purchase|buy (this )?(article|pdf|chapter)|subscribe|subscription (required|needed)|access denied|captcha|institutional (access|login)|rent this article|get access)\b/i;

// True when a page asks for a login, a subscription, a purchase or a captcha: a marker in the title,
// a marker in the first part of a short page (a full article that mentions "log in" somewhere is not
// a login page) or an address on a login host.
export function looksLikeLoginOrPaywall(title, text, url) {
  if (LOGIN_MARKERS.test(String(title ?? ''))) return true;
  const body = String(text ?? '');
  if (body.length < 2500 && LOGIN_MARKERS.test(body.slice(0, 1000))) return true;
  try {
    const u = new URL(url);
    const probe = `${u.hostname}${u.pathname}`.toLowerCase();
    if (LOGIN_HOSTS.some((h) => probe.includes(h))) return true;
    if (/\/(login|signin|sign-in|log-in)(\/|$)/.test(u.pathname.toLowerCase())) return true;
  } catch {}
  return false;
}

const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', ndash: '-', mdash: '-' };

function decodeEntities(s) {
  return s.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (m, e) => {
    if (e[0] === '#') {
      const code = e[1].toLowerCase() === 'x' ? Number.parseInt(e.slice(2), 16) : Number.parseInt(e.slice(1), 10);
      return Number.isFinite(code) && code > 0 && code < 0x110000 ? String.fromCodePoint(code) : m;
    }
    return ENTITIES[e.toLowerCase()] ?? m;
  });
}

// Readable body text of an HTML page: scripts, styles, navigation, headers, footers and asides are
// dropped; the article or main element is preferred over the whole body.
export function extractHtmlText(html) {
  let h = String(html ?? '');
  const title = decodeEntities((h.match(/<title[^>]*>([\s\S]*?)<\/title>/i)?.[1] ?? '').replace(/\s+/g, ' ').trim());
  h = h.replace(/<!--[\s\S]*?-->/g, ' ');
  for (const tag of ['script', 'style', 'noscript', 'svg', 'nav', 'header', 'footer', 'aside', 'form', 'button']) {
    h = h.replace(new RegExp(`<${tag}\\b[\\s\\S]*?<\\/${tag}>`, 'gi'), ' ');
  }
  const region = h.match(/<article\b[\s\S]*<\/article>/i) ?? h.match(/<main\b[\s\S]*<\/main>/i) ?? h.match(/<body\b[\s\S]*/i);
  h = region ? region[0] : h;
  h = h
    .replace(/<\s*br\s*\/?>/gi, '\n')
    .replace(/<\/(p|div|section|article|li|ul|ol|h[1-6]|tr|table|blockquote|figure|figcaption|pre)>/gi, '\n')
    .replace(/<(p|div|section|article|li|h[1-6]|tr|blockquote|figure|pre)\b[^>]*>/gi, '\n')
    .replace(/<\/(td|th)>/gi, '\t')
    .replace(/<[^>]+>/g, '');
  const text = decodeEntities(h)
    .replace(/[ \t\f\v]+/g, ' ')
    .split('\n')
    .map((l) => l.trim())
    .filter((l, i, a) => l || (i > 0 && a[i - 1]))
    .join('\n')
    .trim();
  return { title, text };
}


// Returns an empty text for a broken PDF, so that the caller moves on to the next route.
export async function pdfText(buffer) {
  const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
  const task = pdfjs.getDocument({ data: new Uint8Array(buffer), verbosity: 0, useSystemFonts: true });
  let doc;
  try {
    doc = await task.promise;
  } catch (e) {
    console.error(`fetch-oa: unreadable PDF (${e.name}), trying the next route`);
    return '';
  }
  try {
    const pages = [];
    for (let n = 1; n <= doc.numPages; n++) {
      const content = await (await doc.getPage(n)).getTextContent();
      let line = '';
      for (const item of content.items) {
        line += item.str;
        if (item.hasEOL) line += '\n';
        else if (item.str && !line.endsWith(' ')) line += ' ';
      }
      pages.push(line.replace(/[ \t]+\n/g, '\n').replace(/[ \t]{2,}/g, ' ').trim());
    }
    return pages.join('\n\n');
  } finally { await task.destroy?.(); }
}
