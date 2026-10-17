# Development guide

Status: Wanderveil shell, static Passport identity preview, and static Discover cards/details, 18 October 2026.

## Prerequisites

- Windows development environment with a compatible .NET 10 SDK. The unchanged `Dalamud.NET.Sdk/15.0.0` project/lock targets `net10.0-windows7.0`; .NET 10.0.100 was used locally.
- FFXIV/XIVLauncher/Dalamud for runtime testing, plus compatible Dalamud development assemblies.
- Default development-assembly path: `%AppData%/XIVLauncher/addon/Hooks/dev`. For a custom location, use the SDK's `DALAMUD_HOME` configuration and validate discovery.

There is no `global.json` yet. This migration preserved SDK/dependency versions and the lock file's exact contents. No backend, parent checkout, production account, or renderer library is required.

## Build and format

Run from this repository root:

```powershell
dotnet restore Wanderveil.slnx --locked-mode
dotnet build Wanderveil.slnx -c Debug --no-restore
dotnet build Wanderveil.slnx -c Release --no-restore
dotnet run --project tests/Wanderveil.UiChecks -c Release -p:Platform=x64 --no-build --no-restore -- artifacts/ui
dotnet format whitespace Wanderveil.slnx --no-restore --verify-no-changes
```

Use the solution's default platform mapping, which maps the project to x64. Outputs:

- Debug DLL and generated manifest: `src/Wanderveil/bin/x64/Debug/Wanderveil.dll` and `Wanderveil.json`.
- Release DLL/manifest: the same filenames under `src/Wanderveil/bin/x64/Release/`.
- Release distribution package: `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`.

The solution maps both projects to x64; direct check-project runs need `-p:Platform=x64` to select that output. [Native UI checks](../tests/README.md) use the installed `Dalamud.Bindings.ImGui.dll` and `cimgui.dll`, with no new NuGet dependency. Optional ignored BMP previews are generated under `artifacts/ui/`. CI runs the checks in Debug/Release and uploads previews separately from plugin artifacts. Hosted execution is not claimed from local results.

## Commands and settings

| Entry | Behavior |
| --- | --- |
| `/wanderveil` | Toggle the shell |
| `/wanderveil settings` | Open appearance settings |
| Shell Settings button / Dalamud configuration action | Open appearance settings |
| Dalamud main-UI action | Open the shell |

Passport initially shows a static synthetic identity spread. Explore discoveries selects Discover, whose demo cards open postcard/stamp-preview details. Back returns to the list and restores its scroll position; native Tab/Space navigation works, with focus moved to Back when details open. Collection is disabled and explicitly unsaved. Echoes and Studio remain coming soon. Reduced motion persists through Dalamud configuration; the UI is currently static. Save failures offer retry. Wanderveil uses its own configuration identity; the old sample's arbitrary settings are not imported.

## Optional live observations

When observing the live host, register the Debug DLL using its developer-plugin UI and record these checks. Routine user in-game testing is not required before committing automated-verified work.

- [ ] Load and open through `/wanderveil` and the main-UI action.
- [ ] Toggle closed/reopen and switch all four tabs; resize and increase UI/text scale.
- [ ] Open settings through the command, button, and configuration action.
- [ ] Change reduced motion; unload/reload and confirm it persists.
- [ ] Confirm the shell remains usable at the title screen and across login/logout.
- [ ] Unload/reload repeatedly without duplicate command handlers, windows, or callbacks.
- [ ] Check logs for errors and confirm the old sample command/asset is unused.

Native ImGui frames/previews do not establish FFXIV lifecycle, exact font appearance, or GPU compatibility. Record those observations honestly when available; do not turn this optional checklist into a routine user-testing gate.

## Contribution

Keep public builds independent of private source/packages. Use synthetic fixtures, preserve licenses/provenance, and keep secrets out of Git. Recheck current distribution/privacy/networking/AI disclosure policies at release time; no listing eligibility is assumed.
