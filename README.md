# Wanderveil Client

**Every place has a story. Leave yours.**

A planned FFXIV Dalamud plugin for location-based discoveries, a collectible adventurer passport, and creator-authored adventures. The first product slice is fully offline: view a curated postcard, collect a demo stamp, and find it in the passport.

## Development status

The minimal Wanderveil shell is implemented: Discover, Passport, Echoes, and Studio tabs are marked coming soon, with appearance settings and an original procedural compass. Product screens, stamp collection, passport persistence, networking, and advanced rendering are not implemented yet.

## Build the current baseline

Install a compatible .NET 10 SDK and Dalamud development assemblies. The existing project uses `Dalamud.NET.Sdk/15.0.0`; its lock file targets `net10.0-windows7.0` and CI selects .NET 10. See the [development guide](docs/development.md) for paths and lifecycle checks.

```powershell
dotnet restore Wanderveil.slnx --locked-mode
dotnet build Wanderveil.slnx -c Debug --no-restore
dotnet build Wanderveil.slnx -c Release --no-restore
```

Debug output: `src/Wanderveil/bin/x64/Debug/Wanderveil.dll`. Release package: `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`. `/wanderveil` toggles the shell; `/wanderveil settings` opens appearance settings. Reduced motion saves through Dalamud configuration; this shell has no animation yet.

On 18 October 2026, locked restore and Debug/Release builds passed with zero warnings/errors, both here and from a fresh client-only source copy outside the workspace. Live Dalamud behavior and hosted CI execution remain untested.

## Repository map

| Path | Role |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Contributor/agent working agreement |
| [docs](docs/README.md) | Standalone development, architecture, UI, and protocol notes |
| `Wanderveil.slnx` | Standalone client solution |
| [src](src/README.md) | Wanderveil plugin, shell/settings, and future feature boundaries |
| [tests](tests/README.md) | Reserved meaningful test/fixture location |
| [assets](assets/README.md) | Reserved original/licensed UI assets |

This repository must build independently. No private coordination/server files or packages are required. There is no production backend requirement for the local prototype.

## References and attribution

[Aetherphone](https://github.com/XeldarAlz/FFXIV-Aetherphone) and its [UI toolkit docs](https://github.com/XeldarAlz/FFXIV-Aetherphone/blob/dev/docs/ui-toolkit.md) are the primary UI reference; [LightlessClient](https://git.lightless-sync.org/Lightless-Sync/LightlessClient) is secondary. [CrystalRenderer](https://github.com/Wrepsk/crystal-renderer) is a candidate for optional presentation after a runtime spike. No reference code/assets or renderer dependency were added in this scaffold.

The template's [license](LICENSE.md) is preserved. See [third-party notices](THIRD_PARTY_NOTICES.md) before adding dependencies or art. Wanderveil is a working name and an independent fan project, not affiliated with or endorsed by Square Enix. Distribution eligibility and current policy requirements need review before release.
