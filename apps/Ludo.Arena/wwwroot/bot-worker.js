import { dotnet } from './_framework/dotnet.js';
let bridge;
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    bridge = exports.Ludo.Arena.BrowserBotBridge;
    bridge.Warmup();
    self.postMessage({ type: 'ready' });
} catch (error) {
    self.postMessage({ type: 'error', error: String(error) });
}
self.addEventListener('message', event => {
    const { id, json, command } = event.data;
    try {
        if (!bridge) throw new Error('The C# worker could not initialize.');
        self.postMessage({ type: 'started', id });
        const result = command === 'replay' ? bridge.Replay(json) : bridge.Evaluate(json);
        self.postMessage({ type: 'result', id, result });
    } catch (error) {
        self.postMessage({ type: 'error', id, error: String(error) });
    }
});
