# Wanderveil Client

**Every place has a story. Leave yours.**

A planned FFXIV Dalamud plugin for location-based discoveries, a collectible adventurer passport, and creator-authored adventures. The first product slice is fully offline: view a curated postcard, collect a demo stamp, and find it in the passport.

## Development status

The shell opens to a synthetic Passport identity spread: parchment pages, a portrait placeholder, identity fields, notes, and an empty stamp state. Discover offers two curated demo cards with postcard/details, stamp previews, Back navigation, and missing-art fallback. Cards and detail pages stack when space or text size requires it. Collecting the postcard's demo stamp presses it onto the passport's collection pages, one per discovery; it is kept in memory only, so nothing is saved yet. Studio offers a template chooser (Postcard and Stamp Spot) and a draft editor with a live framed preview; drafts stay in memory and saving/publishing are visibly unavailable. Echoes shows a clearly labelled, non-playable sample adventure dossier with chapters and objectives. Shared UI components, appearance settings, and original procedural artwork are implemented; persistence, networking, and advanced rendering remain planned.

## Build the current baseline

Install a compatible .NET 10 SDK and Dalamud development assemblies. The existing project uses `Dalamud.NET.Sdk/15.0.0`; its lock file targets `net10.0-windows7.0` and CI selects .NET 10. See the [development guide](docs/development.md) for paths and lifecycle checks.

```powershell
dotnet restore Wanderveil.slnx --locked-mode
dotnet build Wanderveil.slnx -c Debug --no-restore
dotnet build Wanderveil.slnx -c Release --no-restore
dotnet run --project tests/Wanderveil.UiChecks -c Release -p:Platform=x64 --no-build --no-restore -- artifacts/ui
```

Debug output: `src/Wanderveil/bin/x64/Debug/Wanderveil.dll`. Release package: `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`. `/wanderveil` toggles the passport; `/wanderveil settings` (and Dalamud's settings button) open it at its own Settings pages. Reduce motion is set there and saves through Dalamud configuration; it makes every animation instant.

Passport is a borderless physical book: a Gridania cover and nation/identity/demo-stamp pages whose art is generated from SVG by [tools/passport-art](tools/passport-art/README.md), with Cinzel, Cormorant Garamond, and Courier Prime type. Click its sides to open and turn pages, and drag it from anywhere; see [runtime controls](docs/passport-runtime.md). It opens, turns pages, and moves between sections with motion (instant with Reduced motion).

Native ImGui checks cover responsive geometry, contrast, text reservation, mouse/keyboard navigation, the collect loop and its duplicate policy, scroll restoration, style cleanup, textured/fallback book layouts, and heading-font fallback. Optional software previews are written under ignored `artifacts/ui/`. These checks run outside FFXIV; live host/fonts/GPU behavior and hosted CI execution are not claimed. Completed slices are committed after appropriate automated verification without requiring routine user in-game testing.

## Repository map

| Path | Role |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Contributor/agent working agreement |
| [docs](docs/README.md) | Standalone development, architecture, UI, and protocol notes |
| `Wanderveil.slnx` | Standalone client solution |
| [src](src/README.md) | Wanderveil plugin, shell/settings, and future feature boundaries |
| [tests](tests/README.md) | Native UI checks and synthetic fixture reservation |
| [assets](assets/README.md) | Reserved original/licensed UI assets |

This repository must build independently. No private coordination/server files or packages are required. There is no production backend requirement for the local prototype.

## References and attribution

[Aetherphone](https://github.com/XeldarAlz/FFXIV-Aetherphone) and its [UI toolkit docs](https://github.com/XeldarAlz/FFXIV-Aetherphone/blob/dev/docs/ui-toolkit.md) are the primary UI reference; [LightlessClient](https://git.lightless-sync.org/Lightless-Sync/LightlessClient) is secondary. [CrystalRenderer](https://github.com/Wrepsk/crystal-renderer) is a candidate for optional presentation after a runtime spike. No reference code/assets or renderer dependency were added in this scaffold.

The template's [license](LICENSE.md) is preserved. See [third-party notices](THIRD_PARTY_NOTICES.md) before adding dependencies or art. Wanderveil is a working name and an independent fan project, not affiliated with or endorsed by Square Enix. Distribution eligibility and current policy requirements need review before release.
