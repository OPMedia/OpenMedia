// Verify every exported PNG: dimensions, alpha channel, and whether the corners
// are actually transparent or actually opaque 鈥?i.e. that "transparent" and
// "opaque" variants really are what their names claim.
import { createRequire } from 'node:module';
import { readdir, stat } from 'node:fs/promises';
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
const OUT = path.join(ROOT, 'png');

// name pattern -> { sizes, transparentCorners }
const EXPECT = [
  { re: /^app-icon-dark\/app-icon-dark-(\d+)\.png$/, sizes: [16,32,48,64,96,128,152,180,192,256,512,1024], corners: 'clear' },
  { re: /^app-icon-light\/app-icon-light-(\d+)\.png$/, sizes: [16,32,48,64,96,128,152,180,192,256,512,1024], corners: 'clear' },
  { re: /^app-icon-dark-square\/app-icon-dark-square-(\d+)\.png$/, sizes: [16,32,48,64,96,128,152,180,192,256,512,1024], corners: 'solid' },
  { re: /^app-icon-light-square\/app-icon-light-square-(\d+)\.png$/, sizes: [16,32,48,64,96,128,152,180,192,256,512,1024], corners: 'solid' },
  { re: /^mark\/mark-gradient-(\d+)\.png$/, sizes: [16,24,32,48,64,96,128,256,512,1024], corners: 'clear' },
  { re: /^mark\/mark-black-(\d+)\.png$/, sizes: [16,24,32,48,64,96,128,256,512,1024], corners: 'clear' },
  { re: /^mark\/mark-white-(\d+)\.png$/, sizes: [16,24,32,48,64,96,128,256,512,1024], corners: 'clear' },
  { re: /^lockup\/lockup-on-light-(\d+)\.png$/, sizes: [512,1024,2048], corners: 'clear' },
  { re: /^lockup\/lockup-on-dark-(\d+)\.png$/, sizes: [512,1024,2048], corners: 'clear' },
  { re: /^lockup\/lockup-plate-light-(\d+)\.png$/, sizes: [512,1024,2048], corners: 'clear' },
  { re: /^lockup\/lockup-plate-dark-(\d+)\.png$/, sizes: [512,1024,2048], corners: 'clear' },
];

const files = [];
async function walk(dir, base = '') {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const rel = base ? `${base}/${e.name}` : e.name;
    if (e.isDirectory()) await walk(path.join(dir, e.name), rel);
    else files.push(rel);
  }
}
await walk(OUT);

const problems = [];
const seen = new Set();

for (const rel of files.sort()) {
  const full = path.join(OUT, rel);
  const meta = await sharp(full).metadata();
  const rule = EXPECT.find((e) => e.re.test(rel));

  if (rel === 'apple-touch-icon-180.png') {
    if (meta.width !== 180 || meta.height !== 180) problems.push(`${rel}: expected 180x180, got ${meta.width}x${meta.height}`);
    const a = await cornerAlpha(full, meta);
    if (a[0] !== 255) problems.push(`${rel}: apple-touch-icon must be opaque, corner alpha=${a[0]}`);
    seen.add(rel);
    continue;
  }

  if (!rule) { problems.push(`${rel}: no expectation rule`); continue; }
  seen.add(rel);

  const size = Number(rel.match(rule.re)[1]);
  if (rule.corners === 'solid' || rel.includes('lockup')) {
    if (rel.includes('lockup')) {
      if (meta.width !== size) problems.push(`${rel}: expected width ${size}, got ${meta.width}`);
    } else if (meta.width !== size || meta.height !== size) {
      problems.push(`${rel}: expected ${size}x${size}, got ${meta.width}x${meta.height}`);
    }
  } else if (meta.width !== size || meta.height !== size) {
    problems.push(`${rel}: expected ${size}x${size}, got ${meta.width}x${meta.height}`);
  }

  const a = await cornerAlpha(full, meta);
  const expectClear = rule.corners === 'clear';
  const avg = a.reduce((s, v) => s + v, 0) / a.length;
  if (expectClear && avg > 8) problems.push(`${rel}: corners should be transparent, avg corner alpha=${avg.toFixed(1)}`);
  if (!expectClear && avg < 250) problems.push(`${rel}: corners should be opaque, avg corner alpha=${avg.toFixed(1)}`);
}

// every declared size must exist
for (const rule of EXPECT) {
  const probe = rule.re.source.replace(/\\d\+/g, '').replace(/[\^\$\\]/g, '');
  for (const size of rule.sizes) {
    const dirFile = rule.re.exec(files.find((f) => rule.re.test(f) && Number(f.match(rule.re)[1]) === size) ?? '');
    if (!dirFile) problems.push(`missing: ${rule.re.source} size ${size}`);
  }
}

async function cornerAlpha(file, meta) {
  const pts = [[0, 0], [meta.width - 1, 0], [0, meta.height - 1], [meta.width - 1, meta.height - 1]];
  const out = [];
  for (const [x, y] of pts) {
    const { data } = await sharp(file).ensureAlpha().extract({ left: x, top: y, width: 1, height: 1 }).raw().toBuffer({ resolveWithObject: true });
    out.push(data[3]);
  }
  return out;
}

console.log(`checked ${seen.size} files, ${files.length - seen.size} unaccounted`);
if (problems.length) {
  console.log(`\nPROBLEMS (${problems.length}):`);
  problems.forEach((p) => console.log('  ' + p));
  process.exitCode = 1;
} else {
  console.log('all good.');
}
void stat;

