// Rasterise the whole Serata icon set to PNG + favicon.ico.
//
//   node export.mjs
//
// Needs sharp. It is resolved from, in order:
//   $env:SERATA_SHARP, %TEMP%\serata-tools, <repo>\node_modules
import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const candidates = [
  process.env.SERATA_SHARP,
  `${process.env.TEMP}/serata-tools/package.json`,
  'C:/Users/65282/Documents/deepseek-harness/default-workspace/_cline-pass-work/repo/package.json',
].filter(Boolean);

let sharp;
for (const base of candidates) {
  try { sharp = createRequire(base)('sharp'); break; } catch { /* next */ }
}
if (!sharp) throw new Error(`sharp not found. Tried:\n${candidates.join('\n')}`);

const here = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(here, '..');
const SVG = path.join(ROOT, 'svg');
const OUT = path.join(ROOT, 'png');

const ICON_SIZES = [16, 32, 48, 64, 96, 128, 152, 180, 192, 256, 512, 1024];
const MARK_SIZES = [16, 24, 32, 48, 64, 96, 128, 256, 512, 1024];
const LOCKUP_WIDTHS = [512, 1024, 2048];

const svg = (name) => readFile(path.join(SVG, `${name}.svg`));

async function png(source, size, out) {
  await sharp(source, { density: 144 }).resize(size, size, { fit: 'contain' }).png({ compressionLevel: 9 }).toFile(out);
  return path.relative(ROOT, out).replace(/\\/g, '/');
}

async function pngWidth(source, width, out) {
  await sharp(source, { density: 144 }).resize({ width }).png({ compressionLevel: 9 }).toFile(out);
  return path.relative(ROOT, out).replace(/\\/g, '/');
}

function icoFromPngs(entries) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(entries.length, 4);
  const dir = Buffer.alloc(16 * entries.length);
  let offset = 6 + dir.length;
  entries.forEach((e, i) => {
    const o = i * 16;
    dir.writeUInt8(e.size >= 256 ? 0 : e.size, o);
    dir.writeUInt8(e.size >= 256 ? 0 : e.size, o + 1);
    dir.writeUInt8(0, o + 2); dir.writeUInt8(0, o + 3);
    dir.writeUInt16LE(1, o + 4);
    dir.writeUInt16LE(32, o + 6);
    dir.writeUInt32LE(e.data.length, o + 8);
    dir.writeUInt32LE(offset, o + 12);
    offset += e.data.length;
  });
  return Buffer.concat([header, dir, ...entries.map((e) => e.data)]);
}

await rm(OUT, { recursive: true, force: true });
await mkdir(OUT, { recursive: true });

const written = [];

// 1. App icons 鈥?rounded (transparent corners) and square (opaque), light and dark.
for (const [dir, file] of [
  ['app-icon-dark', 'app-icon-dark'],
  ['app-icon-light', 'app-icon-light'],
  ['app-icon-dark-square', 'app-icon-dark-square'],
  ['app-icon-light-square', 'app-icon-light-square'],
]) {
  await mkdir(path.join(OUT, dir), { recursive: true });
  const src = await svg(file);
  for (const size of ICON_SIZES) {
    written.push(await png(src, size, path.join(OUT, dir, `${file}-${size}.png`)));
  }
}

// 2. Marks 鈥?always transparent.
await mkdir(path.join(OUT, 'mark'), { recursive: true });
for (const name of ['mark-gradient', 'mark-black', 'mark-white']) {
  const src = await svg(name);
  for (const size of MARK_SIZES) {
    written.push(await png(src, size, path.join(OUT, 'mark', `${name}-${size}.png`)));
  }
}

// 3. Horizontal lockups 鈥?two transparent colourways, two opaque plates.
await mkdir(path.join(OUT, 'lockup'), { recursive: true });
for (const name of ['lockup-on-light', 'lockup-on-dark', 'lockup-plate-light', 'lockup-plate-dark']) {
  const src = await svg(name);
  for (const w of LOCKUP_WIDTHS) {
    written.push(await pngWidth(src, w, path.join(OUT, 'lockup', `${name}-${w}.png`)));
  }
}

// 4. iOS home screen icon 鈥?opaque, straight from the square build.
written.push(await png(await svg('app-icon-dark-square'), 180, path.join(OUT, 'apple-touch-icon-180.png')));

// 5. favicon.ico
const favicon = await svg('favicon');
const icoEntries = [];
for (const size of [16, 32, 48, 64, 128, 256]) {
  icoEntries.push({ size, data: await sharp(favicon, { density: 144 }).resize(size, size).png({ compressionLevel: 9 }).toBuffer() });
}
await writeFile(path.join(ROOT, 'favicon.ico'), icoFromPngs(icoEntries));

written.push('favicon.ico');
written.forEach((f) => console.log(f));
console.log(`\n${written.length} files.`);

