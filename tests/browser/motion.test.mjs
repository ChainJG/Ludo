import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const source = await fs.readFile(new URL('../../apps/Ludo.Arena/wwwroot/motion.js', import.meta.url), 'utf8');
const { movementFrames, playBoardMotion } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('forward motion lands on every square and bounces between them', () => {
 const points = [{x:10,y:20},{x:15,y:20},{x:20,y:20}];
 const frames = movementFrames(points, false);
 assert.equal(frames.length,5);
 assert.deepEqual(frames.filter((_, i) => i % 2 === 0).map(f => [f.left,f.top]), [['10%','20%'],['15%','20%'],['20%','20%']]);
 assert.ok(Number.parseFloat(frames[1].top)<20);
 assert.equal(frames.at(-1).offset,1);
 assert.deepEqual(movementFrames(points, true).map(f => f.top), ['20%','20%','20%']);
});

test('reduced motion bypasses animation completely', async () => {
 await playBoardMotion({querySelectorAll(){throw Error('Motion should be skipped');}}, {moves:[{}]}, true);
});

test('capture waits for arrival and clears its ghost and animation handles', async () => {
 const events = []; let ghostRemoved = false;
 function animation(label) { return { finished:Promise.resolve().then(() => events.push(label)),cancel(){events.push(`clear ${label}`);} }; }
 const token = key => ({dataset:{tokenKey:key},style:{},classList:{add(){},remove(){}},removeAttribute(){},setAttribute(){},remove(){},
  cloneNode(){return token(key);},animate(frames){return animation(key);}});
 const tokens = [token('0-0'),token('1-0')];
 const board = {querySelectorAll:()=>tokens,append(el){if(el.className==='capture-ghost') events.push('ghost created');}};
 const prior = globalThis.document;
 globalThis.document = {createElement:()=>({style:{},setAttribute(){},animate:()=>animation('ghost'),remove(){ghostRemoved=true;}})};
 try {
  const sounds = [];
  await playBoardMotion(board,{moves:[{key:'0-0',points:[{x:1,y:1},{x:1.5,y:1},{x:2,y:1}],captured:false},{key:'1-0',points:[{x:2,y:1},{x:3,y:3}],captured:true}]},false, kind => sounds.push(kind));
  assert.ok(events.indexOf('0-0') < events.indexOf('ghost created'));
  assert.ok(ghostRemoved); assert.ok(events.includes('clear 1-0'));
  assert.deepEqual(tokens.map(t => [t.style.left,t.style.top]), [['2%','1%'],['3%','3%']], 'landing positions survive animation cleanup until the next render');
  assert.deepEqual(sounds,['hop','hop','death'], 'every landing sounds before the capture');
  assert.ok(tokens.every(t => t.style.visibility === ''), 'original pawns are restored');
 } finally { globalThis.document=prior; }
});
