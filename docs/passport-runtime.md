# Gridania passport in the client

Status: static runtime prototype, 18 October 2026. The earlier Gridania gallery was not connected to the installed plugin. This slice embeds the selected artwork and uses the same `PassportDocumentView` composition in the gallery and `BookShell`.

## Review in game

Build the client, then reload Wanderveil in Dalamud so it loads the new DLL. The Debug developer-plugin DLL is `src/Wanderveil/bin/x64/Debug/Wanderveil.dll`; the Release package is `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`. Compilation does not replace an already loaded plugin instance.

Run `/wanderveil`. Each summon starts with the closed Gridania cover anchored at the bottom center of the main viewport. Select **Open**, then use **< / >** to navigate nation banner/map → portrait/signature and identity information → demo stamp pages. Narrow views advance one logical page; wider views advance a spread. **Cover** returns to the closed book. Settings, size controls, dragging, Discover, and its focused Back behavior remain available. **x** and the host's Escape handling dismiss immediately. Sliding, cover opening/closing motion, and page-turn animations remain separate milestones; this prototype uses immediate transitions.

- [Runtime closed cover](previews/passport/runtime-cover.png)
- [Runtime nation spread](previews/passport/runtime-nation.png)
- [Runtime identity spread](previews/passport/runtime-identity.png)
- [Runtime collection spread](previews/passport/runtime-collection.png)

These are native software previews of the production shell, not live game captures. Real host font/GPU/hotkey/lifecycle behavior remains unobserved here. The map retains its visible unvalidated-geography caption, and identity/signature/stamps are synthetic demo content. No collection or signature input/storage is implemented.

## Vector art (branch `feat/svg-passport-assets`)

The embedded plates now come from `assets/passport/vector/`, rasterized from SVG by [tools/passport-art](../tools/passport-art/README.md) following the "Eorzea Passport" Claude Design. Canvas sizes are unchanged, so the composition code is shared; cover text and crest were re-centered for the new board, the map's cartouche label was removed, and button leather samples a plain cover band. The Release package dropped from about 10.9 MB to 2.8 MB. Gallery checks for the plum colorway still use the earlier image-generated study.

## Presentation and assets

`PassportDocumentView` owns reusable live cover/page composition. `PassportPage` records supply stable IDs and presentation kinds; `BookShell` snapshots and validates a nonempty catalog with unique IDs, tracks closed/open state and reading position, and renders only visible pages. The demo provides six logical pages; catalog size is not fixed to six. Navigation clamps safely and preserves child scrolling through stable page IDs. New sessions start closed at the first page.

The selected forest-green cover, banner, overhead map study, and crest are explicitly embedded from `assets/passport/studies/gridania/`, alongside the existing base book/portrait/stamp assets. Plum/intermediate studies and test/gallery sources remain excluded. [Asset provenance](../assets/passport/studies/gridania/PROVENANCE.md) and generation prompts accompany the package. No new generated artwork was needed for this slice.

The demo signature uses bundled **Caveat**, replacing the gallery's Windows-only Segoe Script surrogate. The unmodified variable font is pinned to Google Fonts revision `5571d84c0d8c70ec1af4f64072d8c5cf1e4e9643`, renamed only from `Caveat[wght].ttf` to `Caveat.ttf`; its SIL OFL notice ships with the plugin. Three managed font handles are owned and disposed, including partial-startup/lock failures; shared texture wraps retain host ownership. Draw callbacks perform no file/network access.

## Verification

The native suite exercises closed-cover pointer/keyboard opening, ordered page navigation at wide/narrow sizes, final-page bounds, backward navigation, return-to-cover focus, session reset, expanded/invalid catalogs, style isolation, and existing Discover flow. It also opens the actual built plugin assembly and verifies the selected images/font exist as manifest resources and match preview asset hashes. This directly checks the packaging boundary that caused the previous preview/runtime mismatch.
