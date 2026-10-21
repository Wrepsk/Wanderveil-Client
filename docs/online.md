# Signing in and the passport online

Status: implemented, 21 October 2026, and checked headless against an in-memory server that follows the [v1 contract](api/README.md). On 21 October 2026 the user signed in from the game against a local server (Development, `http://127.0.0.1:5080`) with a real Lodestone code, and reported that everything looked good; the server then held one account verified via the Lodestone and a passport record synced through revision 9.

Every Wanderveil passport belongs to one FFXIV character that has signed in. A sign-in proves that you control the character's Lodestone profile, and nothing more. There are no unverified accounts. Once a character is signed in, the passport works from this PC even when the server cannot be reached.

## Signing in

Without a session, the sign-in sheet opens instead of the passport when you use `/wanderveil` or the plugin's buttons. It never opens by itself on login or when the plugin loads (changed after the first two-player test, 21 October 2026); it does open after a sign-out while the passport was open, to say why. The sheet is a passport-office application form, its type a size up from the game's UI font (it is read at a distance), and its window as tall as its content. It names the logged-in character, then:

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

## Sharing

**Sharing** is the fifth Settings category (an eye), beside General, Customization, Layout, Saved data, and Drawing together; the category tabs are icons and all six sit in one row. It follows ADR-0005:

- **Asking to see a passport** (left page): type a character's name and home world, or use your target in game, and choose **Ask to see it**. The answer never says whether that character uses Wanderveil, so the page says what is true: if they do, they see your request, which waits up to 14 days.
- **Who may open yours** (right page): **Only people I approve** (the default) or **Anyone signed in**. Visitors see your whole passport and can take screenshots, and a block always keeps someone out.
- **Asking to see yours:** each request with **Approve**, **Decline** (the sender is not told), and **Block**.
- **Who can open yours:** your viewers, each with **Remove** and **Block**.
- **You asked to see:** your requests, each character once, *waiting* or *approved*: **Withdraw** while waiting, and **Open** and **Remove** once approved. **Remove** gives up the approval: their passport no longer opens for you until you ask again. An approval also leaves this list when they remove you or either of you blocks the other. Asking again while approved adds nothing. A decline looks like waiting until the request expires.
- **Blocked:** with **Unblock**. Unblocking does not restore an approval the block removed.

Each list shows its first five and how many more, with each entry's actions on its line. The page reads everything again when it opens and every half minute while it shows, and after each action. Reading and changing run in the background. Without the server, the page says so and keeps what it last read. **Open it** (beside **Ask to see it**) and **Open** (on an approved request) open a passport you may see.

**From a player's right-click menu** (in the world, the party list, friends, chat, and other player menus), Wanderveil adds an entry marked with a boxed W. **Open passport** shows where that character approved you. Otherwise **Ask to see passport** opens their passport if they share it with anyone, and asks to see it if not. The chat log then says the request was sent, or why the passport could not open. The menu only offers this while you are signed in, and never for your own character. Previews: [the page](previews/online/settings-sharing.png), [after asking](previews/online/settings-sharing-asked.png).

## Visiting a passport

A passport you may see opens in a book of its own, beside yours: the owner's edition, colours, symbols, identity pages and picture, stamps where they placed them, and drawing and writing.

- **Read-only:** no section tabs or ribbon, no Settings, no editing, no stamps to place, no decorating, and no way out to Discover. Drawing is offered only where the owner invited you (below).
- **Its footer:** says whose it is (*Visiting Name, World*), with **Report** and **Block**.
  - **Report** asks what (their picture, drawing and writing, details and other text, stamps, or something else) and why (harassment, hateful, sexual, spam, impersonating someone, or something else). People at Wanderveil review reports, and the owner is not told who reported.
  - **Block** asks first, then closes the visit.
- **Kept in memory only:** the visited passport is read part by part and never written to disk, so losing access leaves no copy behind (beyond what was on screen). Opening another passport replaces it, and closing it frees it.
- **When it cannot open:** a character without Wanderveil, one that has not shared with you, and one that blocked you all give the same answer: no passport you may open there.

Previews: [a visited passport](previews/online/visit-book.png), [reporting it](previews/online/visit-report.png).

## Drawing together

**Drawing together** is the sixth Settings category (a pencil). It follows ADR-0006: an owner invites people who can open their passport to draw and write on chosen stamp pages.

- **Inviting** (left page): **Let draw** beside someone under *Invite to draw* asks for the pages, as the book numbers them (for example `5, 7-9`; stamp pages run from 5 to 100), then **Let them draw**. Only people approved by name can be invited, as the server requires.
- **Drawing on yours:** each guest with their pages, **Pages** (change them), **Stop** (end the invitation; their marks stay), and **Remove marks** (everything they made).
- **You may draw on:** passports whose owners invited you, with **Open**.
- **On your own pages:** guests' marks lie beneath your own. Hovering one names who drew or wrote it, and a right-click offers removing it. Your eraser, Clear, and Undo never touch them.
- **On a visited passport:** the owner's drawing and other guests' marks lie beneath yours, each named on hover. **Draw** appears in the footer where you are invited; the pencil, eraser, and writing then work only on invited pages. Each stroke or line is saved as its own mark as you make it, and Undo or the eraser erases it on the server. A mark the server refuses (no longer invited, or over a limit) is taken back, and the footer says why. Without an answer, marks wait and are sent when the server answers again. Marks the owner removes leave your copy too.

Guests' marks are read when the stamp pages show, and again every 20 seconds while they do. Strokes drawn by others do not appear live yet: that comes with the live connection (the next slice). Previews: [the Drawing together page](previews/online/settings-drawing.png), [inviting](previews/online/settings-drawing-invite.png), [a guest's mark named on your page](previews/online/codrawing-own.png), [drawing on a visited passport](previews/online/codrawing-visit.png).

## What is not covered yet

Drawing together has not been tried in game yet, and others' strokes do not appear live until the live connection lands. XIVAuth works live: on 21 October 2026, with Wanderveil's XIVAuth application registered (scope `character`, device code flow, confidential client) and configured on a local server, the user signed in from the game through XIVAuth. The passport saved earlier through the Lodestone sign-in loaded correctly, because both sign-ins identify the character by its Lodestone ID. The live test ran on Windows against a local server. Not yet observed: a hosted server over HTTPS, DPAPI under Wine, and other players' characters.
