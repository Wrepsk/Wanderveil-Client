# Passport visual fidelity

Status: stationary material foundation and user-approved shell correction delivered for visual review, 18 October 2026. The user approved an asset-led physical Eorzean passport direction before Studio/Echoes. This does not record final acceptance of the compositions.

## Intended experience refinement

The user's subsequent cover/map/identity references establish the [passport experience](passport-experience.md): arrive closed from the bottom center and wait for explicit opening; show a banner/map nation introduction spread, then portrait/signature opposite identity information, followed by stampable pages. Dismissal closes to the visible cover before sliding down. Nation editions and substantial user customization, including cover colors, are intended; the five pictured editions are examples of an expandable set. These are planned behaviors, not delivered runtime features. This direction supersedes the earlier identity-first page order and current left-page identity-fields/right-page notes composition.

## Book shell correction

Current state (19 October 2026): runtime art comes from SVG via `tools/passport-art`, following the user's Eorzea Passport Claude Design, instead of the image-generated plates described below. The book is its own control surface: side clicks open, turn, and return to the cover; dragging anywhere moves it; sections are paper index tabs; Settings and size/close controls are printed in the page margin or foil-stamped on the cover. The leather bookmarks, leather-backed controls, Open/Cover/previous/next buttons, and edge drag grip described below are superseded. Page contents scale with the book rather than scrolling, and type is Cinzel, Cormorant Garamond, and Courier Prime instead of Source Serif 4. See [runtime controls](passport-runtime.md) for details.

Earlier runtime update: the selected Gridania cover, nation introduction, identity, and demo collection are now connected to `BookShell` through the same composition used by the gallery. See [runtime controls/reload instructions and previews](passport-runtime.md). Static closed/open logical navigation is implemented; motion remains pending. The map remains explicitly unvalidated and the plum colorway remains a study rather than a color picker. Earlier shell/gallery descriptions below record their original scope.

The initial material milestone placed a book inside the conventional Wanderveil window. The user's in-game screenshot demonstrated the missing root composition. The subsequent approved slice makes the book the visible shell: transparent borderless host, attached section bookmarks, discreet leather-backed Settings/size/close controls, and a simple outer-leather drag grip. The outer book never scrolls. Text and native actions scroll inside bounded transparent page children, leaving the binding, page edges, bookmarks, and controls stationary.

The previews below were refreshed for the current passport; the paragraphs after them describe the original shell slice.

- [Complete book shell](previews/passport/shell.png)
- [Single page (nation banner)](previews/passport/shell-narrow.png)
- [Next single page (nation map)](previews/passport/shell-notes.png)
- [Enlarged text](previews/passport/shell-large-text.png)
- [Discover inside the book](previews/passport/shell-discover.png)

These previews run the production `BookShell.Draw`, window flags, and host preparation against an authored test backdrop. They include the surrounding host rather than only an isolated material painter. No new artwork or dependency was added; controls sample the existing original leather plate. Software sampling/body-font appearance differs from the GPU/game host.

Viewport fitting and actual font size select a spread or one page. Single-page Passport provides immediate identity/notes navigation. Controls stack when required by compact/enlarged layouts. The selected bookmark receives keyboard focus on opening; native directional navigation and Space activate actions. Details still focus Back and preserve browse scroll. Escape uses Dalamud's existing close handling. Dragging is limited to a leather grip; plus/minus adjust a session-only preferred size. Settings retains its separate conventional window. Reduced motion is fully immediate because this slice adds no animation.

This remains a stationary presentation prototype. Cover and collection are review targets, not runtime routes. Full logical-page/collection composition and motion follow after visual review. At the time the host kept ImGui's rectangular input region, including transparent material margins; since then only the book's board, tabs, and ribbon take the mouse (see [input ownership](passport-runtime.md#input-ownership)). It is rectangle-based, not per-pixel hit testing. Native previews validate input/layout and style restoration, not live Dalamud hotkey/lifecycle/GPU behavior.

## Visual targets

The committed previews are rendered from real ImGui draw data using the shared book painter, bundled artwork, live text, and Source Serif 4 headings. They are not generated screenshots with baked text. The local Windows Segoe UI font is a test-only surrogate for Dalamud body text and is never bundled. Actual host/GPU appearance remains unobserved.

- [Closed cover](previews/passport/cover.png): dark teal leather, stitched/recessed construction, live gold typography and original compass motif.
- [Opening identity target](previews/passport/identity.png): prominent illustrated portrait and deliberately composed document fields/notes.
- [Demo collection target](previews/passport/collection.png): three engraved ink specimens on real paper texture; no collected/issued state.

Review materials, readability, portrait scale, margins, typography, and stamp balance before milestone 2. Cover and collection compositions live in the test-only `PassportTargets` gallery. They are review targets, not runtime routes. The plugin applies the stationary material surface, heading fonts, and demo portrait to the existing identity/notes composition. The shell correction supersedes milestone 1's retained native window/tab chrome. Discover retains its input/state flow with paper-appropriate text colors inside the book.

## Shared presentation foundation

`IBookAssets`/`BookImage` separate material access from painting. `BookSurface` renders the blank cover or open spread and supplies safe content bounds. Top/bottom construction is preserved; the standalone component gallery can repeat a central paper/binding strip for tall content. The runtime shell instead fixes the natural material aspect and scrolls live page content. Narrow mode crops the appropriate half with immediate identity/notes navigation. General logical-page routing remains milestone 2.

The material painter has no identity, stamp ownership, page count, or collection state. The same material/content regions can display any logical page pair; content and future lazy page routing remain outside the artwork. This milestone does not introduce a fixed array of baked page images or claim a completed arbitrary-page navigator.

Existing `PageFlow`, host scale, measurement/drawing, native actions, and focus behavior remain. `UiContext` optionally supplies two heading fonts. Unsupported decorative glyphs fall back to the current host font as a whole label; this does not establish the host's complete localization coverage.

`PassportAssets` requests embedded images through Dalamud's shared texture provider. The host owns loading/caching and frame-wrap lifetime; no shared wrap is cached or disposed by the plugin. Plugin-owned managed font handles are built once and disposed on normal/partial teardown. Draw callbacks do not read files, decode images, save state, or create fonts. A readable material/portrait fallback remains when resources are unavailable.

## Assets and reproducibility

See [asset provenance and exact prompts](../assets/passport/PROVENANCE.md) and [third-party notices](../THIRD_PARTY_NOTICES.md). Four original generated PNGs are bundled unchanged; no copied game/reference art. Source Serif 4 Regular/Semibold are pinned, unmodified Adobe files with SIL OFL notices included in the release package. Builds require no image-generation service or private source.

Run the standard [native UI checks](../tests/README.md) with an artifact directory. The gallery writes `passport-target-{cover,identity,collection}.png`; complete shell cases write `passport-shell-*.png`; older component views remain BMPs. The CPU renderer supports registered RGBA images and every generated font-atlas texture page. Test-only Windows drawing APIs decode/export images; none enter the plugin package.

## Remaining milestones

1. Refine and review one nation cover, banner/map introduction, portrait/signature versus information identity spread, demo collection spread, and one cover palette variation.
2. Implement closed/open presentation and ordered logical-page navigation with narrow layouts; broader edition/customization controls follow in bounded slices.
3. Add bottom-center closed arrival, explicit cover opening, and close-to-cover-then-slide-down dismissal with focus/input ownership and immediate reduced-motion behavior.
4. Separately prototype one bounded 2.5D turn; evaluate intermediate frames, clipping, perspective, text distortion, interruption, and measured performance. Document any limitations before recommending a renderer change.

CrystalRenderer, full paper physics, custom GPU hooks, elaborate window dragging/resizing, collection persistence, and Studio/Echoes are outside this milestone. CrystalRenderer still requires a separate decision.
