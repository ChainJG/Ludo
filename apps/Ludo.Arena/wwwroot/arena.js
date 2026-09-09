let worker, ready, startupTimer, startupReject;
import { playBoardMotion } from './motion.js';
export async function animateMove(board, json) { await playBoardMotion(board, JSON.parse(json)); }
export async function animateDice(player) {
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const die = document.querySelector(`.player-die[data-player="${player}"] img`);
    if (!die) return;
    const original = die.getAttribute('src'); let frame = 0;
    const timer = setInterval(() => { die.src = `assets/dice-${++frame % 6 + 1}.png`; }, 70);
    const animation = die.animate([
        {transform:'translateY(0) rotate(0) scale(1)'},
        {transform:'translateY(-12px) rotate(160deg) scale(1.12)',offset:.4},
        {transform:'translateY(-5px) rotate(290deg) scale(.95)',offset:.8},
        {transform:'translateY(0) rotate(360deg) scale(1)'}
    ], {duration:500,easing:'ease-out'});
    try { await animation.finished; } finally { clearInterval(timer); die.setAttribute('src', original); animation.cancel(); }
}
let sequence = 0;
const pending = new Map();

function ensureWorker() {
    if (ready) return ready;
    ready = new Promise((resolve, reject) => {
        startupReject = reject;
        worker = new Worker(new URL('bot-worker.js', document.baseURI), { type: 'module' });
        const ownWorker = worker;
        const startup = startupTimer = setTimeout(() => { if (worker === ownWorker) stopWorker(); reject(new Error('The bot could not load. Please reload.')); }, 60000);
        worker.onmessage = ({ data }) => {
            if (worker !== ownWorker) return;
            if (data.type === 'ready') { clearTimeout(startup); startupReject = undefined; resolve(); return; }
            if (!data.id) { clearTimeout(startup); reject(new Error(data.error)); stopWorker(); return; }
            const request = pending.get(data.id);
            if (!request) return;
            if (data.type === 'started') {
                clearTimeout(request.timer);
                request.timer = setTimeout(() => {
                    request.resolve(JSON.stringify({ choice: null, elapsedMilliseconds: request.budget + 1, error: 'Bot exceeded its execution watchdog.' }));
                    pending.delete(data.id);
                    stopWorker();
                }, Math.max(250, request.budget * 4));
            } else {
                clearTimeout(request.timer); pending.delete(data.id);
                if (data.type === 'error') request.reject(new Error(data.error));
                else request.resolve(data.result);
            }
        };
        worker.onerror = event => { clearTimeout(startup); reject(new Error(event.message)); stopWorker(); };
    });
    return ready;
}

export async function executeBot(json, budget, command = 'move') {
    await ensureWorker();
    const id = ++sequence;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => { pending.delete(id); stopWorker(); reject(new Error('Bot worker did not respond.')); }, 15000);
        pending.set(id, { resolve, reject, timer, budget });
        worker.postMessage({ id, json, command });
    });
}
export function stopWorker() {
    clearTimeout(startupTimer); startupReject?.(new Error('Bot startup stopped.')); startupReject = undefined;
    if (worker) worker.terminate();
    for (const request of pending.values()) { clearTimeout(request.timer); request.reject(new Error('Bot execution stopped.')); }
    pending.clear(); worker = undefined; ready = undefined;
}
export function load() { return localStorage.getItem('ludo-arena-v1'); }
export function save(value) { localStorage.setItem('ludo-arena-v1', value); }
export function download(name, json) {
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

let controller, registration;
export function readGame() { if (!controller) throw new Error('Game is loading.'); return controller.invokeMethodAsync('ReadGame'); }
export function loadNeuralModel(json) { if (!controller) throw new Error('Game is loading.'); return controller.invokeMethodAsync('LoadNeuralModel', json); }
export function startGame(players, seats, seed) { if (!controller) throw new Error('Game is loading.'); return controller.invokeMethodAsync('StartGame', players, seats, seed); }
export function openPanel(id) { const panel = document.getElementById(id); if (panel) { panel.open = true; panel.scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth',block:'start'}); } }
export function rollDie() { if (!controller) throw new Error('Game is loading.'); return controller.invokeMethodAsync('RollDie'); }
export function resumeGame() { if (!controller) throw new Error('Game is loading.'); return controller.invokeMethodAsync('ResumeGame'); }
export function moveToken(token) {
    if (!Number.isInteger(token) || token < 0 || token > 3) throw new Error('Token must be an integer from zero to three.');
    if (!controller) throw new Error('Game is loading.');
    return controller.invokeMethodAsync('MoveToken', token);
}
export function unregisterGameTools() { registration?.abort(); registration = undefined; controller = undefined; }
export function registerGameTools(handle) {
    unregisterGameTools(); controller = handle;
    const context = document.modelContext;
    if (!context?.registerTool) return;
    registration = new AbortController();
    const definitions = [
        { name: 'resume_ludo_game', description: 'Resume the saved Ludo match and update the visible board.',
            inputSchema: { type: 'object', properties: {}, additionalProperties: false },
            annotations: { readOnlyHint: false }, execute: resumeGame },
        { name: 'read_ludo_game', description: 'Read the visible Ludo state and currently legal human moves.',
            inputSchema: { type: 'object', properties: {}, additionalProperties: false },
            annotations: { readOnlyHint: true }, execute: readGame },
        { name: 'roll_ludo_die', description: 'Roll for the current human player, then complete resulting bot turns.',
            inputSchema: { type: 'object', properties: {}, additionalProperties: false },
            annotations: { readOnlyHint: false }, execute: rollDie },
        { name: 'move_ludo_token', description: 'Move a legal human token, then complete resulting bot turns.',
            inputSchema: { type: 'object', properties: { token: { type: 'integer', minimum: 0, maximum: 3 } }, required: ['token'], additionalProperties: false },
            annotations: { readOnlyHint: false }, execute: input => moveToken(input.token) }
    ];
    for (const tool of definitions) {
        try { Promise.resolve(context.registerTool(tool, { signal: registration.signal })).catch(() => {}); }
        catch { /* Unsupported draft API must never prevent a game from starting. */ }
    }
}
