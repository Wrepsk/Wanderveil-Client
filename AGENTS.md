# Working on Wanderveil Client

This agreement is self-contained. Read [docs/README.md](docs/README.md), [architecture](docs/architecture.md), and the relevant UI/development notes before editing.

## Current baseline and scope

The executable project is `src/Wanderveil/Wanderveil.csproj`, referenced by `Wanderveil.slnx`. Shell/settings, shared UI, Passport identity, and Discover cards/details exist. Passport visual fidelity now takes priority over Studio/Echoes: milestone 1 adds original book materials, heading fonts, stationary presentation, and cover/identity/collection review targets. Read [the milestone record](docs/passport-visual-fidelity.md) before continuing. Review previews between milestones; full identity/collection page composition, entrance/exit, and a separate 2.5D turn experiment follow in that order. CrystalRenderer needs a separate decision. Preserve existing functionality and avoid elaborate chrome work before the core book is established.

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
dotnet run --project tests/Wanderveil.UiChecks -c Release -p:Platform=x64 --no-build --no-restore -- artifacts/ui
dotnet format whitespace Wanderveil.slnx --no-restore --verify-no-changes
```

Run the native UI check executable for layout, contrast, wrapped-text reservation, mouse/keyboard activation, and style isolation; inspect its software-rendered previews. Add meaningful domain/persistence/lifecycle checks with those features. For docs, check local links and standalone readability. Record live Dalamud evidence when available, without making routine user testing a prerequisite for completion/commits. Headless previews do not establish game-host lifecycle or exact font/GPU appearance.

Update docs with actual behavior and unresolved decisions. Report changed files, checks/results, limits, and next task. Never claim private backend integration or renderer compatibility was tested when it was not.

## Commit workflow — user preference

Commit completed, verified slices automatically, using this repository's Conventional Commit style (for example `feat(ui): add responsive passport identity preview`). Review staged files and preserve unrelated work. Keep commits independent from the parent repository and do not push unless requested. Automated/native UI evidence is sufficient for routine UI commits; do not request in-game testing as a commit gate or claim live behavior you have not observed.
