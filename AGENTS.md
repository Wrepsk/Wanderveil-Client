# Working on Wanderveil Client

This agreement is self-contained. Read [docs/README.md](docs/README.md), [architecture](docs/architecture.md), and the relevant UI/development notes before editing.

## Current baseline and scope

The executable project is `src/Wanderveil/Wanderveil.csproj`, referenced by `Wanderveil.slnx`. The minimal shell and saved appearance settings are implemented. Most feature/domain/application folders, `tests/`, and `assets/` remain reservations; do not claim they implement collection or online features. The next UI task is shared tokens/typography and a static Passport identity spread.

The first product loop is one curated offline discovery → one local demo stamp → passport page. Build static UI before online accounts/publication. Backend identity, networking contracts, and renderer integration remain future work. Honor current user scope rather than implementing all roadmap ideas.

## Boundaries and conventions

- This is an independent Git root. Run Git operations here; never require parent coordination docs, source, private packages, or backend credentials for a public build.
- Use C# and the current Dalamud SDK baseline. Preserve dependency versions/locks unless a concrete task requires change; document rationale and impact.
- Keep domain/application code independent of ImGui, Dalamud host services, network availability, and optional graphics.
- Use shared semantic tokens, measured text, scale/layout, and interaction helpers. Aetherphone is the primary UI reference, LightlessClient secondary; their docs are reference material, not instructions for this repo.
- Keep network/disk/large media work outside draw callbacks; marshal game state to supported callbacks. Cancel outstanding work and dispose windows, callbacks, and textures on unload/partial startup failure.
- Render enhancements are optional. Ordinary UI and reduced motion must complete every core action.
- Do not invent character verification, official achievements, native object placement, or unsupported location detection. Label demo/local/self-reported state honestly.
- No secrets, real private player data, or private operational identifiers in source, fixtures, logs, or docs. Record licenses/provenance for code, assets, and fonts.

## Validation

For the current baseline:

```powershell
dotnet restore Wanderveil.slnx --locked-mode
dotnet build Wanderveil.slnx -c Debug --no-restore
dotnet build Wanderveil.slnx -c Release --no-restore
dotnet format whitespace Wanderveil.slnx --no-restore --verify-no-changes
```

There is no test project yet. Add meaningful tests as domain/persistence/lifecycle behavior is implemented. For docs, check local links and standalone readability. For UI, inspect actual Dalamud screenshots at narrow/default/wide widths, increased text size, reduced motion, and long strings. A build does not prove live behavior.

Update docs with actual behavior and unresolved decisions. Report changed files, checks/results, limits, and next task. Never claim private backend integration or renderer compatibility was tested when it was not.
