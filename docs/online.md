# Signing in and the passport online

Status: implemented, 21 October 2026, and checked headless against an in-memory server that follows the [v1 contract](api/README.md). On 21 October 2026 the user signed in from the game against a local server (Development, `http://127.0.0.1:5080`) with a real Lodestone code, and reported that everything looked good; the server then held one account verified via the Lodestone and a passport record synced through revision 9.

Every Wanderveil passport belongs to one FFXIV character that has signed in. A sign-in proves that you control the character's Lodestone profile, and nothing more. There are no unverified accounts. Once a character is signed in, the passport works from this PC even when the server cannot be reached.

## Signing in

The first time a character logs in without a session, Wanderveil opens the sign-in sheet by itself, once per character per session. It also opens instead of the passport when you use `/wanderveil` or the plugin's buttons without a session. The sheet is a passport-office application form, its type a size up from the game's UI font (it is read at a distance), and its window as tall as its content. It names the logged-in character, then:

1. **Get my code** asks the server for a code (`WV-` and eight characters) for that character's name and home world.
2. You paste the code anywhere in your Character Profile on the Lodestone and save. **Copy** and **Open my Lodestone profile settings** help with this.
3. **Check my profile** asks the server to read the profile. When the code is there you are signed in, the sheet closes, and the passport opens. If it is not there yet, the sheet says so and keeps the code, which works for 15 minutes. **Start again** asks for a new one.

Every refusal has its own words: an unknown character, a Lodestone under maintenance, an expired or already used code, too many tries (with the wait), or an outdated plugin. The sheet's small print says that signing in sends the character's name and world to the Wanderveil server, which reads the public Lodestone profile. Wanderveil never asks for a game password.

**Sign in with XIVAuth**, beside **Get my code**, is the other way. It is for players who already use [XIVAuth](https://xivauth.net), which vouches for characters it has verified through their Lodestone profiles. The server starts XIVAuth's device flow, and the sheet opens XIVAuth's approval page in the browser (once by itself; **Open XIVAuth again** reopens it). The sheet shows XIVAuth's code to check against the page and waits for approval. Meanwhile the plugin asks the server at the interval it gives, waits longer when told to slow down, and keeps trying through brief outages while the sign-in lasts. Approving, with this character shared and verified on XIVAuth, signs in as a Lodestone code does. A refusal, an expired sign-in, or a character XIVAuth did not vouch for is explained, and **Cancel** (or another character logging in) stops the waiting. A server not yet set up for XIVAuth says so once, and the sheet then offers only the Lodestone code for that server until the address changes or the plugin restarts ([preview](previews/online/signin-no-xivauth.png)). Preview: [waiting for XIVAuth](previews/online/signin-xivauth.png).

Previews: [no character](previews/online/signin-no-character.png), [choosing a server](previews/online/signin-server.png), [ready](previews/online/signin-ready.png), [the code](previews/online/signin-code.png), [checked too early](previews/online/signin-problem.png).

## The server address

No public Wanderveil server exists yet, so the sheet asks for an address until one is set (**Choose a Wanderveil server**), and its footer line changes it later. Wanderveil uses HTTPS, or plain HTTP only to this PC (for a server run locally, such as `http://127.0.0.1:5080`), so a session token never crosses a network unencrypted. The address is saved in the plugin's configuration as `ServerUrl`. Sessions belong to the server they signed in to: changing the address signs every character out of the old one here.

## Sessions

`sessions.json` (format `wanderveil.sessions`, version 1) in the plugin's configuration folder keeps one session per character, keyed by the game's content ID, which never leaves this PC. Each entry holds the server, the token, and the verified character the server named. Tokens are protected with Windows data protection (DPAPI, for the current Windows user), called directly through `crypt32`. Where that is unavailable, tokens are kept as they are, the entry says `"protection": "none"`, and the log warns. A token that cannot be unprotected (the file was copied from another PC or user) costs only that character a sign-in. A newer sessions file is left untouched.

When a character logs in with a saved session, Wanderveil asks the server whether the session still holds. If there is no answer (no network, an outage), the session is kept and the passport opens offline. A definite refusal (`401`), from this check or any later call, forgets the session. If the passport was open it closes, and the sheet explains that the session has ended. **Sign out**, in the passport's General settings (it asks twice), forgets the session here and ends it on the server when it can be reached. Nothing on this PC is deleted. Preview: [the account in Settings](previews/online/settings-account.png).

**Delete my online account**, beside it (also asked twice), deletes this character's account on the server, with the passport details kept there, at once, and signs the character out here. Everything on this PC stays, and signing in again starts a new account. Only the server's answer counts: if it cannot be reached, nothing is deleted and Settings says so. Preview: [the question](previews/online/settings-account-delete.png). Your data on the server can be read in full while signed in: the account (`GET /v1/session`) and the passport (`GET /v1/passport`).

## Local passports per character

Each signed-in character has its own local files, named by Lodestone ID: `stamps-lodestone-<id>.json`, `passport-lodestone-<id>.json`, `marks-lodestone-<id>.json`, and `portrait-lodestone-<id>.image`. Switching characters puts one passport away (finishing its saves) and opens the other. Phase 2's single `local` profile is not migrated (the only tester chose this). Its files stay in the folder, unused.

## Passport sync

The whole passport is kept in step with the holder's private record on the server, part by part (ADR-0005), each part with its own revision:

- **The identity:** details, signature, picture placement, and appearance (`passport-*.json`).
- **The stamps:** with their places (`stamps-*.json`).
- **The drawing and writing:** `marks-*.json`, sent gzip-compressed.
- **The picture.** The stored file is sent as it is when it is already a PNG or JPEG within 4 MiB and 2048 px. Otherwise it is re-encoded through Dalamud's texture tools: scaled to fit, as PNG, or JPEG when the PNG is still too large. A picture that cannot be made small enough stays on this PC, and Settings says so.

`passport-sync-lodestone-<id>.json` remembers, for each part, the server revision last agreed and a hash of what both sides had then; the identity-only file written before this change is still read. The rule, for each part:

- **Nothing changed on either side:** nothing to do.
- **Changed here only:** sent, over the agreed revision.
- **Changed on the server only** (on another PC): taken, when the passport opens. Stamps and drawing are replaced whole, and the drawing's undo history is cleared, since undoing past another PC's copy would mix the two. A picture taken from online does not move the picture in its frame: that placement comes with the identity.
- **Changed on both sides:** this PC's copy wins and replaces the server's, and Settings says so.
- **Removed here** (only the picture can be): removed online too.
- **A passport new on this PC:** takes the server's copy if there is one. A passport that could not be read is never replaced by the server's.
- **The server's copy is from a newer Wanderveil:** it is left alone, and Settings asks you to update.

A change is sent two seconds after the last edit. With no answer, it waits and tries again after 30 seconds, doubling the wait up to 5 minutes. Another save in between (`412`) leads to a fresh comparison. All network and disk work runs off the draw path. The server's copy is taken in on the UI thread before the windows draw. General settings show whether the passport is saved online, being compared or saved, or waiting for the server.

## What is not covered yet

There is no sharing yet. XIVAuth works live: on 21 October 2026, with Wanderveil's XIVAuth application registered (scope `character`, device code flow, confidential client) and configured on a local server, the user signed in from the game through XIVAuth. The passport saved earlier through the Lodestone sign-in loaded correctly, because both sign-ins identify the character by its Lodestone ID. The live test ran on Windows against a local server. Not yet observed: a hosted server over HTTPS, DPAPI under Wine, and other players' characters.
