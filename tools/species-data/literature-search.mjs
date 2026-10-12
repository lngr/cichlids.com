#!/usr/bin/env node
// Searches scientific literature for the research agents through the open OpenAlex API, without
// the web search tool of the agent session. Prints per work the year, title, DOI, an open-access
// link where one exists and the abstract, so an agent can decide what to read and cite.
//
//   node tools/species-data/literature-search.mjs "<query>" [limit]
//
// Falls back to the Crossref API (title, year and DOI only) when OpenAlex does not answer.
const [query, limitArg] = process.argv.slice(2);
if (!query) {
  console.error('Usage: node tools/species-data/literature-search.mjs "<query>" [limit]');
  process.exit(2);
}
const limit = Math.min(Number.parseInt(limitArg ?? '8', 10) || 8, 25);

// OpenAlex stores abstracts as an inverted index of word positions.
function abstractText(index) {
  if (!index) return '';
  const words = [];
  for (const [word, positions] of Object.entries(index)) for (const p of positions) words[p] = word;
  return words.filter(Boolean).join(' ');
}

async function openAlex() {
  const url = new URL('https://api.openalex.org/works');
  url.searchParams.set('search', query);
  url.searchParams.set('per-page', String(limit));
  url.searchParams.set('select', 'doi,title,publication_year,open_access,abstract_inverted_index,primary_location');
  const res = await fetch(url, { signal: AbortSignal.timeout(30000) });
  if (!res.ok) throw new Error(`OpenAlex answered HTTP ${res.status}`);
  const data = await res.json();
  return data.results.map((w) => ({
    year: w.publication_year,
    title: w.title,
    venue: w.primary_location?.source?.display_name ?? '',
    doi: w.doi ?? '',
    openAccess: w.open_access?.oa_url ?? '',
    abstract: abstractText(w.abstract_inverted_index),
  }));
}

async function crossref() {
  const url = new URL('https://api.crossref.org/works');
  url.searchParams.set('query', query);
  url.searchParams.set('rows', String(limit));
  url.searchParams.set('select', 'DOI,title,issued,container-title');
  const res = await fetch(url, { signal: AbortSignal.timeout(30000) });
  if (!res.ok) throw new Error(`Crossref answered HTTP ${res.status}`);
  const data = await res.json();
  return data.message.items.map((w) => ({
    year: w.issued?.['date-parts']?.[0]?.[0] ?? '',
    title: w.title?.[0] ?? '',
    venue: w['container-title']?.[0] ?? '',
    doi: w.DOI ? `https://doi.org/${w.DOI}` : '',
    openAccess: '',
    abstract: '',
  }));
}

let works;
try {
  works = await openAlex();
} catch (e) {
  console.error(`literature-search: ${e.message}, using Crossref`);
  works = await crossref();
}
if (works.length === 0) console.log('No works found.');
for (const w of works) {
  console.log(`\n== ${w.year} ${w.title}${w.venue ? ` (${w.venue})` : ''}`);
  if (w.doi) console.log(`   doi: ${w.doi}`);
  if (w.openAccess) console.log(`   open access: ${w.openAccess}`);
  if (w.abstract) console.log(`   abstract: ${w.abstract.length > 900 ? `${w.abstract.slice(0, 900)} ...` : w.abstract}`);
}
