// Plans use percentage board coordinates from C#. This module never changes game state.
export function movementFrames(points, captured) {
    const frames = [];
    for (let i = 0; i < points.length; i++) {
        const p = points[i];
        frames.push({ left: `${p.x}%`, top: `${p.y}%`, transform: 'translate(-50%, -90%) scale(1)', easing: captured ? 'linear' : 'ease-out', offset: i / (points.length - 1) });
        if (!captured && i < points.length - 1) {
            const q = points[i + 1];
            frames.push({ left: `${(p.x + q.x) / 2}%`, top: `${(p.y + q.y) / 2 - 2.1}%`,
                transform: 'translate(-50%, -90%) scale(.94, 1.06)', easing:'ease-in', offset: (i + .5) / (points.length - 1) });
        }
    }
    return frames;
}

export async function playBoardMotion(board, plan, reduced = matchMedia('(prefers-reduced-motion: reduce)').matches) {
    if (reduced || !board || !plan.moves.length) return;
    const tokens = new Map([...board.querySelectorAll('[data-token-key]')].map(el => [el.dataset.tokenKey, el]));
    const ghosts = [], animations = [];
    async function move(item) {
        const token = tokens.get(item.key); if (!token) return;
        token.style.zIndex = item.captured ? '7' : '8';
        token.classList.add('travelling');
        if (item.captured) {
            const ghost = document.createElement('span'); ghost.className = 'capture-ghost'; ghost.setAttribute('aria-hidden', 'true');
            ghost.textContent = '👻'; ghost.style.left = `${item.points[0].x}%`; ghost.style.top = `${item.points[0].y}%`; board.append(ghost); ghosts.push(ghost);
            const float = ghost.animate([{ transform:'translate(-50%,-70%) scale(.5)',opacity:0 },
                { transform:'translate(-50%,-130%) scale(1.2)',opacity:1,offset:.25 },
                { transform:'translate(-35%,-340%) scale(1)',opacity:0 }], { duration:1000,fill:'forwards',easing:'ease-out' });
            animations.push(float);
        }
        const animation = token.animate(movementFrames(item.points, item.captured), {
            duration: item.captured ? Math.min(650, Math.max(160, (item.points.length - 1) * 15)) : (item.points.length - 1) * 180,
            easing:'linear',fill:'forwards'
        });
        animations.push(animation); await animation.finished.catch(() => {});
        // Keep the landing position when the fill animation is released. Blazor's
        // next render takes ownership with the same coordinates, without a snap back.
        const end = item.points.at(-1);
        token.style.left = `${end.x}%`; token.style.top = `${end.y}%`;
    }
    try {
        await Promise.all(plan.moves.filter(m => !m.captured).map(move));
        await Promise.all(plan.moves.filter(m => m.captured).map(move));
        await Promise.all(animations.map(a => a.finished.catch(() => {})));
    } finally {
        for (const animation of animations) animation.cancel();
        for (const ghost of ghosts) ghost.remove();
        for (const token of tokens.values()) { token.classList.remove('travelling'); token.style.zIndex = ''; }
    }
}
