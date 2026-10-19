# Automated checks

`Wanderveil.UiChecks` is a package-free console check project included in `Wanderveil.slnx`. It compiles the same shared UI, Passport, Discover, Studio, and Echoes sources as the plugin and uses the installed Dalamud ImGui managed/native libraries. No FFXIV process or backend is required. The prerequisite is Windows/.NET 10 plus development assemblies, using the standard path or `DALAMUD_HOME` as documented in the [development guide](../docs/development.md).

```powershell
dotnet restore Wanderveil.slnx --locked-mode
dotnet build Wanderveil.slnx -c Release --no-restore
dotnet run --project tests/Wanderveil.UiChecks -c Release -p:Platform=x64 --no-build --no-restore -- artifacts/ui
```

The final directory argument is optional. With it, the test-only CPU rasterizer writes BMP previews, three Passport target PNGs, and complete-shell PNGs under the ignored artifact directory. Shell previews use production window flags/preparation against an authored backdrop to expose transparency and the actual host composition. Native libraries/test code never enter the plugin package. Material checks use Windows Desktop drawing APIs to decode/export PNGs and local Segoe UI as a body-font surrogate; that font is not redistributed. Assets/heading fonts come from the same files embedded in the plugin.

All checks pass locally: 4252 in Debug and 4378 in Release when writing previews (preview capture adds frames that are themselves checked). Coverage includes:

- Page geometry across 45 width/scale/text-size combinations, invalid geometry, and spread/stack transitions.
- Paper body/caption and button-state label contrast.
- Real ImGui frames for wide, narrow, large-text, and long-content Passport cases with no horizontal overflow.
- Mouse release and keyboard activation, style-stack restoration, wrapped-title measurement, and cursor-height reservation.
- Discover card/detail geometry across the same width/scale matrix, including large-text stacking.
- Native keyboard card selection and focused Back activation, mouse release/cancel, and style restoration.
- Wide/narrow/enlarged/long-content discovery previews, empty list and missing-image details, and scrolling to the end of long details without horizontal overflow.
- Collecting: one stamp per discovery even after its design is revised, separate design and instance identifiers, demo provenance and issue date, invalid discoveries or designs refused, and collection order. In the real shell at wide and narrow sizes: opening the postcard, collecting from its page, following its link to the collection spread, the stamp printed there without overflow, no second collect, an inert stamp spot, and five stamps on a three-place spread. A failed save says so under the stamp and its **Try saving again** link retries.
- Stamp pages: added in pairs once the collection spread is full, always with an empty place; turning to them and not past them; a fourth stamp's link opening its stamp page; 500 stamps on 132 pages with End and Page Up, no overflow, and the last spread costing no more per frame than the first (both printed).
- Stamp storage, each case in its own temporary directory: round trip with date offset and design, identifiers continuing after reload, no duplicate after reload, the previous save kept as backup; corrupt, truncated, empty, and foreign files recovered from the backup with the damaged file kept byte for byte; both files unreadable; a newer version and a locked file left untouched with saving off; complete and incomplete interrupted saves; rule-breaking files rejected whole; separate profiles and unsafe profile names; a failed save reported and retried; no saving after disposal; and 500 stamps (size and save/load time are printed).
- Disabled actions, long-button wrapping, short labels at fractional font sizes, and opening a scrolled card then restoring browse position through Back.
- Stationary book content bounds for spreads/page halves and tall measured content; invalid geometry rejection.
- Asset-backed/fallback Passport frames, heading font stack/fallback, and keyboard Explore activation after long-page scrolling.
- Cover/identity/demo collection visual targets with actual material/portrait/stamp textures and multiple font-atlas pages. These remain presentation specimens, not runtime collection routes.
- Complete borderless shell at wide/narrow/compact/enlarged-text/doubled-host-scale sizes: viewport fitting and rounded breakpoints, stationary host through size changes, internal page scrolling, identity/notes navigation, bookmark press/release/cancel, native directional/Space activation, Discover detail/Back inside a page, attached settings/close/size actions, outer-leather drag intent, missing-material fallback, and style cleanup.
- Gridania cover/colorway and nation/identity/collection targets, default page fit, illustration aspect/bounds, narrow pages, font stack cleanup, and enlarged/long information scrolling. Gallery and runtime share composition; selected images and Caveat are bundled, while unused studies/gallery code remain excluded.
- Studio templates and drafts: Postcard and Stamp Spot lead and later templates cannot start a draft; one-line cleaning, character limits that never split an emoji, missing required fields, and save/publish reported unavailable. In the real shell at wide, narrow, and enlarged-text sizes: the Studio tab, starting a postcard from its printed link, typing into the focused first field, Tab between fields, Escape leaving a field without closing the passport or erasing its text, spreads fitting their paper, drafts kept across templates and sections, and Escape closing the passport once no field is active.
- Echoes: the sample dossier opens on its first chapter and sealed chapters reveal no objectives, invalid dossiers are refused, the real shell's Echoes tab fits its spread at wide/narrow/enlarged-text sizes, clicking its pages acts on nothing, and the no-dossier fallback draws readable text.
- States across the real shell (cover, every passport spread, Discover, Echoes, Studio, and a Studio draft): empty content, missing artwork (including the cover swing and a page turn on plain paper), long German text at wide and narrow sizes, and Japanese text with a local Yu Gothic stand-in for the host font. Each frame checks that the host stays still and no page overflows sideways; Japanese checks that every character resolves to a font with its glyph.
- Input ownership: with the same per-frame flags as the plugin window, the margins beside the closed cover and beside the open book's tabs let the mouse through (ImGui does not capture it and clicks do nothing), the cover, tabs, and pages capture it, a drag keeps it past the book's edge until release, and an active Studio field asks for text input until Escape hands it back.
- Layout stability: at book sizes from 640 to 1600 px wide in 6 px steps, filled postcard and stamp spot drafts, Echoes, and Discover settle within a few frames; no page alternates between overflowing and fitting (it did at two sizes before the fix).
- Actual built-plugin manifest resource presence and hash equality for the selected Gridania images/font; native cover opening, ordered wide/narrow page navigation, final-page bounds, backward navigation, return-to-cover keyboard focus, new-session reset, expanded catalogs, and invalid/duplicate IDs.

This is an executable check suite: failures exit nonzero; use `dotnet run` as above rather than expecting a `dotnet test` adapter. CI runs it for both build configurations and uploads previews. It does not prove game-host lifecycle, localization glyph coverage, GPU output, or performance. Routine commits use appropriate automated evidence and preview review without requiring user in-game testing.

Add domain/persistence/authorization/lifecycle tests as those behaviors arrive. Keep [fixtures](fixtures/README.md) synthetic and deterministic; avoid tests that merely restate constants or folder existence.
