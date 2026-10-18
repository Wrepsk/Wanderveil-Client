# Gridania passport in the client

Status: static runtime prototype, 18 October 2026. The earlier Gridania gallery was not connected to the installed plugin. This slice embeds the selected artwork and uses the same `PassportDocumentView` composition in the gallery and `BookShell`.

## Review in game

Build the client, then reload Wanderveil in Dalamud so it loads the new DLL. The Debug developer-plugin DLL is `src/Wanderveil/bin/x64/Debug/Wanderveil.dll`; the Release package is `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`. Compilation does not replace an already loaded plugin instance.

Run `/wanderveil`. Each summon starts with the closed Gridania cover anchored at the bottom center of the main viewport. The book itself is the control surface. Click the right side of the cover to open it. When open, click the right side to turn forward (nation banner/map, then portrait/signature and identity information, then demo stamp pages) and the left side to turn back. Clicking the left side of the first page returns to the cover. Narrow views turn one logical page; wider views turn a spread. Press and drag anywhere on the book that is not a control to move it; a drag never turns a page. A soft shadow on the outer edge and a hand cursor show which way a click will go. On the keyboard, Space or Enter opens the cover, Page Down and Page Up turn pages, and Home returns to the cover.

- [Runtime closed cover](previews/passport/runtime-cover.png)
- [Runtime nation spread](previews/passport/runtime-nation.png)
- [Runtime identity spread](previews/passport/runtime-identity.png)
- [Runtime collection spread](previews/passport/runtime-collection.png)
- [Smallest book size, contents scaled down](previews/passport/runtime-smallest.png)

These are native software previews of the production shell, not live game captures. Real host font/GPU/hotkey/lifecycle behavior remains unobserved here. The map retains its visible unvalidated-geography caption, and identity/signature/stamps are synthetic demo content. No collection or signature input/storage is implemented.

## Vector art (branch `feat/svg-passport-assets`)

The embedded plates now come from `assets/passport/vector/`, rasterized from SVG by [tools/passport-art](../tools/passport-art/README.md) following the "Eorzea Passport" Claude Design. Canvas sizes are unchanged, so the composition code is shared; cover text and crest were re-centered for the new board and the map's cartouche label was removed. With the design typefaces and reduced-size art copies, the Release package is about 4.6 MB (the image-generated plates made it about 10.9 MB). Gallery checks for the plum colorway still use the earlier image-generated study.

Type also follows the design. Cinzel Bold is used for cover lettering. Cormorant Garamond Bold, SemiBold, and Medium are used for page titles, headings, and body or caption text. Courier Prime Regular is used for the passport number and the identity line. Text that a decorative font cannot draw falls back to the host font. Source Serif 4 is no longer shipped. The [notices](../THIRD_PARTY_NOTICES.md) list the pinned sources and licenses.

## Controls on the book

There are no buttons around the book anymore. The sections (Passport, Discover, Echoes, Studio) are paper index tabs tucked behind the pages. The selected tab carries a small edition-colored ribbon at its right end that peeks just below the tab onto the book; `BookControl.Ribbon` takes an extension amount (0-1) so its entrance and exit can be animated later. Settings, smaller, larger, and close (`- + x`) are printed in the paper's lower margin, with page numbers (folios) between them. On the closed cover they are foil-stamped beneath the lower arc. **x** and the host's Escape handling dismiss immediately. Sliding, cover opening/closing motion, and page-turn animations remain separate milestones; turns are immediate.

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
