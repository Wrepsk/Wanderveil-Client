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

// The pennant, with its edition's emblem centred at (100, 340) in its 60 x 60 box.
function banner(id, tokens) {
  return fill(read("banner.svg"), tokens)
    .replace("<!-- include:emblem -->", nest(fill(read(`emblem-${id}.svg`), tokens), 70, 310, 60, 60))
    .replace(/<!-- include:crest x=(\S+) y=(\S+) w=(\S+) h=(\S+) -->/, (_, x, y, w, h) => nest(crest(id, tokens), x, y, w, h));
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

// Colour weight maps, for recolouring plates in the client without losing grain, shading, or embossing.
// Compositing is linear in each fill colour, so a plate rendered with colour c is
//   plate(c) = plate(c0) + sum over tokens k of weight_k * (c_k - c0_k),
// where weight_k is how much of token k reaches each pixel: the difference between the plate rendered with k
// white and with k black (everything else as the edition prints it). Weights are grey, full size only; the
// client makes its own reduced copies after recolouring. Only cloth (cover), foil, and paper are recoloured.
export const RECOLOURED = ["cover", "foil", "paper"];
const plates = (id, tokens) => ({
  cover: [cover(id, tokens), COVER.w, COVER.h],
  crest: [crest(id, tokens), CREST, CREST],
  banner: [banner(id, tokens), 800, 1840],
  "map-overhead": [fill(read("map.svg"), tokens), 1044, 1260],
  "book-open": [fill(read("book-open.svg"), tokens), 1536, 1024],
});
const raw = async (svg) => sharp(Buffer.from(svg)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
async function weights(id, tokens) {
  for (const [plate, [, w, h]] of Object.entries(plates(id, tokens))) {
    for (const token of RECOLOURED) {
      const white = await raw(plates(id, { ...tokens, [token]: "#FFFFFF" })[plate][0]);
      const black = await raw(plates(id, { ...tokens, [token]: "#000000" })[plate][0]);
      const grey = Buffer.alloc(w * h);
      let any = false;
      for (let i = 0; i < w * h; i++) {
        const d = (white.data[i * 4] - black.data[i * 4] + white.data[i * 4 + 1] - black.data[i * 4 + 1] + white.data[i * 4 + 2] - black.data[i * 4 + 2]) / 3;
        grey[i] = Math.max(0, Math.min(255, Math.round(d)));
        if (grey[i] > 0) any = true;
      }
      if (!any) continue; // The token does not reach this plate.
      const file = resolve(out, `editions/${id}/${plate}.w-${token}.png`);
      mkdirSync(dirname(file), { recursive: true });
      writeFileSync(file, await sharp(grey, { raw: { width: w, height: h, channels: 1 } }).toColourspace("b-w").png({ compressionLevel: 9 }).toBuffer());
      results.push(`editions/${id}/${plate}.w-${token}`);
    }
  }
}

// Recolours an edition's plates through its weights and compares them with rendering the colours directly.
async function verify(id, tokens, target) {
  let worst = 0;
  let where = "";
  // The renderer's filters (drop shadows over whole boards) work in 8-bit linear light, which rounds dark
  // colours coarsely and differently for each colour, so a direct render is itself a few levels off in the
  // darks. Compare on average, with a cap on the worst pixel.
  let counted = 0;
  let total = 0;
  for (const [plate, [svg]] of Object.entries(plates(id, tokens))) {
    const base = await raw(svg);
    const direct = await raw(plates(id, { ...tokens, ...target })[plate][0]);
    const maps = {};
    for (const token of RECOLOURED) {
      try { maps[token] = await sharp(resolve(out, `editions/${id}/${plate}.w-${token}.png`)).extractChannel(0).raw().toBuffer(); } catch { }
    }
    const hex = (c) => [1, 3, 5].map((at) => parseInt(c.slice(at, at + 2), 16));
    for (let i = 0; i < base.data.length / 4; i++) {
      if (direct.data[i * 4 + 3] < 8) continue;
      for (let ch = 0; ch < 3; ch++) {
        let value = base.data[i * 4 + ch];
        for (const [token, map] of Object.entries(maps)) value += map[i] / 255 * (hex(target[token] ?? tokens[token])[ch] - hex(tokens[token])[ch]);
        // As seen: weighted by coverage, since nearly transparent pixels keep only rounding noise as colour.
        const error = Math.abs(Math.max(0, Math.min(255, value)) - direct.data[i * 4 + ch]) * direct.data[i * 4 + 3] / 255;
        counted++;
        total += error;
        if (error > worst) { worst = error; where = `${plate} at ${i % base.info.width},${Math.floor(i / base.info.width)} (alpha ${direct.data[i * 4 + 3]})`; }
      }
    }
  }
  if (process.argv.includes("--verbose")) console.log(`  worst ${worst.toFixed(1)} in ${where}; mean ${(total / counted).toFixed(2)}`);
  return { worst, mean: total / counted };
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
  await weights(id, tokens);
  if (process.argv.includes("--verify")) {
    const { worst, mean } = await verify(id, tokens, { cover: "#5A1424", foil: "#DCE6E8", paper: "#ECEAE4" });
    console.log(`${id}: recoloured through weights within ${worst.toFixed(1)} / 255 of a direct render, ${mean.toFixed(2)} on average`);
    if (worst > 32 || mean > 2) throw new Error(`${id}: recolouring through weights is ${worst} / 255 off`);
  }
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
