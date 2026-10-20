# Passport art generator

Rasterizes the passport's SVG sources into the PNG plates the client embeds. The look follows the
"Eorzea Passport" Claude Design canvas: flat foil-stamped covers per nation, guilloche paper, and
inked demo stamps. The approach (versioned vector sources, a Node script, `sharp` rasterization,
fixed canvas sizes so new art drops in without code changes) mirrors the generators under
[Aetherphone's `tools/`](https://github.com/XeldarAlz/FFXIV-Aetherphone/tree/main/tools). No
Aetherphone code or art is copied.

## Run

```sh
cd tools/passport-art
npm ci
npm run build          # all editions
node generate.mjs gridania   # one edition (shared plates are always rewritten)
npm run sheet          # also writes out/contact-sheet.png for review (gitignored)
```

Output goes to `assets/passport/vector/`. Rendering is deterministic for a given `sharp`/librsvg
version; `sharp` is pinned exactly and locked in `package-lock.json`. Building the client never runs
this tool: the PNGs are committed.

## Sources

| File | Output | Canvas |
| --- | --- | --- |
| `svg/cover-base.svg` + `svg/cover-<edition>.svg` | `editions/<edition>/cover.png`, `book-cover.png` (Eorzea) | 1024 × 1536, board at 42,141 940 × 1253 |
| `svg/crest-<edition>.svg` | `editions/<edition>/crest.png` | 1254 × 1254 |
| `svg/banner.svg` + `svg/emblem-<edition>.svg` | `editions/<edition>/banner.png` | 800 × 1840 (crest and lettering are live; the emblem sits at the foot, a 60 × 60 box centred at 100, 340) |
| `svg/map.svg` | `editions/<edition>/map-overhead.png` | 1044 × 1260 |
| `svg/book-open.svg` | `editions/<edition>/book-open.png` | 1536 × 1024 |
| `svg/portrait.svg` | `demo-portrait.png` | 1086 × 1448 |
| `svg/stamp-atlas.svg` | `stamp-atlas.png` | 2172 × 724, three 724 px cells |
| (generated noise) | `grain.png` (one copy) | 512 × 704: the paper's tooth for the pencil, white with alpha, from a fixed seed |
| `svg/crest-*.svg`, `svg/emblem-*.svg`, `svg/symbols.svg` | `symbols.png` (half and quarter copies only) | 1536 × 1024, a 6 × 4 grid of 256 px white masks (grey prints fainter, black is cut out): crests, emblems, then motifs, in `PassportSymbol.All` order |

Templates are ordinary SVG with `{{token}}` slots (`cover`, `foil`, `accent`, `paper`, `ink`).
Palettes live in `editions.mjs`. `<!-- include:decor -->`, `<!-- include:emblem -->`, and `<!-- include:crest ... -->` mark
where the script nests another template.

## Colour weight maps

Each edition plate also gets `<plate>.w-<colour>.png` for the colours the client recolours (`cover`, `foil`, `paper`, and `lines`, the paper's guilloche rings, printed black) where that colour reaches the plate: a grey map of how much of the colour reaches each pixel (the plate rendered with the colour white, minus with it black). The client recolours a plate as plate + weight × (new − printed). `node generate.mjs --verify` checks this for every edition against rendering other colours directly and fails past 2 / 255 on average or 32 at worst (filters work in 8-bit linear light, so soft filtered edges differ by a few levels). Weight maps are full size only; the client makes reduced copies after recolouring.

## Size copies

The game samples textures without mipmaps, so shrinking a plate more than about 2x skips pixels and
edges alias. Every plate is therefore also re-rendered from its SVG at 1/2, 1/4 and 1/8 size
(`<name>.half.png`, `.quarter`, `.eighth`, stopping near 96 px). The client draws the smallest copy
that is still at least as wide as it appears on screen, falling back to any loaded copy while one loads. Every root `<svg>` therefore needs a `viewBox`: without one, rendering smaller crops the drawing, and the generator refuses it. The client checks also compare each copy against its full plate.

## Contracts with the client

- Art holds no text. Nation names, captions, and the passport number stay live in
  `PassportDocumentView`, which places the cover text and crest for the board above.
- `book-open`: the client repeats the 40%–60% row band on tall pages, so keep it quiet.
  The guilloche rings can show a seam there in very large text sizes.
- `BookControl` samples the cover at u 0.30–0.70, v 0.73–0.77 for button leather; keep that area plain.
- Stamp cell 1 is trimmed to 34%–65% of the atlas width.
- Every edition is embedded in the client; `runtimeEdition` only picks the edition for review sheets. The client's edition words and colours (`Features/Passport/PassportEdition.cs`) must match `editions.mjs`.
