# Public API contract

Status: **v1** (`1.0.0`), 21 October 2026. [`v1.yaml`](v1.yaml) (OpenAPI 3.1) describes signing in by character verification, the session, account deletion, and the holder's private passport record. The Wanderveil server and this client implement all of it ([how](../online.md)), and both sign-ins and passport sync have been used in game against a local server. No hosted server exists yet. Until the first client release that uses it, v1 may still change; afterwards, changes within `v1` only add things.

## Additions in 1.1.0 (implemented on the server; not yet used by this client)

Passport sharing ([ADR-0005](https://github.com/Wrepsk/FFXIV-Wanderveil), in the coordination repository) adds, only adding endpoints and codes:

- **The passport's other parts:** `/v1/passport/stamps` and `/v1/passport/marks` (documents with revisions, as the passport; drawing best sent gzip-compressed, and answered compressed when asked), and `/v1/passport/picture` (PNG or JPEG, at most 4 MiB and 2048 px).
- **Sharing:** `/v1/sharing` sets `approved` (the default) or `anyone`.
- **View requests:** `/v1/view-requests` asks (always `202`), lists incoming or outgoing, approves, declines, and withdraws. A decline looks like no answer until the request expires after 14 days.
- **Viewers:** `/v1/viewers` lists approved viewers and removes them.
- **Visits:** `/v1/visits` looks a passport up by name and world, and `/v1/visits/{accountId}/{part}` reads its parts. Visibility is checked on every read, and a passport that does not exist, is hidden, or is blocked all answer the same `passport_not_visible`.
- **Safety:** `/v1/blocks` blocks both ways, and `/v1/reports` reports a passport you opened.

## Draft additions (1.2.0, not implemented): invited co-drawing

Invited co-drawing (ADR-0006, in the coordination repository) adds:

- **Invitations:** `/v1/drawing-grants` (the owner invites an approved viewer to draw on chosen stamp pages, changes the pages, or revokes the invitation) and `/v1/drawing-invitations` (the invitations a guest holds).
- **Guests' marks:** each stroke or line of writing is its own record, with an author and an ID the guest's client makes, so retrying a save is harmless.
  - `/v1/visits/{accountId}/guest-marks` lists them for anyone who may see the passport; a guest adds one with `PUT …/{markId}` and erases their own with `DELETE …/{markId}`.
  - `/v1/passport/guest-marks` is the owner's view: it lists them, and removes one, or all by one guest with `?author=`.
  - Codes: `drawing_not_allowed`, `limit_reached`, `grant_not_found`, `mark_not_found`, and `mark_conflict`.

### The live hub (`/v1/live`, SignalR)

A SignalR hub with the same session: the bearer token goes in the `Authorization` header, and a token in the query string is refused. REST stays the truth: after connecting or reconnecting, a client reads the guest marks and the owner's revisions again, so anything missed arrives that way.

| Direction | Name | Arguments | Meaning |
| --- | --- | --- | --- |
| client → hub | `Follow` | `ownerId` | Follow a passport you may see; refused (with `AccessEnded`) otherwise. |
| client → hub | `Unfollow` | `ownerId` | Stop following. |
| client → hub | `Progress` | `ownerId, strokeId, sheet, colour, width, points` | The points so far of a stroke being drawn, on a page you may draw on (the owner on any stamp page, a guest on invited ones); at most ten a second. Never stored. |
| client → hub | `EndStroke` | `ownerId, strokeId` | The stroke was finished (and saved through REST) or abandoned. |
| hub → client | `StrokeProgress` | `ownerId, author, strokeId, sheet, colour, width, points` | Someone else's stroke in progress. |
| hub → client | `StrokeEnded` | `ownerId, strokeId` | Drop that stroke in progress. |
| hub → client | `MarkAdded` | `ownerId, GuestMark` | A guest's mark was saved. |
| hub → client | `MarkRemoved` | `ownerId, markId` | A guest's mark was erased or removed. |
| hub → client | `OwnerMarksChanged` | `ownerId, revision` | The owner's own drawing has a new revision; read it again. |
| hub → client | `AccessEnded` | `ownerId` | You may no longer see this passport (or draw in it); you no longer follow it. |

`colour` is `#rrggbb`, `width` is as in a stored stroke, and `points` are `[x, y, pressure]` triples in page units, as in the marks file.

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
