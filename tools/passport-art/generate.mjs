import sharp from "sharp";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { editions, runtimeEdition, shared } from "./editions.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const svgDir = resolve(here, "svg");
const out = resolve(here, "../../assets/passport/vector");

// Canvas sizes are part of the client contract (BookSurface/BookShellLayout); change them together.
const COVER = { w: 1024, h: 1536, board: { x: 42, y: 141, w: 940, h: 1253 } };
const CREST = 1254;

const read = (name) => readFileSync(resolve(svgDir, name), "utf8");
const fill = (svg, tokens) => svg.replace(/\{\{(\w+)\}\}/g, (match, key) => {
  if (!(key in tokens)) throw new Error(`Unknown token ${match}`);
  return tokens[key];
});
const inner = (svg) => svg.replace(/^[\s\S]*?<svg[^>]*>/, "").replace(/<\/svg>\s*$/, "");
const viewBox = (svg) => /viewBox="([^"]+)"/.exec(svg)[1];
const nest = (svg, x, y, w, h) => `<svg x="${x}" y="${y}" width="${w}" height="${h}" viewBox="${viewBox(svg)}" overflow="visible">${inner(svg)}</svg>`;
// Without a viewBox, a smaller width/height crops the drawing instead of scaling it.
const sized = (svg, w, h) => svg.replace(/<svg([^>]*)>/, (tag, attrs) => {
  if (!/\sviewBox="/.test(attrs)) throw new Error("Root <svg> needs a viewBox to render at other sizes.");
  return `<svg${attrs.replace(/\s(width|height)="[^"]*"/g, "")} width="${w}" height="${h}">`;
});

async function png(svg, file) {
  const buffer = await sharp(Buffer.from(svg)).png({ compressionLevel: 9, palette: false }).toBuffer();
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, buffer);
  return buffer;
}

function cover(id, tokens) {
  const board = fill(read("cover-base.svg").replace("<!-- include:decor -->", inner(read(`cover-${id}.svg`))), tokens);
  const { x, y, w, h } = COVER.board;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${COVER.w}" height="${COVER.h}" viewBox="0 0 ${COVER.w} ${COVER.h}">
    <defs><filter id="lift" x="-10%" y="-10%" width="120%" height="120%"><feDropShadow dx="0" dy="14" stdDeviation="16" flood-color="#000" flood-opacity=".45"/></filter></defs>
    <g filter="url(#lift)">${nest(board, x, y, w, h)}</g></svg>`;
}

const crest = (id, tokens) => fill(read(`crest-${id}.svg`), tokens);

function banner(id, tokens) {
  return fill(read("banner.svg"), tokens).replace(/<!-- include:crest x=(\S+) y=(\S+) w=(\S+) h=(\S+) -->/,
    (_, x, y, w, h) => nest(crest(id, tokens), x, y, w, h));
}

// The game samples textures without mipmaps, so shrinking a plate more than about 2x skips pixels
// and edges alias. Each plate therefore also ships re-rendered at 1/2, 1/4 and 1/8 size (down to
// about 96 px); the client draws the smallest copy that is still at least as large as on screen.
export const LEVELS = [["", 1], [".half", 2], [".quarter", 4], [".eighth", 8]];
const results = [];
async function emit(svg, path, w, h) {
  const file = resolve(out, path);
  for (const [suffix, divisor] of LEVELS) {
    if (divisor > 1 && Math.min(w, h) / divisor < 96) break;
    const target = suffix ? file.replace(/\.png$/, `${suffix}.png`) : file;
    await png(sized(svg, Math.round(w / divisor), Math.round(h / divisor)), target);
    results.push(path + suffix);
  }
  return file;
}

const only = new Set(process.argv.slice(2).filter((arg) => !arg.startsWith("--")));
for (const [id, palette] of Object.entries(editions)) {
  if (only.size > 0 && !only.has(id)) continue;
  const tokens = { ...shared, ...palette };
  await emit(cover(id, tokens), `editions/${id}/cover.png`, COVER.w, COVER.h);
  await emit(crest(id, tokens), `editions/${id}/crest.png`, CREST, CREST);
  await emit(banner(id, tokens), `editions/${id}/banner.png`, 800, 1840);
  await emit(fill(read("map.svg"), tokens), `editions/${id}/map-overhead.png`, 1044, 1260);
  await emit(fill(read("book-open.svg"), tokens), `editions/${id}/book-open.png`, 1536, 1024);
}

// Edition-independent plates; the generic closed cover is the Eorzea edition without a crest.
const neutral = { ...shared, ...editions.eorzea };
await emit(cover("eorzea", neutral), "book-cover.png", COVER.w, COVER.h);
await emit(fill(read("portrait.svg"), neutral), "demo-portrait.png", 1086, 1448);
await emit(read("stamp-atlas.svg"), "stamp-atlas.png", 2172, 724);

if (process.argv.includes("--sheet")) {
  // Review-only contact sheet (gitignored): every edition cover with its crest, then the runtime spread.
  const tile = { w: 320, h: 480 };
  const ids = Object.keys(editions);
  const layers = [];
  for (const [index, id] of ids.entries()) {
    const tokens = { ...shared, ...editions[id] };
    const art = `<svg xmlns="http://www.w3.org/2000/svg" width="${tile.w}" height="${tile.h}" viewBox="0 0 ${COVER.w} ${COVER.h}">
      ${inner(cover(id, tokens))}${nest(crest(id, tokens), 337, 484, 350, 350)}</svg>`;
    layers.push({ input: await sharp(Buffer.from(art)).png().toBuffer(), left: 16 + index * (tile.w + 16), top: 16 });
  }
  const spread = await sharp(resolve(out, `editions/${runtimeEdition}/book-open.png`)).resize(960).toBuffer();
  layers.push({ input: spread, left: 16, top: tile.h + 32 });
  const width = 16 + ids.length * (tile.w + 16);
  const sheet = resolve(here, "out/contact-sheet.png");
  mkdirSync(dirname(sheet), { recursive: true });
  await sharp({ create: { width, height: tile.h + 32 + 640 + 16, channels: 4, background: "#2C3B33" } })
    .composite(layers).png().toFile(sheet);
  console.log(`sheet: ${sheet}`);
}

console.log(`${results.length} plates written to ${out}`);
