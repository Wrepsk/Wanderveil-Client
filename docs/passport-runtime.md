# Gridania passport in the client

Status: runtime prototype, 19 October 2026. The SVG art, book-surface controls, motion, page turns, section riffles, and in-book Settings were merged into `master` at the user's request on 19 October 2026; live smoothness and GPU appearance are not yet observed. The gallery and `BookShell` share the same `PassportDocumentView` composition.

## Review in game

Build the client, then reload Wanderveil in Dalamud so it loads the new DLL. The Debug developer-plugin DLL is `src/Wanderveil/bin/x64/Debug/Wanderveil.dll`; the Release package is `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`. Compilation does not replace an already loaded plugin instance.

Run `/wanderveil`. Each summon starts with the closed Gridania cover; the first summon of a plugin session centers it on the screen, and later summons return it to where it was last dismissed (or dragged). The book itself is the control surface. Click the right side of the cover to open it. When open, click the right side to turn forward (nation banner/map, then portrait/signature and identity information, then demo stamp pages) and the left side to turn back. Clicking the left side of the first page returns to the cover. Narrow views turn one logical page; wider views turn a spread. Press and drag anywhere on the book that is not a control to move it; a drag never turns a page. A soft shadow on the outer edge and a hand cursor show which way a click will go. On the keyboard, Space or Enter opens the cover, Page Down and Page Up turn pages, and Home returns to the cover.

- [Runtime closed cover](previews/passport/runtime-cover.png)
- [Runtime nation spread](previews/passport/runtime-nation.png)
- [Runtime identity spread](previews/passport/runtime-identity.png)
- [Runtime collection spread](previews/passport/runtime-collection.png)
- [Smallest book size, contents scaled down](previews/passport/runtime-smallest.png)

These are native software previews of the production shell, not live game captures. Real host font/GPU/hotkey/lifecycle behavior remains unobserved here. The map retains its visible unvalidated-geography caption, and identity/signature are synthetic demo content. Stamps are collected in memory only (see below); signature input and all storage are not implemented.

## Vector art

The embedded plates now come from `assets/passport/vector/`, rasterized from SVG by [tools/passport-art](../tools/passport-art/README.md) following the "Eorzea Passport" Claude Design. Canvas sizes are unchanged, so the composition code is shared; cover text and crest were re-centered for the new board and the map's cartouche label was removed. With the design typefaces and reduced-size art copies, the Release package is about 4.6 MB (the image-generated plates made it about 10.9 MB). Gallery checks for the plum colorway still use the earlier image-generated study.

Type also follows the design. Cinzel Bold is used for cover lettering. Cormorant Garamond Bold, SemiBold, and Medium are used for page titles, headings, and body or caption text. Courier Prime Regular is used for the passport number and the identity line. Text that a decorative font cannot draw falls back to the host font. Source Serif 4 is no longer shipped. The [notices](../THIRD_PARTY_NOTICES.md) list the pinned sources and licenses.

## Controls on the book

There are no buttons around the book anymore. The sections (Passport, Discover, Echoes, Studio) are paper index tabs tucked behind the pages. The selected tab carries a small edition-colored ribbon at its right end that peeks just below the tab onto the book; it unrolls after the pages appear and rerolls before moving to a newly selected tab. Settings, smaller, larger, and close (`- + x`) are printed in the paper's lower margin, with page numbers (folios) between them. On the closed cover only `- + x` are foil-stamped beneath the lower arc; there is no separate settings window anymore. **x**, Escape, and `/wanderveil` play the dismissal sequence below. Pages turn with motion; see [page turns](#page-turns-milestone-4-experiment).

## Motion

Summoning slides the closed passport up from below the screen to its place (the screen center the first time, then where it was dismissed); it waits closed until you click its right side (or press Space/Enter). The cover then swings open on its spine with perspective, its artwork, crest, and lettering turning with it, while the book glides so the spread ends centered; the cover's inside lands as the first left page. Pages, tabs, controls, and the tab ribbon fade in once it lands. Dismissing from an open book closes the cover first, quickly (0.25 s rather than the 0.6 s of closing back to the cover), then slides the closed book down off the screen and hides the window; a closed book slides down directly. Clicking the first page's left side or pressing Home closes back to the cover without dismissing.

Any request can interrupt a motion and reverse it from where it is, for example summoning again while the book slides away. The book takes no clicks, drags, or page keys while it moves. The **Reduced motion** setting makes every step instant with the same results, including explicit opening. In a single-page layout the cover swings away to the left and fades, since there is no left page for it to land on.

`BookShellMotion` sequences the motion without ImGui, using the shared motion tokens in `UiMotion` (slide 0.42 s, cover 0.6 s, dismissal close 0.25 s, page reveal 0.18 s, page turn 0.5 s, riffle leaf 0.34 s with 0.07 s stagger, ribbon unroll 0.22 s and roll-up 0.12 s, one easing curve, and a 0.1 s longest frame); `BookShellPlacement` holds the resting place and slide offset; `LeafProjection` draws a leaf turning on a hinge as textured strips and per-glyph quads. The host window applies the slide offset and keeps the bottom center anchored while the book changes between closed and open sizes; the closed cover is sized to be exactly the leaf that swings open.

- [Cover swinging open](previews/passport/motion-opening-early.png)
- [Inside landing as the left page](previews/passport/motion-opening-late.png)
- [Closing back to the cover](previews/passport/motion-closing.png)

The headless preview window is pinned at the top left rather than anchored at the bottom center, so the book's glide toward center is offset in these frames. Frame pacing, smoothness, and GPU appearance in game are not yet observed.

## Changing sections and Settings

Choosing another tab riffles a quick burst of pages, like an old flipbook settling on a page: five leaves for a neighbouring section, up to nine for a longer jump, each turning in 0.34 s and starting 0.07 s after the previous one. A later section flips right-to-left, an earlier one left-to-right, the same way as turning pages by their sides. The first leaf carries the old spread away and the last brings the new one in; the leaves between are blank, and pages not yet covered or already revealed stay live underneath. Tabs and page clicks are ignored while a riffle runs, and reduced motion switches instantly. The Passport section keeps its reading position while you visit other sections.

Settings are pages of the book. The printed Settings mark riffles forward to them; the left page has a **Back to …** link that riffles back to the section and passport spread you came from, and the right page holds the options. **Reduce motion** is a printed checkbox that saves immediately; if saving fails, a **Try saving again** link appears. Dalamud's settings button and `/wanderveil settings` open the passport straight onto these pages, opening the cover if it was closed. The tab ribbon rolls up while Settings are shown, since they have no tab.

- [A riffle in flight](previews/passport/riffle.png)
- [Settings pages](previews/passport/settings.png)

## Collecting a stamp

The first product loop runs in the book: open Discover, open **The scenic route** postcard, and choose **Collect demo stamp** beneath it. The stamp is issued at once: the postcard's faint proof of the design becomes the inked stamp (*Your stamp*, *Collected 19 OCT 2026 / Local demo*), its card in the list says *Collected*, and a line beneath says whether it is saved to your passport on this PC. Keyboard focus moves to **See it in your passport »**, which riffles back to the passport's collection spread, where the stamp is pressed in the first of three places with its name and date. Empty places are faint dashed rings marked *Awaiting a stamp*; with no stamps the right page invites you to collect one and offers **Explore discoveries »**. A count of local demo stamps (*saved on this PC*, or *not saved*) closes the spread, and stamps past the third are counted rather than shown until stamp paging exists.

Duplicate policy: a passport holds at most one stamp per discovery. Once collected, the postcard offers no Collect action, and collecting again (even after its design is revised) returns the stamp already held unchanged. **Where the lanterns wait** is a stamp spot: its stamp is collected at the place itself, which needs location awareness Wanderveil does not have, so its Collect action is printed but inert with that reason.

`StampDesign` (identifier, revision, name, artwork, provenance), `StampInstance` (its own identifier, the design revision it was issued with, the discovery, the date, provenance), and `StampCollection` (the in-memory stamps and the collect rule) hold no ImGui state; `DiscoveryPreview` names the design a discovery awards and whether reading it is enough to collect. `StampArt` draws a design as a faint proof or an issued stamp, falling back to the procedural motif without art. Everything is local demo provenance.

- [Collected postcard](previews/passport/collect-discover.png)
- [The stamp on the collection spread](previews/passport/collect-passport.png)
- [The stamp on a single page](previews/passport/collect-narrow.png)
- [More stamps than places, with a long name](previews/passport/collect-full.png)
- [An empty collection spread](previews/passport/runtime-collection.png)

## Saving stamps

Collected stamps are saved on this PC and come back when the plugin reloads or the game restarts. They live in Dalamud's configuration folder for Wanderveil (`pluginConfigs/Wanderveil/` under the XIVLauncher folder) as `stamps-local.json`. There is one local passport for now; files are named by profile so character-scoped passports can follow without a format change. Nothing leaves the PC.

The file is versioned JSON, written readable:

```json
{
  "format": "wanderveil.passport-stamps",
  "version": 1,
  "stamps": [
    {
      "id": "local-demo-0001",
      "discoveryId": "demo-scenic-route",
      "collectedAt": "2026-10-19T18:30:00.0000000+02:00",
      "provenance": "local-demo",
      "design": { "id": "demo-scenic-route-stamp", "revision": 1, "name": "Scenic route", "artwork": 0, "provenance": "local-demo" }
    }
  ]
}
```

Each stamp keeps a copy of the design revision it was issued with, so a later design change never alters it. Only `local-demo` provenance is accepted, so a hand-edited file cannot add official or online stamps.

- **Saving.** Each collect saves a snapshot on a background task, never in a draw callback. Saves run one at a time, and if several are waiting only the newest is written. The file is written to `stamps-local.json.tmp`, flushed to disk, then swapped in one step, and the previous save is kept as `stamps-local.json.bak`. Closing the plugin waits up to three seconds for a save in progress.
- **Interrupted save.** On load, a complete leftover `.tmp` file is the newest save, so the swap is finished. An incomplete one is dropped, since the file it was replacing is intact.
- **Unreadable file.** A file that is corrupt, truncated, empty, not a Wanderveil stamp file, or breaks the rules (duplicate stamps or discoveries, missing fields, a bad date or revision, other provenance) is never partly loaded. It is renamed aside as `stamps-local.json.unreadable-<time>`, and the backup is restored (one stamp may be missing: the backup is the save before last). If the backup is unreadable too, both are kept aside and the passport starts empty. The collection page says what happened.
- **Newer or locked file.** A file from a newer Wanderveil version, or one that cannot be opened (locked or access denied), is left untouched, and saving is turned off for the session. The page says so; stamps collected meanwhile last until the game closes.
- **Failed save.** If writing fails, the stamp stays collected, the line under it says it could not be saved, and **Try saving again** retries. The error goes to the Dalamud log.
- **Migrations.** A future format bumps `version` and adds a step that upgrades the older file in memory when it is read. The next save writes the new version, and the backup keeps the old one. Version 1 is the first, so there are no steps yet. Local demo stamps are never promoted to server-issued stamps by a migration.

`StampStore` (file format, atomic save, recovery) and `StampPersistence` (load at startup, background saves, state) live in `Infrastructure/Storage/`. The pages see only `IStampStorage`: what loading found and whether saving works.

## Echoes

The Echoes tab is an honest preview of future adventures. The left page says what Echoes will be and that it cannot be played yet, then shows a framed sample dossier (*The Lantern Keeper's Letter*, a synthetic three-chapter mystery) labelled *Preview / not playable / nothing is tracked*. The right page lists its chapters: the first is open, marked with an inked ring, with its objectives as hollow printed diamonds (never checkboxes) and one hint; the later chapters carry a wax seal, show only their title and when they would open, and reveal no objectives. A closing line says playable adventures, saved progress, and rewards arrive later. The pages have no controls, so clicking them does nothing; on a single page the chapters follow the dossier.

`EchoPreview` holds the sample and validates that a dossier opens on its first chapter and that sealed chapters carry no objectives; it has no ImGui or host state and is not a content contract. `EchoesView` draws it, with a readable fallback when no dossier is given.

- [Echoes spread](previews/passport/echoes.png)
- [Echoes on a single page](previews/passport/echoes-narrow.png)

## Studio

The Studio tab opens on a template chooser. The left page introduces Studio and says plainly that drafts stay in memory until the plugin unloads and nothing is saved, published, or sent. The right page lists **Postcard** and **Stamp Spot** with a **Start …** link each, then Riddle, Challenge, and Trail under *Coming later* as plain text that cannot be chosen.

Starting a template turns the spread into a draft: printed fields on the left (each a native one-line text input on a ruled line, in the page's own type) and a framed preview card on the right that updates as you type. The cursor starts in the first field; Tab moves between fields. Escape inside a field only leaves it and keeps the text; with no field active it closes the passport as usual. Fields are one line, stripped of control characters, and limited by user-perceived characters (the label shows how many are left near the limit). A status line names the required fields still empty. **Save draft** and **Publish** are printed but inert, with the reason beneath them, so draft, saved, and published stay distinct; the preview is marked *Draft / not saved / not published* and its stamp *a design preview, not an issued stamp*. **« All templates** returns to the chooser and keeps each template's draft (its link becomes **Continue your draft**); visiting other sections keeps the open draft too. A postcard has no picture yet: a placeholder says uploads come later. On a single page the preview follows the fields.

`StudioTemplate` (catalog and fields) and `StudioDraft` (values, limits, missing fields, and the unavailable save/publish states) hold no ImGui or host state; `StudioView` draws them, and `BookControl.Field` is the printed input. Switching between the chooser and a draft is immediate rather than a page turn.

- [Template chooser](previews/passport/studio-templates.png)
- [Postcard draft and preview](previews/passport/studio-postcard.png)
- [Stamp spot draft with a missing field](previews/passport/studio-stamp-spot.png)
- [Postcard draft on a single page](previews/passport/studio-narrow.png)

## Input ownership

The passport's window is a transparent rectangle larger than the book (room for the tabs, the plate's shadow, and the closed cover's margin). Only the parts the book actually draws take the mouse: the board of the open book (`BookSurface.Board`, the plate's opaque area), each tab with its ribbon, the closed cover's board, and, while the cover swings, the whole window. Anywhere else the window sets `NoMouseInputs`, so clicks, scrolls, and hovering pass through to the game. A press, drag, or edit that began on the book keeps the mouse until it ends, so dragging the book past its edge keeps moving it, and while a Studio field is active the first click off the book only leaves the field. These regions are rectangles, not per-pixel shapes, and they are decided from the previous frame, so there is one frame of lag as the mouse crosses an edge.

While a Studio field is active ImGui reports that it wants text input, which is what the host uses to keep typed keys from the game; leaving the field hands them back. The book's own keys (Escape, Page Up/Down, Home, Space/Enter on the cover) never act while a field is active. Whether the game still receives movement keys while the book merely has focus (no field active) depends on Dalamud and is not yet observed in game.

## Empty, missing, long, and Japanese content

Each section explains its own empty state on its pages: Discover says the sample journal is empty, Echoes says there is no sample dossier and that chapters appear once there is one, and Studio says no template can be started when none is available (later templates are still listed). Nothing in these states is fetched. `BookShellContent` carries the discoveries, dossier, and templates into the shell, so these states are exercised in the real book.

While artwork loads, or if a plate cannot load, the book draws itself instead: an edition-cloth cover board with a foil frame and the usual foil lettering, and an open book with cloth board, paper, and a soft gutter. Turning pages and the cover swing keep plain paper, and pages that need art say *Artwork is loading or unavailable*. Previously a missing cover was a flat beige rectangle.

Long text wraps inside its paper, and words too long for a line are broken anywhere (ImGui's rule). A long holder name signs smaller, down to half size, and then wraps rather than being cut off. Very long pages scroll inside their paper. Studio fields hold each field's limit; their single line scrolls sideways while typing, and the preview wraps.

Text the decorative faces cannot draw, such as Japanese, falls back to the host font; in game that is Dalamud's default font, whose coverage depends on the client's language (Japanese is expected, Korean and Chinese are not verified). The headless checks stand in local Yu Gothic for it. Japanese lines break anywhere rather than by Japanese line-breaking rules, and interface labels themselves are English only; there is no localization yet.

- [Cover without artwork](previews/passport/states-missing-art-cover.png)
- [A page turning without artwork](previews/passport/states-missing-art-turn.png)
- [Long identity text and signature](previews/passport/states-long-identity.png)
- [Japanese identity](previews/passport/states-japanese-identity.png)
- [Japanese Studio draft](previews/passport/states-japanese-studio-draft.png)
- [Echoes with no dossier](previews/passport/states-empty-echoes.png)

## Page turns (Milestone 4 experiment)

Clicking a page side (or Page Up/Down) turns the page as a paper leaf rotating about the spine in 0.5 s. It curls as it goes: its outer edge trails the motion, most while it stands upright, so the page bows into an arc and lies flat again as it lands. Perspective, light that varies across the curve, and a shadow on the paper it hangs over complete it. The leaf carries its real page content: the front shows the page being turned, the back the page it lands as, including text, portrait, stamps, and the nation banner. The two pages that stay put remain live and readable throughout. On a single page, the page swings away to the left about its outer edge and fades, revealing the next page beneath it. Clicking the same way again lands the current turn and starts the next; clicking the other way reverses the turn from where the leaf is. Reduced motion turns instantly.

How it works: ImGui draws child windows above their parent and cannot transform a child's contents, so the turning leaf gets its own child window, created after the static pages so it draws above them. Its paper is drawn as perspective strips; its page content is composed flat at the page's resting place exactly as it would be on the page, then every vertex it produced is moved through the same curled hinge projection (`LeafProjection.Warp`) and its clip rectangles are widened. While on a turning leaf, large art (portrait, stamps, map, banner) and rules are drawn in 16 vertical strips (`BookSurface.Strips`) so they bend with the paper; glyphs are already small. The leaf's child window uses the same padding and scrollbar reservation as a resting page, so content wraps identically and does not shift as a turn starts or lands. No render-to-texture, renderer dependency, or GPU hook is involved.

Measured in the headless checks: about 0.12 ms of CPU and 3,272 vertices to build a turning spread frame, against 0.04 ms and 1,544 for a still spread. GPU cost and frame pacing in game are not measured.

Limitations of this approach:

- The curl is a single circular arc across the page's width; real paper also bends diagonally from a corner. That would need a 2D tessellation of the page rather than vertical strips.
- The warp is perspective-correct at each vertex but straight within each triangle. Glyphs, paper strips, and stripped art are small enough that this is not visible at normal sizes.
- Content on the leaf is not interactive while it moves, and it shows the page from its top: a scrolled page (only possible with enlarged host text) is drawn unscrolled during the turn.
- While a leaf is in motion its geometry may draw a few pixels beyond the book's window, because perspective enlarges the lifted edge; nothing is clipped at the window edge.

Recommendation: this ordinary-ImGui approach is good enough for the passport's page turns, so CrystalRenderer is not needed for them. Revisit a renderer only if curled pages or real lighting become a goal.

- [Identity page lifting with its content](previews/passport/turn-lifting.png)
- [Page curled while upright](previews/passport/turn-curl.png)
- [Stamp page landing on the left](previews/passport/turn-landing.png)
- [Single page swinging away](previews/passport/turn-single.png)

The first page is a full-height nation pennant hung from the top edge of the paper down to the lower margin, with the crest and foil lettering drawn live over the generated banner plate.

Artwork is drawn from the generated size copy closest to its on-screen width (full, half, quarter, or eighth), so plates are never shrunk more than about 2x and their edges stay smooth instead of aliasing. While a newly needed copy loads, the nearest loaded copy draws.

## Text rendering

Each font is built at every pixel size that is actually drawn (rounded to whole pixels, at most 48 sizes), instead of one 24 px font stretched to all sizes, so text stays sharp. A new size draws once at the scaled base font while the atlas rebuilds. Cover lettering takes its sizes, positions, and letter-spacing from the design board, and page contents (text, spacing, artwork) scale with the page width, so a smaller book shows smaller contents instead of scrolling. If a page still would not fit, it shrinks itself to its paper within a couple of frames and grows back when the book is enlarged. Enlarged host text keeps its size and may scroll instead. The headless checks use fixed-size fonts, so their previews are slightly softer than in game.

## Presentation and assets

`PassportDocumentView` owns reusable live cover/page composition. `PassportPage` records supply stable IDs and presentation kinds; `BookShell` snapshots and validates a nonempty catalog with unique IDs, tracks closed/open state and reading position, and renders only visible pages. The demo provides six logical pages; catalog size is not fixed to six. Navigation clamps safely and preserves child scrolling through stable page IDs. New sessions start closed at the first page.

The Gridania cover, banner, map, crest, and open-book plates, plus the generic cover, portrait placeholder, and stamp atlas, are embedded from `assets/passport/vector/` with their reduced copies; see [vector asset provenance](../assets/passport/vector/PROVENANCE.md). The earlier image-generated plates and Gridania studies stay in the repository for comparison and gallery checks but are not embedded. [Their provenance](../assets/passport/studies/gridania/PROVENANCE.md) still accompanies the package.

The demo signature uses bundled **Caveat**, replacing the gallery's Windows-only Segoe Script surrogate. The unmodified variable font is pinned to Google Fonts revision `5571d84c0d8c70ec1af4f64072d8c5cf1e4e9643`, renamed only from `Caveat[wght].ttf` to `Caveat.ttf`; its SIL OFL notice ships with the plugin. Six base font handles plus up to 48 per-size handles are owned and disposed, including partial-startup/lock failures; shared texture wraps retain host ownership. Draw callbacks perform no file/network access.

## Verification

The native suite exercises closed-cover pointer/keyboard opening, ordered page navigation at wide/narrow sizes, final-page bounds, backward navigation, return-to-cover focus, session reset, expanded/invalid catalogs, style isolation, and existing Discover flow. It also opens the actual built plugin assembly and verifies the selected images/font exist as manifest resources and match preview asset hashes. This directly checks the packaging boundary that caused the previous preview/runtime mismatch.
