# Test reservation

No test project is configured yet; a successful shell build is not a test suite. Add a host-independent test project when actual domain/persistence behavior lands. Current migration validation covers locked restore, Debug/Release builds, formatting, generated identity/package inspection, and a fresh standalone source copy. Live runtime checks remain pending in the development guide.

Priority scenarios: duplicate demo collection, stamp provenance, persistence round-trip/migration/corruption recovery, profile isolation, and lifecycle cancellation. Add authorization/consent/contract checks only as online features arrive. Keep [fixtures](fixtures/README.md) synthetic and deterministic.

UI requires live visual/input review in addition to any layout-unit checks. Do not write tests that only restate constants or folder existence to inflate coverage.
