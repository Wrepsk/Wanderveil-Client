# Client architecture

Status: shell, shared UI foundations, and synthetic Passport identity preview under `src/Wanderveil/`, built by root `Wanderveil.slnx`. Product/domain layers remain planned.

## Layers

UI/shell/components consume view state and return intents. Application use cases coordinate domain operations. Domain models and interfaces describe passports, discoveries, and stamps without ImGui or game-host dependencies. Infrastructure implements local storage, supported Dalamud access, future HTTP/events/media, and optional rendering.

The current/planned organization is recorded in [src/Wanderveil/README.md](../src/Wanderveil/README.md). One plugin project contains the entry point/configuration, `UI/` shell, and `Features/Settings/`. Other feature and domain/application folders are reserved. Avoid speculative assemblies.

## Lifecycle

`Plugin` constructs the windows and passes explicit configuration/callbacks; its private injected host services do not leak into windows. It registers `/wanderveil` and draw/open callbacks, rejects a conflicting command, and removes its registrations/windows on disposal or partial startup failure. Current windows own no textures/hooks/background tasks. ImGui calls stay in draw callbacks. Future game-state access uses supported host threads and background work needs cancellation/safe result handoff.

The shell uses native tabs, shared typography/buttons, and an original procedural compass. Passport shows responsive synthetic identity/notes pages; the other destinations remain coming soon. Settings save reduced motion through a host callback, with failure/retry feedback. The UI is static, independent of accounts/game state/advanced rendering. Shared components are described in [the component guide](ui-components.md). The native check executable links those sources without loading the game host; its software rasterizer is test-only.

## First local loop

Curated discovery fixture → collect use case → demo stamp instance → local passport repository → passport view. Repeated collection follows a clear duplicate rule. Persistence is versioned, atomic, and recoverable. Demo provenance is visible and never promoted to an official or server-issued claim.

## Future boundaries

Public API consumers use versioned public contracts, not private project references. Sharing/publication and location transmission are explicit opt-ins. Online authority and unverified character claims are distinguished. The ordinary UI remains complete if the optional renderer is absent or fails.

Feature areas are Discover, Passport, Echoes, Studio, Social, and Settings. Start with only the domain/use cases needed by a bounded phase; Echoes/social remain future work.
