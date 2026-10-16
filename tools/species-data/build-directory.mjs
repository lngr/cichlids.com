#!/usr/bin/env node
// Accepted catalog species and legacy aliases share immutable archived source inputs.
import {existsSync, readFileSync, readdirSync} from 'node:fs';
import {dirname, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {Document, parse, parseDocument, stringify} from 'yaml';
import {asyncBufferFromFile, parquetMetadataAsync, parquetReadObjects, parquetSchema} from 'hyparquet';
import {safeWriteFile} from './safe-write.mjs';
import {resolveSnapshot} from './source-archive.mjs';

export const CATALOG_REF = 'researcharchive.calacademy.org/research/ichthyology/catalog/fishcatget.asp/q-b612c649/2026-10-14T110429Z';
export const FISHBASE_REF = 'huggingface.co/datasets/cboettig/fishbase/resolve/main/data/fb/v25.04/parquet/species.parquet/2026-10-14T165859Z';
const SPECIES_DIR = resolve(dirname(fileURLToPath(import.meta.url)), '../../data/species');
const CATALOG_BASE = 'https://researcharchive.calacademy.org/research/ichthyology/catalog/';
const MANUAL_MATCHES = new Set(['correction', 'trade-form', 'hybrid', 'domestic-form']);
const compare = (a,b) => a < b ? -1 : a > b ? 1 : 0;
const cleanAuthor = text => text.replace(/\s*\[[^\]]*\]/g,'').replace(/\s+/g,' ').trim();
export const speciesId = name => name.toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'');
const yaml = data => stringify(data, {lineWidth:0});

export function parseCatalog(text) {
  const records=[];
  for (const paragraph of text.split(/\n\s*\n/)) {
    const line=paragraph.replace(/\s+/g,' ').trim();
    if (!/^[a-z-]+, [A-Z][a-z]+ /.test(line)) continue;
    const status=/Current status: (Valid as|Synonym of) ([A-Z][a-z]+) ([a-z-]+) (.+?)\. Cichlidae/.exec(line);
    if (!status) {
      if (/Current status: Synonym of [A-Z][a-z]+\. Cichlidae/.test(line)) continue;
      if (/Current status: (Valid as|Synonym of)/.test(line)) throw new Error(`Unrecognized catalog status: ${line}`);
      continue;
    }
    const nominal=/^([a-z-]+), ([A-Z][a-z]+)(?: \([A-Z][a-z]+\))?(?: (?!(?:van|von|de|da|del|der|den|du|le|ter)\b)[a-z-]+(?: var\.)?)? (.*?)\s(\d{4})/.exec(line);
    const reference=/ref\. (\d+)\]/.exec(line);
    if (!nominal || !reference) throw new Error(`Unrecognized original description citation: ${line}`);
    const [,epithet,genus,originalAuthor,originalYear]=nominal;
    const ref=reference[1];
    const end=reference.index+reference[0].length;
    let depth=0;
    let start=end-1;
    for (; start>=nominal[0].length; start--) {
      if (line[start]===']') depth++;
      if (line[start]==='[' && --depth===0) break;
    }
    if (start<nominal[0].length) throw new Error(`Unrecognized publication citation: ${line}`);
    const details=line.slice(nominal[0].length,start);
    const publication=line.slice(start+1,reference.index).replace(/;\s*$/,'').trim();
    const authorship=/^\(?(.+?) (\d{4})\)?$/.exec(status[4]);
    if (!authorship) throw new Error(`Unrecognized accepted authorship: ${status[4]}`);
    const pages=details.replace(/[,:]\s*(?:[\d ]+(?:(?:unnumb\.|unnumbered)\s+)?)?[\"']?(?:Figs?\.?|Pls?\.?).*$/i,'').trim();
    const citation=`${cleanAuthor(originalAuthor)} ${originalYear}${pages}${publication.trim() ? ` [${publication.trim()}]` : ''}`;
    const tail=line.slice(end).trim();
    const locality=tail.split(/(?:Holotype|Lectotype|Syntypes?|Neotype|Paratypes?|Paralectotypes?|Type catalog|Replacement name|Awaiting original|(?:MRAC\.\s*)?Type information|No types? known|Types? (?:not|unknown)|&bull;|•|Current status:)/)[0].trim();
    const final=line.slice(status.index);
    records.push({
      status: status[1] === 'Valid as' ? 'valid' : 'synonym',
      name:`${status[2]} ${status[3]}`, genus:status[2], epithet:status[3],
      author:authorship[1], year:Number(authorship[2]),
      subfamily:/Cichlidae: ([A-Za-z]+)\./.exec(final)?.[1] ?? null,
      distribution:/Distribution: (.*?)(?= IUCN\b| Habitat:|$)/.exec(final)?.[1]?.replace(/\.$/,'') ?? null,
      habitat:/Habitat: ([^.]+)\./.exec(final)?.[1] ?? null,
      originalDescription:{citation,catalogRef:Number(ref),url:`${CATALOG_BASE}getref.asp?id=${ref}`,originalCombination:`${genus} ${epithet}`,typeLocality:locality || null},
    });
  }
  return records;
}

function nameIndex(entries, names) {
  const index=new Map();
  for (const entry of entries) for (const name of names(entry)) {
    if (!index.has(name)) index.set(name,[]);
    index.get(name).push(entry);
  }
  return index;
}
function uniqueMatch(candidates=[]) {
  const unique=[...new Map(candidates.map(x=>[x.SpecCode ?? x.id,x])).values()];
  return unique.length === 1 ? unique[0] : null;
}

export function buildDirectory(records, fishbaseRows, profiles, seriouslyFish={}) {
  const accepted=records.filter(x=>x.status==='valid');
  const byName=new Map();
  for (const record of accepted) {
    if (byName.has(record.name)) throw new Error(`Duplicate accepted catalog name: ${record.name}`);
    byName.set(record.name,record);
  }
  const aliases=nameIndex(records.filter(x=>x.status==='synonym'),x=>[x.name]);
  const fishbase=nameIndex(fishbaseRows,x=>[`${x.Genus} ${x.Species}`]);
  const profileNames=nameIndex(profiles,x=>[x.taxonomy?.validName]);
  return accepted.map(record=>{
    const {status,...entry}=record;
    const synonyms=[...new Set([
      ...(aliases.get(entry.name) ?? []).map(x=>x.originalDescription.originalCombination),
      entry.originalDescription.originalCombination,
    ])].filter(x=>x!==entry.name).sort(compare);
    const exactRows=fishbase.get(entry.name);
    const fb=exactRows?.length ? uniqueMatch(exactRows) : uniqueMatch(synonyms.flatMap(x=>fishbase.get(x) ?? []));
    const profile=uniqueMatch(profileNames.get(entry.name));
    const sf=seriouslyFish[speciesId(entry.name)];
    const commercial=['commercial','highly commercial'].includes(fb?.Aquarium);
    return {
      id:speciesId(entry.name), ...entry, synonyms,
      links:{seriouslyFish:sf?.url ?? null,catalogOfFishes:`${CATALOG_BASE}fishcatget.asp?tbl=species&genus=${encodeURIComponent(entry.genus)}&species=${encodeURIComponent(entry.epithet)}`,fishbase:fb ? `https://www.fishbase.se/summary/${fb.SpecCode}` : null},
      fishbase:fb ? {specCode:fb.SpecCode,aquarium:fb.Aquarium ?? null} : null,
      trade:commercial || sf?.url ? true : sf ? false : null,
      profile:profile?.id ?? null,
    };
  }).sort((a,b)=>compare(a.name,b.name));
}

export function buildLegacyMapping(legacy, directory, profiles, existing=[]) {
  const manual=new Map(existing.filter(x=>MANUAL_MATCHES.has(x.match)).map(x=>[x.legacyId,x]));
  const exact=nameIndex(directory,x=>[x.name]);
  const synonyms=nameIndex(directory,x=>x.synonyms);
  const profileIndex=nameIndex(profiles,x=>x.legacyIds ?? []);
  return [...legacy].sort((a,b)=>a.legacyId-b.legacyId).map(row=>{
    if (manual.has(row.legacyId)) return manual.get(row.legacyId);
    const legacyName=`${row.genus} ${row.species}`;
    const result={legacyId:row.legacyId,legacyName,target:null,match:'open'};
    const claim=profileIndex.get(row.legacyId);
    if (claim?.length > 1) throw new Error(`Multiple profiles claim legacyId ${row.legacyId}`);
    const profile=claim?.[0];
    if (profile) {
      const target=uniqueMatch(exact.get(profile.taxonomy?.validName));
      result.target=profile.status==='described' ? target?.id ?? profile.id : profile.id;
      result.match='profile';
      return result;
    }
    const valid=uniqueMatch(exact.get(legacyName));
    const alias=uniqueMatch(synonyms.get(legacyName));
    if (valid || alias) { result.target=(valid ?? alias).id; result.match=valid ? 'exact' : 'synonym'; }
    return result;
  });
}

export function impliedTradeForms(profiles) {
  return profiles.filter(x=>x.status!=='described').map(profile=>({
    id:profile.id,name:profile.taxonomy.validName,
    kind:profile.status==='undescribed' ? 'trade-form' : profile.status,
    parents:profile.taxonomy.parents ?? [],note:null,
    sources:profile.taxonomy.source ? [profile.taxonomy.source] : [],
  })).sort((a,b)=>compare(a.id,b.id));
}

// The files of the species directory lie in a tree that agents write: the data directory above it
// is the trusted base of the writes, and a symlink below it is refused.
const writeBase = path => dirname(dirname(path));

export function writeMapping(path, mappings) {
  const doc=existsSync(path) ? parseDocument(readFileSync(path,'utf8')) : parseDocument('mappings: []\n');
  if (doc.errors.length) throw new Error(`Invalid mapping YAML: ${doc.errors[0].message}`);
  const manual=new Map((doc.get('mappings')?.items ?? []).filter(node=>MANUAL_MATCHES.has(node.get('match'))).map(node=>[node.get('legacyId'),node]));
  const sequence=doc.createNode(mappings.map(row=>manual.get(row.legacyId) ?? doc.createNode(row)));
  sequence.commentBefore=doc.get('mappings')?.commentBefore;
  sequence.comment=doc.get('mappings')?.comment;
  doc.set('mappings',sequence);
  safeWriteFile(path,doc.toString({lineWidth:0}),writeBase(path));
}

export async function readFishbase(path) {
  const file=await asyncBufferFromFile(path);
  const metadata=await parquetMetadataAsync(file);
  const columns=parquetSchema(metadata).children.map(x=>x.element.name);
  const wanted=['SpecCode','Genus','Species','Author','FamCode','Aquarium'];
  for (const name of wanted) if (!columns.includes(name)) throw new Error(`FishBase column missing: ${name}`);
  const rows=await parquetReadObjects({file,columns:wanted});
  return {columns,rows};
}

export async function build({speciesDir=SPECIES_DIR,root,catalogRef=CATALOG_REF,fishbaseRef=FISHBASE_REF,readFishbase:readTable=readFishbase,directoryOnly=false}={}) {
  const catalog=resolveSnapshot(catalogRef,{root});
  const fishbase=resolveSnapshot(fishbaseRef,{root});
  const table=await readTable(fishbase.originalPath);
  const profiles=readdirSync(join(speciesDir,'profiles')).filter(x=>x.endsWith('.yaml')).sort().map(x=>parse(readFileSync(join(speciesDir,'profiles',x),'utf8')));
  const records=parseCatalog(readFileSync(catalog.textPath,'utf8'));
  const seriouslyFishPath=join(speciesDir,'seriously-fish.yaml');
  const seriouslyFish=existsSync(seriouslyFishPath) ? parse(readFileSync(seriouslyFishPath,'utf8')).species : {};
  const species=buildDirectory(records,table.rows,profiles,seriouslyFish);
  const speciesIds=new Set(species.map(entry=>entry.id));
  for(const id of Object.keys(seriouslyFish)) if(!speciesIds.has(id)) throw new Error(`Unknown Seriously Fish species id: ${id}`);
  const sources=[
    {kind:'catalog-of-fishes',title:"Eschmeyer's Catalog of Fishes, Cichlidae family list",snapshot:catalog},
    {kind:'database',title:'FishBase species table v25.04',snapshot:fishbase},
  ].map(({snapshot,...source})=>({...source,url:snapshot.meta.url,accessed:snapshot.meta.fetchedAt.slice(0,10),archived:snapshot.ref}));
  if(existsSync(seriouslyFishPath)) {
    const accessed=Object.values(seriouslyFish).map(entry=>entry.checked).sort().at(-1) ?? null;
    sources.push({kind:'database',title:'Seriously Fish species page checks (data/species/seriously-fish.yaml)',url:'https://www.seriouslyfish.com/',accessed});
  }
  const mappingPath=join(speciesDir,'legacy-mapping.yaml');
  const existing=existsSync(mappingPath) ? parse(readFileSync(mappingPath,'utf8')).mappings : [];
  const mappings=buildLegacyMapping(JSON.parse(readFileSync(join(speciesDir,'legacy-catalog.json'),'utf8')),species,profiles,existing);
  const formsPath=join(speciesDir,'trade-forms.yaml');
  const forms=existsSync(formsPath) ? parse(readFileSync(formsPath,'utf8')).forms : [];
  const ids=new Set(forms.map(x=>x.id));
  const additions=impliedTradeForms(profiles).filter(x=>!ids.has(x.id));
  safeWriteFile(join(speciesDir,'directory.yaml'),yaml({sources,species}),writeBase(mappingPath));
  if(!directoryOnly) writeMapping(mappingPath,mappings);
  if (!directoryOnly && (!existsSync(formsPath) || additions.length)) {
    // A missing file starts as a block sequence, so that curated entries keep one field per line.
    const doc=existsSync(formsPath) ? parseDocument(readFileSync(formsPath,'utf8')) : new Document({forms: []});
    for (const form of additions) doc.get('forms').add(doc.createNode(form, {flow: false}));
    safeWriteFile(formsPath,doc.toString({lineWidth:0}),writeBase(formsPath));
  }
  const familyCode=table.rows.find(x=>x.Genus==='Geophagus')?.FamCode;
  const cichlids=familyCode === undefined ? [] : table.rows.filter(x=>x.FamCode===familyCode);
  const counts=list=>Object.fromEntries([...new Set(list)].sort((a,b)=>compare(String(a),String(b))).map(value=>[value,list.filter(x=>x===value).length]));
  return {directory:species.length,fishbaseMatches:species.filter(x=>x.fishbase).length,fishbaseColumns:table.columns,aquariumValues:[...new Set(table.rows.map(x=>x.Aquarium ?? null))].sort((a,b)=>compare(String(a),String(b))),cichlidAquarium:counts(cichlids.map(x=>x.Aquarium ?? null)),mappingCounts:counts(mappings.map(x=>x.match)),open:mappings.filter(x=>x.match==='open').map(({legacyId,legacyName})=>({legacyId,name:legacyName}))};
}

if (process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  try { console.log(JSON.stringify(await build({directoryOnly:process.argv.includes('--directory-only')}),null,2)); }
  catch (error) { console.error(`build-directory: ${error.message}`); process.exitCode=1; }
}
