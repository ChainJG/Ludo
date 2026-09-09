// Original, locally synthesized game sounds; no downloads or audio tracking.
export function scheduleSound(context, output, kind, when = context.currentTime) {
    function note(start, duration, frequency, endFrequency, volume, type = 'sine') {
        const oscillator = context.createOscillator(), envelope = context.createGain();
        oscillator.type = type;
        oscillator.frequency.setValueAtTime(frequency, when + start);
        oscillator.frequency.exponentialRampToValueAtTime(endFrequency, when + start + duration);
        envelope.gain.setValueAtTime(0, when + start);
        envelope.gain.linearRampToValueAtTime(volume, when + start + .006);
        envelope.gain.exponentialRampToValueAtTime(.0001, when + start + duration);
        oscillator.connect(envelope); envelope.connect(output);
        oscillator.start(when + start); oscillator.stop(when + start + duration + .01);
        oscillator.onended = () => { oscillator.disconnect(); envelope.disconnect(); };
    }
    if (kind === 'dice') {
        for (let i = 0; i < 9; i++) note(i * .065, .045, 420 + (i % 3) * 170, 110, .22, 'triangle');
    } else if (kind === 'hop') {
        note(0, .11, 620, 190, .32, 'sine');
        note(0, .04, 1350, 600, .08, 'triangle');
    } else if (kind === 'death') {
        note(0, .48, 330, 55, .22, 'triangle');
        note(.09, .4, 247, 45, .15, 'sine');
    } else if (kind === 'victory') {
        [523.25, 659.25, 783.99, 1046.5].forEach((pitch, i) => note(i * .14, .45, pitch, pitch, .18, 'triangle'));
        [523.25, 659.25, 783.99].forEach(pitch => note(.7, .85, pitch, pitch, .12, 'sine'));
    } else throw new Error(`Unknown game sound: ${kind}`);
}

export function createSoundBank() {
    let context, master, enabled = true;
    const played = {dice:0,hop:0,death:0,victory:0};
    function unlock() {
        if (!enabled) return;
        try {
            const Audio = globalThis.AudioContext || globalThis.webkitAudioContext;
            if (!Audio) return;
            if (!context) { context = new Audio(); master = context.createGain(); master.gain.value = .38; master.connect(context.destination); }
            if (context.state === 'suspended') void context.resume().catch(() => {});
        } catch { /* Audio availability must never interrupt a turn. */ }
    }
    return {
        unlock,
        setEnabled(value) { enabled = !!value; if (master) master.gain.setValueAtTime(enabled ? .38 : 0, context.currentTime); },
        play(kind) {
            if (!enabled || globalThis.document?.visibilityState === 'hidden' || context?.state !== 'running') return;
            try { scheduleSound(context, master, kind); played[kind]++; } catch { /* Keep the game playable without audio. */ }
        },
        read() { return {enabled, state:context?.state ?? 'locked', played:{...played}}; }
    };
}
