# Passport visual fidelity

Status: milestone 1 delivered for visual review, 18 October 2026. The user approved an asset-led physical Eorzean passport direction before Studio/Echoes. This does not record final acceptance of the compositions.

## Visual targets

The committed previews are rendered from real ImGui draw data using the shared book painter, bundled artwork, live text, and Source Serif 4 headings. They are not generated screenshots with baked text. The local Windows Segoe UI font is a test-only surrogate for Dalamud body text and is never bundled. Actual host/GPU appearance remains unobserved.

- [Closed cover](previews/passport/cover.png): dark teal leather, stitched/recessed construction, live gold typography and original compass motif.
- [Opening identity target](previews/passport/identity.png): prominent illustrated portrait and deliberately composed document fields/notes.
- [Demo collection target](previews/passport/collection.png): three engraved ink specimens on real paper texture; no collected/issued state.

Review materials, readability, portrait scale, margins, typography, and stamp balance before milestone 2. Cover and collection compositions live in the test-only `PassportTargets` gallery. They are review targets, not runtime routes. The plugin currently applies the stationary material surface, heading fonts, and demo portrait to the existing identity/notes composition. Discover and its input flow remain unchanged. Native window/tab chrome is retained until the core book experience is established.

## Shared presentation foundation

`IBookAssets`/`BookImage` separate material access from painting. `BookSurface` renders the blank cover or open spread and supplies safe content bounds. Top/bottom construction is preserved; taller content repeats only a central paper/binding strip. Narrow mode crops the appropriate half of the same material and preserves the existing stacked reading flow. A future single-page navigator belongs to milestone 2.

The material painter has no identity, stamp ownership, page count, or collection state. The same material/content regions can display any logical page pair; content and future lazy page routing remain outside the artwork. This milestone does not introduce a fixed array of baked page images or claim a completed arbitrary-page navigator.

Existing `PageFlow`, host scale, measurement/drawing, native actions, and focus behavior remain. `UiContext` optionally supplies two heading fonts. Unsupported decorative glyphs fall back to the current host font as a whole label; this does not establish the host's complete localization coverage.

`PassportAssets` requests embedded images through Dalamud's shared texture provider. The host owns loading/caching and frame-wrap lifetime; no shared wrap is cached or disposed by the plugin. Plugin-owned managed font handles are built once and disposed on normal/partial teardown. Draw callbacks do not read files, decode images, save state, or create fonts. A readable material/portrait fallback remains when resources are unavailable.

## Assets and reproducibility

See [asset provenance and exact prompts](../assets/passport/PROVENANCE.md) and [third-party notices](../THIRD_PARTY_NOTICES.md). Four original generated PNGs are bundled unchanged; no copied game/reference art. Source Serif 4 Regular/Semibold are pinned, unmodified Adobe files with SIL OFL notices included in the release package. Builds require no image-generation service or private source.

Run the standard [native UI checks](../tests/README.md) with an artifact directory. The gallery writes `passport-target-{cover,identity,collection}.png`; runtime/fallback/long-content views remain BMPs. The CPU renderer supports registered RGBA images and every generated font-atlas texture page. Test-only Windows drawing APIs decode/export images; none enter the plugin package.

## Remaining milestones

1. Review this stationary visual target and refine it where needed.
2. Implement the approved identity and demo collection spreads with logical-page navigation and narrow layouts.
3. Add entrance/exit with explicit focus/input ownership and immediate reduced-motion behavior.
4. Separately prototype one bounded 2.5D turn; evaluate intermediate frames, clipping, perspective, text distortion, interruption, and measured performance. Document any limitations before recommending a renderer change.

CrystalRenderer, full paper physics, custom GPU hooks, elaborate window dragging/resizing, collection persistence, and Studio/Echoes are outside this milestone. CrystalRenderer still requires a separate decision.
