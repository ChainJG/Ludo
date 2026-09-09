// Percentage coordinates come exclusively from the shared C# board presentation.
export const HOP_MS = 260;
const active = new Set();
const completed = {hops:0,captures:0,dice:0};
export function readMotion() {
    return { completed:{...completed}, active:[...active].map(({animation,...info}) => ({...info,
        playState:animation.playState, currentTime:animation.currentTime, progress:animation.effect?.getComputedTiming().progress ?? null})) };
}
export async function trackAnimation(element, frames, options, info) {
    const animation = element.animate(frames, options), entry = {...info,animation};
    active.add(entry);
    try { await animation.finished; }
    finally { active.delete(entry); animation.cancel(); }
}
export function movementFrames(points, captured) {
    const frames = [];
    for (let i = 0; i < points.length; i++) {
        const p = points[i];
        frames.push({left:`${p.x}%`,top:`${p.y}%`,transform:'translate(-50%, -90%) scale(1)',
            easing:captured ? 'linear' : 'ease-out',offset:i / (points.length - 1)});
        if (!captured && i < points.length - 1) {
            const q = points[i + 1];
            frames.push({left:`${(p.x + q.x) / 2}%`,top:`${(p.y + q.y) / 2 - 2.8}%`,
                transform:'translate(-50%, -90%) scale(.92, 1.08)',easing:'ease-in',offset:(i + .5) / (points.length - 1)});
        }
    }
    return frames;
}

const skeleton = '<svg viewBox="0 0 64 88" fill="none" stroke="#19304f" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path fill="#fff8e9" d="M14 21C14 1 50 1 50 21c0 9-5 13-10 15v7H24v-7c-5-2-10-6-10-15Z"/><ellipse fill="#19304f" cx="24" cy="23" rx="5" ry="6"/><ellipse fill="#19304f" cx="40" cy="23" rx="5" ry="6"/><path d="m30 32 2-3 2 3M29 37v6m6-6v6"/><g stroke="#fff8e9" stroke-width="5"><path d="M32 46v22m-12-18 12 4 12-4m-24 9 12 4 12-4M18 48 9 59l6 9m31-20 9 11-6 9M32 68l-9 9-3 7m12-16 9 9 3 7"/></g></svg>';

export async function playBoardMotion(board, plan, reduced = false, sound = () => {}) {
    if (!board || !plan.moves.length) return;
    if (reduced) { sound(plan.moves.some(m => m.captured) ? 'death' : 'hop'); return; }
    const tokens = new Map([...board.querySelectorAll('[data-token-key]')].map(el => [el.dataset.tokenKey,el]));
    const travellers = [], ghosts = [], ghostFlights = [];
    async function move(item) {
        const token = tokens.get(item.key);
        if (!token) throw new Error(`Cannot animate missing pawn ${item.key}.`);
        // A separate traveller keeps Blazor and idle effects from resetting a hop.
        const pawn = token.cloneNode(true);
        pawn.removeAttribute('data-token-key'); pawn.removeAttribute('id');
        pawn.setAttribute('aria-hidden','true'); pawn.tabIndex = -1;
        pawn.classList.remove('legal'); pawn.classList.add('travelling');
        pawn.style.zIndex = item.captured ? '7' : '8';
        token.style.visibility = 'hidden'; board.append(pawn); travellers.push({token,pawn,item});
        if (item.captured) {
            sound('death');
            const ghost = document.createElement('span'); ghost.className = 'capture-ghost'; ghost.setAttribute('aria-hidden','true');
            ghost.innerHTML = skeleton; ghost.style.left = `${item.points[0].x}%`; ghost.style.top = `${item.points[0].y}%`;
            board.append(ghost); ghosts.push(ghost);
            ghostFlights.push(trackAnimation(ghost,[
                {transform:'translate(-50%,-60%) rotate(-12deg) scale(.55)',opacity:0},
                {transform:'translate(-50%,-130%) rotate(7deg) scale(1.1)',opacity:1,offset:.2},
                {transform:'translate(-25%,-330%) rotate(-8deg) scale(.85)',opacity:0}
            ],{duration:1200,fill:'forwards',easing:'ease-out'},{kind:'skeleton',key:item.key}));
            await trackAnimation(pawn,movementFrames(item.points,true),{
                duration:Math.min(1050,Math.max(300,(item.points.length - 1) * 25)),fill:'forwards',easing:'linear'
            },{kind:'capture',key:item.key,steps:item.points.length-1});
            completed.captures++;
        } else {
            for (let step = 1; step < item.points.length; step++) {
                await trackAnimation(pawn,movementFrames(item.points.slice(step-1,step+1),false),
                    {duration:HOP_MS,fill:'forwards',easing:'linear'},{kind:'hop',key:item.key,step,steps:item.points.length-1});
                const landing = item.points[step];
                pawn.style.left = `${landing.x}%`; pawn.style.top = `${landing.y}%`;
                sound('hop'); completed.hops++;
            }
        }
        const end = item.points.at(-1);
        pawn.style.left = `${end.x}%`; pawn.style.top = `${end.y}%`;
    }
    try {
        await Promise.all(plan.moves.filter(m => !m.captured).map(move));
        await Promise.all(plan.moves.filter(m => m.captured).map(move));
        await Promise.all(ghostFlights);
    } finally {
        for (const {token,pawn,item} of travellers) {
            const end = item.points.at(-1);
            token.style.left = `${end.x}%`; token.style.top = `${end.y}%`;
            token.style.visibility = ''; pawn.remove();
        }
        for (const ghost of ghosts) ghost.remove();
    }
}

export async function playDice(die, reduced = false, sound = () => {}) {
    sound('dice');
    if (reduced) return;
    if (!die) throw new Error('The active player die is missing.');
    const original = die.getAttribute('src'); let frame = 0;
    const timer = setInterval(() => { die.src = `assets/dice-${++frame % 6 + 1}.png`; },65);
    try {
        await trackAnimation(die,[
            {transform:'translateY(0) rotate(0) scale(1)'},
            {transform:'translateY(-18px) rotate(190deg) scale(1.16)',offset:.35},
            {transform:'translateY(-8px) rotate(400deg) scale(.95)',offset:.7},
            {transform:'translateY(0) rotate(540deg) scale(1)'}
        ],{duration:650,easing:'ease-out'},{kind:'dice'});
        completed.dice++;
    } finally { clearInterval(timer); die.setAttribute('src',original); }
}
