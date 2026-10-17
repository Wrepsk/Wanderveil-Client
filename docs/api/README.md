# Public API contract reservation

No API, generated client, event schema, or production endpoint exists yet. The local prototype requires none.

Future public contracts must be versioned and available from a clean public checkout. Do not reference private backend source or packages. Define identifiers, errors, pagination, capabilities, resource authorization, privacy projections, concurrency, and idempotency before consumers are built. Event delivery must reconcile through an authoritative read path.

Add the smallest OpenAPI/schema artifact needed by an actual backend slice rather than speculative endpoints. Contract fixtures use synthetic data and must not include tokens, real private profiles, or operational addresses.
