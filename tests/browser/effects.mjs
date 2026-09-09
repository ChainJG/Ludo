import { chromium } from 'playwright';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname,'../..');
let executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE;
if (!executablePath && process.platform === 'win32') {
 const cache = path.join(process.env.LOCALAPPDATA,'ms-playwright');
 const versions = (await fs.readdir(cache)).filter(n => n.startsWith('chromium_headless_shell-')).sort().reverse();
 if (versions.length) executablePath = path.join(cache,versions[0],'chrome-headless-shell-win64/chrome-headless-shell.exe');
}
const browser = await chromium.launch({headless:true,args:['--autoplay-policy=no-user-gesture-required'],...(executablePath ? {executablePath} : {})});
const page = await browser.newPage();
const errors = []; page.on('pageerror',error => errors.push(error.message));
const url = process.env.ARENA_URL || 'http://127.0.0.1:5252/Ludo/';
const call = (name,...args) => page.evaluate(async ({name,args}) => (await import(new URL('arena.js',document.baseURI)))[name](...args),{name,args});
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve,milliseconds));
async function ready(saved = false) {
 for (let i=0;i<600;i++) { try { const state=await call('readGame'); if(saved ? state.hasSavedGame : state.canRoll) return state; } catch {} await sleep(100); }
 throw Error('Arena did not initialize.');
}
try {
 // An OS preference must not silently override the explicit Arena animation switch.
 await page.emulateMedia({reducedMotion:'reduce'});
 await page.goto(url); await ready();
 assert.deepEqual((await call('readEffects')).preferences,{sound:true,motion:true});
 const recording = JSON.parse(await fs.readFile(path.join(root,'artifacts/match-native.json'),'utf8'));
 // Find authentic capture and victory turns by replaying the native fixture in C#.
 const fixtures = await page.evaluate(async record => {
  const api = await import(new URL('arena.js',document.baseURI));
  const found = {};
  for(let i=0;i<record.actions.length;i++) {
   if(record.actions[i].kind !== 1) continue;
   const state = JSON.parse(await api.executeBot(JSON.stringify({...record,actions:record.actions.slice(0,i+1)}),10000,'replay')).view;
   for(const [name,yes] of [['capture',state.lastEvent.captures.length > 0],['victory',state.finishingOrder.length > 0]])
    if(yes && !found[name]) found[name]={index:i,state};
   if(found.capture && found.victory) break;
  }
  return found;
 },recording);
 assert.ok(fixtures.capture && fixtures.victory);
 for(const [name,fixture] of Object.entries(fixtures)) {
  assert.equal(recording.actions[fixture.index-1].kind,0);
  const saved = {match:{...recording,seats:[null,null],actions:recording.actions.slice(0,fixture.index-1),result:null},records:{},counted:false};
  await call('save',JSON.stringify(saved)); await page.reload(); await ready(true);
  await call('setEffectsPreferences',true,true); await call('resumeGame');
  const baseline = await call('readEffects'), seen=[];
  let done=false, result, failure;
  const action = (async () => {
   result=await call('rollDie');
   if(result.canMove) result=await call('moveToken',recording.actions[fixture.index].value);
  })().catch(error=>{failure=error;}).finally(()=>{done=true;});
  const deadline=Date.now()+15000;
  while(!done && Date.now()<deadline) { const effects=await call('readEffects'); seen.push(...effects.motion.active); await sleep(35); }
  assert.ok(done,`${name} animation must complete`); await action; if(failure) throw failure;
  assert.equal(result.error,''); assert.deepEqual(result.state,fixture.state);
  const effects=await call('readEffects');
  for(const kind of name==='capture' ? ['dice','hop','capture','skeleton'] : ['dice','hop']) {
   assert.ok(seen.some(a=>a.kind===kind && a.playState==='running' && a.currentTime>0 && a.progress>0 && a.progress<1),`${name}: ${kind} must actually advance on the browser animation timeline`);
  }
  const hops=fixture.state.lastEvent.from === -1 ? 1 : fixture.state.lastEvent.die;
  assert.equal(effects.motion.completed.hops-baseline.motion.completed.hops,hops,'each travelled square gets its own hop');
  assert.equal(effects.audio.played.hop-baseline.audio.played.hop,hops,'each landing gets one sound');
  assert.equal(effects.audio.played.dice-baseline.audio.played.dice,1);
  assert.ok(effects.audio.played[name==='capture' ? 'death' : 'victory']>0);
  assert.equal(effects.motion.active.length,0,'all effect handles must be released');
  process.stdout.write(`PASS: ${name} turn has running dice, per-square hops, synchronized audio${name==='capture' ? ', reverse travel and floating skeleton' : ', and victory fanfare'}.\n`);
 }
 // Render the real synthesis code offline, checking all four sounds produce distinct,
 // non-clipping waveforms. This does not depend on a speaker being attached to CI.
 const signals=await page.evaluate(async () => {
  const {scheduleSound}=await import(new URL('sound.js?v=effects-2',document.baseURI));
  const signals=[];
  for(const kind of ['dice','hop','death','victory']) {
   const context=new OfflineAudioContext(1,48000,24000), gain=context.createGain();
   gain.gain.value=.38; gain.connect(context.destination); scheduleSound(context,gain,kind,0);
   const samples=(await context.startRendering()).getChannelData(0);
   signals.push({kind,peak:samples.reduce((a,b)=>Math.max(a,Math.abs(b)),0),energy:samples.reduce((a,b)=>a+b*b,0)});
  }
  return signals;
 });
 assert.ok(signals.every(s=>s.peak>.01 && s.peak<1 && s.energy>1));
 assert.equal(new Set(signals.map(s=>s.energy.toFixed(3))).size,4);
 await call('setEffectsPreferences',false,false);
 const muted=await call('readEffects'); await call('celebrateVictory');
 assert.deepEqual((await call('readEffects')).audio.played,muted.audio.played,'muting must suppress sound');
 await page.reload(); await ready(true);
 assert.deepEqual((await call('readEffects')).preferences,{sound:false,motion:false},'effect switches persist across reloads');
 assert.deepEqual(errors,[]);
 process.stdout.write('PASS: four distinct audio waveforms, mute, and saved animation preferences.\n');
} finally { await browser.close(); }
