// Build a single contact sheet so the whole set can be eyeballed in one image.
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const candidates = [
  process.env.SERATA_SHARP,
  `${process.env.TEMP}/serata-tools/package.json`,
  'C:/Users/65282/Documents/deepseek-harness/default-workspace/_cline-pass-work/repo/package.json',
].filter(Boolean);
let sharp;
for (const base of candidates) { try { sharp = createRequire(base)('sharp'); break; } catch {} }
if (!sharp) throw new Error('sharp not found');

const here = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(here, '..');
const P = (p) => path.join(ROOT, p);
const W = 1560, H = 1180;
const DARK = '#0b0d14', LIGHT = '#F7F5F2', MID = '#6b7280';

const layers = [];
const tile = (left, top, width, height, background) => layers.push({ left, top, width, height, background });
async function place(file, width, left, top, tileBg) {
  const img = sharp(await readFile(P(file))).resize({ width, fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } });
  const { data, info } = await img.png().toBuffer({ resolveWithObject: true });
  if (tileBg) layers.push({ left, top, width: info.width + 40, height: info.height + 40, background: tileBg, tileBefore: true });
  layers.push({ left: left + (tileBg ? 20 : 0), top: top + (tileBg ? 20 : 0), input: data });
}

await place('png/app-icon-dark/app-icon-dark-256.png', 200, 60, 40);
await place('png/app-icon-light/app-icon-light-256.png', 200, 320, 40);
await place('png/app-icon-dark-square/app-icon-dark-square-256.png', 200, 580, 40);
await place('png/app-icon-light-square/app-icon-light-square-256.png', 200, 840, 40);
await place('png/apple-touch-icon-180.png', 200, 1100, 40);

await place('png/mark/mark-gradient-1024.png', 130, 60, 300, LIGHT);
await place('png/mark/mark-black-1024.png', 130, 240, 300, LIGHT);
await place('png/mark/mark-white-1024.png', 130, 420, 300, DARK);
await place('png/mark/mark-white-1024.png', 130, 600, 300, MID);
await place('png/mark/mark-gradient-1024.png', 130, 780, 300, DARK);

await place('png/lockup/lockup-on-light-2048.png', 470, 60, 500, LIGHT);
await place('png/lockup/lockup-on-dark-2048.png', 470, 800, 500);

await place('png/lockup/lockup-plate-light-2048.png', 470, 60, 790);
await place('png/lockup/lockup-plate-dark-2048.png', 470, 800, 790);

// size ladders along the bottom, dark row then light row
const LADDER = [16, 32, 48, 64, 96, 128];
let x = 62;
for (const s of LADDER) {
  await place(`png/app-icon-dark/app-icon-dark-${s}.png`, s, x, 1130 - s);
  x += s + 28;
}

let x2 = 700;
for (const s of LADDER) {
  await place(`png/app-icon-light/app-icon-light-${s}.png`, s, x2, 1130 - s);
  x2 += s + 28;
}

// tiles must be painted before the images that sit on them
const ordered = layers
  .map((l, i) => ({ l, i }))
  .sort((a, b) => (a.l.tileBefore ? 0 : 1) - (b.l.tileBefore ? 0 : 1) || a.i - b.i)
  .map(({ l }) => ({ left: l.left, top: l.top, width: l.width, height: l.height, background: l.background, input: l.input }))
  .map((c) => (c.input ? { input: c.input, left: c.left, top: c.top } : {
    input: { create: { width: c.width, height: c.height, channels: 4, background: c.background } },
    left: c.left, top: c.top,
  }));

await sharp({ create: { width: W, height: H, channels: 4, background: DARK } })
  .composite(ordered)
  .png()
  .toFile(P('overview.png'));

console.log('overview.png');




