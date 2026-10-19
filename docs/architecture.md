# Client architecture

Status: shell, shared UI foundations, synthetic Passport identity preview, and static Discover cards/details under `src/Wanderveil/`, built by root `Wanderveil.slnx`. Product/domain layers remain planned.

## Layers

UI/shell/components consume view state and return intents. Application use cases coordinate domain operations. Domain models and interfaces describe passports, discoveries, and stamps without ImGui or game-host dependencies. Infrastructure implements local storage, supported Dalamud access, future HTTP/events/media, and optional rendering.

The current/planned organization is recorded in [src/Wanderveil/README.md](../src/Wanderveil/README.md). One plugin project contains the entry point/configuration, `UI/` shell, and `Features/Settings/`. Other feature and domain/application folders are reserved. Avoid speculative assemblies.

## Lifecycle

`Plugin` constructs the windows and passes explicit configuration/callbacks; its private injected host services do not leak into windows. It registers `/wanderveil` and draw/open callbacks, rejects a conflicting command, and removes its registrations/windows on disposal or partial startup failure. It now owns `PassportAssets` managed font handles; shared image loading/frame-wrap lifetime belong to Dalamud's texture provider. Font handles are disposed after removing windows on normal/partial teardown. No hooks/background tasks were added. ImGui calls stay in draw callbacks. Future game-state access uses supported host threads and background work needs cancellation/safe result handoff.

`MainWindow` owns host sizing, bottom-center summon positioning, Escape handling, and callbacks. `BookShell` owns closed/open presentation and a validated snapshot of stable logical page IDs; it renders visible pages through `PassportDocumentView`, shared with the gallery. Explicit Open starts at the nation spread, followed by identity and demo stamp pages; narrow views advance one page and wider views advance a spread. Cover returns to the closed book. Transparent page children scroll independently. Discover retains selection, browse scroll, and focused Back behavior. Settings and size/drag controls remain. The static client has no account/game-state/advanced-renderer dependency; see [runtime controls and packaging evidence](passport-runtime.md).

`DiscoveryPreview` holds synthetic discovery data and the `StampDesign` it awards, not a protocol contract. Collecting goes through `StampCollection`, an in-memory domain object with no ImGui state that `BookShell` shares between Discover and the passport's collection pages; no location reads, network/media loads, or storage occur. Postcard scenery uses original procedural geometry; stamps use the bundled impression atlas with the procedural motif as fallback.

## First local loop

Before remaining screens, the [Passport visual-fidelity milestone](passport-visual-fidelity.md) establishes materials and live page composition. The selected Gridania edition is now connected to the runtime rather than confined to the gallery. Material access, logical page catalog, content, and future collection ownership remain separate. Entrance/exit and the bounded 2.5D turn remain later; no renderer dependency is installed.

Curated discovery fixture → collect use case → demo stamp instance → local passport repository → passport view. Repeated collection follows a clear duplicate rule: one stamp per discovery per passport. The steps up to the passport view exist in memory; the local repository is next. Persistence is versioned, atomic, and recoverable. Demo provenance is visible and never promoted to an official or server-issued claim.

## Future boundaries

The [passport experience](passport-experience.md) now has static closed/open state and ordered nation/identity/demo-stamp pages. Stamps are now collected in memory; edition selection, palette preferences, signature input, and persistence remain future work. Keep those data concerns separate from rendering and motion; the current page records are presentation metadata, not a storage contract.

Public API consumers use versioned public contracts, not private project references. Sharing/publication and location transmission are explicit opt-ins. Online authority and unverified character claims are distinguished. The ordinary UI remains complete if the optional renderer is absent or fails.

Feature areas are Discover, Passport, Echoes, Studio, Social, and Settings. Start with only the domain/use cases needed by a bounded phase; Echoes/social remain future work.
