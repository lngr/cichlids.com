import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, readFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {parse} from 'yaml';
import {candidateUrls, pageDecision, checkSpecies, runCheck} from './check-seriously-fish.mjs';

const species={id:'one-alpha',name:'One alpha',synonyms:['Old alpha','One alpha','Old alpha']};
const page=name=>`<html><head><title>${name} (Fish) · Seriously Fish</title></head><body><h2>Maintenance</h2><p>Care text</p><h2>Water conditions</h2></body></html>`;
test('Given accepted names and synonyms, candidates use unique accepted-name-first species slugs',()=>{
  assert.deepEqual(candidateUrls(species),['https://www.seriouslyfish.com/species/one-alpha','https://www.seriouslyfish.com/species/old-alpha']);
});
test('Given a matching species title and care section, a page is found',()=>{
  assert.equal(pageDecision({html:page('One alpha'),names:['One alpha']}),'found');
  assert.equal(pageDecision({html:page('Old alpha'),names:['One alpha','Old alpha']}),'found');
});
test('Given absent pages, errors and unrelated documents, only explicit absence settles the check',()=>{
  for(const status of [404,410]) assert.equal(pageDecision({status}),'absent');
  for(const status of [403,429,500,502,503]) assert.equal(pageDecision({status}),'undecided');
  for(const html of ['<title>Search · Seriously Fish</title>',page('Other beta'),'<title>One alpha · Seriously Fish</title>']) assert.equal(pageDecision({html,names:['One alpha']}),'undecided');
  assert.equal(pageDecision({error:new Error('HTTP 404')}),'absent');
  for(const message of ['fetch failed','HTTP 503','HTTP 404 after redirect failure']) assert.equal(pageDecision({error:new Error(message)}),'undecided');
});
test('Given an absent valid name and a synonym page, the synonym supplies the archive reference',async()=>{
  const calls=[];
  const result=await checkSpecies(species,{fetchPage:async url=>{calls.push(url);if(url.endsWith('one-alpha')) throw new Error('HTTP 404');return {html:page('Old alpha'),ref:'archive/ref',meta:{httpStatus:200}};},checked:'2026-10-14'});
  assert.equal(calls.length,2);assert.deepEqual(result,{url:calls[1],archived:'archive/ref',checked:'2026-10-14'});
});
test('Given a temporary failure among absent candidates, the species stays undecided',async()=>{
  assert.equal(await checkSpecies(species,{fetchPage:async url=>{throw new Error(url.endsWith('one-alpha')?'HTTP 503':'HTTP 404');}}),undefined);
  const result=await checkSpecies(species,{fetchPage:async()=>{throw new Error('HTTP 404');},checked:'2026-10-14'});
  assert.deepEqual(result,{url:null,archived:null,checked:'2026-10-14'});
});
test('Given a temporary failure followed by a synonym page, positive evidence settles the species',async()=>{
  const result=await checkSpecies(species,{fetchPage:async url=>{if(url.endsWith('one-alpha')) throw new Error('HTTP 503');return {html:page('Old alpha'),ref:'archive/ref',meta:{httpStatus:200}};}});
  assert.ok(result.url.endsWith('old-alpha'));
});
test('Given recorded negative decisions, resuming skips their candidates',async t=>{
  const root=mkdtempSync(join(tmpdir(),'seriously-fish-test-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
  const output=join(root,'result.yaml');let calls=0;
  const options={species:[species],output,fetchPage:async()=>{calls++;throw new Error('HTTP 404');},log:()=>{}};
  await runCheck(options);assert.equal(calls,2);
  await runCheck(options);assert.equal(calls,2);
  assert.equal(parse(readFileSync(output,'utf8')).species['one-alpha'].url,null);
});

test('Given a synonym URL with a different genus in the title, its matching canonical species route identifies the page',()=>{
  const html=page('Another alpha').replace('</head>','<link rel="canonical" href="https://www.seriouslyfish.com/species/old-alpha"/></head>');
  assert.equal(pageDecision({html,names:['One alpha','Old alpha'],url:'https://www.seriouslyfish.com/species/old-alpha'}),'found');
  assert.equal(pageDecision({html,names:['One alpha'],url:'https://www.seriouslyfish.com/species/one-alpha'}),'undecided');
});

test('Given a synonym route whose canonical and title use another genus, matching epithets identify its care page',()=>{
  const html=page('Another alpha').replace('</head>','<link rel="canonical" href="https://www.seriouslyfish.com/species/another-alpha"/></head>');
  assert.equal(pageDecision({html,names:['One alpha','Old alpha'],url:'https://www.seriouslyfish.com/species/old-alpha'}),'found');
  assert.equal(pageDecision({html,names:['One beta'],url:'https://www.seriouslyfish.com/species/one-beta'}),'undecided');
});

test('Given a homonymous synonym page naming another accepted species, it supplies no evidence for the requested species',()=>{
  const html=page('Other alpha').replace('</head>','<link rel="canonical" href="https://www.seriouslyfish.com/species/other-alpha"/></head>');
  assert.equal(pageDecision({html,names:['One alpha','Old alpha'],url:'https://www.seriouslyfish.com/species/old-alpha',validNames:new Set(['One alpha','Other alpha'])}),'absent');
});

test('Given a species page with sparse husbandry content, its title and canonical species URL establish existence',()=>{
  const html='<title>One alpha · Seriously Fish</title><link rel="canonical" href="https://www.seriouslyfish.com/species/one-alpha"/><main><h2>Distribution</h2><p>River basin</p><h2>Habitat</h2><p>Shallow water</p></main>';
  assert.equal(pageDecision({html,names:['One alpha'],url:'https://www.seriouslyfish.com/species/one-alpha'}),'found');
});

test('Given an undecided species, resuming retries it and persists a positive decision',async t=>{
  const root=mkdtempSync(join(tmpdir(),'seriously-fish-resume-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
  const output=join(root,'result.yaml');let calls=0;let unavailable=true;
  const options={species:[species],output,log:()=>{},fetchPage:async()=>{
    calls++;
    if(unavailable) throw new Error('HTTP 503');
    return {html:page('One alpha'),ref:'archive/ref',meta:{httpStatus:200}};
  }};
  const failed=await runCheck(options);assert.equal(failed.undecided,1);assert.equal(failed.decided,0);assert.equal(calls,2);
  unavailable=false;
  const resumed=await runCheck(options);assert.equal(resumed.undecided,0);assert.equal(calls,3);
  assert.equal(parse(readFileSync(output,'utf8')).species['one-alpha'].url,'https://www.seriouslyfish.com/species/one-alpha');
  await runCheck(options);assert.equal(calls,3);
});

test('Given quotation marks around a genus or epithet, title identity ignores the quotation marks',()=>{
  for(const name of ['One &quot;alpha&quot;','&quot;One&quot; alpha','One ‘alpha’']) {
    const html=page(name).replace('</head>','<link rel="canonical" href="https://www.seriouslyfish.com/species/one-alpha"/></head>');
    assert.equal(pageDecision({html,names:['One alpha'],url:'https://www.seriouslyfish.com/species/one-alpha'}),'found');
  }
});

test('Given an agent-planted symlink as output, the check refuses to write through it',async t=>{
  const {symlinkSync,mkdirSync,writeFileSync,realpathSync}=await import('node:fs');
  const dir=realpathSync(mkdtempSync(join(tmpdir(),'seriously-fish-link-')));t.after(()=>rmSync(dir,{recursive:true,force:true}));
  const output=join(dir,'data','species','seriously-fish.yaml');
  mkdirSync(join(dir,'data','species'),{recursive:true});
  writeFileSync(join(dir,'target.yaml'),'species: {}\n');
  symlinkSync(join(dir,'target.yaml'),output);
  await assert.rejects(runCheck({species:[species],output,log:()=>{},fetchPage:async()=>{throw new Error('HTTP 404');}}),/refusing to write through the symlink/);
  assert.equal(readFileSync(join(dir,'target.yaml'),'utf8'),'species: {}\n');
});
