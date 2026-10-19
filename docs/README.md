# Client documentation

Status: Phase 01 UI prototype, 18 October 2026. Shared components, static Passport identity spread, and static Discover cards/details are implemented; remaining screens and collection features are planned.

- [Development and current build](development.md)
- [Client architecture and future layout](architecture.md)
- [UI direction and reference sources](ui-guidelines.md)
- [Implemented UI components](ui-components.md)
- [Passport visual-fidelity milestone and review previews](passport-visual-fidelity.md)
- [Intended passport experience, nation editions, and page order](passport-experience.md)
- [Gridania static cover and spread review](passport-gridania-study.md)
- [Gridania runtime controls, reload instructions, and previews](passport-runtime.md)
- [Optional rendering investigation](rendering.md)
- [API contract status](api/README.md)
- [Asset organization](../assets/README.md)
- [Native UI checks](../tests/README.md)
- [Third-party notices](../THIRD_PARTY_NOTICES.md)

## First implementation tasks

1. Review the Gridania cover/colorway, banner/map introduction, portrait/signature versus information identity spread, and stamp collection targets; refine the artwork/material model before broader customization.
2. Review the connected static runtime prototype, then add closed bottom-center arrival and close-to-cover dismissal motion; separately evaluate a bounded 2.5D page turn. Review previews between milestones; do not introduce CrystalRenderer without a separate decision.
3. Studio's chooser and draft preview are implemented; Echoes and the remaining Phase 1 controls/states follow. Collection/persistence remain Phase 2.

No account, private repository, production backend, or advanced renderer is needed for those product steps. Unresolved online contracts, identity proof, and renderer delivery require explicit decisions before implementation.

Completed slices are committed after automated verification and preview inspection; routine user in-game testing is not a commit gate. Live observations remain useful separately and must not be fabricated.
