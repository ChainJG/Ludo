import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
const root = path.resolve(process.argv[2] || 'artifacts/arena/wwwroot');
const prefix = process.argv[3] || '/Ludo/';
const port = Number(process.argv[4] || 5252);
const mime = { '.html':'text/html', '.js':'text/javascript', '.css':'text/css', '.json':'application/json', '.wasm':'application/wasm', '.png':'image/png' };
http.createServer(async (request, response) => {
 try {
  const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
  if (!pathname.startsWith(prefix)) { response.writeHead(404).end(); return; }
  const relative = pathname.slice(prefix.length) || 'index.html';
  const file = path.resolve(root, relative);
  if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return; }
  const content = await fs.readFile(file);
  response.writeHead(200, { 'Content-Type':mime[path.extname(file)] || 'application/octet-stream', 'Cache-Control':'no-store' }); response.end(content);
 } catch { response.writeHead(404).end(); }
}).listen(port, '127.0.0.1', () => process.stdout.write(`Static Arena: http://127.0.0.1:${port}${prefix}\n`));
