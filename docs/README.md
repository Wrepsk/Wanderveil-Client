# Client documentation

Status: Phase 01 UI prototype, 18 October 2026. Shared components, static Passport identity spread, and static Discover cards/details are implemented; remaining screens and collection features are planned.

- [Development and current build](development.md)
- [Client architecture and future layout](architecture.md)
- [UI direction and reference sources](ui-guidelines.md)
- [Implemented UI components](ui-components.md)
- [Optional rendering investigation](rendering.md)
- [API contract status](api/README.md)
- [Asset organization](../assets/README.md)
- [Native UI checks](../tests/README.md)
- [Third-party notices](../THIRD_PARTY_NOTICES.md)

## First implementation tasks

1. Add Studio template chooser/postcard preview with publication visibly unavailable.
2. Complete remaining Phase 1 stamp/page controls, Echoes preview, and shared input/state coverage.
3. Complete one local postcard → demo stamp → persisted passport loop in Phase 2.

No account, private repository, production backend, or advanced renderer is needed for those product steps. Unresolved online contracts, identity proof, and renderer delivery require explicit decisions before implementation.

Completed slices are committed after automated verification and preview inspection; routine user in-game testing is not a commit gate. Live observations remain useful separately and must not be fabricated.
