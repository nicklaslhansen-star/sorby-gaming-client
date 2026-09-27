'use strict';
const W = 1920, H = 1080, FPS = 60, DUR = 25;
const cv = document.getElementById('c'); const out = cv.getContext('2d');
const mk = () => { const c = document.createElement('canvas'); c.width = W; c.height = H; return c; };
const SC = mk(), sctx = SC.getContext('2d');
const TMP = mk(), tctx = TMP.getContext('2d');
const CA1 = mk(), CA2 = mk();
const COL = { navy: '#0b1020', panel: '#0e162a', panel2: '#131d36', line: '#34466e', line2: '#4b5f8d', cyan: '#65d7ff', ice: '#a3e9ff', green: '#75f1b0', amber: '#ffd889', white: '#f7f9fc', muted: '#b9c4dc', dim: '#7f8fb3', pink: '#ff3d81' };
const FONT = 'Bahnschrift, "Segoe UI", sans-serif';

// ---------- utils ----------
const cl = (x, a = 0, b = 1) => x < a ? a : x > b ? b : x;
const lerp = (a, b, t) => a + (b - a) * t;
const pr = (t, a, b) => cl((t - a) / (b - a));
const E = {
  oCub: t => 1 - Math.pow(1 - t, 3), iCub: t => t * t * t, ioCub: t => t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2,
  oQuart: t => 1 - Math.pow(1 - t, 4), iQuart: t => t * t * t * t,
  oExpo: t => t >= 1 ? 1 : 1 - Math.pow(2, -10 * t), iExpo: t => t <= 0 ? 0 : Math.pow(2, 10 * t - 10),
  ioExpo: t => t <= 0 ? 0 : t >= 1 ? 1 : t < .5 ? Math.pow(2, 20 * t - 10) / 2 : (2 - Math.pow(2, -20 * t + 10)) / 2,
  oBack: (t, s = 1.70158) => 1 + (s + 1) * Math.pow(t - 1, 3) + s * Math.pow(t - 1, 2),
};
const spring = (t, f = 2.2, z = 5) => t <= 0 ? 0 : 1 - Math.exp(-z * t) * Math.cos(2 * Math.PI * f * t);
function rng(seed) { return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const hash = n => { const x = Math.sin(n * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); };
const rgba = (hex, a) => { const n = parseInt(hex.slice(1), 16); return `rgba(${n >> 16},${n >> 8 & 255},${n & 255},${a})`; };
const pad2 = n => String(Math.max(0, Math.floor(n))).padStart(2, '0');
const mmss = s => pad2(s / 60) + ':' + pad2(s % 60);

function setFont(c, size, weight = 700, stretch = 'normal', spacing = 0) { c.font = `${weight} ${size}px ${FONT}`; c.fontStretch = stretch; c.letterSpacing = spacing + 'px'; }
const tw = (c, s) => c.measureText(s).width;

function revealText(c, str, x, y, t, o) {
  setFont(c, o.size, o.weight || 700, o.stretch || 'normal', o.spacing || 0);
  const total = tw(c, str) - (o.spacing || 0);
  const x0 = o.align === 'center' ? x - total / 2 : o.align === 'right' ? x - total : x;
  c.textAlign = 'left'; c.textBaseline = 'alphabetic';
  for (let i = 0; i < str.length; i++) {
    const off = tw(c, str.slice(0, i));
    const q = E.oExpo(pr(t, o.start + i * o.stagger, o.start + i * o.stagger + o.dur));
    if (q <= 0) continue;
    c.save();
    c.beginPath(); c.rect(x0 + off - o.size * 0.3, y - o.size * 1.05, o.size * 1.6, o.size * 1.3); c.clip();
    c.fillStyle = o.fill || COL.white;
    c.fillText(str[i], x0 + off, y + (1 - q) * o.size * 1.1);
    c.restore();
  }
  return { x0, w: total };
}
const GLY = 'ABCDEFGHJKLMNOPRSTUVXYZÆØÅ0123456789#/<>+=';
function scramble(c, str, x, y, t, o) {
  setFont(c, o.size, o.weight || 600, 'normal', o.spacing || 0);
  const total = tw(c, str) - (o.spacing || 0);
  const x0 = o.align === 'center' ? x - total / 2 : o.align === 'right' ? x - total : x;
  const n = str.length, fr = Math.floor(t * 24);
  c.textAlign = 'left'; c.fillStyle = o.fill || COL.cyan;
  for (let i = 0; i < n; i++) {
    const ti = o.start + (i / n) * o.dur;
    if (t < ti - 0.15 || str[i] === ' ') continue;
    const ch = t >= ti ? str[i] : GLY[Math.floor(hash(i * 13 + fr) * GLY.length)];
    c.fillText(ch, x0 + tw(c, str.slice(0, i)), y);
  }
  return { x0, w: total };
}
function fadeWords(c, str, x, y, t, start, o) {
  setFont(c, o.size, o.weight || 400, 'normal', 0);
  c.textAlign = 'left';
  const words = str.split(' '); let off = 0;
  words.forEach((w, i) => {
    const q = E.oCub(pr(t, start + i * 0.035, start + i * 0.035 + 0.4));
    c.save(); c.globalAlpha *= q; c.fillStyle = o.fill || COL.muted;
    c.fillText(w, x + off, y + (1 - q) * 18); c.restore();
    off += tw(c, w + ' ');
  });
}
function chip(c, text, x, y, T, icon = '●') {
  if (T <= 0) return;
  const q = E.oBack(pr(T, 0, 0.45)), a = pr(T, 0, 0.15);
  setFont(c, 24, 600, 'normal', 1);
  const w = tw(c, text) + 86, h = 54;
  c.save(); c.globalAlpha *= a; c.translate(x, y); c.scale(q, q);
  c.fillStyle = rgba(COL.cyan, 0.09); c.strokeStyle = rgba(COL.cyan, 0.55); c.lineWidth = 1.5;
  c.beginPath(); c.roundRect(0, -h / 2, w, h, h / 2); c.fill(); c.stroke();
  c.fillStyle = COL.cyan; setFont(c, 22, 700); c.textAlign = 'center'; c.textBaseline = 'middle';
  c.fillText(icon, 32, 1);
  setFont(c, 24, 600, 'normal', 1); c.textAlign = 'left'; c.fillStyle = COL.white; c.fillText(text, 56, 1);
  c.restore(); c.textBaseline = 'alphabetic';
}
function ripple(c, x, y, T, max = 70, col = COL.white) {
  if (T < 0 || T > 0.5) return;
  const q = E.oCub(T / 0.5);
  c.save(); c.strokeStyle = rgba(col, (1 - q) * 0.8); c.lineWidth = 3 * (1 - q) + 1;
  c.beginPath(); c.arc(x, y, max * q, 0, 7); c.stroke(); c.restore();
}
function ring(c, x, y, r, lw, a) { if (a <= 0 || lw <= 0) return; c.save(); c.globalCompositeOperation = 'lighter'; c.strokeStyle = rgba(COL.cyan, a); c.lineWidth = lw; c.shadowColor = COL.cyan; c.shadowBlur = 30; c.beginPath(); c.arc(x, y, Math.max(0, r), 0, 7); c.stroke(); c.restore(); }
function rays(c, x, y, t, a, n = 14, len = 1500) {
  if (a <= 0) return;
  c.save(); c.translate(x, y); c.rotate(t * 0.12); c.globalCompositeOperation = 'lighter';
  const g = c.createRadialGradient(0, 0, 0, 0, 0, len); g.addColorStop(0, rgba(COL.cyan, a)); g.addColorStop(1, rgba(COL.cyan, 0));
  c.fillStyle = g;
  for (let i = 0; i < n; i++) { c.rotate(Math.PI * 2 / n); const s = 0.03 + (i % 3) * 0.025; c.beginPath(); c.moveTo(0, 0); c.lineTo(len, -len * s); c.lineTo(len, len * s); c.fill(); }
  c.restore();
}
function drawLogo(c, x, y, size, a = 1) {
  c.save(); c.globalAlpha *= a;
  const g = c.createRadialGradient(x, y, 0, x, y, size * 1.2); g.addColorStop(0, rgba(COL.cyan, 0.45)); g.addColorStop(1, rgba(COL.cyan, 0));
  c.globalCompositeOperation = 'lighter'; c.fillStyle = g; c.fillRect(x - size * 1.2, y - size * 1.2, size * 2.4, size * 2.4);
  c.globalCompositeOperation = 'source-over'; c.imageSmoothingQuality = 'high';
  c.shadowColor = 'rgba(0,0,0,.6)'; c.shadowBlur = 40; c.shadowOffsetY = 16;
  c.drawImage(LOGO, x - size / 2, y - size / 2, size, size);
  c.restore();
}
function drawCursor(c, x, y, press = 0) {
  c.save(); c.translate(x, y); const s = 1.25 * (1 - press * 0.15); c.scale(s, s);
  c.beginPath(); c.moveTo(0, 0); c.lineTo(0, 34); c.lineTo(9, 26); c.lineTo(16, 41); c.lineTo(22, 38); c.lineTo(15, 24); c.lineTo(26, 24); c.closePath();
  c.shadowColor = 'rgba(0,0,0,.55)'; c.shadowBlur = 12; c.shadowOffsetY = 4;
  c.fillStyle = '#fff'; c.fill(); c.shadowColor = 'transparent'; c.strokeStyle = COL.navy; c.lineWidth = 2.2; c.stroke(); c.restore();
}

// ---------- assets ----------
const BG = new Image(); BG.src = 'assets/bg.jpg';
const LOGO = new Image(); LOGO.src = 'assets/logo256.png';
const VIG = mk(); { const v = VIG.getContext('2d'); const g = v.createRadialGradient(W / 2, H / 2, H * 0.35, W / 2, H / 2, W * 0.72); g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,.6)'); v.fillStyle = g; v.fillRect(0, 0, W, H); }

// ---------- QR ----------
const QN = 25;
function makeQR(seed) {
  const m = [], r = rng(seed);
  for (let i = 0; i < QN; i++) { m[i] = []; for (let j = 0; j < QN; j++) m[i][j] = r() < 0.47 ? 1 : 0; }
  const finder = (oi, oj) => { for (let i = -1; i < 8; i++) for (let j = -1; j < 8; j++) { const ii = oi + i, jj = oj + j; if (ii < 0 || jj < 0 || ii >= QN || jj >= QN) continue; m[ii][jj] = (i >= 0 && i <= 6 && j >= 0 && j <= 6) && (i == 0 || i == 6 || j == 0 || j == 6 || (i >= 2 && i <= 4 && j >= 2 && j <= 4)) ? 1 : 0; } };
  finder(0, 0); finder(0, QN - 7); finder(QN - 7, 0);
  for (let k = 8; k < QN - 8; k++) { m[6][k] = k % 2 == 0 ? 1 : 0; m[k][6] = k % 2 == 0 ? 1 : 0; }
  for (let i = -2; i <= 2; i++) for (let j = -2; j <= 2; j++) m[18 + i][18 + j] = Math.max(Math.abs(i), Math.abs(j)) != 1 ? 1 : 0;
  return m;
}
const QA = makeQR(7), QB = makeQR(42);
const isFinder = (i, j) => (i < 8 && j < 8) || (i < 8 && j >= QN - 8) || (i >= QN - 8 && j < 8);
// o: {build: start time or null, flip: start time or null}
function drawQR(c, x, y, size, T, o = {}) {
  const ms = size / QN;
  const fq = o.build == null ? 1 : E.oBack(pr(T, o.build, o.build + 0.3));
  // finders
  for (const [oi, oj] of [[0, 0], [0, QN - 7], [QN - 7, 0]]) {
    if (fq <= 0) continue;
    const cx = x + (oj + 3.5) * ms, cy = y + (oi + 3.5) * ms;
    c.save(); c.translate(cx, cy); c.scale(fq, fq);
    c.fillStyle = COL.navy; c.fillRect(-3.5 * ms, -3.5 * ms, 7 * ms, 7 * ms);
    c.fillStyle = '#fff'; c.fillRect(-2.5 * ms, -2.5 * ms, 5 * ms, 5 * ms);
    c.fillStyle = fq < 0.98 ? COL.cyan : COL.navy; c.fillRect(-1.5 * ms, -1.5 * ms, 3 * ms, 3 * ms);
    c.restore();
  }
  for (let i = 0; i < QN; i++) for (let j = 0; j < QN; j++) {
    if (isFinder(i, j)) continue;
    let val = QA[i][j], sx = 1, sy = 1, tint = false;
    if (o.flip != null) { const f = pr(T, o.flip + j * 0.018, o.flip + j * 0.018 + 0.22); sx = Math.abs(Math.cos(Math.PI * f)); val = f < 0.5 ? QA[i][j] : QB[i][j]; tint = f > 0 && f < 1; }
    if (!val) continue;
    if (o.build != null) { const d = o.build + 0.15 + ((i + j) / 48) * 0.75 + hash(i * 31 + j) * 0.12; const r = pr(T, d, d + 0.2); if (r <= 0) continue; sy = sx = E.oBack(r, 2.4); tint = tint || r < 0.7; }
    const cx = x + (j + 0.5) * ms, cy = y + (i + 0.5) * ms;
    c.fillStyle = tint ? COL.cyan : COL.navy;
    c.fillRect(cx - ms * sx / 2 - 0.3, cy - ms * sy / 2 - 0.3, ms * sx + 0.6, ms * sy + 0.6);
  }
}

// ---------- background ----------
const PARTS = [...Array(130)].map((_, i) => { const r = rng(i + 99); return { x: r() * W, y: r() * H, z: 0.2 + r() * 0.8, s: r() * 6.28 }; });
function drawBG(c, t) {
  c.save();
  const g = c.createRadialGradient(W / 2, H * 0.42, 50, W / 2, H * 0.5, W * 0.75);
  g.addColorStop(0, '#11223f'); g.addColorStop(0.45, '#0a1224'); g.addColorStop(1, '#03060d');
  c.fillStyle = g; c.fillRect(0, 0, W, H);
  c.globalCompositeOperation = 'lighter';
  for (let k = 0; k < 5; k++) {
    const r = rng(k * 7 + 3); const span = W + 1400; const x = ((r() * span + t * (40 + r() * 60)) % span) - 700; const wd = 30 + r() * 140;
    const gg = c.createLinearGradient(x, 0, x + wd, 0); gg.addColorStop(0, rgba(COL.cyan, 0)); gg.addColorStop(0.5, rgba(COL.cyan, 0.035 + r() * 0.04)); gg.addColorStop(1, rgba(COL.cyan, 0));
    c.fillStyle = gg; c.beginPath(); c.moveTo(x + 450, 0); c.lineTo(x + 450 + wd, 0); c.lineTo(x - 450 + wd, H); c.lineTo(x - 450, H); c.fill();
  }
  const hz = H * 0.68;
  const lg = c.createLinearGradient(0, hz, 0, H); lg.addColorStop(0, rgba(COL.cyan, 0)); lg.addColorStop(1, rgba(COL.cyan, 0.13));
  c.strokeStyle = lg; c.lineWidth = 1.5;
  for (let i = -16; i <= 16; i++) { c.beginPath(); c.moveTo(W / 2 + i * 38, hz); c.lineTo(W / 2 + i * 300, H + 40); c.stroke(); }
  for (let k = 0; k < 12; k++) { const z = ((k + t * 0.7) % 12) / 12; const y = hz + (H - hz) * Math.pow(z, 2.3); c.beginPath(); c.moveTo(0, y); c.lineTo(W, y); c.stroke(); }
  for (const p of PARTS) {
    const y = ((p.y - t * 22 * p.z) % H + H) % H, x = p.x + Math.sin(t * 0.5 + p.s) * 24;
    c.fillStyle = rgba(COL.cyan, (0.12 + 0.4 * p.z) * (0.6 + 0.4 * Math.sin(t * 2 + p.s)));
    c.beginPath(); c.arc(x, y, p.z * 2.4, 0, 7); c.fill();
  }
  c.restore();
}

// ---------- monitor / phone ----------
function drawMonitor(c, cx, cy, w, content) {
  const h = w * 9 / 16, b = 16, x = cx - w / 2, y = cy - h / 2;
  c.save();
  c.fillStyle = '#0a0f1c'; c.strokeStyle = COL.line2; c.lineWidth = 2;
  c.beginPath(); c.moveTo(cx - w * 0.045, y + h + b); c.lineTo(cx + w * 0.045, y + h + b); c.lineTo(cx + w * 0.06, y + h + b + 80); c.lineTo(cx - w * 0.06, y + h + b + 80); c.closePath(); c.fill(); c.stroke();
  c.beginPath(); c.roundRect(cx - w * 0.2, y + h + b + 76, w * 0.4, 14, 7); c.fill(); c.stroke();
  c.shadowColor = rgba(COL.cyan, 0.35); c.shadowBlur = 60;
  c.beginPath(); c.roundRect(x - b, y - b, w + 2 * b, h + 2 * b, 18); c.fill(); c.shadowBlur = 0; c.stroke();
  c.save(); c.beginPath(); c.roundRect(x, y, w, h, 6); c.clip(); c.fillStyle = '#000'; c.fillRect(x, y, w, h);
  content(c, x, y, w, h);
  const rg = c.createLinearGradient(x, y, x + w * 0.6, y + h); rg.addColorStop(0, 'rgba(255,255,255,.07)'); rg.addColorStop(0.5, 'rgba(255,255,255,0)'); c.fillStyle = rg; c.fillRect(x, y, w, h);
  c.restore();
  c.restore();
}
// lock screen = default background + QR in its white box
function lockScreen(c, x, y, w, h, T, o = {}) {
  c.drawImage(BG, x, y, w, h);
  const qs = w * 0.165, qx = x + w * 0.4997 - qs / 2, qy = y + h * 0.6041 - qs / 2;
  drawQR(c, qx, qy, qs, T, o);
  if (o.scan) {
    for (const [a, b] of o.scan) {
      if (T < a || T > b) continue;
      const q = (T - a) / (b - a), yy = qy + qs * (0.5 - 0.5 * Math.cos(q * Math.PI * 2));
      c.save(); c.globalCompositeOperation = 'lighter';
      const g = c.createLinearGradient(0, yy - 40, 0, yy); g.addColorStop(0, rgba(COL.cyan, 0)); g.addColorStop(1, rgba(COL.cyan, 0.35));
      c.fillStyle = g; c.fillRect(qx - 14, yy - 40, qs + 28, 40);
      c.shadowColor = COL.cyan; c.shadowBlur = 20; c.fillStyle = COL.ice; c.fillRect(qx - 18, yy - 2, qs + 36, 4); c.restore();
    }
  }
  setFont(c, w * 0.014, 600, 'normal', 1); c.textAlign = 'right'; c.fillStyle = 'rgba(255,255,255,.55)';
  c.fillText('PC 07  ·  v1.4.0', x + w - w * 0.015, y + h - w * 0.012);
  return { qx, qy, qs };
}

// ---------- shared step text ----------
function stepText(c, T, x, o) {
  c.save();
  c.save(); setFont(c, 460, 700, 'condensed', 0); c.textAlign = 'left';
  c.globalAlpha = 0.13 * E.oCub(pr(T, 0, 0.6)); c.strokeStyle = COL.cyan; c.lineWidth = 2;
  c.strokeText('0' + o.step, x + 150 + (1 - E.oExpo(pr(T, 0, 1))) * 120, 700); c.restore();
  scramble(c, `TRIN ${o.step} AF 3`, x + 4, 368, T, { start: 0.05, dur: 0.35, size: 22, spacing: 8 });
  c.save(); c.fillStyle = COL.cyan; c.fillRect(x + 4, 384, 60 * E.oExpo(pr(T, 0.1, 0.6)), 3); c.restore();
  const g = c.createLinearGradient(0, 430, 0, 560); g.addColorStop(0, '#ffffff'); g.addColorStop(1, '#bfeeff');
  revealText(c, o.title, x, 560, T, { size: 180, stretch: 'condensed', spacing: 4, start: 0.1, stagger: 0.05, dur: 0.6, fill: g });
  o.lines.forEach((ln, i) => fadeWords(c, ln, x + 4, 632 + i * 48, T, 0.4 + i * 0.12, { size: 36 }));
  if (o.chip) chip(c, o.chip, x + 4, 752, T - o.chipT, o.icon);
  c.restore();
}

// ---------- scenes ----------
const TITLE = 'SØRBY GAMING', TB = 770, TS = 170;
function sIntro(c, t) {
  const cx = W / 2, cy = H / 2;
  if (t < 1.0) {
    const grow = E.oExpo(pr(t, 0.08, 0.7)), col = E.iCub(pr(t, 0.72, 1.0));
    const w = 1500 * grow * (1 - col) + 4;
    c.save(); c.globalCompositeOperation = 'lighter';
    const lg = c.createLinearGradient(cx - w / 2, 0, cx + w / 2, 0); lg.addColorStop(0, rgba(COL.cyan, 0)); lg.addColorStop(0.5, rgba(COL.ice, 1)); lg.addColorStop(1, rgba(COL.cyan, 0));
    c.fillStyle = lg; c.shadowColor = COL.cyan; c.shadowBlur = 30; c.fillRect(cx - w / 2, cy - 2, w, 4); c.shadowBlur = 0;
    for (let i = -7; i <= 7; i++) { const a = grow * (1 - col) * (1 - Math.abs(i) / 8) * 0.6; c.fillStyle = rgba(COL.cyan, a); c.fillRect(cx + i * 100 * grow - 1, cy + 14, 2, i % 2 ? 8 : 16); }
    const core = 10 + 110 * col; const rg = c.createRadialGradient(cx, cy, 0, cx, cy, core); rg.addColorStop(0, rgba(COL.ice, 0.9 * pr(t, 0.1, 0.3))); rg.addColorStop(1, rgba(COL.cyan, 0));
    c.fillStyle = rg; c.fillRect(cx - core, cy - core, core * 2, core * 2);
    for (let k = 0; k < 46; k++) {
      const r = rng(k + 500), ang = r() * 6.283, st = 0.4 + r() * 0.3, q = E.iCub(pr(t, st, 1.0));
      if (q <= 0 || q >= 1) continue;
      const d = (1 - q) * (450 + r() * 700), l = 30 + 120 * q;
      c.strokeStyle = rgba(COL.ice, 0.3 + 0.6 * q); c.lineWidth = 2;
      c.beginPath(); c.moveTo(cx + Math.cos(ang) * d, cy + Math.sin(ang) * d); c.lineTo(cx + Math.cos(ang) * (d + l), cy + Math.sin(ang) * (d + l)); c.stroke();
    }
    c.restore();
    c.save(); c.globalAlpha = 1 - pr(t, 0.74, 0.9);
    scramble(c, 'SØRBY ESPORT PRÆSENTERER', cx, cy - 44, t, { start: 0.2, dur: 0.45, size: 24, spacing: 12, align: 'center', fill: COL.white });
    scramble(c, 'EVENTSYSTEM  //  2026', cx, cy + 70, t, { start: 0.35, dur: 0.35, size: 16, spacing: 8, align: 'center', fill: COL.cyan });
    c.restore();
    return;
  }
  const k = t - 1.0;
  const push = 1 + 0.035 * E.ioCub(pr(t, 1.0, 3.0));
  setFont(c, TS, 700, 'condensed', 6);
  const tw0 = tw(c, TITLE) - 6, tx = cx - tw0 / 2, ow = tw(c, 'Ø') - 6;
  const F = { x: tx + tw(c, 'S') + ow / 2, y: TB - TS * 0.355 };
  const Fp = { x: cx + (F.x - cx) * push, y: cy + (F.y - cy) * push };
  const zp = pr(t, 3.0, 3.5), z = Math.pow(70, E.iCub(zp));
  const pan = E.ioCub(pr(t, 2.85, 3.4)), C = { x: lerp(Fp.x, cx, pan), y: lerp(Fp.y, cy, pan) };
  c.save();
  c.translate(C.x, C.y); c.scale(z, z); c.translate(-Fp.x, -Fp.y);
  c.translate(cx, cy); c.scale(push, push); c.translate(-cx, -cy);
  const LY = 375;
  rays(c, cx, LY, t, 0.1 * pr(k, 0, 0.2));
  for (const [d, a] of [[0, 1], [0.09, 0.6], [0.2, 0.35]]) { const q = E.oCub(pr(k, d, d + 0.8)); ring(c, cx, LY, 40 + q * 1300, 34 * (1 - q), (1 - q) * a); }
  // shards
  for (let i = 0; i < 60; i++) {
    const r = rng(i + 900), ang = r() * 6.283, sp = 600 + r() * 1100, q = E.oExpo(pr(k, 0, 1.4)), d = 60 + sp * q;
    const a = (1 - pr(k, 0.3, 1.4)) * 0.9; if (a <= 0) continue;
    c.save(); c.translate(cx + Math.cos(ang) * d, LY + Math.sin(ang) * d); c.rotate(ang + k * 3 * (r() - .5));
    c.fillStyle = rgba(r() < 0.3 ? COL.white : COL.cyan, a); c.fillRect(-10 - 14 * r(), -1.5, 20 + 28 * r(), 3); c.restore();
  }
  const ls = lerp(2.6, 1, spring(k, 1.5, 6));
  c.save(); c.translate(cx, LY); c.rotate((1 - spring(k, 1.3, 6)) * -0.25); drawLogo(c, 0, 0, 300 * ls, pr(k, 0, 0.05)); c.restore();
  // title with shine via TMP
  tctx.setTransform(1, 0, 0, 1, 0, 0); tctx.clearRect(0, 0, W, H); tctx.setTransform(c.getTransform());
  const g = tctx.createLinearGradient(0, TB - 130, 0, TB); g.addColorStop(0, '#ffffff'); g.addColorStop(1, '#aee9ff');
  revealText(tctx, TITLE, cx, TB, k, { size: TS, stretch: 'condensed', spacing: 6, start: 0.18, stagger: 0.04, dur: 0.7, align: 'center', fill: g });
  const sh = pr(k, 1.05, 1.7);
  if (sh > 0 && sh < 1) {
    tctx.globalCompositeOperation = 'source-atop'; const sx = lerp(tx - 300, tx + tw0 + 300, E.ioCub(sh));
    const sg = tctx.createLinearGradient(sx - 120, 0, sx + 120, 0); sg.addColorStop(0, 'rgba(101,215,255,0)'); sg.addColorStop(0.5, 'rgba(101,215,255,.95)'); sg.addColorStop(1, 'rgba(101,215,255,0)');
    tctx.fillStyle = sg; tctx.fillRect(sx - 200, TB - 200, 400, 260); tctx.globalCompositeOperation = 'source-over';
  }
  tctx.setTransform(1, 0, 0, 1, 0, 0);
  c.save(); c.setTransform(1, 0, 0, 1, 0, 0); c.shadowColor = rgba(COL.cyan, 0.5); c.shadowBlur = 30; c.drawImage(TMP, 0, 0); c.restore();
  const sub = "EVENTSYSTEMET TIL JERES GAMING-PC'ER";
  const r = scramble(c, sub, cx, 850, k, { start: 0.7, dur: 0.6, size: 28, spacing: 12, align: 'center' });
  const lq = E.oExpo(pr(k, 0.75, 1.4));
  c.fillStyle = rgba(COL.cyan, 0.7); c.fillRect(r.x0 - 40 - 160 * lq, 840, 160 * lq, 2); c.fillRect(r.x0 + r.w + 40, 840, 160 * lq, 2);
  c.restore();
  if (zp > 0) {
    const rx = ow * 0.26 * push * z, ry = TS * 0.3 * push * z;
    c.save(); c.beginPath(); c.ellipse(C.x, C.y, rx, ry, 0, 0, 7); c.clip();
    drawBG(c, t); sScan(c, t - 3.5);
    c.restore();
    ring(c, C.x, C.y, rx, 6, 0.6 * (1 - zp));
  }
}

function sScan(c, T) {
  c.save();
  const s = 1 + 0.22 * (1 - E.oCub(pr(T, -0.5, 0.5))) + 0.025 * pr(T, 0, 4);
  c.translate(W / 2, H / 2); c.scale(s, s); c.translate(-W / 2, -H / 2);
  stepText(c, T, 130, { step: 1, title: 'SCAN', lines: ['PC\'en står låst og viser', 'en QR-kode på skærmen.'], chip: 'Ny kode efter hver session', chipT: 2.55, icon: '↻' });
  const MW = 1000, mx = 1275 + (1 - E.oExpo(pr(T, -0.4, 0.8))) * 160;
  drawMonitor(c, mx, 520, MW, (c, x, y, w, h) => {
    const on = E.oExpo(pr(T, 0.08, 0.4));
    if (on <= 0) return;
    c.save(); c.beginPath(); c.rect(x, y + h / 2 - h * on / 2, w, h * on); c.clip();
    lockScreen(c, x, y, w, h, T, { build: 0.4, flip: 2.55, scan: [[1.55, 2.5], [3.0, 3.95]] });
    c.fillStyle = `rgba(200,240,255,${(1 - pr(T, 0.1, 0.5)) * 0.8})`; c.fillRect(x, y, w, h);
    c.restore();
  });
  c.restore();
}

// --- phone ---
const PW = 390, PH = 800;
const TAPS = [[0.8, 195, 674], [1.35, 195, 322], [1.7, 170, 483], [2.0, 195, 443], [2.25, 170, 503], [2.6, 195, 560]];
function phoneScreen(c, T) {
  const w = PW - 28, h = PH - 28;
  // A: viewfinder
  const slide = E.oExpo(pr(T, 0.9, 1.25));
  if (slide < 1) {
    const g = c.createRadialGradient(w / 2, h * 0.4, 20, w / 2, h * 0.5, h * 0.7); g.addColorStop(0, '#24375c'); g.addColorStop(1, '#070b14');
    c.fillStyle = g; c.fillRect(0, 0, w, h);
    // camera view of the lock screen; the QR sits in the background's white box (same centre as lockScreen)
    const bw = w * 2.8, bh = bw * 0.5625, bx = w / 2 - bw * 0.4997, by = h * 0.1;
    const jx = Math.sin(T * 7) * 3, jy = Math.cos(T * 5) * 3, vy = by + bh * 0.6041;
    c.save(); c.globalAlpha = 0.5; c.drawImage(BG, bx + jx, by + jy, bw, bh); c.restore();
    const qs = 170, qx = w / 2 - qs / 2 + jx, qy = vy - qs / 2 + jy;
    c.fillStyle = '#fff'; c.fillRect(qx - 10, qy - 10, qs + 20, qs + 20); drawQR(c, qx, qy, qs, T, {});
    const lk = E.oExpo(pr(T, 0.15, 0.55)), bs = lerp(1.7, 1, lk) * (qs / 2 + 26);
    c.strokeStyle = T > 0.5 ? COL.cyan : '#ffd84d'; c.lineWidth = 6; c.lineCap = 'round';
    for (const [sx, sy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) { const px = w / 2 + sx * bs, py = vy + sy * bs; c.beginPath(); c.moveTo(px, py - sy * 34); c.lineTo(px, py); c.lineTo(px - sx * 34, py); c.stroke(); }
    const pq = E.oBack(pr(T, 0.55, 0.85));
    if (pq > 0) {
      c.save(); c.translate(w / 2, 660); c.scale(pq, pq); c.fillStyle = '#f5f7fb'; c.beginPath(); c.roundRect(-160, -30, 320, 60, 30); c.fill();
      setFont(c, 17, 600); c.textAlign = 'center'; c.fillStyle = '#10223a'; c.fillText('sorby-esport-web.onrender.com', 0, 6); c.restore();
    }
  }
  // B: form
  if (T > 0.9) {
    c.save(); c.translate(0, (1 - slide) * h);
    const g = c.createRadialGradient(w / 2, 0, 10, w / 2, 0, h); g.addColorStop(0, '#253b71'); g.addColorStop(0.55, '#0b1020'); g.addColorStop(1, '#0b1020');
    c.fillStyle = g; c.fillRect(0, 0, w, h);
    const done = pr(T, 2.8, 3.0);
    c.save(); c.globalAlpha = 1 - done;
    setFont(c, 15, 700, 'normal', 2.5); c.textAlign = 'left'; c.fillStyle = COL.cyan; c.fillText('SØRBY ESPORT', 26, 112);
    setFont(c, 32, 700); c.fillStyle = COL.white; c.fillText('Klar til at spille?', 26, 152);
    setFont(c, 17, 400); c.fillStyle = COL.muted; c.fillText('Svar på to hurtige spørgsmål.', 26, 184);
    const sel = (y, label, value, active) => {
      setFont(c, 18, 700); c.fillStyle = COL.white; c.fillText(label, 26, y);
      c.fillStyle = '#0e162a'; c.strokeStyle = active ? COL.cyan : COL.line2; c.lineWidth = active ? 2.5 : 1.5;
      c.beginPath(); c.roundRect(26, y + 14, w - 52, 54, 10); c.fill(); c.stroke();
      setFont(c, 18, value ? 600 : 400); c.fillStyle = value ? COL.white : COL.dim; c.fillText(value || 'Vælg svar', 44, y + 48);
      c.strokeStyle = COL.muted; c.lineWidth = 2; c.beginPath(); c.moveTo(w - 62, y + 36); c.lineTo(w - 54, y + 44); c.lineTo(w - 46, y + 36); c.stroke();
    };
    sel(282, 'Hvor gammel er du?', T > 1.72 ? '13–15 år' : '', T > 1.3 && T < 1.8);
    sel(402, 'Er du medlem af Sørby Esport?', T > 2.27 ? 'Ja' : '', T > 1.95 && T < 2.35);
    const bp = T > 2.6 && T < 2.75 ? 0.96 : 1;
    c.save(); c.translate(w / 2, 560); c.scale(bp, bp); c.fillStyle = T > 2.45 ? COL.cyan : rgba(COL.cyan, 0.45);
    c.beginPath(); c.roundRect(-(w - 52) / 2, -29, w - 52, 58, 12); c.fill();
    if (T > 2.62) { c.strokeStyle = '#062135'; c.lineWidth = 3.5; c.beginPath(); c.arc(0, 0, 13, T * 12, T * 12 + 4.5); c.stroke(); }
    else { setFont(c, 20, 700); c.textAlign = 'center'; c.fillStyle = '#062135'; c.fillText('Start PC', 0, 7); }
    c.restore();
    // dropdowns
    const dd = (y0, opts, openA, openB, pick) => {
      const q = E.oExpo(pr(T, openA, openA + 0.12)) * (1 - pr(T, openB, openB + 0.08)); if (q <= 0) return;
      c.save(); c.globalAlpha *= q; c.translate(0, (1 - q) * -12);
      const hh = opts.length * 50 + 12; c.fillStyle = '#141f38'; c.strokeStyle = COL.line2; c.lineWidth = 1.5; c.shadowColor = 'rgba(0,0,0,.6)'; c.shadowBlur = 24;
      c.beginPath(); c.roundRect(26, y0, w - 52, hh, 12); c.fill(); c.shadowBlur = 0; c.stroke();
      opts.forEach((o, i) => { const on = i === pick && T > openB - 0.08; if (on) { c.fillStyle = rgba(COL.cyan, 0.22); c.beginPath(); c.roundRect(32, y0 + 6 + i * 50, w - 64, 46, 8); c.fill(); }
        setFont(c, 18, on ? 700 : 400); c.textAlign = 'left'; c.fillStyle = on ? COL.cyan : COL.white; c.fillText(o, 48, y0 + 36 + i * 50); });
      c.restore();
    };
    dd(354, ['Under 10 år', '10–12 år', '13–15 år', '16–17 år', '18+ år'], 1.38, 1.76, 2);
    dd(474, ['Ja', 'Nej'], 2.03, 2.3, 0);
    c.restore();
    // C: success
    if (done > 0) {
      c.save(); c.globalAlpha = done;
      const cq = E.oBack(pr(T, 2.85, 3.15)); c.fillStyle = rgba(COL.green, 0.15); c.strokeStyle = COL.green; c.lineWidth = 5;
      c.beginPath(); c.arc(w / 2, 300, 70 * cq, 0, 7); c.fill(); c.stroke();
      const ck = pr(T, 2.95, 3.2);
      if (ck > 0) { c.lineCap = 'round'; c.lineJoin = 'round'; c.lineWidth = 9; c.beginPath(); const p1 = [w / 2 - 30, 300], p2 = [w / 2 - 8, 322], p3 = [w / 2 + 32, 278]; c.moveTo(...p1); const a = cl(ck * 2); c.lineTo(lerp(p1[0], p2[0], a), lerp(p1[1], p2[1], a)); if (ck > 0.5) { const b = (ck - 0.5) * 2; c.lineTo(lerp(p2[0], p3[0], b), lerp(p2[1], p3[1], b)); } c.stroke(); }
      revealText(c, "PC'en er startet!", w / 2, 440, T, { size: 32, start: 3.0, stagger: 0.012, dur: 0.35, align: 'center' });
      c.globalAlpha = done * pr(T, 3.1, 3.3); setFont(c, 18, 400); c.textAlign = 'center'; c.fillStyle = COL.muted; c.fillText('God fornøjelse 🎮', w / 2, 476);
      c.restore();
    }
    c.restore();
  }
  // status bar & island
  setFont(c, 15, 700); c.textAlign = 'left'; c.fillStyle = '#fff'; c.fillText('14:32', 30, 36);
  c.fillRect(w - 58, 25, 26, 12); c.fillStyle = '#000'; c.beginPath(); c.roundRect(w / 2 - 55, 14, 110, 32, 16); c.fill();
  // finger
  if (T > 0.5 && T < 2.85) {
    let fx = TAPS[0][1], fy = TAPS[0][2] + 60, press = 0;
    for (let i = 0; i < TAPS.length; i++) {
      const [tt, x, y] = TAPS[i]; const [pt, px, py] = i ? TAPS[i - 1] : [0.5, 195, 700];
      if (T <= tt) { const q = E.ioCub(pr(T, Math.max(pt + 0.06, tt - 0.25), tt)); fx = lerp(px, x, q); fy = lerp(py, y, q); break; }
      fx = x; fy = y;
    }
    for (const [tt, x, y] of TAPS) { press = Math.max(press, 1 - Math.abs(T - tt) / 0.07); ripple(c, x, y, T - tt, 60); }
    const a = pr(T, 0.5, 0.65) * (1 - pr(T, 2.7, 2.85));
    c.save(); c.globalAlpha = a; c.fillStyle = 'rgba(255,255,255,.35)'; c.strokeStyle = 'rgba(255,255,255,.9)'; c.lineWidth = 2.5;
    c.beginPath(); c.arc(fx, fy, 24 * (1 - cl(press) * 0.25), 0, 7); c.fill(); c.stroke(); c.restore();
  }
}
function sSvar(c, T) {
  c.save();
  c.translate(W / 2, H / 2); c.scale(1 + 0.025 * pr(T, 0, 4), 1 + 0.025 * pr(T, 0, 4)); c.translate(-W / 2, -H / 2);
  stepText(c, T, 1010, { step: 2, title: 'SVAR', lines: ['Gæsten scanner koden og', 'svarer på eventets spørgsmål.'], chip: 'Egne spørgsmål pr. event', chipT: 1.4, icon: '✎' });
  const en = E.oExpo(pr(T, -0.15, 0.75));
  const px = 580, py = 545 + (1 - en) * 800 + Math.sin(T * 1.6) * 7, rot = lerp(-22, -3.5, en) * Math.PI / 180 + Math.sin(T * 1.1) * 0.01;
  c.save(); c.translate(px, py); c.rotate(rot);
  c.shadowColor = rgba(COL.cyan, 0.35); c.shadowBlur = 70; c.fillStyle = '#0a0f1c';
  c.beginPath(); c.roundRect(-PW / 2, -PH / 2, PW, PH, 58); c.fill(); c.shadowBlur = 0;
  c.strokeStyle = COL.line2; c.lineWidth = 3; c.stroke();
  c.strokeStyle = rgba(COL.cyan, 0.5); c.lineWidth = 1.5; c.beginPath(); c.roundRect(-PW / 2 + 4, -PH / 2 + 4, PW - 8, PH - 8, 55); c.stroke();
  c.save(); c.translate(-PW / 2 + 14, -PH / 2 + 14); c.beginPath(); c.roundRect(0, 0, PW - 28, PH - 28, 46); c.clip();
  phoneScreen(c, T);
  const rg = c.createLinearGradient(0, 0, PW, PH * 0.6); rg.addColorStop(0, 'rgba(255,255,255,.08)'); rg.addColorStop(0.45, 'rgba(255,255,255,0)'); c.fillStyle = rg; c.fillRect(0, 0, PW, PH);
  c.restore(); c.restore();
  c.restore();
}

// --- game + timer ---
function gameScreen(c, x, y, w, h, T) {
  const hz = y + h * 0.62;
  const sky = c.createLinearGradient(0, y, 0, hz); sky.addColorStop(0, '#12022b'); sky.addColorStop(0.6, '#4a0d57'); sky.addColorStop(1, '#ff4f8b');
  c.fillStyle = sky; c.fillRect(x, y, w, h);
  const sx = x + w / 2, sy = hz - h * 0.02, sr = h * 0.27;
  c.save(); c.beginPath(); c.arc(sx, sy, sr, Math.PI, 0); c.clip();
  const sg = c.createLinearGradient(0, sy - sr, 0, sy); sg.addColorStop(0, '#ffe66d'); sg.addColorStop(1, '#ff3d81'); c.fillStyle = sg; c.fillRect(sx - sr, sy - sr, sr * 2, sr);
  c.fillStyle = '#7a1560'; for (let k = 0; k < 7; k++) { const yy = sy - sr * 0.5 + k * sr * 0.08 + (T * 20 % (sr * 0.08)); c.fillRect(sx - sr, yy, sr * 2, 2 + k * 1.6); }
  c.restore();
  c.fillStyle = '#1a0833'; c.strokeStyle = '#ff5ac8'; c.lineWidth = 2; c.beginPath(); c.moveTo(x, hz);
  for (let i = 0; i <= 24; i++) { const px = x + (i / 24) * w, d = Math.abs(i - 12) / 12; c.lineTo(px, hz - (0.25 + 0.75 * d) * h * (0.06 + hash(i * 3.3) * 0.12)); }
  c.lineTo(x + w, hz); c.closePath(); c.fill(); c.stroke();
  c.fillStyle = '#0b0220'; c.fillRect(x, hz, w, y + h - hz);
  c.strokeStyle = 'rgba(255,90,200,.8)'; c.lineWidth = 1.5;
  for (let i = -14; i <= 14; i++) { c.beginPath(); c.moveTo(sx + i * w * 0.02, hz); c.lineTo(sx + i * w * 0.16, y + h); c.stroke(); }
  for (let k = 0; k < 14; k++) { const z = ((k + T * 3) % 14) / 14, yy = hz + (y + h - hz) * Math.pow(z, 2.2); c.beginPath(); c.moveTo(x, yy); c.lineTo(x + w, yy); c.stroke(); }
  setFont(c, w * 0.018, 700, 'normal', 3); c.fillStyle = '#fff'; c.textAlign = 'left'; c.fillText('LEVEL 1', x + w * 0.03, y + h * 0.08);
  c.textAlign = 'right'; c.fillText('SCORE ' + String(Math.floor(pr(T, 1.2, 3.5) * 4250)).padStart(6, '0'), x + w * 0.97, y + h * 0.08);
}
function timerOverlay(c, x, y, w, h, T) {
  const a = E.oExpo(pr(T, 0, 0.5)); if (a <= 0) return;
  const ex = E.ioCub(pr(T, 0.6, 1.0));
  const pw = lerp(170, 330, ex), ph = 64, px = x + w - 24 - pw + (1 - a) * 260, py = y + h - 24 - ph;
  const rem = 30 * 60 - T;
  c.save(); c.globalAlpha = a;
  c.fillStyle = 'rgba(11,16,32,.88)'; c.strokeStyle = rgba(COL.cyan, 0.7); c.lineWidth = 2;
  c.beginPath(); c.roundRect(px, py, pw, ph, 32); c.fill(); c.stroke();
  c.strokeStyle = rgba(COL.cyan, 0.25); c.lineWidth = 5; c.beginPath(); c.arc(px + 33, py + 32, 15, 0, 7); c.stroke();
  c.strokeStyle = COL.cyan; c.beginPath(); c.arc(px + 33, py + 32, 15, -Math.PI / 2, -Math.PI / 2 + 6.283 * (rem / 1800)); c.stroke();
  c.save(); c.beginPath(); c.rect(px, py, pw, ph); c.clip();
  if (ex > 0) { c.globalAlpha = a * ex; setFont(c, 17, 600); c.textAlign = 'left'; c.fillStyle = COL.muted; c.fillText('Tid tilbage', px + 64, py + 39); }
  c.restore();
  setFont(c, 30, 700, 'normal', 1); c.textAlign = 'right'; c.fillStyle = COL.white; c.fillText(mmss(rem), px + pw - 22, py + 43);
  c.restore();
}
function drawLock(c, cx, cy, s, open, color) {
  const bw = s, bh = s * 0.78, sw = s * 0.56, sh = s * 0.52;
  c.save(); c.translate(cx, cy);
  c.save(); c.translate(0, -open * s * 0.2); c.translate(sw / 2, 0); c.rotate(-open * 0.45); c.translate(-sw / 2, 0);
  c.strokeStyle = color; c.lineWidth = s * 0.13; c.lineCap = 'round';
  c.beginPath(); c.moveTo(-sw / 2, s * 0.06); c.lineTo(-sw / 2, -sh + sw / 2); c.arc(0, -sh + sw / 2, sw / 2, Math.PI, 0); c.lineTo(sw / 2, s * 0.06); c.stroke(); c.restore();
  c.fillStyle = color; c.beginPath(); c.roundRect(-bw / 2, 0, bw, bh, s * 0.12); c.fill();
  c.fillStyle = COL.navy; c.beginPath(); c.arc(0, bh * 0.42, s * 0.09, 0, 7); c.fill(); c.fillRect(-s * 0.035, bh * 0.42, s * 0.07, bh * 0.28);
  c.restore();
}
function sSpil(c, T) {
  c.save();
  c.translate(W / 2, H / 2); c.scale(1 + 0.03 * pr(T, 0, 3.5), 1 + 0.03 * pr(T, 0, 3.5)); c.translate(-W / 2, -H / 2);
  stepText(c, T, 130, { step: 3, title: 'SPIL', lines: ['PC\'en låser selv op og', 'tæller ned til tiden er gået.'], chip: 'Nedtælling på skærmen', chipT: 1.55, icon: '◷' });
  const MW = 1020, mx = 1275 + (1 - E.oExpo(pr(T, -0.2, 0.7))) * -120, my = 515;
  drawMonitor(c, mx, my, MW, (c, x, y, w, h) => {
    const iris = E.iCub(pr(T, 1.02, 1.45)) * Math.hypot(w, h) * 0.6;
    if (iris < Math.hypot(w, h) * 0.6) {
      lockScreen(c, x, y, w, h, T, { flip: -10 });
      const dk = pr(T, 0.45, 0.7); c.fillStyle = `rgba(4,8,18,${dk * 0.72})`; c.fillRect(x, y, w, h);
      ripple(c, x + w / 2, y + h / 2, T - 0.48, 420, COL.cyan);
      const lq = E.oBack(pr(T, 0.5, 0.75)); const open = E.oBack(pr(T, 0.8, 0.98), 3);
      if (lq > 0) { c.save(); c.translate(x + w / 2, y + h / 2 + 10); c.scale(lq, lq); c.shadowColor = open > 0.3 ? COL.green : COL.cyan; c.shadowBlur = 40; drawLock(c, 0, -10, 130, open, open > 0.3 ? COL.green : COL.cyan); c.restore(); }
    }
    if (iris > 0) {
      c.save(); c.beginPath(); c.arc(x + w / 2, y + h / 2, iris, 0, 7); c.clip(); gameScreen(c, x, y, w, h, T); c.restore();
      if (iris < Math.hypot(w, h) * 0.6) { c.save(); c.strokeStyle = COL.ice; c.lineWidth = 8; c.shadowColor = COL.cyan; c.shadowBlur = 30; c.beginPath(); c.arc(x + w / 2, y + h / 2, iris, 0, 7); c.stroke(); c.restore(); }
    }
    // toast
    const tq = E.oExpo(pr(T, 1.45, 1.8)) * (1 - E.iCub(pr(T, 2.9, 3.2)));
    if (tq > 0) {
      c.save(); c.globalAlpha = tq; c.translate(x + w / 2, y + 44 + (1 - tq) * -40);
      c.fillStyle = 'rgba(11,16,32,.9)'; c.strokeStyle = rgba(COL.green, 0.8); c.lineWidth = 2; c.beginPath(); c.roundRect(-160, -26, 320, 52, 26); c.fill(); c.stroke();
      c.fillStyle = COL.green; c.beginPath(); c.arc(-130, 0, 7, 0, 7); c.fill();
      setFont(c, 21, 600); c.textAlign = 'center'; c.fillStyle = COL.white; c.fillText('Session startet  ·  30 min', 12, 8); c.restore();
    }
    timerOverlay(c, x, y, w, h, T - 1.55);
  });
  // incoming signal
  if (T < 0.62) {
    const q = E.ioCub(pr(T, 0.0, 0.5)); const P = s => { const u = 1 - s; return [u * u * (-80) + 2 * u * s * 520 + s * s * mx, u * u * 1000 + 2 * u * s * 980 + s * s * my]; };
    c.save(); c.globalCompositeOperation = 'lighter'; c.lineCap = 'round';
    for (let i = 0; i < 18; i++) { const s0 = cl(q - i * 0.02), s1 = cl(q - (i + 1) * 0.02); const [x0, y0] = P(s0), [x1, y1] = P(s1); c.strokeStyle = rgba(COL.ice, (1 - i / 18) * 0.9 * (1 - pr(T, 0.5, 0.62))); c.lineWidth = 10 * (1 - i / 18); c.beginPath(); c.moveTo(x0, y0); c.lineTo(x1, y1); c.stroke(); }
    const [hx, hy] = P(q); const rg = c.createRadialGradient(hx, hy, 0, hx, hy, 50); rg.addColorStop(0, rgba(COL.ice, 0.95 * (1 - pr(T, 0.5, 0.62)))); rg.addColorStop(1, rgba(COL.cyan, 0)); c.fillStyle = rg; c.fillRect(hx - 50, hy - 50, 100, 100);
    c.restore();
  }
  c.restore();
}

// --- dashboard ---
const PCS = [
  ['01', 'p', 1122], ['02', 'p', 435], ['03', 'f'], ['04', 'p', 1443],
  ['05', 'p', 710], ['06', 'p', 252], ['07', 'p', 1742], ['08', 'f'],
  ['09', 'p', 151], ['10', 'f'], ['11', 'p', 1269], ['12', 'o']];
function sDash(c, T) {
  c.save();
  const pan = lerp(18, -18, E.ioCub(pr(T, 0, 3.5)));
  c.translate(W / 2, H / 2); c.scale(1.0 + 0.03 * pr(T, 0, 3.5), 1.0 + 0.03 * pr(T, 0, 3.5)); c.translate(-W / 2 + pan, -H / 2);
  scramble(c, "ADMIN-PANEL  ·  PC'ER", 154, 150, T, { start: 0.05, dur: 0.35, size: 22, spacing: 8 });
  revealText(c, 'FULDT OVERBLIK', 150, 262, T, { size: 112, stretch: 'condensed', spacing: 3, start: 0.1, stagger: 0.03, dur: 0.6 });
  const started = T > 2.4;
  const pills = [['Online', 11, COL.cyan], ['I brug', started ? 9 : 8, COL.cyan], ['Ledige', started ? 2 : 3, COL.green], ['Offline', 1, COL.dim]];
  pills.forEach(([l, v, col], i) => {
    const q = E.oBack(pr(T, 0.25 + i * 0.06, 0.65 + i * 0.06)); if (q <= 0) return;
    const x = 1110 + i * 170, y = 170;
    c.save(); c.translate(x + 75, y + 45); c.scale(q, q); c.fillStyle = COL.panel; c.strokeStyle = COL.line; c.lineWidth = 1.5; c.beginPath(); c.roundRect(-75, -45, 150, 96, 14); c.fill(); c.stroke();
    setFont(c, 17, 600); c.textAlign = 'left'; c.fillStyle = COL.muted; c.fillText(l, -55, -12);
    const cnt = Math.round(v * E.oExpo(pr(T, 0.3 + i * 0.06, 1.3))); const bump = (i === 1 || i === 2) ? 1 + 0.25 * (1 - pr(T, 2.42, 2.7)) * (T > 2.42 ? 1 : 0) : 1;
    c.save(); c.translate(-55, 34); c.scale(bump, bump); setFont(c, 44, 700); c.fillStyle = col; c.fillText(String(cnt), 0, 0); c.restore();
    c.restore();
  });
  const CW = 382, CH = 196, GX = 26, GY = 24, X0 = 150, Y0 = 330;
  PCS.forEach(([id, st0, sec0], n) => {
    const col = n % 4, row = Math.floor(n / 4), x = X0 + col * (CW + GX), y = Y0 + row * (CH + GY);
    const q = E.oBack(pr(T, 0.2 + (row + col) * 0.055, 0.75 + (row + col) * 0.055), 1.4); if (q <= 0) return;
    let st = st0, sec = sec0 - T;
    if (id === '03' && started) { st = 'p'; sec = 1800 - (T - 2.4); }
    if (id === '06' && T > 1.78) sec += 600;
    const accent = st === 'p' ? (sec < 300 ? COL.amber : COL.cyan) : st === 'f' ? COL.green : COL.dim;
    c.save(); c.translate(x + CW / 2, y + CH / 2); c.scale(1, q); c.globalAlpha = cl(q * 1.5);
    const flash = id === '06' ? (T > 1.78 ? 1 - pr(T, 1.78, 2.4) : 0) : id === '03' ? (T > 2.4 ? 1 - pr(T, 2.4, 3.0) : 0) : 0;
    c.fillStyle = COL.panel; c.strokeStyle = flash > 0 ? rgba(COL.cyan, 0.4 + flash * 0.6) : COL.line; c.lineWidth = 1.5 + flash * 2;
    if (flash > 0) { c.shadowColor = COL.cyan; c.shadowBlur = 40 * flash; }
    c.beginPath(); c.roundRect(-CW / 2, -CH / 2, CW, CH, 16); c.fill(); c.shadowBlur = 0; c.stroke();
    c.fillStyle = accent; c.beginPath(); c.roundRect(-CW / 2, -CH / 2 + 18, 4, CH - 36, 2); c.fill();
    c.translate(-CW / 2, -CH / 2);
    setFont(c, 30, 700); c.textAlign = 'left'; c.fillStyle = COL.white; c.fillText('PC ' + id, 26, 50);
    const label = st === 'p' ? 'I brug' : st === 'f' ? 'Ledig' : 'Offline';
    setFont(c, 17, 700); const lw = tw(c, label) + 44;
    c.fillStyle = rgba(accent, 0.15); c.beginPath(); c.roundRect(CW - 22 - lw, 22, lw, 34, 17); c.fill();
    c.fillStyle = accent; c.beginPath(); c.arc(CW - 22 - lw + 18, 39, 5, 0, 7); c.fill(); c.fillText(label, CW - 22 - lw + 30, 45);
    if (st === 'p') {
      const s = Math.max(0, sec);
      if (id === '06' && T > 1.7 && T < 2.1) {
        const rq = E.oExpo(pr(T, 1.78, 2.05));
        c.save(); c.beginPath(); c.rect(20, 76, 240, 64); c.clip(); setFont(c, 58, 700); c.fillStyle = COL.white;
        const m = mmss(s).slice(2), oldT = mmss(Math.max(0, sec0 - T)).slice(0, 2), newT = mmss(Math.max(0, sec0 - T + 600)).slice(0, 2);
        const tw2 = tw(c, rq > 0.5 ? newT : oldT);
        c.fillText(oldT, 26, 130 - rq * 64); c.fillText(newT, 26, 130 + (1 - rq) * 64); c.fillText(m, 26 + tw2, 130); c.restore();
      } else { setFont(c, 58, 700); c.fillStyle = COL.white; c.fillText(mmss(s), 26, 130); }
      setFont(c, 16, 400); c.fillStyle = COL.dim; c.fillText('tilbage', 26 + 150, 128);
      c.fillStyle = COL.line; c.beginPath(); c.roundRect(26, 158, CW - 52, 8, 4); c.fill();
      c.fillStyle = accent; c.beginPath(); c.roundRect(26, 158, (CW - 52) * cl(s / 1800), 8, 4); c.fill();
      const hov = id === '06' ? pr(T, 1.5, 1.65) : 0;
      c.fillStyle = hov > 0 ? rgba(COL.cyan, 0.2 + hov * 0.25) : 'rgba(255,255,255,.04)'; c.strokeStyle = hov > 0 ? COL.cyan : COL.line2; c.lineWidth = 1.5;
      c.beginPath(); c.roundRect(CW - 128, 92, 104, 40, 10); c.fill(); c.stroke();
      setFont(c, 17, 700); c.textAlign = 'center'; c.fillStyle = hov > 0 ? COL.white : COL.muted; c.fillText('+10 min', CW - 76, 118);
    } else if (st === 'f') {
      setFont(c, 26, 600); c.fillStyle = COL.muted; c.fillText('Viser QR-koden', 26, 118);
      c.fillStyle = COL.green; for (let i = 0; i < 3; i++) c.fillRect(26 + i * 12, 146, 8, 8);
    } else { setFont(c, 24, 400); c.fillStyle = COL.dim; c.fillText('Sidst set 13:47', 26, 118); }
    c.restore();
  });
  // cursor: to PC06 +10 button
  const bx = X0 + 1 * (CW + GX) + CW - 76, by = Y0 + 1 * (CH + GY) + 112;
  const cq = E.ioCub(pr(T, 1.0, 1.65)); const cx = lerp(1500, bx, cq), cy = lerp(1080, by, cq);
  if (T > 0.95) { ripple(c, bx, by, T - 1.75, 70, COL.cyan); drawCursor(c, cx - 4, cy - 4, cl(1 - Math.abs(T - 1.75) / 0.08)); }
  c.restore();
  // toast (screen space)
  const tq = E.oExpo(pr(T, 1.85, 2.25)) * (1 - E.iCub(pr(T, 3.15, 3.45)));
  if (tq > 0) {
    c.save(); c.globalAlpha = tq; c.translate(W / 2, 1010 + (1 - tq) * 90);
    c.fillStyle = 'rgba(14,22,42,.96)'; c.strokeStyle = rgba(COL.cyan, 0.7); c.lineWidth = 2; c.shadowColor = 'rgba(0,0,0,.5)'; c.shadowBlur = 30;
    c.beginPath(); c.roundRect(-250, -32, 500, 64, 32); c.fill(); c.shadowBlur = 0; c.stroke();
    c.fillStyle = COL.cyan; setFont(c, 24, 700); c.textAlign = 'center'; c.fillText('✓', -210, 9);
    setFont(c, 22, 600); c.fillStyle = COL.white; c.fillText('PC 06 fik 10 minutter ekstra', 16, 8); c.restore();
  }
}

// --- stats ---
const AGES = [['Under 10 år', 34], ['10–12 år', 71], ['13–15 år', 82], ['16–17 år', 38], ['18+ år', 23]];
function sStats(c, T) {
  c.save();
  const col = E.iCub(pr(T, 2.62, 3.0));
  c.translate(W / 2, H / 2); const s = (1 + 0.03 * pr(T, 0, 2.6)) * (1 - col * 0.85); c.scale(s, s); c.rotate(col * 0.12); c.translate(-W / 2, -H / 2);
  c.globalAlpha = 1 - pr(T, 2.75, 3.0);
  scramble(c, 'EFTER EVENTET', 154, 176, T, { start: 0.05, dur: 0.35, size: 22, spacing: 8 });
  revealText(c, 'ALT I TAL', 150, 290, T, { size: 112, stretch: 'condensed', spacing: 3, start: 0.1, stagger: 0.04, dur: 0.6 });
  const kp = [[248, '', 'Sessioner'], [12, '', "PC'er i brug"], [186, ' t', 'Samlet spilletid']];
  kp.forEach(([v, suf, l], i) => {
    const y = 470 + i * 175, q = E.oExpo(pr(T, 0.2 + i * 0.12, 1.4 + i * 0.12)), a = pr(T, 0.2 + i * 0.12, 0.35 + i * 0.12);
    c.save(); c.globalAlpha *= a;
    c.fillStyle = COL.cyan; c.fillRect(150, y - 88, 5, 120 * E.oExpo(pr(T, 0.2 + i * 0.12, 0.8 + i * 0.12)));
    setFont(c, 108, 700, 'condensed'); c.textAlign = 'left'; c.fillStyle = COL.white; c.fillText(Math.round(v * q) + suf, 182, y);
    setFont(c, 26, 400); c.fillStyle = COL.muted; c.fillText(l, 186, y + 36); c.restore();
  });
  // panel
  const pq = E.oExpo(pr(T, 0.15, 0.7));
  c.save(); c.globalAlpha *= pq; c.translate((1 - pq) * 120, 0);
  c.fillStyle = 'rgba(14,22,42,.92)'; c.strokeStyle = COL.line; c.lineWidth = 1.5; c.beginPath(); c.roundRect(760, 150, 1010, 800, 22); c.fill(); c.stroke();
  setFont(c, 30, 700); c.textAlign = 'left'; c.fillStyle = COL.white; c.fillText('Hvor gammel er du?', 810, 222);
  AGES.forEach(([l, v], i) => {
    const y = 290 + i * 66, q = E.oExpo(pr(T, 0.35 + i * 0.07, 1.25 + i * 0.07));
    setFont(c, 22, 400); c.fillStyle = COL.muted; c.fillText(l, 810, y + 8);
    c.fillStyle = COL.line; c.beginPath(); c.roundRect(980, y - 14, 640, 30, 8); c.fill();
    const bg = c.createLinearGradient(980, 0, 1620, 0); bg.addColorStop(0, '#2aa7d8'); bg.addColorStop(1, COL.cyan);
    c.fillStyle = bg; c.beginPath(); c.roundRect(980, y - 14, Math.max(1, 640 * (v / 90) * q), 30, 8); c.fill();
    setFont(c, 22, 700); c.fillStyle = COL.white; c.fillText(String(Math.round(v * q)), 1640, y + 9);
  });
  c.fillStyle = COL.line; c.fillRect(810, 632, 910, 1.5);
  setFont(c, 26, 700); c.fillStyle = COL.white; c.fillText('Er du medlem af Sørby Esport?', 810, 690);
  const dq = E.oExpo(pr(T, 0.6, 1.5)), dx = 920, dy = 810, R = 82;
  c.lineWidth = 28; c.strokeStyle = COL.line2; c.beginPath(); c.arc(dx, dy, R, 0, 7); c.stroke();
  c.strokeStyle = COL.cyan; c.lineCap = 'round'; c.beginPath(); c.arc(dx, dy, R, -Math.PI / 2, -Math.PI / 2 + 6.283 * 0.62 * dq); c.stroke(); c.lineCap = 'butt';
  setFont(c, 34, 700); c.textAlign = 'center'; c.fillStyle = COL.white; c.fillText(Math.round(62 * dq) + ' %', dx, dy + 12);
  c.textAlign = 'left'; setFont(c, 22, 600);
  c.fillStyle = COL.cyan; c.fillRect(1050, 772, 16, 16); c.fillStyle = COL.white; c.fillText('Ja  ·  62 %', 1078, 787);
  c.fillStyle = COL.line2; c.fillRect(1050, 818, 16, 16); c.fillStyle = COL.white; c.fillText('Nej  ·  38 %', 1078, 833);
  // download button
  const bq = E.oBack(pr(T, 0.9, 1.3)), press = cl(1 - Math.abs(T - 1.95) / 0.08), ok = T > 2.0;
  if (bq > 0) {
    c.save(); c.translate(1560, 805); c.scale(bq * (1 - press * 0.06), bq * (1 - press * 0.06));
    c.fillStyle = ok ? COL.green : COL.cyan; c.beginPath(); c.roundRect(-160, -32, 320, 64, 14); c.fill();
    setFont(c, 21, 700); c.textAlign = 'center'; c.fillStyle = '#062135'; c.fillText(ok ? '✓  Hentet' : 'Download (Excel/CSV)', 0, 8); c.restore();
    ripple(c, 1560, 805, T - 1.95, 110, COL.cyan);
    const fq = pr(T, 2.0, 2.6);
    if (fq > 0 && fq < 1) { c.save(); c.globalAlpha = 1 - fq; c.translate(1560 + fq * 40, 760 - E.oCub(fq) * 150); c.fillStyle = '#fff'; c.beginPath(); c.roundRect(-26, -32, 52, 64, 6); c.fill(); setFont(c, 14, 700); c.textAlign = 'center'; c.fillStyle = '#10223a'; c.fillText('.CSV', 0, 6); c.restore(); }
  }
  c.restore();
  c.restore();
  if (col > 0) {
    const r = 10 + 160 * col; const g = c.createRadialGradient(W / 2, H / 2, 0, W / 2, H / 2, r);
    g.addColorStop(0, rgba(COL.ice, col)); g.addColorStop(1, rgba(COL.cyan, 0));
    c.save(); c.globalCompositeOperation = 'lighter'; c.fillStyle = g; c.fillRect(W / 2 - r, H / 2 - r, r * 2, r * 2); c.restore();
  }
}

function sOutro(c, T) {
  const cx = W / 2, LY = 360;
  c.save();
  const s = 1 + 0.04 * E.oCub(pr(T, 0, 3.5)); c.translate(cx, H / 2); c.scale(s, s); c.translate(-cx, -H / 2);
  rays(c, cx, LY, T + 21.5, 0.11);
  for (const [d, a] of [[0, 1], [0.1, 0.5]]) { const q = E.oCub(pr(T, d, d + 0.9)); ring(c, cx, LY, 40 + q * 1400, 30 * (1 - q), (1 - q) * a); }
  for (let i = 0; i < 70; i++) {
    const r = rng(i + 1700), ang = r() * 6.283, d = 80 + (500 + r() * 1200) * E.oExpo(pr(T, 0, 1.6)), a = (1 - pr(T, 0.2, 1.6)) * 0.9; if (a <= 0) continue;
    c.save(); c.translate(cx + Math.cos(ang) * d, LY + Math.sin(ang) * d); c.rotate(ang); c.fillStyle = rgba(r() < 0.3 ? COL.white : COL.cyan, a); c.fillRect(-18, -1.5, 36, 3); c.restore();
  }
  drawLogo(c, cx, LY, 290 * lerp(0.3, 1, spring(T, 1.4, 6)), pr(T, 0, 0.05));
  const words = [['SCAN.', COL.white], ['SVAR.', COL.white], ['SPIL.', COL.cyan]];
  setFont(c, 150, 700, 'condensed', 4);
  const ws = words.map(([w]) => tw(c, w) - 4), gap = 60, total = ws.reduce((a, b) => a + b) + gap * 2;
  let x = cx - total / 2;
  words.forEach(([w, col], i) => {
    const st = i * 0.5, q = E.oExpo(pr(T, st, st + 0.35)), a = pr(T, st, st + 0.04);
    if (a > 0) {
      const sc = lerp(1.8, 1, q), wx = x + ws[i] / 2;
      c.save(); c.globalAlpha = a; c.translate(wx, 745); c.scale(sc, sc);
      setFont(c, 150, 700, 'condensed', 4); c.textAlign = 'center'; c.fillStyle = col;
      if (i === 2) { c.shadowColor = COL.cyan; c.shadowBlur = 40; }
      c.fillText(w, 0, 0); c.restore();
      const f = 1 - pr(T, st, st + 0.3);
      if (f > 0) { c.save(); c.globalCompositeOperation = 'lighter'; c.fillStyle = rgba(COL.ice, f * 0.8); c.fillRect(x - 10, 760, ws[i] + 20, 6); c.restore(); }
    }
    x += ws[i] + gap;
  });
  const r = scramble(c, 'SØRBY GAMING  ·  KLAR TIL NÆSTE EVENT', cx, 850, T, { start: 1.35, dur: 0.6, size: 28, spacing: 12, align: 'center' });
  const lq = E.oExpo(pr(T, 1.4, 2.0));
  c.fillStyle = rgba(COL.cyan, 0.7); c.fillRect(r.x0 - 40 - 140 * lq, 840, 140 * lq, 2); c.fillRect(r.x0 + r.w + 40, 840, 140 * lq, 2);
  c.restore();
}

// ---------- HUD ----------
function drawHUD(c, t) {
  const a = pr(t, 1.3, 1.8) * (1 - pr(t, 21.2, 21.45)) + pr(t, 22.2, 22.8) * (1 - pr(t, 24.4, 24.8));
  if (a <= 0) return;
  c.save(); c.globalAlpha = a * 0.75;
  const m = 38, L = 44; c.strokeStyle = COL.cyan; c.lineWidth = 3;
  for (const [x, y, sx, sy] of [[m, m, 1, 1], [W - m, m, -1, 1], [W - m, H - m, -1, -1], [m, H - m, 1, -1]]) { c.beginPath(); c.moveTo(x, y + sy * L); c.lineTo(x, y); c.lineTo(x + sx * L, y); c.stroke(); }
  setFont(c, 17, 700, 'normal', 6); c.textAlign = 'left'; c.fillStyle = COL.white; c.fillText('SØRBY GAMING', 96, 80);
  c.fillStyle = COL.cyan; if (Math.floor(t * 2) % 2 === 0) { c.beginPath(); c.arc(84, 74, 4, 0, 7); c.fill(); }
  c.textAlign = 'right'; c.fillStyle = COL.muted; c.fillText('EVENTSYSTEM  v1.4', W - 96, 80);
  setFont(c, 16, 600, 'normal', 4); c.textAlign = 'left'; c.fillStyle = COL.dim;
  const f = Math.floor(t * FPS); c.fillText(`TC 00:00:${pad2(f / FPS)}:${pad2(f % FPS)}`, 96, H - 70);
  c.textAlign = 'right'; c.fillText('SCAN  ·  SVAR  ·  SPIL', W - 96, H - 70);
  // step tracker
  const sa = pr(t, 3.7, 4.1) * (1 - pr(t, 14.7, 15.0));
  if (sa > 0) {
    c.globalAlpha = a * sa; const xs = [W / 2 - 220, W / 2, W / 2 + 220], y = 76, act = t < 7.5 ? 0 : t < 11.5 ? 1 : 2;
    c.fillStyle = COL.line; c.fillRect(xs[0], y + 16, 440, 2);
    const p = t < 7.5 ? pr(t, 3.5, 7.5) * 0.5 * 0 : t < 11.5 ? 0.5 * pr(t, 7.3, 7.7) : 0.5 + 0.5 * pr(t, 11.3, 11.7);
    c.fillStyle = COL.cyan; c.fillRect(xs[0], y + 16, 440 * p, 2);
    ['01 SCAN', '02 SVAR', '03 SPIL'].forEach((l, i) => {
      c.fillStyle = i <= act ? COL.cyan : COL.line2; c.beginPath(); c.arc(xs[i], y + 17, i === act ? 7 : 5, 0, 7); c.fill();
      setFont(c, 15, 700, 'normal', 4); c.textAlign = 'center'; c.fillStyle = i === act ? COL.white : COL.dim; c.fillText(l, xs[i], y - 2);
    });
  }
  c.restore();
}

// ---------- compositing ----------
const SCENES = [{ a: 0, f: sIntro }, { a: 3.5, f: sScan }, { a: 7.5, f: sSvar }, { a: 11.5, f: sSpil }, { a: 15, f: sDash }, { a: 18.5, f: sStats }, { a: 21.5, f: sOutro }];
const CUTS = [7.5, 11.5, 15, 18.5];
function slashWipe(c, t, b, A, B, dir) {
  const p = pr(t, b - 0.22, b + 0.22), e = E.ioCub(p), sl = 280;
  const X = dir > 0 ? lerp(-sl - 150, W + sl + 150, e) : lerp(W + sl + 150, -sl - 150, e);
  A();
  c.save(); c.beginPath();
  if (dir > 0) { c.moveTo(-60, -60); c.lineTo(X + sl, -60); c.lineTo(X - sl, H + 60); c.lineTo(-60, H + 60); }
  else { c.moveTo(W + 60, -60); c.lineTo(X + sl, -60); c.lineTo(X - sl, H + 60); c.lineTo(W + 60, H + 60); }
  c.closePath(); c.clip(); drawBG(c, t); B(); c.restore();
  c.save(); c.globalCompositeOperation = 'lighter';
  const band = (off, wd, a) => { const g = c.createLinearGradient(X + off - wd * dir, 0, X + off, 0); g.addColorStop(0, rgba(COL.cyan, 0)); g.addColorStop(1, rgba(COL.cyan, a));
    c.fillStyle = g; c.beginPath(); c.moveTo(X + off + sl, -60); c.lineTo(X + off + sl - wd * dir, -60); c.lineTo(X + off - sl - wd * dir, H + 60); c.lineTo(X + off - sl, H + 60); c.fill(); };
  band(0, 160, 0.55); band(-dir * 190, 16, 0.4); band(-dir * 260, 6, 0.5);
  c.strokeStyle = '#eafaff'; c.lineWidth = 4; c.shadowColor = COL.cyan; c.shadowBlur = 30; c.beginPath(); c.moveTo(X + sl, -60); c.lineTo(X - sl, H + 60); c.stroke();
  c.restore();
}
function drawScenes(c, t) {
  if (t < 3.5) return sIntro(c, t);
  if (t >= 21.5) return sOutro(c, t - 21.5);
  for (let i = 0; i < CUTS.length; i++) {
    const b = CUTS[i];
    if (t >= b - 0.22 && t < b + 0.22) { const A = SCENES[i + 1], B = SCENES[i + 2]; return slashWipe(c, t, b, () => A.f(c, t - A.a), () => B.f(c, t - B.a), i % 2 ? -1 : 1); }
  }
  let s = SCENES[0]; for (const sc of SCENES) if (t >= sc.a) s = sc; s.f(c, t - s.a);
}
const HITS = [{ t: 1.0, sh: 24, ab: 16, fl: 0.85 }, { t: 3.5, ab: 10, sh: 6 }, { t: 7.5, ab: 9, pre: 1 }, { t: 11.5, ab: 9, pre: 1 }, { t: 12.3, sh: 6, ab: 5 }, { t: 12.55, fl: 0.2, ab: 6 }, { t: 15, ab: 9, pre: 1 }, { t: 18.5, ab: 9, pre: 1 }, { t: 21.5, sh: 22, ab: 16, fl: 0.9 }, { t: 22.0, sh: 8, ab: 6 }, { t: 22.5, sh: 10, ab: 8 }];
function fx(t) {
  let sx = 0, sy = 0, ab = 0, fl = 0;
  for (const h of HITS) {
    const d = t - h.t;
    if (h.pre && d < 0 && d > -0.3) ab += h.ab * Math.exp(d * 14);
    if (d < 0) continue;
    const k = Math.exp(-d * 9);
    if (h.sh) { sx += h.sh * k * Math.sin(d * 71); sy += h.sh * k * Math.cos(d * 53); }
    if (h.ab) ab += h.ab * Math.exp(-d * 10);
    if (h.fl) fl += h.fl * Math.exp(-d * 8);
  }
  return { sx, sy, ab, fl };
}
function chroma(src, amt) {
  const a = CA1.getContext('2d'), b = CA2.getContext('2d');
  a.globalCompositeOperation = 'copy'; a.drawImage(src, 0, 0); a.globalCompositeOperation = 'multiply'; a.fillStyle = '#f00'; a.fillRect(0, 0, W, H);
  b.globalCompositeOperation = 'copy'; b.drawImage(src, 0, 0); b.globalCompositeOperation = 'multiply'; b.fillStyle = '#0ff'; b.fillRect(0, 0, W, H);
  out.fillStyle = '#000'; out.globalCompositeOperation = 'source-over'; out.fillRect(0, 0, W, H);
  out.globalCompositeOperation = 'lighter'; out.drawImage(CA1, -amt, 0); out.drawImage(CA2, amt, 0); out.globalCompositeOperation = 'source-over';
}
function renderFrame(t) {
  const c = sctx; c.setTransform(1, 0, 0, 1, 0, 0); c.globalAlpha = 1; c.globalCompositeOperation = 'source-over';
  const f = fx(t);
  drawBG(c, t);
  c.save(); c.translate(f.sx, f.sy); drawScenes(c, t); c.restore();
  drawHUD(c, t);
  if (f.ab > 0.6) chroma(SC, f.ab); else { out.globalCompositeOperation = 'copy'; out.drawImage(SC, 0, 0); out.globalCompositeOperation = 'source-over'; }
  out.drawImage(VIG, 0, 0);
  if (f.fl > 0.01) { out.fillStyle = `rgba(225,247,255,${Math.min(1, f.fl)})`; out.fillRect(0, 0, W, H); }
  const blk = Math.max(1 - pr(t, 0, 0.08), pr(t, 24.55, 25));
  if (blk > 0) { out.fillStyle = `rgba(0,0,0,${blk})`; out.fillRect(0, 0, W, H); }
}

// ---------- drivers ----------
const Q = new URLSearchParams(location.search);
Promise.all([BG.decode(), LOGO.decode(), document.fonts.ready]).then(async () => {
  if (Q.has('render')) {
    const n = DUR * FPS, from = +(Q.get('from') || 0), to = +(Q.get('to') || n);
    for (let i = from; i < to; i++) {
      renderFrame(i / FPS);
      const blob = await new Promise(r => cv.toBlob(r, 'image/jpeg', 0.96));
      await fetch('/frame?i=' + i, { method: 'POST', body: blob });
    }
    await fetch('/done', { method: 'POST' });
  } else if (Q.has('sheet')) {
    const [a, b, n] = Q.get('sheet').split(',').map(Number), cols = Math.ceil(Math.sqrt(n)), tw_ = W / cols, th = H / cols;
    const acc = mk(), ac = acc.getContext('2d'); ac.fillStyle = '#222'; ac.fillRect(0, 0, W, H);
    for (let i = 0; i < n; i++) { const t = a + (b - a) * i / Math.max(1, n - 1); renderFrame(t); ac.drawImage(cv, (i % cols) * tw_, Math.floor(i / cols) * th, tw_ - 3, th - 3); ac.fillStyle = '#ff0'; ac.font = '20px sans-serif'; ac.fillText(t.toFixed(2), (i % cols) * tw_ + 6, Math.floor(i / cols) * th + 22); }
    out.drawImage(acc, 0, 0);
  } else if (Q.has('play')) {
    const t0 = performance.now(); const loop = () => { renderFrame(((performance.now() - t0) / 1000) % DUR); requestAnimationFrame(loop); }; loop();
  } else renderFrame(+(Q.get('t') || 2));
  document.title = 'ready';
});
