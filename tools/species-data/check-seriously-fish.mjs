#!/usr/bin/env node
// Species pages provide positive evidence of aquarium keeping; HTTP absence settles negative checks.
import {existsSync, readFileSync} from 'node:fs';
import {dirname, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {parse, stringify} from 'yaml';
import {safeWriteFile} from './safe-write.mjs';
import {archiveUrl, resolveSnapshot} from './source-archive.mjs';
import {extractHtmlText} from './source-content.mjs';

export const SITE_URL='https://www.seriouslyfish.com/';
const SPECIES_DIR=resolve(dirname(fileURLToPath(import.meta.url)), '../../data/species');
const slug=name=>name.toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'');
export const candidateUrls=species=>[...new Set([species.name,...species.synonyms].map(name=>`${SITE_URL}species/${slug(name)}`))];

export function pageDecision({status=200,html='',names=[],url,validNames=new Set(),error}={}) {
  if(error) {
    const match=/^HTTP (\d{3})$/.exec(error.message);
    return match && [404,410].includes(Number(match[1])) ? 'absent' : 'undecided';
  }
  if([404,410].includes(status)) return 'absent';
  if(status!==200) return 'undecided';
  const {title:rawTitle,text}=extractHtmlText(html);
  const title=rawTitle.replace(/["'‘’“”]/g,'');
  const matches=names.some(name=>title.toLowerCase().startsWith(name.toLowerCase()) && /^[\s(·|:]/.test(title.slice(name.length)));
  const canonical=(html.match(/<link\b[^>]*>/gi) ?? []).filter(tag=>/\brel=["']canonical["']/i.test(tag)).map(tag=>/\bhref=["']([^"']+)["']/i.exec(tag)?.[1]);
  const sameRoute=url && canonical.some(link=>link?.replace(/\/$/,'')===url.replace(/\/$/,''));
  const scientificName=/^["'‘’]?([A-Z][a-z]+)["'‘’]? ([a-z-]+)(?=[\s(·|:])/.exec(title);
  const titleName=scientificName ? `${scientificName[1]} ${scientificName[2]}` : null;
  if(titleName && validNames.has(titleName) && titleName!==names[0]) return 'absent';
  const sameEpithet=scientificName && names.some(name=>name.split(' ').at(-1)===scientificName[2]);
  const titleRoute=scientificName && canonical.some(link=>link?.replace(/\/$/,'')===`${SITE_URL}species/${slug(`${scientificName[1]} ${scientificName[2]}`)}`);
  const care=/\bMaintenance\b/i.test(text) && /\bWater conditions\b/i.test(text);
  return (matches || (scientificName && (sameRoute || (sameEpithet && titleRoute)))) && /Seriously Fish/i.test(title) && (care || (scientificName && (sameRoute || titleRoute))) ? 'found' : 'undecided';
}

async function fetchArchivedPage(url) {
  const snapshot=await archiveUrl(url);
  const resolved=resolveSnapshot(snapshot.ref);
  return {...snapshot,html:readFileSync(resolved.originalPath,'utf8')};
}

export async function checkSpecies(species,{fetchPage=fetchArchivedPage,checked=new Date().toISOString().slice(0,10),onAttempt=()=>{},validNames=new Set()}={}) {
  let undecided=false;
  for(const url of candidateUrls(species)) {
    let decision;
    let snapshot;
    try {
      snapshot=await fetchPage(url);
      decision=pageDecision({url,validNames,status:snapshot.meta.httpStatus,html:snapshot.html,names:[species.name,...species.synonyms]});
    } catch(error) { decision=pageDecision({error});onAttempt({url,decision,error:error.message}); }
    if(snapshot) onAttempt({url,decision,archived:snapshot.ref});
    if(decision==='found') return {url,archived:snapshot.ref,checked};
    if(decision==='undecided') undecided=true;
  }
  return undecided ? undefined : {url:null,archived:null,checked};
}

export async function runCheck({species,output=join(SPECIES_DIR,'seriously-fish.yaml'),fetchPage=fetchArchivedPage,log=console.log}={}) {
  const doc=existsSync(output) ? parse(readFileSync(output,'utf8')) : {species:{}};
  if(!doc?.species || Array.isArray(doc.species) || typeof doc.species!=='object') throw new Error('Seriously Fish output needs a species mapping');
  const ids=new Set(species.map(entry=>entry.id));
  for(const id of Object.keys(doc.species)) if(!ids.has(id)) throw new Error(`Unknown Seriously Fish species id: ${id}`);
  const validNames=new Set(species.map(entry=>entry.name));
  let attempted=0;
  for(const entry of species) {
    if(Object.hasOwn(doc.species,entry.id)) continue;
    attempted++;
    const result=await checkSpecies(entry,{fetchPage,validNames,onAttempt:attempt=>{
      if(attempt.decision==='undecided') log(JSON.stringify({id:entry.id,...attempt}));
    }});
    if(result!==undefined) {
      doc.species[entry.id]=result;
      doc.species=Object.fromEntries(Object.entries(doc.species).sort(([a],[b])=>a.localeCompare(b,'en')));
      safeWriteFile(output,stringify(doc,{lineWidth:0}),dirname(dirname(output)));
    }
    log(JSON.stringify({id:entry.id,result:result?.url ?? (result ? 'absent' : 'undecided'),decided:Object.keys(doc.species).length,total:species.length}));
  }
  return {attempted,decided:Object.keys(doc.species).length,total:species.length,undecided:species.length-Object.keys(doc.species).length};
}

if(process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  try {
    if(process.argv.length>2) throw new Error('Usage: node tools/species-data/check-seriously-fish.mjs');
    const {species}=parse(readFileSync(join(SPECIES_DIR,'directory.yaml'),'utf8'));
    const report=await runCheck({species});
    console.log(JSON.stringify(report));
    if(report.undecided) process.exitCode=1;
  } catch(error) { console.error(`check-seriously-fish: ${error.message}`);process.exitCode=1; }
}
