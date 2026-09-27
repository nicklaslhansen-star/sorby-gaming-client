const http = require('http'), fs = require('fs'), path = require('path');
const dir = __dirname, frames = path.join(dir, 'frames');
fs.mkdirSync(frames, { recursive: true });
const types = { '.html': 'text/html', '.js': 'text/javascript', '.jpg': 'image/jpeg', '.png': 'image/png' };
let count = 0;
http.createServer((req, res) => {
  const u = new URL(req.url, 'http://x');
  if (req.method === 'POST' && u.pathname === '/frame') {
    const chunks = []; req.on('data', d => chunks.push(d));
    req.on('end', () => { const i = +u.searchParams.get('i'); fs.writeFileSync(path.join(frames, String(i).padStart(5, '0') + '.jpg'), Buffer.concat(chunks)); if (++count % 100 === 0) console.log('frames', count); res.end('ok'); });
    return;
  }
  if (u.pathname === '/done') { console.log('DONE', count); res.end('ok'); fs.writeFileSync(path.join(dir, 'done.txt'), String(count)); return; }
  const f = path.join(dir, u.pathname === '/' ? 'index.html' : u.pathname);
  fs.readFile(f, (e, d) => { if (e) { res.statusCode = 404; return res.end(); } res.setHeader('Content-Type', types[path.extname(f)] || 'application/octet-stream'); res.setHeader('Cache-Control', 'no-store'); res.end(d); });
}).listen(8765, () => console.log('listening 8765'));
