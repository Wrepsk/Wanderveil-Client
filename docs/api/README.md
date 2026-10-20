# Public API contract

Status: **draft**, 20 October 2026. [`v1.yaml`](v1.yaml) (OpenAPI 3.1) describes the first online slice: signing in by character verification, the session, and the holder's private passport record. This client implements the Lodestone sign-in, the session, and the passport record ([how](../online.md)); XIVAuth is not implemented. No hosted server exists. Until the first client release that uses it, the draft may change without compatibility promises; afterwards, changes within `v1` only add things.

## What it covers

- **Sign-in.** Every account is one FFXIV character (global data centres), verified in one of two ways. **Lodestone:** the server issues a code (`WV-` and 8 characters), the player puts it in their Lodestone profile's self-introduction, and verifying reads the profile. **XIVAuth:** a device flow; the player approves in the browser while the plugin polls. Either one returns a session. There are no unverified accounts.
- **Session.** Read who is signed in, sign out, delete the account.
- **Passport.** Read and save the holder's passport document (the local `wanderveil.passport` file, versions 1 to 3, at most 256 KiB, no picture) with revision checks.

A verification proves that, at sign-in, the person controlled that character's Lodestone profile (or that XIVAuth had verified it). It does not prove the plugin runs on that character, or anything about the game. The client says "verified via the Lodestone" or "via XIVAuth", never "official".

## Rules for every endpoint

- **Paths** start with the major version, `/v1/`. Request and response bodies are JSON in camelCase. Times are RFC 3339 in UTC. Unknown response fields are ignored by the client, so additions are compatible.
- **Client version.** Every request sends `Wanderveil-Client: <plugin version>`. A version the server no longer accepts gets `426` with `client_outdated`; the client says to update and keeps working locally.
- **Sessions.** `Authorization: Bearer <token>`. The token is opaque and is shown once. Sessions end after 30 days unused or 180 days after sign-in, at sign-out, or when the account is deleted. Any `401` (`session_invalid`) means sign in again; it is never retried.
- **Errors** are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details (`application/problem+json`) with a stable `code` from the contract's list, a `traceId`, and for `invalid_request` the failing fields. Clients branch on `code`, never on the text.
- **Retries.** `429` and `503` carry `Retry-After` where the server knows it; retry after that, within a bounded budget. Sign-in failures other than `code_not_found` and XIVAuth's `pending` are final for that challenge or flow. Transport failures (no answer) are outages: the client keeps working locally.
- **Concurrency.** The passport record has an integer revision, sent as a strong `ETag`. A save needs `If-None-Match: *` (first save) or `If-Match` (the revision last seen); otherwise `428`. A changed record answers `412` (`revision_conflict`) and keeps the stored one. Saving the same document again succeeds without a new revision, so retrying a save is safe.
- **Authorization.** Every resource belongs to the session's account, and the server never takes an account, character, or Lodestone ID from a request as authority. There is no way to read another account's passport in `v1`.
- **Secrets.** Challenge and flow IDs are 128-bit secrets returned only to whoever started them; a code seen on a profile cannot verify without its challenge ID. Tokens, challenge IDs, and flow IDs never appear in logs, diagnostics, or fixtures.

## Not in v1 yet

Sharing or showing a passport to anyone else, stamps, drawings, the picture, linking several characters, "sign out everywhere", and Chinese (Rising Stones) or Korean characters. Each needs its own decision before it is added.

Contract fixtures use synthetic data only. The design and its threat model are recorded in the coordination repository's ADR-0004 (private).
