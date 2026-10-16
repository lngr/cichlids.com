import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, mkdirSync, readFileSync, readdirSync, realpathSync, symlinkSync, writeFileSync, rmSync} from 'node:fs';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {parse, stringify} from 'yaml';
import {storeSnapshot} from './source-archive.mjs';
import {parseCatalog, buildDirectory, buildLegacyMapping, impliedTradeForms, writeMapping, build} from './build-directory.mjs';

const abalios = 'abalios, Geophagus López-Fernández [H.] & Taphorn [D. C.] 2004:6, Figs. 2b, 3-5 [Zootaxa No. 439; ref. 27573] Laguna Larga, Venezuela. Holotype: MCNG 47600. •Synonym of Geophagus altifrons Heckel 1840. Current status: Valid as Geophagus abalios López-Fernández & Taphorn 2004. Cichlidae: Cichlinae. Distribution: South America: Amazon and Orinoco river basins. Habitat: freshwater.';
const moved = 'beta, Heros Smith [J.] 1900:12 [4], Pl. 1 [Journal v. 2 [for 1899]; ref. 123] Lake One, Country. Syntypes: ABC 1. Current status: Valid as Cichlasoma beta (Smith 1900). Cichlidae: Cichlinae. Habitat: freshwater; brackish.';
const synonym = 'gamma, Acara Jones [A.] 1901:9 [Journal v. 3; ref. 456] River Two. Holotype: ABC 2. •Valid as Acara gamma Jones 1901. Current status: Synonym of Cichlasoma beta (Smith 1900). Cichlidae: Cichlinae.';
const unknown = 'delta, Heros Smith [J.] 1902:5 [Journal; ref. 789] No types known. •Valid as Heros delta Smith 1902. Current status: Unknown. Cichlidae.';
const catalogText = [abalios, moved, synonym, unknown].join('\n\n');
const rows = [{Genus:'Geophagus',Species:'abalios',SpecCode:42,Aquarium:'never/rarely',FamCode:349}, {Genus:'Acara',Species:'gamma',SpecCode:43,Aquarium:null,FamCode:349}];
const described = {id:'cichlasoma-beta',status:'described',legacyIds:[1],taxonomy:{validName:'Cichlasoma beta'}};
const domestic = {id:'red-form',status:'domestic-form',legacyIds:[2],taxonomy:{validName:'Red form',parents:['cichlasoma-beta'],source:{kind:'database',title:'Form source'}}};

function sandbox(t) { const dir=mkdtempSync(join(tmpdir(),'cichlids-directory-')); t.after(()=>rmSync(dir,{recursive:true,force:true})); return dir; }

test('Given catalog history, only the final valid status supplies species and final synonyms supply aliases', () => {
  const records=parseCatalog(catalogText.replace('Lake One, Country.', 'Lake One,\nCountry.'));
  const entries=buildDirectory(records,rows,[described]);
  assert.deepEqual(entries.map(x=>x.name),['Cichlasoma beta','Geophagus abalios']);
  const beta=entries[0];
  assert.equal(beta.author,'Smith'); assert.equal(beta.year,1900);
  assert.equal(beta.distribution,null); assert.equal(beta.subfamily,'Cichlinae');
  assert.equal(beta.habitat,'freshwater; brackish');
  assert.deepEqual(beta.synonyms,['Acara gamma','Heros beta']);
  assert.equal(beta.originalDescription.citation,'Smith 1900:12 [4] [Journal v. 2 [for 1899]]');
  assert.equal(beta.originalDescription.catalogRef,123);
  assert.equal(beta.originalDescription.typeLocality,'Lake One, Country.');
  assert.equal(beta.originalDescription.originalCombination,'Heros beta');
  assert.equal(beta.profile,'cichlasoma-beta');
  assert.deepEqual(beta.fishbase,{specCode:43,aquarium:null});
  assert.equal(beta.links.fishbase,'https://www.fishbase.se/summary/43');
  const geo=entries[1];
  assert.equal(geo.originalDescription.citation,'López-Fernández & Taphorn 2004:6 [Zootaxa No. 439]');
  assert.equal(geo.originalDescription.url,'https://researcharchive.calacademy.org/research/ichthyology/catalog/getref.asp?id=27573');
  assert.equal(geo.links.catalogOfFishes,'https://researcharchive.calacademy.org/research/ichthyology/catalog/fishcatget.asp?tbl=species&genus=Geophagus&species=abalios');
});

test('Given absent catalog fields and FishBase matches, the directory keeps null values', () => {
  const text='epsilon, Tilapia (Test) Smith [J.] 1910 [ ref. 11] No types known. Current status: Valid as Tilapia epsilon Smith 1910. Cichlidae.';
  const entry=buildDirectory(parseCatalog(text),[],[])[0];
  assert.equal(entry.originalDescription.citation,'Smith 1910');
  assert.equal(entry.originalDescription.typeLocality,null);
  assert.equal(entry.subfamily,null); assert.equal(entry.habitat,null);
  assert.equal(entry.fishbase,null); assert.equal(entry.links.fishbase,null); assert.equal(entry.profile,null);
});

test('Given malformed valid records and duplicate accepted names, parsing fails visibly', () => {
  assert.throws(()=>parseCatalog('beta, Heros Smith 1900. Current status: Valid as Cichlasoma beta (Smith 1900). Cichlidae.'),/description|citation/i);
  assert.throws(()=>buildDirectory(parseCatalog([moved,moved].join('\n\n')),[],[]),/duplicate/i);
});

test('Given exact and synonym FishBase rows, exact has priority and ambiguous aliases remain unmatched', () => {
  const entries=buildDirectory(parseCatalog(catalogText),[...rows,{Genus:'Cichlasoma',Species:'beta',SpecCode:99,Aquarium:'commercial'}],[]);
  assert.equal(entries[0].fishbase.specCode,99);
  const ambiguous=buildDirectory(parseCatalog(catalogText),[...rows,{Genus:'Heros',Species:'beta',SpecCode:100,Aquarium:'commercial'}],[]);
  assert.equal(ambiguous[0].fishbase,null);
});

test('Given legacy names, authoritative profiles win, manual entries survive and unrecognized names stay open', () => {
  const entries=buildDirectory(parseCatalog(catalogText),rows,[described]);
  const legacy=[{legacyId:5,genus:'Heros',species:'wrong'},{legacyId:4,genus:'Acara',species:'gamma'},{legacyId:3,genus:'Geophagus',species:'abalios'},{legacyId:1,genus:'Other',species:'name'},{legacyId:2,genus:'Red',species:'form'}];
  const manual={legacyId:5,legacyName:'Heros wrong',target:'cichlasoma-beta',match:'correction',note:'Spelling correction.'};
  const mappings=buildLegacyMapping(legacy,entries,[described,domestic],[manual]);
  assert.deepEqual(mappings.map(x=>x.match),['profile','profile','exact','synonym','correction']);
  assert.deepEqual(mappings[4],manual);
  assert.equal(mappings[1].target,'red-form');
  const open=buildLegacyMapping([{legacyId:6,genus:'Heros',species:'wrong'}],entries,[],[])[0];
  assert.equal(open.match,'open'); assert.equal(open.target,null);
  for(const match of ['trade-form','hybrid','domestic-form']) assert.deepEqual(buildLegacyMapping([legacy[0]],entries,[],[{...manual,match}])[0],{...manual,match});
});

test('Given homonymous original combinations, automatic legacy matching does not select an arbitrary species', () => {
  const entries=[{id:'one-a',name:'One a',synonyms:['Old a']},{id:'two-a',name:'Two a',synonyms:['Old a']}];
  assert.equal(buildLegacyMapping([{legacyId:1,genus:'Old',species:'a'}],entries,[],[])[0].match,'open');
});

test('Given non-described profiles, trade forms use profile names, kinds, parents and sources', () => {
  const forms=impliedTradeForms([described,domestic,{id:'test-sp',status:'undescribed',taxonomy:{validName:"Test sp. 'One'"}},{id:'test-hybrid',status:'hybrid',taxonomy:{validName:'Test hybrid'}}]);
  assert.equal(forms.length,3);
  assert.deepEqual(forms.find(x=>x.id==='red-form'),{id:'red-form',name:'Red form',kind:'domestic-form',parents:['cichlasoma-beta'],note:null,sources:[domestic.taxonomy.source]});
  assert.equal(forms.find(x=>x.id==='test-sp').kind,'trade-form');
  assert.deepEqual(forms.find(x=>x.id==='test-hybrid').parents,[]);
});

test('Given hand-curated YAML, rebuilding mappings retains its entry comments and values', t => {
  const path=join(sandbox(t),'mapping.yaml');
  writeFileSync(path,'mappings:\n  # Human citation\n  - legacyId: 1\n    legacyName: Old a\n    target: one-a\n    match: correction\n    note: "Spelling correction." # Checked citation\n');
  const manual=parse(readFileSync(path,'utf8')).mappings[0];
  writeMapping(path,[manual,{legacyId:2,legacyName:'Other sp.',target:null,match:'open'}]);
  const text=readFileSync(path,'utf8');
  assert.match(text,/# Human citation/); assert.match(text,/# Checked citation/);
  assert.deepEqual(parse(text).mappings[0],manual);
});

test('Given archived fixtures, the build resolves references, shares sources and reproduces output bytes', async t => {
  const root=sandbox(t); const speciesDir=join(root,'data'); mkdirSync(join(speciesDir,'profiles'),{recursive:true});
  const snapshot=await storeSnapshot({url:'https://example.org/catalog',body:catalogText,contentType:'text/plain'}, {root:join(root,'archive')});
  const fishbase=await storeSnapshot({url:'https://example.org/fishbase',body:'FishBase data. '.repeat(20),contentType:'text/plain'}, {root:join(root,'archive')});
  writeFileSync(join(speciesDir,'profiles','cichlasoma-beta.yaml'),stringify(described));
  writeFileSync(join(speciesDir,'profiles','red-form.yaml'),stringify(domestic));
  writeFileSync(join(speciesDir,'legacy-catalog.json'),JSON.stringify([{legacyId:1,genus:'Other',species:'name'}]));
  const options={speciesDir,root:join(root,'archive'),catalogRef:snapshot.ref,fishbaseRef:fishbase.ref,readFishbase:async()=>({rows,columns:['SpecCode','Genus','Species','Author','FamCode','Aquarium']})};
  await build(options);
  const paths=['directory.yaml','legacy-mapping.yaml','trade-forms.yaml'].map(x=>join(speciesDir,x));
  const bytes=paths.map(x=>readFileSync(x,'utf8'));
  await build(options); assert.deepEqual(paths.map(x=>readFileSync(x,'utf8')),bytes);
  const doc=parse(bytes[0]); assert.equal(doc.sources.length,2); assert.equal(doc.species.length,2);
  assert.equal(doc.sources[0].archived,snapshot.ref); assert.equal(doc.sources[0].accessed,snapshot.meta.fetchedAt.slice(0,10));
  assert.ok(doc.species.every(x=>!Object.hasOwn(x,'sources')));
});

test('Given a synonym assigned only to a genus, the parser supplies no species alias', () => {
  assert.deepEqual(parseCatalog('kakaitsel, Glyphisodon Lacepède 1802:542 [Book; ref. 4929] Suriname. Current status: Synonym of Etroplus. Cichlidae: Etroplinae.'),[]);
});

for (const author of ['van Oijen','de Zeeuw','ter Huurne']) {
  test(`Given original author ${author}, citation retains its name particle`, () => {
    const text=`alpha, Haplochromis ${author} [J.] 1991:21, Figs. 3-4 [Journal; ref. 11] Lake One. Holotype: ABC 1. Current status: Valid as Haplochromis alpha ${author} 1991. Cichlidae: Pseudocrenilabrinae.`;
    assert.equal(buildDirectory(parseCatalog(text),[],[])[0].originalDescription.citation,`${author} 1991:21 [Journal]`);
  });
}
for (const figures of [', 2 figs.',', "Fig. 3" (unpublished)',', 6 unnumb. figs.',', 8 unnumbered figs.']) {
  test(`Given figure qualifier ${figures}, citation retains pages and publication`, () => {
    const text=`alpha, Haplochromis Smith [J.] 1991:21${figures} [Journal; ref. 11] Lake One. Holotype: ABC 1. Current status: Valid as Haplochromis alpha Smith 1991. Cichlidae: Pseudocrenilabrinae.`;
    assert.equal(buildDirectory(parseCatalog(text),[],[])[0].originalDescription.citation,'Smith 1991:21 [Journal]');
  });
}
test('Given figure numbers without a page, the citation keeps year and publication', () => {
  const text='alpha, Haplochromis Smith [J.] 1991:Figs. 3-4 [Journal; ref. 11] Lake One. Holotype: ABC 1. Current status: Valid as Haplochromis alpha Smith 1991. Cichlidae: Pseudocrenilabrinae.';
  assert.equal(buildDirectory(parseCatalog(text),[],[])[0].originalDescription.citation,'Smith 1991 [Journal]');
});

test('Given ambiguous exact FishBase rows, a synonym row cannot resolve the ambiguity', () => {
  const entries=buildDirectory(parseCatalog(catalogText),[...rows,{Genus:'Cichlasoma',Species:'beta',SpecCode:98},{Genus:'Cichlasoma',Species:'beta',SpecCode:99}],[]);
  assert.equal(entries[0].fishbase,null);
});
for (const [tail,locality] of [
  ['Replacement name for Tilapia bayoni Boulenger 1911, preoccupied by Paratilapia bayoni Boulenger 1909. &bull;',null],
  ['Makombe, Burundi, northern part of Lake Tanganyika. Awaiting original. &bull;','Makombe, Burundi, northern part of Lake Tanganyika.'],
  ['Lake Tanganyika. MRAC. Type information and locality not given in original description. &bull;','Lake Tanganyika.'],
]) {
  test(`Given catalog annotation ${tail}, type locality contains only locality text`, () => {
    const text=`alpha, Haplochromis Smith [J.] 1991:21 [Journal; ref. 11] ${tail} Current status: Valid as Haplochromis alpha Smith 1991. Cichlidae: Pseudocrenilabrinae.`;
    assert.equal(buildDirectory(parseCatalog(text),[],[])[0].originalDescription.typeLocality,locality);
  });
}

test('Given Aquarium values and Seriously Fish decisions, trade combines positive evidence and preserves uncertainty',()=>{
  for(const aquarium of ['commercial','highly commercial','never/rarely','potential','public aquariums','show aquarium',null]) {
    for(const decision of [undefined,{url:null,archived:null,checked:'2026-10-14'},{url:'https://www.seriouslyfish.com/species/geophagus-abalios',archived:'ref',checked:'2026-10-14'}]) {
      const sf=decision===undefined ? {} : {'geophagus-abalios':decision};
      const entry=buildDirectory(parseCatalog(abalios),[{...rows[0],Aquarium:aquarium}],[],sf)[0];
      const commercial=['commercial','highly commercial'].includes(aquarium);
      assert.equal(entry.trade,commercial || !!decision?.url ? true : decision===undefined ? null : false);
      assert.equal(entry.links.seriouslyFish,decision?.url ?? null);
    }
  }
});

test('Given a Seriously Fish file, the build adds its derived source and directory-only mode preserves curated files',async t=>{
  const root=sandbox(t);const speciesDir=join(root,'data');mkdirSync(join(speciesDir,'profiles'),{recursive:true});
  const catalog=await storeSnapshot({url:'https://example.org/catalog',body:catalogText,contentType:'text/plain'},{root:join(root,'archive')});
  const fishbase=await storeSnapshot({url:'https://example.org/fishbase',body:'FishBase data. '.repeat(20),contentType:'text/plain'},{root:join(root,'archive')});
  writeFileSync(join(speciesDir,'legacy-catalog.json'),'[]');
  writeFileSync(join(speciesDir,'legacy-mapping.yaml'),'mappings: []\n# curated\n');
  writeFileSync(join(speciesDir,'trade-forms.yaml'),'forms: []\n# curated\n');
  writeFileSync(join(speciesDir,'seriously-fish.yaml'),stringify({species:{'geophagus-abalios':{url:null,archived:null,checked:'2026-10-14'}}}));
  const options={speciesDir,root:join(root,'archive'),catalogRef:catalog.ref,fishbaseRef:fishbase.ref,readFishbase:async()=>({rows,columns:[]}),directoryOnly:true};
  await build(options);
  const doc=parse(readFileSync(join(speciesDir,'directory.yaml'),'utf8'));
  assert.deepEqual(doc.sources[2],{kind:'database',title:'Seriously Fish species page checks (data/species/seriously-fish.yaml)',url:'https://www.seriouslyfish.com/',accessed:'2026-10-14'});
  assert.equal(doc.species.find(x=>x.id==='geophagus-abalios').trade,false);
  assert.equal(readFileSync(join(speciesDir,'legacy-mapping.yaml'),'utf8'),'mappings: []\n# curated\n');
  assert.equal(readFileSync(join(speciesDir,'trade-forms.yaml'),'utf8'),'forms: []\n# curated\n');
});

test('Given an agent-planted symlink, the mapping and the directory writes refuse it and leave its target alone',t => {
  const dir=realpathSync(sandbox(t));
  const speciesDir=join(dir,'data','species');
  mkdirSync(speciesDir,{recursive:true});
  const target=join(dir,'elsewhere.yaml');
  writeFileSync(target,'mappings: []\n');
  const mapping=join(speciesDir,'legacy-mapping.yaml');
  symlinkSync(target,mapping);
  assert.throws(()=>writeMapping(mapping,[{legacyId:2,legacyName:'Other sp.',target:null,match:'open'}]),/refusing to write through the symlink/);
  assert.equal(readFileSync(target,'utf8'),'mappings: []\n');
  rmSync(mapping);
  symlinkSync(join(dir,'elsewhere-dir'),join(dir,'data','linked'));
  mkdirSync(join(dir,'elsewhere-dir'));
  assert.throws(()=>writeMapping(join(dir,'data','linked','legacy-mapping.yaml'),[]),/refusing to write through the symlink/);
  assert.deepEqual(readdirSync(join(dir,'elsewhere-dir')),[]);
});
