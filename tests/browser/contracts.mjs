import { chromium } from 'playwright';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
const root = path.resolve(import.meta.dirname, '../..');
const url = process.env.ARENA_URL || 'http://127.0.0.1:5252/Ludo/';
let executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE;
if (!executablePath && process.platform === 'win32') {
 const cache = path.join(process.env.LOCALAPPDATA, 'ms-playwright');
 const versions = (await fs.readdir(cache)).filter(n => n.startsWith('chromium_headless_shell-')).sort().reverse();
 if (versions.length) executablePath = path.join(cache, versions[0], 'chrome-headless-shell-win64/chrome-headless-shell.exe');
}
const browser = await chromium.launch({ headless:true, ...(executablePath ? { executablePath } : {}) });
const page = await browser.newPage();
const errors = [], badRequests = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => { if (message.type() === 'error') process.stderr.write(message.text() + '\n'); });
page.on('response', response => { if (response.status() >= 400) badRequests.push(`${response.status()} ${response.url()}`); });
try {
 for (let attempt = 0; attempt < 100; attempt++) {
  try { if ((await fetch(url)).ok) break; } catch {}
  await new Promise(resolve => setTimeout(resolve, 100));
 }
 await page.goto(url);
 const read = () => page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).readGame());
 async function waitReady(predicate) {
  const until = Date.now() + 60000;
  while (Date.now() < until) {
   try { const state = await read(); if (predicate(state)) return state; } catch {}
   await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error('Game did not become ready.');
 }
 await waitReady(state => state.canRoll);
 const first = await read();
 assert.equal(first.state.players.length, 2);
 assert.equal(first.state.rollNumber, 0);
 await assert.rejects(() => page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).moveToken(-1)));
 assert.deepEqual((await read()).state, first.state, 'invalid input must not change state');
 let position;
 for (let roll = 0; roll < 30; roll++) {
  position = await read();
  if (position.canMove) break;
  await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).rollDie());
 }
 assert.equal(position.canMove, true, 'human must eventually receive legal moves');
 assert.equal(position.error, '', 'bot execution should not report an incident');
 const requestFile = path.join(root, 'artifacts/browser-bot-request.json');
 const cli = path.join(root, 'apps/Ludo.Cli/bin/Release/net10.0/Ludo.Cli.dll');
 const complex = structuredClone(position.state);
 complex.rules.playerCount = 4; complex.currentPlayer = 0; complex.die = 6; complex.consecutiveSixes = 1;
 complex.players = [[2,15,28,43],[1,12,29,40],[5,14,27,39],[3,18,29,45]].map((tokens,seat) => ({ seat,tokens,status:0 }));
 for (const [scenario, view] of [['opening', position.state], ['four-player middle game', complex]])
 for (const key of ['v1','v2','v3','v4','v5','v6','v7','v9']) {
  const request = { bot:{key}, view, seed:9876 };
  await fs.writeFile(requestFile, JSON.stringify(request));
  const native = JSON.parse(execFileSync('dotnet', [cli, 'analyze', '--file', requestFile], { encoding:'utf8' }));
  const wasm = JSON.parse(await page.evaluate(async json => (await import(new URL('arena.js', document.baseURI))).executeBot(json, 10000), JSON.stringify(request)));
  assert.equal(wasm.error, null);
  assert.deepEqual(wasm.choice.move, native.choice.move, `${key}: browser/native move mismatch`);
  assert.equal(wasm.choice.nodes, native.choice.nodes);
  for (let i = 0; i < wasm.choice.candidates.length; i++)
   assert.ok(Math.abs(wasm.choice.candidates[i].score - native.choice.candidates[i].score) < 1e-8, `${key}: evaluation mismatch`);
  process.stdout.write(`${key} (${scenario}): identical native/browser choice; browser ${wasm.elapsedMilliseconds.toFixed(2)} ms\n`);
 }
 for (const [match, final] of [['match-native.json','native-final.json'],['match-four.json','four-final.json']]) {
  const record = await fs.readFile(path.join(root, 'artifacts', match), 'utf8');
  const expected = await fs.readFile(path.join(root, 'artifacts', final), 'utf8');
  const replayed = await page.evaluate(async json => (await import(new URL('arena.js', document.baseURI))).executeBot(json, 10000, 'replay'), record);
  assert.equal(replayed, expected, 'native/browser final replay state must match byte for byte');
 }
 const beforeReload = (await read()).state;
 await page.reload();
 await waitReady(state => state.hasSavedGame);
 const resumed = await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).resumeGame());
 assert.deepEqual(resumed.state, beforeReload, 'refresh/resume must preserve the pending decision');
 const legal = resumed.legalMoves[0].tokenId;
 const moved = await page.evaluate(async token => (await import(new URL('arena.js', document.baseURI))).moveToken(token), legal);
 assert.equal(moved.error, '');
 assert.notDeepEqual(moved.state.players, beforeReload.players, 'legal move must update the visible game state');
 const model = JSON.parse(await fs.readFile(path.join(root, 'src/Ludo.Bots/Models/neural-v7.json'), 'utf8'));
 const spec = { key:'v7', model };
 const loaded = await page.evaluate(async json => (await import(new URL('arena.js', document.baseURI))).loadNeuralModel(json), JSON.stringify(spec));
 assert.equal(loaded, model.name);
 await assert.rejects(() => page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).loadNeuralModel('{"key":"v7","model":{"schema":999}}')));
 const fresh = await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).startGame(2, ['human','custom-neural'], 6553));
 assert.equal(fresh.canRoll, true);
 // A second roll during the first roll animation must be rejected.
 const pendingRoll = page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).rollDie());
 await new Promise(resolve => setTimeout(resolve, 80));
 assert.equal((await read()).busy, true);
 await assert.rejects(() => page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).rollDie()));
 await pendingRoll;
 assert.equal((await read()).error, '');
 // Exercise actual human turns through the public controller, including the single
 // available pawn case. Reduced motion keeps this sequence fast without changing rules.
 await page.emulateMedia({ reducedMotion:'reduce' });
 await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).setEffectsPreferences(false, false));
 await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).startGame(2, ['human','human'], 1));
 let forcedFour = false, manualChoice = false, bonusWaits = false;
 for (let turn = 0; turn < 150 && !(forcedFour && manualChoice && bonusWaits); turn++) {
  const before = await read();
  assert.equal(before.error, '');
  if (before.canMove) {
   assert.ok(before.legalMoves.length > 1, 'a sole legal move must never wait for a click');
   manualChoice = true;
   await page.evaluate(async token => (await import(new URL('arena.js', document.baseURI))).moveToken(token), before.legalMoves[0].tokenId);
  } else {
   assert.equal(before.canRoll, true);
   const savedBeforeRoll = await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).load());
   const after = await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).rollDie());
   assert.equal(after.error, '');
   const player = before.state.currentPlayer, oldTokens = before.state.players[player].tokens;
   const active = oldTokens.map((progress, token) => ({progress,token})).filter(p => p.progress >= 0 && p.progress <= 52);
   if (active.length === 1 && oldTokens.filter(p => p === -1).length === 3 && after.state.lastEvent.die === 4) {
    assert.equal(after.state.players[player].tokens[active[0].token], active[0].progress + 4, 'the only available pawn must automatically advance four squares');
    assert.equal(after.canMove, false);
    // An older save may stop after the roll, before the forced move was played.
    const oldSave = JSON.parse(savedBeforeRoll);
    oldSave.match.actions.push({kind:0,value:4});
    await page.evaluate(async json => (await import(new URL('arena.js', document.baseURI))).save(json), JSON.stringify(oldSave));
    await page.reload();
    await waitReady(state => state.hasSavedGame);
    const restored = await page.evaluate(async () => (await import(new URL('arena.js', document.baseURI))).resumeGame());
    assert.deepEqual(restored.state, after.state, 'resuming a pending forced roll must complete the same move');
    forcedFour = true;
   }
   if (after.canMove) assert.ok(after.legalMoves.length > 1);
  }
  const current = await read();
  if (current.canRoll && current.state.consecutiveSixes > 0) {
   const rolls = current.state.rollNumber;
   await new Promise(resolve => setTimeout(resolve, 30));
   assert.equal((await read()).state.rollNumber, rolls, 'bonus rolls must wait for the human');
   bonusWaits = true;
  }
 }
 assert.ok(forcedFour && manualChoice && bonusWaits, 'forced four, manual choices and bonus-roll waiting must all be exercised');
 assert.deepEqual(errors, []);
 assert.deepEqual(badRequests, [], 'all static resources must work under the repository subpath');
 const webMcpAvailable = await page.evaluate(() => !!document.modelContext?.registerTool);
 process.stdout.write(`PASS: browser C# bots, exact replay, invalid inputs, legal actions, forced moves, bonus-roll waiting, refresh/resume, static subpath. Native WebMCP registry available: ${webMcpAvailable}.\n`);
} finally { await browser.close(); }
