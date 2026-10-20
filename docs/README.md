# Client documentation

Status: Phase 02 in progress, 19 October 2026. The passport book holds Passport, Discover, Studio, Echoes, and Settings pages with motion, and the postcard's demo stamp can be collected, placed anywhere on blank stamp pages (up to page 100), arranged, and saved locally. The identity pages are filled in on the page (details, signature, own picture, name on the cover) and saved locally; edition choice and personalization come next.

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
3. Phase 1 is complete; Phase 2's collect loop, local saving, free stamp placement, and identity/cover preferences work. Edition choice and colours work too. Next: symbols and structure, then drawing on pages (Phase 2).

No account, private repository, production backend, or advanced renderer is needed for those product steps. Unresolved online contracts, identity proof, and renderer delivery require explicit decisions before implementation.

Completed slices are committed after automated verification and preview inspection; routine user in-game testing is not a commit gate. Live observations remain useful separately and must not be fabricated.
