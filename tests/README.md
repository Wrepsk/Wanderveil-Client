# Automated checks

`Wanderveil.UiChecks` is a package-free console check project included in `Wanderveil.slnx`. It compiles the same shared UI, Passport, and Discover sources as the plugin and uses the installed Dalamud ImGui managed/native libraries. No FFXIV process or backend is required. The prerequisite is Windows/.NET 10 plus development assemblies, using the standard path or `DALAMUD_HOME` as documented in the [development guide](../docs/development.md).

```powershell
dotnet restore Wanderveil.slnx --locked-mode
dotnet build Wanderveil.slnx -c Release --no-restore
dotnet run --project tests/Wanderveil.UiChecks -c Release -p:Platform=x64 --no-build --no-restore -- artifacts/ui
```

The final directory argument is optional. With it, the test-only CPU rasterizer writes BMP previews, three Passport target PNGs, and complete-shell PNGs under the ignored artifact directory. Shell previews use production window flags/preparation against an authored backdrop to expose transparency and the actual host composition. Native libraries/test code never enter the plugin package. Material checks use Windows Desktop drawing APIs to decode/export PNGs and local Segoe UI as a body-font surrogate; that font is not redistributed. Assets/heading fonts come from the same files embedded in the plugin.

All 1076 checks pass locally in both Debug and Release. Coverage includes:

- Page geometry across 45 width/scale/text-size combinations, invalid geometry, and spread/stack transitions.
- Paper body/caption and button-state label contrast.
- Real ImGui frames for wide, narrow, large-text, and long-content Passport cases with no horizontal overflow.
- Mouse release and keyboard activation, style-stack restoration, wrapped-title measurement, and cursor-height reservation.
- Discover card/detail geometry across the same width/scale matrix, including large-text stacking.
- Native keyboard card selection and focused Back activation, mouse release/cancel, and style restoration.
- Wide/narrow/enlarged/long-content discovery previews, empty list and missing-image details, and scrolling to the end of long details without horizontal overflow.
- Disabled collection, long-button wrapping, short labels at fractional font sizes, and opening a scrolled card then restoring browse position through Back.
- Stationary book content bounds for spreads/page halves and tall measured content; invalid geometry rejection.
- Asset-backed/fallback Passport frames, heading font stack/fallback, and keyboard Explore activation after long-page scrolling.
- Cover/identity/demo collection visual targets with actual material/portrait/stamp textures and multiple font-atlas pages. These remain presentation specimens, not runtime collection routes.
- Complete borderless shell at wide/narrow/compact/enlarged-text/doubled-host-scale sizes: viewport fitting and rounded breakpoints, stationary host through size changes, internal page scrolling, identity/notes navigation, bookmark press/release/cancel, native directional/Space activation, Discover detail/Back inside a page, attached settings/close/size actions, outer-leather drag intent, missing-material fallback, and style cleanup.

This is an executable check suite: failures exit nonzero; use `dotnet run` as above rather than expecting a `dotnet test` adapter. CI runs it for both build configurations and uploads previews. It does not prove game-host lifecycle, localization glyph coverage, GPU output, or performance. Routine commits use appropriate automated evidence and preview review without requiring user in-game testing.

Add domain/persistence/authorization/lifecycle tests as those behaviors arrive. Keep [fixtures](fixtures/README.md) synthetic and deterministic; avoid tests that merely restate constants or folder existence.
