#!/usr/bin/env node
// Renders species profiles into one self-contained, read-only HTML page: a comparison table of
// the profiles and one panel per species with all features, sources, agent disagreements and the
// written profile. Hard feature cards carry a badge with the automatic verification (method and
// votes). The knowledge articles of data/knowledge/articles follow as further panels.
//
//   node tools/species-data/render-review.mjs <out.html> [profile.yaml ...]
//
// Without profile paths it renders all of data/species/profiles/*.yaml.
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parse } from 'yaml';
import { safeWriteFile } from './safe-write.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const [out, ...args] = process.argv.slice(2);
if (!out) {
  console.error('Usage: node tools/species-data/render-review.mjs <out.html> [profile.yaml ...]');
  process.exit(2);
}
const profileDir = join(root, 'data', 'species', 'profiles');
const files = args.length
  ? args
  : readdirSync(profileDir).filter((f) => f.endsWith('.yaml')).sort().map((f) => join(profileDir, f));
const profiles = files.map((f) => parse(readFileSync(f, 'utf8'), { maxAliasCount: -1 }));
const articleDir = join(root, 'data', 'knowledge', 'articles');
const articles = (existsSync(articleDir) ? readdirSync(articleDir).filter((f) => f.endsWith('.md')).sort() : []).map((f) => {
  const text = readFileSync(join(articleDir, f), 'utf8');
  const m = text.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  return { meta: parse(m[1]), body: m[2] };
});

const HARD = ['temperatureC', 'ph', 'ghDgh', 'predation', 'diet', 'speciesOnly', 'hybridGroups'];
const LABELS = {
  origin: 'Herkunft',
  temperatureC: 'Temperatur (°C)',
  ph: 'pH',
  ghDgh: 'Gesamthärte (dGH)',
  khDkh: 'Karbonathärte (dKH)',
  maxSizeCm: 'Maximalgröße (cm)',
  predation: 'Raubverhalten (0-3)',
  diet: 'Ernährung',
  aggressionConspecific: 'Aggression Artgenossen (0-4)',
  aggressionHeterospecific: 'Aggression andere Arten (0-4)',
  territory: 'Revier',
  habitatZones: 'Lebensraumzonen',
  social: 'Sozialstruktur',
  speciesOnly: 'Nur Artbecken',
  hybridGroups: 'Kreuzungsgruppen',
  coloration: 'Färbung',
  feedingSpeed: 'Fressgeschwindigkeit',
  minTank: 'Mindestbecken',
  lifespanYears: 'Lebenserwartung (Jahre)',
  breeding: 'Brutpflege',
  beginnerSuitability: 'Eignung für Einsteiger',
};
const PROFILE_LABELS = {
  overview: 'Überblick',
  habitat: 'Lebensraum',
  sizeAndGrowth: 'Größe und Wachstum',
  husbandry: 'Haltung',
  socialBehavior: 'Sozialverhalten',
  diet: 'Ernährung',
  breeding: 'Zucht',
  sexing: 'Geschlechtsbestimmung',
  compatibility: 'Vergesellschaftung',
  problems: 'Typische Probleme',
};
const STATUS = { described: 'beschrieben', undescribed: 'unbeschrieben', hybrid: 'Hybride', 'domestic-form': 'Zuchtform' };
const METHOD = {
  unanimous: 'einstimmig',
  'qualified-majority': 'qualifizierte Mehrheit',
  conservative: 'vorsichtigster Wert',
  median: 'Median der Stimmen',
};
const CONF = { high: 'hoch', medium: 'mittel', low: 'niedrig' };

const esc = (s) =>
  String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

function fmt(v) {
  if (v === null || v === undefined) return '<span class="muted">leer</span>';
  if (typeof v === 'boolean') return v ? 'ja' : 'nein';
  if (Array.isArray(v) && v.length === 0) return '<span class="muted">keine</span>';
  if (Array.isArray(v)) return v.map((x) => (typeof x === 'object' ? fmt(x) : esc(x))).join(', ');
  if (typeof v === 'object') {
    const keys = Object.keys(v);
    if (keys.length === 2 && 'min' in v && 'max' in v) return `${esc(v.min)} bis ${esc(v.max)}`;
    return `<dl class="kv">${keys.map((k) => `<dt>${esc(k)}</dt><dd>${fmt(v[k])}</dd>`).join('')}</dl>`;
  }
  return esc(v);
}

function short(v) {
  if (v === null || v === undefined) return '';
  if (typeof v === 'boolean') return v ? 'ja' : 'nein';
  if (Array.isArray(v) && v.length === 0) return 'keine';
  if (Array.isArray(v)) return v.map(short).join(', ');
  if (typeof v === 'object') {
    if ('min' in v && 'max' in v && Object.keys(v).length === 2) return `${v.min}-${v.max}`;
    if ('type' in v) return `${v.type}${v.bloatSensitive ? ', Bloat' : ''}`;
    if ('structure' in v) return `${v.structure}${v.ratio ? ` 1:${v.ratio}` : ''}${v.minGroup ? ` ab ${v.minGroup}` : ''}`;
    if ('male' in v) return `M ${v.male} / W ${v.female}`;
    if ('volumeL' in v) return `${v.volumeL} L, ${v.lengthCm} cm`;
    if ('size' in v) return `${v.size}${v.breedingOnly ? ' (nur Brut)' : ''}`;
    if ('baseColors' in v) return `${v.baseColors.join('/')} ${v.pattern}`;
    if ('region' in v) return v.region;
    return JSON.stringify(v);
  }
  return String(v);
}

// Forum and archive posts are community experience; they follow the literature and database
// sources under their own heading.
const COMMUNITY_KINDS = new Set(['forum', 'archive']);

function communitySplit(list, kindOf) {
  const items = list ?? [];
  return [items.filter((x) => !COMMUNITY_KINDS.has(kindOf(x))), items.filter((x) => COMMUNITY_KINDS.has(kindOf(x)))];
}

function listWithCommunity(main, community, render) {
  const first = main.map(render).join('');
  const second = community.length ? `<li class="community">Aus der Community</li>${community.map(render).join('')}` : '';
  return `<ul class="src">${first}${second}</ul>`;
}

function sources(list) {
  const [main, community] = communitySplit(list, (s) => s.kind);
  return listWithCommunity(main, community, (s) => {
    const title = esc(s.title ?? s.kind);
    const link = s.url ? `<a href="${esc(s.url)}" target="_blank" rel="noopener">${title}</a>` : title;
    return `<li><span class="kind">${esc(s.kind)}</span> ${link}${s.note ? ` <span class="muted">(${esc(s.note)})</span>` : ''}</li>`;
  });
}

function disagreementsFor(p, key) {
  return (p.provenance?.disagreements ?? []).filter((d) => d.field.split('.')[1] === key && d.field.startsWith('features.'));
}

// German label of the agent run that made a proposal (generator, checker-a, tiebreaker-2 ...).
function agentLabel(by) {
  const run = String(by ?? "");
  if (run === "generator") return "Generator";
  if (run.startsWith("checker-")) return `Prüfer ${run.slice(8).toUpperCase()}`;
  if (run.startsWith("tiebreaker-")) return `Tie-Breaker ${run.slice(11)}`;
  return run;
}

function disagreementBlock(d) {
  return `<div class="dis"><div class="dis-field">${esc(d.field)}</div><ul>${d.proposals
    .map((x) => `<li><b>${esc(agentLabel(x.by))}</b>: ${fmt(x.value)}</li>`)
    .join('')}</ul><div><b>Entscheidung:</b> ${fmt(d.decision)}</div><p>${esc(d.rationale)}</p></div>`;
}

function verificationBadge(v) {
  if (!v) return '';
  const label = v.method === 'waterbody' ? `Seewerte ${v.waterbody}` : (METHOD[v.method] ?? v.method);
  const votes = v.votes ? ` <span class="muted">${esc(v.votes.agree)} von ${esc(v.votes.total)}</span>` : '';
  return `<span class="tag ver ${esc(v.method)}">${esc(label)}</span>${votes}`;
}

function evidenceList(list) {
  if (!list?.length) return '';
  const [main, community] = communitySplit(list, (e) => e.source?.kind);
  return `<details><summary>Angaben der Quellen (${list.length})</summary>${listWithCommunity(main, community, (e) => {
    const title = esc(e.source?.title ?? e.source?.kind);
    const link = e.source?.url ? `<a href="${esc(e.source.url)}" target="_blank" rel="noopener">${title}</a>` : title;
    return `<li>${link}: ${fmt(e.value)}${e.note ? ` <span class="muted">(${esc(e.note)})</span>` : ''}</li>`;
  })}</details>`;
}

function toleranceBlock(t) {
  if (!t) return '';
  return `<div class="tol"><b>Toleranz:</b> ${fmt(t.value)} ${verificationBadge(t.verification)}${evidenceList(t.evidence)}</div>`;
}

function featureCard(p, key) {
  const f = p.features[key];
  if (!f) return '';
  const hard = HARD.includes(key);
  const dis = disagreementsFor(p, key);
  return `<article class="feat${hard ? ' hard' : ''}" id="${esc(p.id)}-${key}">
  <header><h4>${esc(LABELS[key] ?? key)}${hard ? ' <span class="tag hard">hart</span>' : ''}${
    dis.length ? ` <span class="tag dis-tag">${dis.length} Abweichung${dis.length > 1 ? 'en' : ''}</span>` : ''
  }</h4><span class="conf ${esc(f.confidence)}">${esc(CONF[f.confidence] ?? f.confidence)}</span></header>
  <div class="val">${fmt(f.value)}</div>
  ${hard ? `<div class="verif">${verificationBadge(f.verification) || '<span class="muted">keine Verifikation</span>'}</div>` : ''}
  <p class="why">${esc(f.rationale)}</p>
  ${toleranceBlock(f.tolerance)}
  ${evidenceList(f.evidence)}
  <details><summary>Quellen (${(f.sources ?? []).length})</summary>${sources(f.sources)}</details>
  ${dis.length ? `<div class="dis-wrap"><div class="dis-title">So wurde entschieden</div>${dis.map(disagreementBlock).join('')}</div>` : ''}
</article>`;
}

function speciesPanel(p) {
  const keys = Object.keys(LABELS).filter((k) => p.features[k]);
  const hard = keys.filter((k) => HARD.includes(k));
  const soft = keys.filter((k) => !HARD.includes(k));
  const t = p.taxonomy;
  const otherDis = (p.provenance?.disagreements ?? []).filter((d) => !d.field.startsWith('features.'));
  const names = Object.entries(p.names?.trade ?? {})
    .map(([l, n]) => `<dt>${esc(l)}</dt><dd>${esc(n.join(', '))}</dd>`)
    .join('');
  return `<section class="panel" id="p-${esc(p.id)}" hidden>
  <h2><i>${esc(t.validName)}</i> <span class="muted">${esc(t.author ?? '')}</span></h2>
  <p class="meta"><span class="tag">${esc(STATUS[p.status] ?? p.status)}</span> Legacy-ID ${esc(
    (p.legacyIds ?? []).join(', '),
  )} · Datei <code>${esc(p.id)}.yaml</code> · ${(p.provenance?.disagreements ?? []).length} entschiedene Abweichungen</p>
  <details><summary>Taxonomie, Synonyme und Namen</summary>
    ${t.synonyms?.length ? `<p><b>Synonyme:</b> ${t.synonyms.map((s) => `<i>${esc(s.name)}</i>${s.note ? ` (${esc(s.note)})` : ''}`).join('; ')}</p>` : ''}
    ${t.legacyNames?.length ? `<p><b>Legacy-Namen:</b> ${esc(t.legacyNames.join(', '))}</p>` : ''}
    ${t.note ? `<p>${esc(t.note)}</p>` : ''}
    <dl class="kv">${names}</dl>
    ${p.variants?.length ? `<p><b>Varianten:</b> ${p.variants.map((v) => `${esc(v.name)} <span class="muted">(${esc(v.kind)}${v.note ? `, ${esc(v.note)}` : ''})</span>`).join('; ')}</p>` : ''}
  </details>
  ${otherDis.length ? `<h3>Weitere Entscheidungen der Agenten</h3>${otherDis.map(disagreementBlock).join('')}` : ''}
  <h3>Harte Merkmale <span class="muted">(automatisch verifiziert)</span></h3>
  ${hard.map((k) => featureCard(p, k)).join('')}
  <h3>Weitere Merkmale</h3>
  ${soft.map((k) => featureCard(p, k)).join('')}
  <h3>Partner</h3>
  <div class="partners">
    <div><h4>Geeignet</h4><ul>${(p.partners?.suitable ?? [])
      .map((x) => `<li><i>${esc(x.species)}</i>: ${esc(x.reason)}${x.condition ? `<br><span class="muted">Bedingung: ${esc(x.condition)}</span>` : ''}</li>`)
      .join('')}</ul></div>
    <div><h4>Ungeeignet</h4><ul>${(p.partners?.unsuitable ?? [])
      .map((x) => `<li><i>${esc(x.species)}</i>: ${esc(x.reason)}</li>`)
      .join('')}</ul></div>
  </div>
  <h3>Artenprofil</h3>
  ${Object.entries(p.profile ?? {})
    .map(
      ([k, s]) =>
        `<details><summary>${esc(PROFILE_LABELS[k] ?? k)}</summary><p>${esc(s.text)}</p>${sources(s.sources)}</details>`,
    )
    .join('')}
  <p class="muted runs">Läufe: ${(p.provenance?.runs ?? []).map((r) => `${esc(r.role)} (${esc(r.model)})`).join(', ')}</p>
</section>`;
}

function overview() {
  const keys = ['temperatureC', 'ph', 'ghDgh', 'khDkh', 'maxSizeCm', 'predation', 'diet', 'aggressionConspecific', 'aggressionHeterospecific', 'territory', 'social', 'speciesOnly', 'hybridGroups', 'feedingSpeed', 'minTank', 'beginnerSuitability'];
  return `<section class="panel" id="p-overview">
  <h2>Pilotprofile im Vergleich</h2>
  <p>Je Art erzeugt ein Generator das Profil, zwei oder drei Prüfer bestimmen die Merkmale unabhängig, ein Schiedsrichter entscheidet die Abweichungen. Harte Merkmale (fett) tragen auf den Seiten der Arten ein Abzeichen mit der automatischen Verifikation: einstimmig, qualifizierte Mehrheit, vorsichtigster Wert oder Seewerte, jeweils mit der Zahl der übereinstimmenden Stimmen. Ein Klick auf eine Art öffnet ihr Profil, ein Klick auf einen Wert springt zum Merkmal.</p>
  <div class="scroll"><table class="ov">
    <thead><tr><th>Art</th>${keys.map((k) => `<th class="${HARD.includes(k) ? 'hard' : ''}">${esc(LABELS[k])}</th>`).join('')}</tr></thead>
    <tbody>${profiles
      .map(
        (p) =>
          `<tr><td><a href="#" data-go="${esc(p.id)}"><i>${esc(p.taxonomy.validName)}</i></a></td>${keys
            .map((k) => {
              const f = p.features[k];
              if (!f) return '<td></td>';
              const d = disagreementsFor(p, k).length;
              return `<td><a href="#" data-go="${esc(p.id)}" data-feat="${k}">${esc(short(f.value))}</a> <span class="dot ${esc(f.confidence)}" title="Sicherheit ${esc(CONF[f.confidence])}"></span>${d ? ' <span class="dis-mark" title="Abweichung entschieden">≠</span>' : ''}</td>`;
            })
            .join('')}</tr>`,
      )
      .join('')}</tbody>
  </table></div>
  <p class="legend"><span class="dot high"></span> hoch <span class="dot medium"></span> mittel <span class="dot low"></span> niedrig · <span class="dis-mark">≠</span> Agenten waren uneinig, Schiedsrichter hat entschieden</p>
</section>`;
}

const css = `
:root{--bg:#fbfaf7;--fg:#1d2321;--muted:#68716d;--card:#fff;--line:#e2e0d8;--accent:#0f6e63;--hard:#a1450f;--hardbg:#fff5ec;--high:#2f8a3e;--medium:#c49112;--low:#c2412d;--dis:#6b4bb3}
@media (prefers-color-scheme:dark){:root{--bg:#141817;--fg:#e8ebe9;--muted:#9aa39f;--card:#1d2321;--line:#2e3633;--accent:#4cc0b0;--hard:#f0a36b;--hardbg:#2a2018;--dis:#b39af0}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
header.top{position:sticky;top:0;z-index:5;background:var(--bg);border-bottom:1px solid var(--line);padding:10px 16px 0}
header.top h1{font-size:17px;margin:0 0 8px}
nav{display:flex;flex-wrap:wrap;gap:0 4px;max-height:40vh;overflow-y:auto}nav button{flex:none;border:0;background:none;color:var(--muted);padding:6px 10px;border-bottom:3px solid transparent;font:inherit;cursor:pointer}
nav button.on{color:var(--fg);border-color:var(--accent);font-weight:600}
main{max-width:900px;margin:0 auto;padding:12px 16px 80px}main:has(#p-overview:not([hidden])){max-width:none}
h2{font-size:21px;margin:12px 0 4px}h3{font-size:17px;margin:26px 0 8px;border-bottom:1px solid var(--line);padding-bottom:4px}h4{margin:0;font-size:15px}
.muted{color:var(--muted);font-weight:400}.meta{color:var(--muted);font-size:13px}
.tag{display:inline-block;font-size:11px;padding:1px 7px;border-radius:9px;background:var(--line);color:var(--fg);font-weight:500;vertical-align:middle}
.tag.hard{background:var(--hard);color:#fff}.tag.dis-tag{background:var(--dis);color:#fff}
.feat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px;margin:10px 0}
.feat.hard{border-left:4px solid var(--hard)}
.feat header{display:flex;justify-content:space-between;gap:8px;align-items:flex-start}
.conf{font-size:12px;padding:1px 8px;border-radius:9px;color:#fff;flex:none}.conf.high{background:var(--high)}.conf.medium{background:var(--medium)}.conf.low{background:var(--low)}
.val{font-size:17px;font-weight:600;margin:6px 0}.why{margin:4px 0;font-size:14px}
dl.kv{display:grid;grid-template-columns:max-content 1fr;gap:2px 10px;margin:0;font-size:14px;font-weight:400}dl.kv dt{color:var(--muted)}dl.kv dd{margin:0}
details{margin:6px 0}summary{cursor:pointer;color:var(--accent);font-size:14px}
ul.src{font-size:13px;padding-left:18px;margin:4px 0}ul.src .kind{color:var(--muted);font-size:11px;text-transform:uppercase}
a{color:var(--accent);overflow-wrap:anywhere}
.article p,.article li{max-width:70ch}.summary{font-style:italic;color:var(--muted)}sup .cite{font-size:10px;text-decoration:none}
.src li.community{list-style:none;margin:6px 0 2px -18px;font-size:11px;text-transform:uppercase;color:var(--muted)}
.dis-wrap{margin-top:10px}.dis-title{font-size:13px;font-weight:600;color:var(--dis);margin-bottom:2px}
.dis{border-left:3px solid var(--dis);padding:4px 10px;margin:8px 0;font-size:14px}.dis-field{font-family:ui-monospace,monospace;font-size:12px;color:var(--dis)}.dis ul{margin:4px 0;padding-left:18px}
.partners{display:grid;grid-template-columns:1fr 1fr;gap:12px}.partners ul{padding-left:18px;font-size:14px}
@media (max-width:640px){.partners{grid-template-columns:1fr}}
.scroll{overflow-x:auto;border:1px solid var(--line);border-radius:10px}
table{border-collapse:collapse;width:100%;font-size:13px;background:var(--card)}th,td{padding:6px 8px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
th{position:sticky;top:0;background:var(--card)}td:first-child,th:first-child{position:sticky;left:0;background:var(--card);min-width:120px}
th.hard{color:var(--hard)}.ov td{white-space:nowrap}.ov a{overflow-wrap:normal}td a{text-decoration:none}
.dot{display:inline-block;width:8px;height:8px;border-radius:50%}.dot.high{background:var(--high)}.dot.medium{background:var(--medium)}.dot.low{background:var(--low)}
.dis-mark{color:var(--dis);font-weight:700}.legend{font-size:12px;color:var(--muted)}
.tol{margin:2px 0 6px;font-size:14px}.verif{margin:2px 0 6px;font-size:13px}.tag.ver{background:var(--high);color:#fff}.tag.ver.conservative{background:var(--low)}.tag.ver.median,.tag.ver.qualified-majority{background:var(--medium)}.tag.ver.waterbody{background:var(--accent);color:var(--bg)}
.runs{font-size:12px;margin-top:20px}
`;

const js = `
const panels=[...document.querySelectorAll('.panel')],tabs=[...document.querySelectorAll('nav button')];
function show(id,feat){panels.forEach(p=>p.hidden=p.id!=='p-'+id);tabs.forEach(t=>t.classList.toggle('on',t.dataset.p===id));
 if(feat){const el=document.getElementById(id+'-'+feat);if(el){el.scrollIntoView({block:'start'});window.scrollBy(0,-90)}}else window.scrollTo(0,0);
 try{history.replaceState(null,'','#'+id)}catch(e){}}
tabs.forEach(t=>t.onclick=()=>show(t.dataset.p));
document.querySelectorAll('[data-go]').forEach(a=>a.onclick=e=>{e.preventDefault();show(a.dataset.go,a.dataset.feat)});
const h=location.hash.slice(1);show(panels.some(p=>p.id==='p-'+h)?h:'overview');
`;

// Inline Markdown of the articles: bold, italics and footnote citations linked to the source list.
// Citation numbers of the article being rendered, in the order of its source list.
let citeNumbers = new Map();

function inline(text, id) {
  return esc(text)
    .replace(/\*\*(.+?)\*\*/g, '<b>$1</b>')
    .replace(/(^|[^*])\*([^*]+)\*/g, '$1<i>$2</i>')
    .replace(/(?:\[\^[a-z0-9-]+\])+/g, (group) => {
      const keys = [...group.matchAll(/\[\^([a-z0-9-]+)\]/g)].map((m) => m[1]);
      const links = keys.map((k) => `<a href="#${id}-src-${k}" class="cite">${citeNumbers.get(k) ?? k}</a>`);
      return `<sup>[${links.join(', ')}]</sup>`;
    });
}

// Block Markdown of the articles: headings, paragraphs, lists and tables.
function markdown(body, id) {
  const out = [];
  for (const block of body.split(/\n\s*\n/)) {
    const lines = block.trim().split('\n');
    if (!lines[0]) continue;
    const h = lines[0].match(/^(#{1,4})\s+(.*)$/);
    if (h && lines.length === 1) out.push(`<h${h[1].length + 1}>${inline(h[2], id)}</h${h[1].length + 1}>`);
    else if (lines.every((l) => l.trim().startsWith('|'))) {
      const rows = lines.filter((l) => !/^\|[\s:|-]+\|$/.test(l.trim())).map((l) => l.trim().replace(/^\||\|$/g, '').split('|'));
      out.push(`<div class="scroll"><table>${rows.map((r, i) => `<tr>${r.map((c) => (i ? `<td>${inline(c.trim(), id)}</td>` : `<th>${inline(c.trim(), id)}</th>`)).join('')}</tr>`).join('')}</table></div>`);
    } else if (lines.every((l) => /^\s*([-*]|\d+\.)\s/.test(l) || /^\s{2,}\S/.test(l))) {
      const items = block.trim().split(/\n(?=\s*(?:[-*]|\d+\.)\s)/).map((l) => l.replace(/^\s*(?:[-*]|\d+\.)\s+/, ''));
      out.push(`<ul>${items.map((i) => `<li>${inline(i.replace(/\n\s*/g, ' '), id)}</li>`).join('')}</ul>`);
    } else out.push(`<p>${inline(lines.join(' '), id)}</p>`);
  }
  return out.join('\n');
}

function articlePanel({ meta, body }) {
  const id = `a-${meta.id}`;
  const srcs = Object.entries(meta.sources ?? {})
    .map(([k, s], i) => `<li id="${id}-src-${esc(k)}">${i + 1}. <span class="kind">${esc(s.kind)}</span> ${s.url ? `<a href="${esc(s.url)}" target="_blank" rel="noopener">${esc(s.title)}</a>` : esc(s.title)} <span class="muted">[${esc(k)}]</span></li>`)
    .join('');
  return `<section class="panel article" id="p-${id}" hidden>
  <h2>${esc(meta.title)}</h2>
  <p class="meta"><span class="tag">Fachartikel</span> ${esc((meta.topics ?? []).join(', '))}</p>
  <p class="summary">${esc(meta.summary)}</p>
  ${((citeNumbers = new Map(Object.keys(meta.sources ?? {}).map((k, i) => [k, i + 1]))), markdown(body, id))}
  <h3>Quellen</h3><ul class="src">${srcs}</ul>
  ${(meta.provenance?.disagreements ?? []).length ? `<h3>Entscheidungen der Prüfung</h3>${meta.provenance.disagreements.map(disagreementBlock).join('')}` : ''}
</section>`;
}

const html = `<!doctype html>
<html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Artenprofile und Fachartikel</title><style>${css}</style></head>
<body>
<header class="top"><h1>Artenprofile und Fachartikel</h1>
<nav><button data-p="overview">Übersicht</button>${articles.map((a) => `<button data-p="a-${esc(a.meta.id)}">${esc(a.meta.title)}</button>`).join('')}</nav></header>
<main>
${overview()}
${profiles.map(speciesPanel).join('\n')}
${articles.map(articlePanel).join('\n')}
</main>
<script>${js}</script>
</body></html>`;

safeWriteFile(out, html, dirname(resolve(out)));
console.log(`${out}: ${profiles.length} profiles (${files.map((f) => basename(f)).join(', ')})`);
