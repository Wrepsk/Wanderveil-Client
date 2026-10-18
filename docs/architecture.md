# Client architecture

Status: shell, shared UI foundations, synthetic Passport identity preview, and static Discover cards/details under `src/Wanderveil/`, built by root `Wanderveil.slnx`. Product/domain layers remain planned.

## Layers

UI/shell/components consume view state and return intents. Application use cases coordinate domain operations. Domain models and interfaces describe passports, discoveries, and stamps without ImGui or game-host dependencies. Infrastructure implements local storage, supported Dalamud access, future HTTP/events/media, and optional rendering.

The current/planned organization is recorded in [src/Wanderveil/README.md](../src/Wanderveil/README.md). One plugin project contains the entry point/configuration, `UI/` shell, and `Features/Settings/`. Other feature and domain/application folders are reserved. Avoid speculative assemblies.

## Lifecycle

`Plugin` constructs the windows and passes explicit configuration/callbacks; its private injected host services do not leak into windows. It registers `/wanderveil` and draw/open callbacks, rejects a conflicting command, and removes its registrations/windows on disposal or partial startup failure. It now owns `PassportAssets` managed font handles; shared image loading/frame-wrap lifetime belong to Dalamud's texture provider. Font handles are disposed after removing windows on normal/partial teardown. No hooks/background tasks were added. ImGui calls stay in draw callbacks. Future game-state access uses supported host threads and background work needs cancellation/safe result handoff.

`MainWindow` owns host sizing/clamping, Escape close handling, and callbacks. `BookShell` owns in-memory section/page presentation and returns Settings/close/drag intents. `BookShellLayout` fits a fixed natural-aspect book to the viewport; `BookControl` preserves native button semantics under original leather paint. The host is borderless/transparent with fixed content extents, so zooming cannot scroll the outer object. Transparent child pages scroll live content independently. Passport switches to one identity/notes page when font size or viewport requires it. Discover owns its selected preview and browse-scroll restoration; native cards open postcard/reward details, with keyboard focus transferred to Back. Switching bookmarks retains the selected discovery for the window's lifetime. Echoes/Studio remain coming soon. Settings save reduced motion through a host callback, with failure/retry feedback. The UI is static, independent of accounts/game state/advanced rendering. Shared components are described in [the component guide](ui-components.md). The native check executable links those sources without loading the game host; its software rasterizer is test-only.

`DiscoveryPreview` holds synthetic presentation data, not a domain discovery or protocol contract. No location reads, network/media loads, collect use case, or storage occur in Discover draw callbacks. Postcard scenery and stamp motifs use original procedural draw-list geometry; the missing-image case retains its caption. The disabled collection button and explanatory copy communicate the Phase 1 boundary.

## First local loop

Before remaining screens, the approved [Passport visual-fidelity milestone](passport-visual-fidelity.md) establishes original materials, stationary book painting, and review targets. `BookSurface`/`IBookAssets` keep physical material independent of page content and future page counts. Runtime retains the existing identity/notes structure; cover/collection target composition is test-only until reviewed. Entrance/exit and a separate 2.5D turn come later; no renderer dependency is installed.

Curated discovery fixture → collect use case → demo stamp instance → local passport repository → passport view. Repeated collection follows a clear duplicate rule. Persistence is versioned, atomic, and recoverable. Demo provenance is visible and never promoted to an official or server-issued claim.

## Future boundaries

The intended [passport experience](passport-experience.md) adds closed/open presentation state, a nation introduction before identity, and expandable stamp pages. Keep edition definitions and user palette preferences separate from identity/stamp data; keep logical page routing separate from material rendering and motion state. These are design boundaries, not implemented types or a persistence contract. The current `BookShell` remains the stationary identity/notes baseline.

Public API consumers use versioned public contracts, not private project references. Sharing/publication and location transmission are explicit opt-ins. Online authority and unverified character claims are distinguished. The ordinary UI remains complete if the optional renderer is absent or fails.

Feature areas are Discover, Passport, Echoes, Studio, Social, and Settings. Start with only the domain/use cases needed by a bounded phase; Echoes/social remain future work.
