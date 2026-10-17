# Development guide

Status: minimal Wanderveil shell, migrated from the sample on 18 October 2026.

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
dotnet format whitespace Wanderveil.slnx --no-restore --verify-no-changes
```

Use the solution's default platform mapping, which maps the project to x64. Outputs:

- Debug DLL and generated manifest: `src/Wanderveil/bin/x64/Debug/Wanderveil.dll` and `Wanderveil.json`.
- Release DLL/manifest: the same filenames under `src/Wanderveil/bin/x64/Release/`.
- Release distribution package: `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`.

All commands above passed during migration. Debug/Release each had zero warnings/errors. A fresh client-only source copy outside the workspace also restored/built both configurations. No test project exists yet. The PR workflow now uses locked restore and a Debug/Release matrix with corrected artifact paths; hosted execution has not been tested.

## Commands and settings

| Entry | Behavior |
| --- | --- |
| `/wanderveil` | Toggle the shell |
| `/wanderveil settings` | Open appearance settings |
| Shell Settings button / Dalamud configuration action | Open appearance settings |
| Dalamud main-UI action | Open the shell |

The four destination tabs are coming-soon placeholders. Reduced motion persists through Dalamud configuration; the current shell is static. Save failures offer retry. The new internal name uses Wanderveil's own configuration identity; the old sample's arbitrary settings are not imported.

## Live smoke checklist — pending

Register the Debug DLL using the current Dalamud developer-plugin UI, then verify:

- [ ] Load and open through `/wanderveil` and the main-UI action.
- [ ] Toggle closed/reopen and switch all four tabs; resize and increase UI/text scale.
- [ ] Open settings through the command, button, and configuration action.
- [ ] Change reduced motion; unload/reload and confirm it persists.
- [ ] Confirm the shell remains usable at the title screen and across login/logout.
- [ ] Unload/reload repeatedly without duplicate command handlers, windows, or callbacks.
- [ ] Check logs for errors and confirm the old sample command/asset is unused.

No live runtime test was performed during this migration. Builds, package inspection, and source lifecycle review do not substitute for these checks.

## Contribution

Keep public builds independent of private source/packages. Use synthetic fixtures, preserve licenses/provenance, and keep secrets out of Git. Recheck current distribution/privacy/networking/AI disclosure policies at release time; no listing eligibility is assumed.
