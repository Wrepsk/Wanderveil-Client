# Client documentation

Status: minimal Wanderveil shell, 18 October 2026. The named plugin builds independently; product screens and collection features remain planned.

- [Development and current build](development.md)
- [Client architecture and future layout](architecture.md)
- [UI direction and reference sources](ui-guidelines.md)
- [Optional rendering investigation](rendering.md)
- [API contract status](api/README.md)
- [Asset organization](../assets/README.md)
- [Testing reservation](../tests/README.md)
- [Third-party notices](../THIRD_PARTY_NOTICES.md)

## First implementation tasks

1. Perform the [live shell smoke checklist](development.md); compilation and standalone builds already pass.
2. Build shared tokens/typography and a static Passport identity spread, then the other themed screens.
3. Complete one local postcard → demo stamp → persisted passport loop.

No account, private repository, production backend, or advanced renderer is needed for those product steps. Unresolved online contracts, identity proof, and renderer delivery require explicit decisions before implementation.
