// Synthesized soundtrack for the Sørby Gaming reel: 25 s, 48 kHz stereo, 120 BPM.
const fs = require('fs');
const SR = 48000, DUR = 25, N = SR * DUR;
const L = new Float32Array(N), R = new Float32Array(N);
const revL = new Float32Array(N), revR = new Float32Array(N);
let seed = 1; const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296 * 2 - 1; };
const mtof = m => 440 * Math.pow(2, (m - 69) / 12);
function add(t0, len, fn, gain = 1, pan = 0, rev = 0) {
  const s0 = Math.floor(t0 * SR), n = Math.floor(len * SR);
  const gl = gain * Math.cos((pan + 1) * Math.PI / 4), gr = gain * Math.sin((pan + 1) * Math.PI / 4);
  const st = {};
  for (let i = 0; i < n; i++) {
    const k = s0 + i; if (k < 0 || k >= N) continue;
    const v = fn(i / SR, st, i);
    L[k] += v * gl; R[k] += v * gr;
    if (rev) { revL[k] += v * gl * rev; revR[k] += v * gr * rev; }
  }
}
// one-pole filters in closure state
const lp = (st, key, x, fc) => { const a = 1 - Math.exp(-2 * Math.PI * fc / SR); st[key] = (st[key] || 0) + a * (x - (st[key] || 0)); return st[key]; };
const hp = (st, key, x, fc) => x - lp(st, key, x, fc);
// state-variable filter
function svf(st, key, x, fc, q) { const f = 2 * Math.sin(Math.PI * Math.min(fc, SR / 6) / SR); const s = st[key] || (st[key] = { l: 0, b: 0 }); const h = x - s.l - q * s.b; s.b += f * h; s.l += f * s.b; return s; }

// ---------- instruments ----------
const kick = (t, g = 1) => add(t, 0.5, (x, st) => { const f = 45 + 110 * Math.exp(-x * 28); st.ph = (st.ph || 0) + 2 * Math.PI * f / SR; return (Math.sin(st.ph) * Math.exp(-x * 7) + (x < 0.004 ? rnd() * 0.5 : 0)) ; }, 0.9 * g);
const clap = t => add(t, 0.35, (x, st) => { const env = (x < 0.03 ? (Math.floor(x / 0.01) % 1 === 0 ? Math.exp(-(x % 0.01) * 300) : 0) : 0) + Math.exp(-x * 18); return svf(st, 'f', rnd(), 1500, 0.6).b * env; }, 0.5, 0, 0.35);
const hat = (t, open = false, g = 1) => add(t, open ? 0.25 : 0.06, (x, st) => hp(st, 'h', rnd(), 7000) * Math.exp(-x * (open ? 14 : 70)), 0.22 * g, 0.25);
function saw(ph) { return 2 * (ph - Math.floor(ph + 0.5)); }
const bass = (t, len, m) => add(t, len, (x, st) => { const f = mtof(m); const v = saw(x * f) + 0.5 * Math.sin(2 * Math.PI * f * 0.5 * x); const env = Math.min(1, x / 0.005) * Math.min(1, (len - x) / 0.02) * (0.6 + 0.4 * Math.exp(-x * 8)); return lp(st, 'a', lp(st, 'b', v, 180 + 700 * Math.exp(-x * 12)), 900) * env; }, 0.42);
const pad = (t, len, notes, g = 1, cut = 1800) => notes.forEach((m, j) => add(t, len, (x, st) => { const f = mtof(m); let v = 0; for (const d of [-0.12, 0, 0.11]) v += saw(x * f * Math.pow(2, d / 12) + d * 7); const env = Math.min(1, x / 0.25) * Math.min(1, (len - x) / 0.4); return lp(st, 'a', lp(st, 'b', v, cut), cut) * env / 3; }, 0.075 * g, j % 2 ? 0.35 : -0.35, 0.5));
const pluck = (t, m, g = 1, pan = 0) => add(t, 0.4, (x, st) => { const f = mtof(m); const v = (saw(x * f) + saw(x * f * 1.005)) * 0.5; return lp(st, 'a', v, 800 + 5000 * Math.exp(-x * 20)) * Math.exp(-x * 9); }, 0.13 * g, pan, 0.45);
const riser = (t, len, g = 1) => add(t, len, (x, st) => { const p = x / len; const s = svf(st, 'f', rnd(), 300 + 9000 * p * p, 0.3); return (s.b * 0.8 + Math.sin(2 * Math.PI * (200 + 1400 * p * p) * x) * 0.15) * p * p; }, 0.5 * g, 0, 0.4);
const whoosh = (t, len = 0.45, g = 1, pan = 0) => add(t - len / 2, len, (x, st) => { const p = x / len; const env = Math.sin(Math.PI * p) ** 2; return svf(st, 'f', rnd(), 400 + 5000 * Math.sin(Math.PI * p), 0.5).b * env; }, 0.55 * g, pan, 0.35);
const boom = (t, g = 1) => { add(t, 2.2, (x, st) => { const f = 32 + 70 * Math.exp(-x * 10); st.ph = (st.ph || 0) + 2 * Math.PI * f / SR; return Math.sin(st.ph) * Math.exp(-x * 2.2); }, 0.95 * g); add(t, 1.6, (x, st) => lp(st, 'a', rnd(), 2500 * Math.exp(-x * 2) + 200) * Math.exp(-x * 3.5), 0.55 * g, 0, 0.8); };
const blip = (t, f, g = 1, pan = 0, len = 0.06) => add(t, len, x => Math.sin(2 * Math.PI * f * x) * Math.exp(-x * 60), 0.12 * g, pan, 0.25);
const click = (t, g = 1) => { add(t, 0.03, (x, st) => hp(st, 'h', rnd(), 2500) * Math.exp(-x * 250), 0.5 * g, 0.1); blip(t, 2200, 0.7 * g, 0.1, 0.04); };
const chime = (t, notes, g = 1, step = 0.07) => notes.forEach((m, i) => add(t + i * step, 0.9, x => (Math.sin(2 * Math.PI * mtof(m) * x) + 0.3 * Math.sin(4 * Math.PI * mtof(m) * x)) * Math.exp(-x * 5), 0.11 * g, (i % 2 ? 0.3 : -0.3), 0.6));
const hum = (t, len, g = 1) => add(t, len, (x, st) => { const env = Math.min(1, x / 0.08) * Math.min(1, (len - x) / 0.1); return (saw(x * 110) * 0.3 + Math.sin(2 * Math.PI * 880 * x) * 0.2) * env * (0.7 + 0.3 * Math.sin(x * 40)); }, 0.06 * g, 0.2, 0.2);

// ---------- arrangement ----------
const B = 0.5; // beat
// intro
riser(0.05, 0.95, 1.2);
add(0.1, 0.9, (x, st) => { const p = x / 0.9; return Math.sin(2 * Math.PI * (55 + 30 * p) * x) * p; }, 0.25);
boom(1.0, 1.1); kick(1.0, 1.2);
chime(1.02, [81, 88, 93], 0.8, 0.05);
// music bed 1.0 → 21.0 : Am F C G per bar (2 s)
const CH = [[57, [69, 72, 76]], [53, [65, 69, 72]], [48, [67, 72, 76]], [55, [67, 71, 74]]];
for (let bar = 0; bar < 10; bar++) {
  const t0 = 1.0 + bar * 2, [root, tri] = CH[bar % 4];
  pad(t0, 2.05, tri, bar < 1 ? 0.8 : 1, bar < 1 ? 1200 : 2200);
  for (let b = 0; b < 4; b++) {
    const tb = t0 + b * B;
    if (tb >= 20.5) continue;
    if (bar >= 1 || b >= 2) kick(tb);
    if (b % 2 === 1 && bar >= 1) clap(tb);
    hat(tb + B / 2, b === 3, bar >= 1 ? 1 : 0.6);
    if (bar >= 2) { hat(tb + B / 4, false, 0.5); hat(tb + 3 * B / 4, false, 0.5); }
    // bass: offbeat eighths
    if (bar >= 1) { bass(tb + B / 2, B / 2 - 0.02, root - 12); if (b === 3) bass(tb + 0.75 * B, B / 4 - 0.02, root - 12 + 12); }
  }
  // arp from bar 2
  if (bar >= 2) for (let s = 0; s < 16; s++) { const tt = t0 + s * B / 4; if (tt >= 20.5) break; const nt = tri[[0, 1, 2, 1][s % 4]] + (s >= 8 ? 12 : 0); pluck(tt, nt, s % 4 === 0 ? 1 : 0.65, s % 2 ? 0.4 : -0.4); }
}
riser(20.4, 1.1, 1.1);
// outro
boom(21.5, 1.2); kick(21.5, 1.3);
pad(21.5, 3.5, [69, 72, 76, 81], 1.4, 3000); bass(21.5, 3.3, 45);
for (const [i, tt] of [21.5, 22.0, 22.5].entries()) { kick(tt, 1.1); chime(tt, [[76, 81], [79, 84], [81, 88]][i], 1.1, 0.03); add(tt, 0.2, (x, st) => hp(st, 'h', rnd(), 1500) * Math.exp(-x * 25), 0.35, 0, 0.5); }
chime(22.9, [69, 76, 81, 84, 88], 0.6, 0.09);

// ---------- sfx ----------
whoosh(3.25, 0.6, 1.3); add(2.95, 0.55, (x, st) => { const p = x / 0.55; return Math.sin(2 * Math.PI * (80 + 900 * p * p * p) * x) * p; }, 0.12);
boom(3.5, 0.35);
add(3.58, 0.3, x => Math.sin(2 * Math.PI * (60 + 2000 * x) * x) * Math.exp(-x * 12), 0.18); // power on
for (let i = 0; i < 26; i++) blip(3.9 + i * 0.043 + (i % 3) * 0.008, 1400 + (i % 5) * 260, 0.55, ((i * 7) % 10) / 5 - 1);
hum(5.05, 0.95); hum(6.5, 0.95);
for (let i = 0; i < 12; i++) blip(6.05 + i * 0.022, 900 + i * 80, 0.5, i / 6 - 1); // qr flip
chime(6.05, [84, 88], 0.4);
for (const w of [7.5, 11.5, 15.0, 18.5]) whoosh(w, 0.5, 1.2, w === 11.5 || w === 18.5 ? -0.3 : 0.3);
// phone
for (const d of [0.8, 1.35, 1.7, 2.0, 2.25, 2.6]) click(7.5 + d, 0.8);
blip(8.05, 1760, 0.8); blip(8.12, 2350, 0.8); // qr locked
whoosh(8.55, 0.35, 0.6);
chime(10.35, [72, 76, 79, 84], 1.0, 0.06);
// spil
whoosh(11.75, 0.55, 0.9, -0.6); blip(12.0, 520, 1.2);
add(12.3, 0.05, (x, st) => hp(st, 'h', rnd(), 1800) * Math.exp(-x * 120), 0.9); blip(12.3, 1200, 1);
chime(12.36, [76, 83], 0.8, 0.05);
whoosh(12.75, 0.5, 1.0); chime(12.95, [81, 88], 0.5, 0.05); blip(13.05, 1500, 0.6);
// dashboard
for (let i = 0; i < 8; i++) blip(15.2 + i * 0.055, 700 + i * 90, 0.45, i / 4 - 1);
click(16.75, 1.1); for (let i = 0; i < 5; i++) blip(16.78 + i * 0.04, 1800 + i * 150, 0.5);
whoosh(16.95, 0.3, 0.5); chime(17.0, [84, 88], 0.45);
blip(17.4, 900, 0.9); chime(17.42, [79, 84], 0.5);
// stats
for (let i = 0; i < 26; i++) blip(18.7 + i * 0.045, 1100 + i * 35, 0.35 * (1 - i / 30), (i % 2) ? 0.5 : -0.5, 0.03);
click(20.45, 1.0); chime(20.5, [84, 91], 0.5);
add(21.0, 0.5, (x, st) => { const p = x / 0.5; return svf(st, 'f', rnd(), 6000 - 5500 * p, 0.4).b * p * p; }, 0.6); // suck

// ---------- reverb (Schroeder) ----------
function reverb(inp, delays, seedOff) {
  const out = new Float32Array(N);
  for (const d0 of delays) { const d = d0 + seedOff, buf = new Float32Array(d); let idx = 0, lpS = 0; for (let i = 0; i < N; i++) { const y = buf[idx]; lpS += 0.3 * (y - lpS); buf[idx] = inp[i] + lpS * 0.8; idx = (idx + 1) % d; out[i] += y; } }
  for (const d of [556, 441, 341, 225]) { const buf = new Float32Array(d); let idx = 0; for (let i = 0; i < N; i++) { const b = buf[idx]; const y = -out[i] + b; buf[idx] = out[i] + b * 0.5; out[i] = y; idx = (idx + 1) % d; } }
  return out;
}
const combs = [1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116].map(v => Math.round(v * SR / 44100));
const wl = reverb(revL, combs, 0), wr = reverb(revR, combs, 23);
// ---------- master ----------
let peak = 0;
for (let i = 0; i < N; i++) { L[i] += wl[i] * 0.09; R[i] += wr[i] * 0.09; peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i])); }
const pre = 3.2 / peak;
const buf = Buffer.alloc(44 + N * 4);
buf.write('RIFF', 0); buf.writeUInt32LE(36 + N * 4, 4); buf.write('WAVE', 8); buf.write('fmt ', 12); buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(2, 22); buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 4, 28); buf.writeUInt16LE(4, 32); buf.writeUInt16LE(16, 34); buf.write('data', 36); buf.writeUInt32LE(N * 4, 40);
for (let i = 0; i < N; i++) {
  const fade = Math.min(1, (N - i) / (SR * 0.45));
  const l = Math.tanh(L[i] * pre) * 0.89 * fade, r = Math.tanh(R[i] * pre) * 0.89 * fade;
  buf.writeInt16LE(Math.round(l * 32767), 44 + i * 4); buf.writeInt16LE(Math.round(r * 32767), 46 + i * 4);
}
fs.writeFileSync(__dirname + '/audio.wav', buf);
console.log('peak', peak.toFixed(2), 'written');
