# Gridania passport in the client

Status: runtime prototype, 19 October 2026. The SVG art, book-surface controls, motion, page turns, section riffles, and in-book Settings were merged into `master` at the user's request on 19 October 2026; live smoothness and GPU appearance are not yet observed. The gallery and `BookShell` share the same `PassportDocumentView` composition.

## Review in game

Build the client, then reload Wanderveil in Dalamud so it loads the new DLL. The Debug developer-plugin DLL is `src/Wanderveil/bin/x64/Debug/Wanderveil.dll`; the Release package is `src/Wanderveil/bin/x64/Release/Wanderveil/latest.zip`. Compilation does not replace an already loaded plugin instance.

Run `/wanderveil`. Each summon starts with the closed cover of the passport's edition (see [Editions](#editions)); the first summon of a plugin session centers it on the screen, and later summons return it to where it was last dismissed (or dragged). The book itself is the control surface. Click the right side of the cover to open it. When open, click the right side to turn forward (nation banner/map, then portrait/signature and identity information, then blank stamp pages up to page 100) and the left side to turn back. Clicking the left side of the first page returns to the cover. Narrow views turn one logical page; wider views turn a spread. Press and drag anywhere on the book that is not a control to move it; a drag never turns a page. A soft shadow on the outer edge and a hand cursor show which way a click will go. On the keyboard, Space or Enter opens the cover, Page Down and Page Up turn pages, End jumps to the last page with a stamp, and Home returns to the cover.

- [Runtime closed cover](previews/passport/runtime-cover.png)
- [Runtime nation spread](previews/passport/runtime-nation.png)
- [Runtime identity spread](previews/passport/runtime-identity.png)
- [The first stamp pages, blank but for the engraved number](previews/passport/runtime-collection.png)
- [Smallest book size, contents scaled down](previews/passport/runtime-smallest.png)

These are native software previews of the production shell, not live game captures. Real host font/GPU/hotkey/lifecycle behavior remains unobserved here. The map retains its visible unvalidated-geography caption, and identity/signature are synthetic demo content. Stamps are collected in memory only (see below); signature input and all storage are not implemented.

## Vector art

The embedded plates now come from `assets/passport/vector/`, rasterized from SVG by [tools/passport-art](../tools/passport-art/README.md) following the "Eorzea Passport" Claude Design. Canvas sizes are unchanged, so the composition code is shared; cover text and crest were re-centered for the new board and the map's cartouche label was removed. With the design typefaces and reduced-size art copies, the Release package is about 4.6 MB (the image-generated plates made it about 10.9 MB). Gallery checks for the plum colorway still use the earlier image-generated study.

Type also follows the design. Cinzel Bold is used for cover lettering. Cormorant Garamond Bold, SemiBold, and Medium are used for page titles, headings, and body or caption text. Courier Prime Regular is used for the passport number and the identity line. Text that a decorative font cannot draw falls back to the host font. Source Serif 4 is no longer shipped. The [notices](../THIRD_PARTY_NOTICES.md) list the pinned sources and licenses.

## Controls on the book

There are no buttons around the book anymore. The sections (Passport, Discover, Echoes, Studio) are paper index tabs tucked behind the pages. The selected tab carries a small edition-colored ribbon at its right end that peeks just below the tab onto the book; it unrolls after the pages appear and rerolls before moving to a newly selected tab. Settings, smaller, larger, and close (`- + x`) are printed in the paper's lower margin, with page numbers (folios) between them. On the closed cover only `- + x` are foil-stamped beneath the lower arc; there is no separate settings window anymore. **x**, Escape, and `/wanderveil` play the dismissal sequence below. Pages turn with motion; see [page turns](#page-turns-milestone-4-experiment).

## Motion

Summoning slides the closed passport up from below the screen to its place (the screen center the first time, then where it was dismissed); it waits closed until you click its right side (or press Space/Enter). The cover then swings open on its spine with perspective, its artwork, crest, and lettering turning with it, while the book glides so the spread ends centered; the cover's inside lands as the first left page. Pages, tabs, controls, and the tab ribbon fade in once it lands. Dismissing from an open book closes the cover first, quickly (0.25 s rather than the 0.6 s of closing back to the cover), then slides the closed book down off the screen and hides the window; a closed book slides down directly. Clicking the first page's left side or pressing Home closes back to the cover without dismissing.

Any request can interrupt a motion and reverse it from where it is, for example summoning again while the book slides away. The book takes no clicks, drags, or page keys while it moves. The **Reduced motion** setting makes every step instant with the same results, including explicit opening. In a single-page layout the cover swings away to the left and fades, since there is no left page for it to land on.

`BookShellMotion` sequences the motion without ImGui, using the shared motion tokens in `UiMotion` (slide 0.42 s, cover 0.6 s, dismissal close 0.25 s, page reveal 0.18 s, page turn 0.5 s, riffle leaf 0.34 s with 0.07 s stagger, ribbon unroll 0.22 s and roll-up 0.12 s, one easing curve, and a 0.1 s longest frame); `BookShellPlacement` holds the resting place and slide offset; `LeafProjection` draws a leaf turning on a hinge as textured strips and per-glyph quads. The host window applies the slide offset and keeps the bottom center anchored while the book changes between closed and open sizes; the closed cover is sized to be exactly the leaf that swings open.

- [Cover swinging open](previews/passport/motion-opening-early.png)
- [Inside landing as the left page](previews/passport/motion-opening-late.png)
- [Closing back to the cover](previews/passport/motion-closing.png)

The headless preview window is pinned at the top left rather than anchored at the bottom center, so the book's glide toward center is offset in these frames. Frame pacing, smoothness, and GPU appearance in game are not yet observed.

## Changing sections and Settings

Choosing another tab riffles a quick burst of pages, like an old flipbook settling on a page: five leaves for a neighbouring section, up to nine for a longer jump, each turning in 0.34 s and starting 0.07 s after the previous one. A later section flips right-to-left, an earlier one left-to-right, the same way as turning pages by their sides. The first leaf carries the old spread away and the last brings the new one in; the leaves between are blank, and pages not yet covered or already revealed stay live underneath. Tabs and page clicks are ignored while a riffle runs, and reduced motion switches instantly. The Passport section keeps its reading position while you visit other sections.

Settings are pages of the book. The printed Settings mark riffles forward to them; the left page has a **Back to …** link that riffles back to the section and passport spread you came from, and the right page holds the options. **Reduce motion** is a printed checkbox that saves immediately; if saving fails, a **Try saving again** link appears. Dalamud's settings button and `/wanderveil settings` open the passport straight onto these pages, opening the cover if it was closed. The tab ribbon rolls up while Settings are shown, since they have no tab.

- [A riffle in flight](previews/passport/riffle.png)
- [Settings pages](previews/passport/settings.png)

## Editions

The passport comes in six nation editions: **Eorzea**, **Gridania**, **Ul'dah**, **Limsa Lominsa**, **Ishgard**, and **Garlemald**. Each has its own cover (cloth, foil, decoration, crest, and lettering such as *Ul'dah / Thanalan* or *Garlean Passport*), its own banner with a nation emblem at its foot (Eorzea's crystal, Gridania's sprig, Ul'dah's scales, Limsa Lominsa's anchor, Ishgard's lance, Garlemald's cog) and map page, its own book cloth, and its own ink for the printed controls on the paper (links and toggles: Ul'dah's are wine red, Garlemald's crimson). Choosing one is only how the passport looks; it says nothing about the character.

- **A new passport** starts unissued, with the shared Eorzea cover. Opening it shows *Choose your passport*: what an edition is on the left, and the six covers on the right, each named with its region (one page holds both when narrow). Clicking a cover issues the passport in that edition: the unissued book is put away (it closes and slides down) and the new edition's passport slides up in its place, closed, showing its cover. Passports saved before editions existed are Gridania and stay so. [The choosing spread](previews/passport/edition-issue.png).
- **Changing it** is in Settings, under *Passport edition*: the six covers in a row (hover names each), with the current one underlined. Picking another asks first, naming what changes and what stays: *Change to Garlemald? Its cover, crest, colours, and opening pages replace Gridania's, and your symbols come off. Your details, picture, cover line, and stamps stay as they are.* **Change to …** or **Keep …** answers it. Confirming exchanges the book: the old passport closes and slides away, then the new edition's passport slides up from below, closed; opened, it is still at Settings. Afterwards *Your previous edition was …* offers **Change back to …**, which exchanges them again. With reduced motion the new passport simply appears. Summoning the passport mid-exchange keeps the new edition. [The confirmation](previews/passport/edition-settings.png).
- **What a change resets** is the appearance: the edition's own design, the colours, and the symbols, and later the structure choices that will layer over it. Everything the holder wrote or chose stays: details, signature, picture and its frame, the cover line, stamps, and where they are placed (user decision, 20 October 2026).

On the Eorzea and Garlemald covers, which carry a chip mark at the foot, the maker's mark (*WANDERVEIL*) sits above the chip. The nation pages' footer offers nothing to arrange (only **Decorate**, and **Place stamp** when a stamp is waiting); the identity pages offer **Edit** and **Decorate**. The open hint on a closed cover runs down the board's whole right edge. [Every edition's opening spread](previews/passport/editions-open.png). The six editions' plates are embedded, about 14 MB more than Gridania alone (the plugin is about 22 MB). Each edition's textures load only when first drawn. The editions' words and colours are in `Features/Passport/PassportEdition.cs`, matching `tools/passport-art/editions.mjs`; the edition's region names follow the game's geography loosely and are not checked against official lore.

## Settings categories

Settings are pages of the book, in four categories. Opening them swaps the tab bar: the four section tabs sink behind the book, then four category tabs rise in their place, each marked by a symbol rather than a word (hover names it). **General** is a gear, **Customization** a painter's palette, **Layout** a laid-out page, and **Saved data** a storage box. Choosing another category riffles a few leaves, as choosing another section does, and the Settings pages remember the last category for the session. Leaving Settings (**« Back to …**, at the top of every category's left page) swaps the tabs back before the section's ribbon unrolls. With reduced motion the swap is instant. [Tabs rising](previews/passport/settings-tabs-rising.png).

- **General:** Reduce motion.
- **Customization:** the edition and **How it looks** on the left page, the colours on the right ([Customization](previews/passport/settings-customization.png)). While a change of edition waits for its answer, the preview and colours step aside, since the change would reset them.
- **Layout:** the identity spread's layout on the left page; the pages' background and the nation pages on the right ([Layout](previews/passport/layout-nation-settings.png)).
- **Saved data:** how many stamps are kept and whether they are saved, and whether the passport's details are saved, with anything loading them found and a retry when saving failed ([Saved data](previews/passport/settings-saved.png)).

## Identity layout

The identity spread can be laid out three ways, chosen under Settings' **Layout** from small sketches of the spread (hover one for what it is): **Classic** (portrait and signature on the left page, details on the right; the default), **Mirrored** (the same, sides swapped), or **Compact** (the portrait beside the name, home world, data center, favorite job, and free company on the left page, the signature beneath; the passport's record on the right: issue date and number, the wish for the road, about the adventurer, and the machine line). **Explore discoveries** always ends the details' page. Editing works the same in every layout; each page still shrinks to fit its paper. The layout is part of the appearance: a change of edition returns to Classic and **Change back** restores it. Symbols stay on the page they were placed on, whichever layout is printed there. Previews: [mirrored](previews/passport/layout-identity-mirrored.png), [compact](previews/passport/layout-identity-compact.png), [compact while editing](previews/passport/layout-identity-compact-edit.png).

## Nation pages

The nation spread (the banner and the map) can be rearranged under Settings' **Layout**, **Nation pages**: the **Banner** and **Map** boxes show or hide each, and **Swap sides** puts the map on the left and the banner on the right. A hidden one leaves its page blank paper (printed on the background), free for symbols; a line under the controls says what each side shows. Symbols stay on the page they were placed on. The arrangement is part of the appearance: a change of edition returns to the banner left and map right, and **Change back** restores it. A passport not yet issued still opens on choosing its edition there. Previews: [the banner alone, on the right](previews/passport/layout-nation-swapped.png), [swapped](previews/passport/layout-nation-map-left.png).

## Page background

Every page of the passport (stamp pages and Settings included) is printed on one background, chosen under Settings' **Layout** from small sheets of paper showing each: **Rings** (the edition's guilloche, as before and by default), **Plain**, **Lined**, **Grid**, **Dots**, **Zigzag**, **Waves**, or **Emblems** (the edition's own emblem repeated: Gridania's sprig, Limsa Lominsa's anchor, and so on). Patterns print in the rings' colour (the **Rings** row in Customization, kept faintly visible by the guard) and as faintly as the rings, beneath everything on the page, symbols included. They turn with the page. The background is part of the appearance: a change of edition returns to the rings and **Change back** brings it back.

How it works: the rings are printed into the open-book plate, so for any other background the plate is recoloured with its rings in the paper's own colour, which inks them out. The pattern is then drawn live over the paper (`UI/Components/PagePattern.cs`), on resting pages and, warped with the content, on turning leaves; long lines are drawn in short pieces so they bend with the paper. Emblems come from the symbol plate. Previews: [waves](previews/passport/layout-waves.png), [emblems](previews/passport/layout-edition.png), [mid-turn on zigzags](previews/passport/layout-turn.png). In the headless previews a few emblem stems, a pixel thin at that size, drop out; the game's own texture filtering is expected to keep them but is not yet observed.

## Colours

Customization's right page has **Colours**: seven rows, one per part of the passport, each named beside its swatches. **Cover** is the cloth of the cover, spine, book edges, and banner. **Foil** is the cover lettering, crest, decoration, and banner trim (and symbols placed on the cover). **Accent** is the edition's second colour in its art: Gridania's leaves, Ul'dah's wine details, Limsa Lominsa's brass, Garlemald's red, in the crests, cover decoration, banner emblem, and the map's land; it is decoration, so the guard never moves it ([a teal accent](previews/passport/colour-accent.png)). **Page ink** is the printed links and controls on the paper. **Paper** is the pages. **Text** is the text on the pages and its captions (a softer shade of it); its first swatch is **Automatic** (dark on light paper, light on dark). **Rings** is the paper's guilloche; **Automatic** follows the text. Each other row starts with the edition's own colour (marked with a dot), then curated swatches, then a custom colour: its wheel opens a colour picker (hue bar and hex). Dragging previews on the swatch, and letting go keeps the colour. A pick is kept and saved at once; **Use … 's own colours** returns them all to the edition's. Colours are part of the appearance, so changing edition returns to the new edition's colours, and **Change back** brings the old ones back with the old edition. While a change of edition waits for its answer, the colour rows step aside. [Colours in Settings](previews/passport/colour-settings.png), [a recoloured Gridania](previews/passport/colour-recoloured.png).

- **Keep colours readable** (a checkbox under the rows, on by default) is the legibility guard below. Ticked, a line of small boxes says which colours it **may adjust** (Cover, Foil, Ink, Paper, Text, Rings; all at first): of two colours that do not read together, the guard moves the one it may, preferring foil over cover and text or ink over paper, so untick Text and a dark paper lightens for the text instead. **Rings** keeps the paper's rings faintly visible: rings too close to the paper (gold on cream, say) move just far enough to stand 3:1 from it, which at their 5% printing shows as faint texture; the paper never moves for them, and the note says *Rings adjusted to stay visible.* Unticked, every colour is used exactly as picked, even where text becomes hard to read, and the page says so ([wine text on black paper, unguarded](previews/passport/colour-unguarded.png)). The Settings pages themselves always stay readable (their text and their page plates both), so these controls can always be found again; **How it looks**, on Customization's left page, shows a small cover and page in the exact colours the passport will use. **Use … 's own colours** returns every colour to the edition's and keeps the guard's settings.
- **Legibility.** The paper is always as picked: on a dark paper, text and captions print light instead (*On this dark paper, text prints light.*), so black paper with white text works ([black paper](previews/passport/colour-dark-paper.png)); the engraved passport number is pressed into whatever the paper is, and the paper's guilloche rings turn light too, so they show on dark paper as they do on cream. Automatic text and captions always read on the paper (4.5:1; black or white text reaches that on any paper); picked text is moved toward black or white if it would not. The foil must read on its cloth (3:1) and page ink on the paper (4.5:1); a colour that would not is moved toward white or black just far enough, and the row says *Adjusted to stay readable: …*.
- **Material-aware.** The plates are recoloured, not tinted: the cloth keeps its weave and grain, embossing and shadows stay, and lettering stays live. `tools/passport-art` writes, for each plate, a grey weight map per recolourable colour (cloth, foil, paper, the rings, and the accent): how much of that colour reaches each pixel, the difference between rendering the plate with that colour white and black. Compositing is linear in each colour, so the plate in new colours is the printed plate plus weight × (new − printed). The generator checks this against rendering the new colours directly, within 1.4 / 255 on average for every edition; the worst pixels, about 16 / 255, are soft filtered edges, where the renderer's own linear-light filters round dark colours differently for each colour. The edition's accent is recoloured too (its own row).
- **In the client.** A colour change asks for the current edition's five plates recoloured. A background task reads them and their weight maps from the plugin (a small PNG reader of its own), recolours them, makes the reduced copies by coverage-weighted averaging, and uploads them as textures. They are swapped in at the start of a frame, and the set they replace is freed two frames later. Until the new set is ready, the previous recoloured set of the same edition stays (so a change never flashes the printed colours), otherwise the printed plates. The edition shelf always shows each edition as printed. The weight maps add about 6.6 MB (the plugin is about 29 MB).

## Symbols

The cover and the first four pages (the nation pages and the identity spread) can be decorated with symbols from a provided set of 24: the six nations' crests, their six emblems, and twelve motifs (sparkle, shard, moon, sun, leaf, waves, snowflake, key, flower, feather, mountains, heart). Every symbol is available whatever the edition. Symbols follow the passport's colours: on the cover they are pressed in its foil (with the same embossed edge as the lettering), on the pages printed in page ink. On a page they lie on the paper beneath everything printed there (banner, map, text, picture), above only the paper's rings; on the cover, beneath the lettering and crest. They turn with the cover and the pages.

- **Decorating.** On the first pages the footer offers **Decorate** (beside **Edit** on the identity spread); on the closed cover, a small sparkle mark beside −, +, and × (hover names it **Decorate**), as quiet as they are; while decorating, the cover offers **Done** and the sheet in words. Either opens a sheet of the symbols, laid over the right page (the only page, when narrow) or over the cover. Picking one slides the sheet away with the symbol in hand; a click puts it down where the mouse is, and the sheet comes back for the next. The mouse wheel turns the symbol in hand; Escape or a right click puts it back on the sheet. **Close sheet** (or Escape) puts the sheet away to show the symbols placed: a click picks one up again (or a drag carries it), even to the other page; its round knob turns it (drag round) and resizes it (drag in or out, from 5% to 60% of the page's width), the symbol turning and growing as the knob moves ([held](previews/passport/symbols-knob-held.png)); its × takes it off; and its colour dot (a quarter turn the other way) opens its colours: the passport's own (it then follows the foil or page ink, as by default), graphite and the six inks, or any colour from a picker, which previews on the symbol while dragged ([a symbol's colours](previews/passport/symbols-colour.png)). Escape closes the colours alone. **Done**, Escape, turning on to the stamp pages, or changing section ends decorating; closing the book to its cover carries on decorating there. A symbol stays whole on its page or cover, as a stamp does. Each page and the cover hold up to 24.
- **Edition.** Symbols are part of the appearance: changing edition clears them, and **Change back** brings them back. Resetting colours leaves them.
- **Removing them all.** Under the colours, Customization's **Symbols and drawing** says what is placed and drawn, and offers **Remove all symbols** and **Remove all drawing** (each only when there is something to remove). Each asks first (*Remove all 3 symbols from the cover and pages? This cannot be undone.*), answered by **Remove them** or **Keep them** (**Remove it** or **Keep it** for drawing, which also forgets Draw mode's undo, so the warning holds). [Symbols](previews/passport/symbols-clear.png), [drawing](previews/passport/drawing-clear.png). A moved symbol keeps its own colour. On the closed cover, the symbols' handles are drawn in the book's own window, so the cover's marks still take clicks with symbols near them.

Previews: [the sheet](previews/passport/symbols-sheet.png), [decorating](previews/passport/symbols-decorating.png), [placed on the pages](previews/passport/symbols-pages.png), [on Ul'dah's cover](previews/passport/symbols-cover.png). The symbol plate (`symbols.png`, a 6 × 4 grid of white masks with half and quarter copies, about 0.3 MB) is generated by `tools/passport-art` from the crests and emblems and `svg/symbols.svg`; the client tints it as it draws.

## Your identity

The identity spread is yours to fill in. A new passport is issued the first time the plugin loads: blank, dated that day, with its own passport number (*EZ* and eight digits, drawn at random once and never changed). That number is printed on the identity page and engraved on every stamp page. Until you write anything, the information page says *Not filled in yet. Choose Edit to write your details.* Empty fields print blank.

**Editing.** On the identity spread the footer offers **Edit**. The pages are then written on in place: name, home world, data center, favorite job, free company, motto, and the few words about you, each typed over a faint inset in the page's own type, wrapping at word breaks as in print. Enter or Tab leaves a field. Everything you type is kept at once and saved in the background, with no separate save step. **Done**, Escape (outside a field), turning the page, or changing section ends editing. While editing, a click on the paper only leaves a field; it never turns the page. Each field has a limit (40 characters for the name, 24 for home world and data center, 120 for the motto, 400 for the bio). Line breaks are not kept, and spaces are tidied.

- **Fill in from my character** copies the logged-in character's name, home world, and data center (read from Dalamud's player state on the game's thread) once. They are then yours to change. Nothing on the page is treated as verified: the passport is self-reported. Without a logged-in character, nothing changes and the page says so.
- **Signature.** The signature line signs in the handwriting face (Caveat). Left empty, your name signs, as before. Typing a signature replaces it.
- **Picture.** **Choose a picture** opens Dalamud's file picker for a PNG, JPEG, WebP, or BMP up to 16 MB. The picture is read, decoded, and copied beside the passport on a background task, so the game never waits on it. A picture taller than 1200 pixels is scaled down once for display. It then fills the 3:4 portrait frame. Drag it to move it, and scroll over it to zoom (up to 4×); it always covers the frame. **Remove picture** takes it out and deletes the copy, and the placeholder returns. A file that is too large or not a picture says so under the frame. So that a picture never sits on the paper as a bare rectangle, it is set in a frame, chosen in Edit mode from the row of words under the picture (hover one for what it is): **Printed** (the default: an ink keyline around a slightly lighter mount, with a soft shadow, like a printed passport photo), **Corners** (black album photo corners), **Embossed** (a mount pressed into the paper, no ink), **Deckled** (the picture's own edge torn rough), or **None**. The frame stays within the portrait area, and the picture is dragged within the frame's opening. All of it is drawn geometry, so it turns with the page. See [the five frames](previews/passport/portrait-frames.png).
- **Take a picture.** Beside **Choose a picture**, **Take a picture** captures the game itself. The open book slides down out of the way (staying open, on its page, still editing), and the screen dims around a 3:4 frame guide with a small panel: **Take**, a countdown (**No countdown**, **3 s**, **5 s**, or **10 s**; 3 seconds at first, kept for the session), and **Cancel**, with a tip: pose anywhere; in group pose (`/gpose`) the game's HUD hides and you can choose poses and expressions, and elsewhere the game's Hide UI key (Scroll Lock by default) hides it. Only the panel takes the mouse, so the camera can still be turned around the guide. **Take** (after the countdown, shown large in the guide) captures what is inside the guide, and the book slides back up with the picture in its frame, ready to move and zoom like a chosen one. **Cancel** or Escape, also mid-countdown, brings the book back with nothing taken. While the guide is open it stays visible in group pose (Dalamud otherwise hides plugin windows there). The capture uses Dalamud's own viewport capture, taken before any plugin window draws, so neither the guide nor the panel (nor the passport) is in the picture; the game's own HUD is, unless hidden. It is saved as PNG through Dalamud's encoder and kept like a chosen picture. Previews: [the guide](previews/passport/camera-guide.png), [counting down](previews/passport/camera-countdown.png), [back with the picture](previews/passport/camera-taken.png) (the checks stand in a crest for the capture, which needs the game). The game's own portrait editor (the adventurer plate's) is not used: Dalamud offers no way to read its image, and reaching into the game for it would break with patches.
- **On the cover.** An optional line, up to 28 characters, is embossed in the cover's foil between *The Black Shroud* and the maker's mark, set smaller when long so it stays inside the vine border. **Use my name** fills it with your name; left empty, the cover is as printed.

Previews: [a new passport](previews/passport/identity-blank.png), [editing](previews/passport/identity-edit.png), [filled in from a character](previews/passport/identity-filled.png), [a placed picture](previews/passport/identity-picture.png), [a name on the cover](previews/passport/cover-line.png). The picker, picture loading, and character reading need the game and are not covered by the headless checks; the checks use stand-ins for them.

## Collecting and placing stamps

The first product loop runs in the book: open Discover, open **The scenic route** postcard, and choose **Collect demo stamp** beneath it. The stamp is issued at once: the postcard's faint proof of the design becomes the inked stamp (*Your stamp*, *Collected 19 OCT 2026 / Local demo*), its card in the list says *Collected*, and a line beneath says whether it is saved. Keyboard focus moves to **Place it in your passport »**.

**Stamp pages** follow the identity pages. They are blank paper with only their page numbers and the passport number (*EZ 0042 1187* in the sample) engraved in pale typewriter letters at the lower right as on the printed design (a stamp pressed over it covers it): no fixed places, no printed captions, nothing else the owner did not put there. The passport has 100 pages for now (pages 5 to 100 are stamp pages; turning stops at the last spread), and End jumps to the last page with a stamp. To go to any page, click a page number at the foot of the book, type the page, and press Enter; numbers outside 1 to 100 go to the first or last page, and Escape or clicking away leaves it. A jump of more than one spread riffles through the pages in book order, as when changing sections (End jumps the same way); a neighbouring spread simply turns. This works with a stamp in hand too, limited to the stamp pages. Only pages with stamps are saved (see below). The identity page shows the same passport number.

**Placing.** The link opens the first empty page after your last stamp with the new stamp in hand: it follows the mouse, faintly, with a shadow in its own shape. Click anywhere on a stamp page to press it there. While it is in hand the mouse wheel turns it, Page Up/Down turn pages, and resting at the book's outer edge turns the page there, so you can carry it to any page. A held stamp keeps to the stamp pages: the book will not turn or jump back past page 5 or close to its cover until the stamp is put down or back. It keeps its size while pages turn beneath it. Escape, a right click, or **Put back** in the footer returns it. In game the plugin takes Escape from the game's key state whenever the book has a use for it (a stamp in hand, stamp info open, or the book focused), so the game's system menu does not open as well. A stamp not placed yet waits: the footer then offers **Place stamp** (with a count when several wait), which picks the next one up the same way. Once placed, the postcard's link reads **See it in your passport »** and opens its page.

**Stamp info.** Click a stamp to open its info page over the opposite page (over its own page when only one page shows), and click again, **Close**, or Escape to close it. It shows the stamp, its name, when it was collected and on which page, what it came from (*Postcard: The scenic route*), and who designed it (*Ari Vale / Demo creator*), with a note that it is a local demo stamp, not an official achievement. Turning the page or changing section closes it.

**Arrange mode.** The footer's **Arrange** turns it on (**Done** turns it off), so stamps never shift by accident. While arranging, drag a stamp anywhere on the open pages, including across the spread; or click it to pick it up and carry it as above. Each stamp shows a round knob: drag it around the stamp to turn the stamp. A quarter turn round from the knob, a small × takes the stamp off its page: it is never deleted (an earned stamp stays in the passport) and waits, offered by **Place stamp**, to be placed again. Next to a page edge, the knob and × stay on the page rather than making it scroll. A moved stamp lands on top of the others. Outside Arrange mode, a drag on a stamp does nothing and dragging the paper moves the book as before.

Duplicate policy: a passport holds at most one stamp per discovery. Once collected, the postcard offers no Collect action, and collecting again (even after its design is revised) returns the stamp already held unchanged. **Where the lanterns wait** is a stamp spot: its stamp is collected at the place itself, which needs location awareness Wanderveil does not have, so its Collect action is printed but inert with that reason.

`StampDesign` (identifier, revision, name, designer, artwork, provenance), `StampInstance` (its own identifier, the design revision it was issued with, the discovery and what it was, the date, provenance), `StampPlacement` (page, place across and down the page from 0 to 1, turn), and `StampCollection` (the stamps, their placements in layer order, and the collect and place rules) hold no ImGui state. `BookShellStamps` draws and handles the stamps on stamp pages, Arrange mode, carrying, and the info page. Stamps scale with the page, at about a third of its width, and stay wholly on their page. Everything is local demo provenance. Settings shows how many stamps you hold, how many wait to be placed, and whether they are saved.

- [Collected postcard](previews/passport/collect-discover.png)
- [Placing it: the stamp in hand](previews/passport/collect-placing.png)
- [Placed on the first stamp page](previews/passport/collect-passport.png)
- [Its info page, opposite](previews/passport/collect-info.png)
- [Its info page on a single page](previews/passport/collect-narrow-info.png)
- [Arrange mode, with a turning knob and ×, and the engraved number](previews/passport/collect-arrange.png)
- [Carrying a stamp to a later page](previews/passport/collect-carry.png)

## Drawing and writing

The stamp pages can be drawn and written on (the nation and identity pages cannot; user decision, 20 October 2026). On the stamp pages the footer offers **Draw**, beside the stamp control. Draw mode turns the footer into a toolbar (on a narrow page it lies just above the footer, on its own paper), with **Done** at its end:

- **Pencil**: hold the mouse on a page and draw. The line is a grainy pencil: the paper's tooth shows through it, the same way under every stroke, and it tapers where it starts and ends. The mouse is smoothed and sampled every couple of pixels; moving slowly presses harder (a wider, darker line) than moving fast.
- **Eraser**: rubs out only what it passes over, so a stroke crossed in the middle becomes two. A line of writing it touches goes whole. A circle shows its size.
- **Text** (*Aa*): click a page and type a line in the handwriting face; Enter or clicking away keeps it, Escape leaves it as it was. Clicking a line rewrites it; emptying it takes it away.
- **Palette**: graphite, the passport's page ink, sepia, red, blue, green, and white, then **any colour** (the wheel), picked in a popup. **Eyedropper**: shows the colour of the line or writing under it and, clicked, takes it up (its palette swatch, or as the custom colour) and hands back the tool you had. **Sizes**: fine, medium, bold (a line's width, the eraser's size, and writing's height).
- **Undo** (the curved arrow, or Ctrl+Z) takes back the last change, an eraser's whole drag as one; it remembers the session's last 64 changes.
- **Clear** (the bin) offers the left or right page shown (or this page, when one shows) and clears its drawing and writing; Undo brings it back.

Drawing reaches the paper's left, right, and top edges (a little in from them), not only the page's printed area; the foot keeps its margin for the footer. Existing marks stay where they were: they are measured from the page's printed area and may reach up to a quarter of its width past it. While drawing, the page takes the mouse, so a click never turns the page or moves the book; Page Up and Page Down still turn. **Done**, Escape, turning back to the identity pages, closing the book, or changing section ends Draw mode. Drawing lies above the stamps and never changes them; it turns with the page. Each page holds up to 20,000 pencil samples and 30 lines of writing (120 characters each), the passport 200,000 samples; past that the page says it is full. A page at its limit draws in about 1.9 ms a frame headless (best of three), against 0.02 ms empty. Previews: [drawing](previews/passport/drawing-pages.png), [done](previews/passport/drawing-done.png), [a single page](previews/passport/drawing-narrow.png), [mid-turn](previews/passport/drawing-turn.png). The approach was chosen by [the brush spike](drawing-brush-spike.md).

Drawing is saved in its own file, `marks-local.json` (`wanderveil.passport-marks`, version 1), apart from the stamps and the identity, so a change of edition never touches it. Strokes are kept as vectors (page, colour, width, and their points as one flat list of x, y, pressure in page-width units, to four decimals); writing as its page, text, place, size, and colour. It is saved and recovered like the stamps (`SafeFile`, `BackgroundSaver`), and Settings' **Saved data** says how much there is and whether it is saved, with a retry when saving failed.

## Saving stamps

Collected stamps are saved on this PC and come back when the plugin reloads or the game restarts. They live in Dalamud's configuration folder for Wanderveil (`pluginConfigs/Wanderveil/` under the XIVLauncher folder) as `stamps-local.json`. There is one local passport for now; files are named by profile so character-scoped passports can follow without a format change. Nothing leaves the PC.

The file is versioned JSON, written readable:

```json
{
  "format": "wanderveil.passport-stamps",
  "version": 2,
  "stamps": [
    {
      "id": "local-demo-0001",
      "discoveryId": "demo-scenic-route",
      "collectedAt": "2026-10-19T18:30:00.0000000+02:00",
      "provenance": "local-demo",
      "source": { "kind": "POSTCARD", "title": "The scenic route" },
      "design": { "id": "demo-scenic-route-stamp", "revision": 1, "name": "Scenic route", "artwork": 0, "provenance": "local-demo", "creator": "Ari Vale / Demo creator" }
    }
  ],
  "placements": [
    { "stamp": "local-demo-0001", "page": 21, "x": 0.3, "y": 0.7, "rotation": -0.4 }
  ]
}
```

Each stamp keeps a copy of the design revision it was issued with, so a later design change never alters it. Only `local-demo` provenance is accepted, so a hand-edited file cannot add official or online stamps.

Placements are sparse: one entry per placed stamp, in layer order (the last is on top). `page` counts stamp pages from 0 (the first after the identity pages, printed page 5), `x` and `y` place the stamp's centre across and down the page from 0 to 1, and `rotation` is its turn in radians. The example's stamp sits on page 21 of an otherwise empty book, and nothing is written for the empty pages before it. A stamp with no entry is waiting to be placed.

- **Saving.** Each collect, placement, move, or turn saves a snapshot on a background task, never in a draw callback. Saves run one at a time, and if several are waiting only the newest is written. The file is written to `stamps-local.json.tmp`, flushed to disk, then swapped in one step, and the previous save is kept as `stamps-local.json.bak`. Closing the plugin waits up to three seconds for a save in progress.
- **Interrupted save.** On load, a complete leftover `.tmp` file is the newest save, so the swap is finished. An incomplete one is dropped, since the file it was replacing is intact.
- **Unreadable file.** A file that is corrupt, truncated, empty, not a Wanderveil stamp file, or breaks the rules (duplicate stamps or discoveries, missing fields, a bad date or revision, other provenance, a placement off its page or for an unknown stamp, two placements for one stamp) is never partly loaded. It is renamed aside as `stamps-local.json.unreadable-<time>`, and the backup is restored (one stamp may be missing: the backup is the save before last). If the backup is unreadable too, both are kept aside and the passport starts empty. Settings says what happened.
- **Newer or locked file.** A file from a newer Wanderveil version, or one that cannot be opened (locked or access denied), is left untouched, and saving is turned off for the session. Settings says so; stamps collected or moved meanwhile last until the game closes.
- **Failed save.** If writing fails, the stamp stays collected, the line under it says it could not be saved, and **Try saving again** retries. The error goes to the Dalamud log.
- **Migrations.** Each format bump adds a step that upgrades the older file in memory when it is read; the next save writes the new version, and the backup keeps the old one. Version 1 (before placements) becomes version 2 with every stamp waiting to be placed and no designer or source recorded (the info page says *Not recorded*).

`StampStore` (file format) and `StampPersistence` (load at startup, background saves, state) live in `Infrastructure/Storage/`. The pages see only `ISavedState`: what loading found and whether saving works.

## Saving your identity

The identity is saved beside the stamps as `passport-local.json`. Your picture, if any, is saved as `portrait-local.image`, a copy of the file you chose. Both are in their own files, apart from the stamps, so a later appearance reset (changing the starting passport) can never touch a stamp. The identity file is versioned JSON:

```json
{
  "format": "wanderveil.passport",
  "version": 3,
  "number": "EZ 3383 2844",
  "issued": "2026-10-20",
  "name": "Wren Halloway",
  "homeWorld": "Faerie",
  "dataCenter": "Light",
  "favoriteJob": "",
  "freeCompany": "",
  "motto": "Onwards",
  "bio": "",
  "signature": "",
  "coverLine": "",
  "portrait": { "x": 0.5, "y": 0.5, "zoom": 1 },
  "frame": "printed",
  "appearance": {
    "edition": "gridania",
    "colours": { "cloth": "#5A1424", "paper": "#E6DCC6" },
    "symbols": [ { "symbol": "moon", "on": "cover", "x": 0.8, "y": 0.1, "turn": 0, "size": 0.18 } ]
  },
  "previousAppearance": { "edition": "uldah" }
}
```

`portrait` is where the picture sits: the picture's point at the frame's centre (0 to 1 across and down) and its zoom past just covering the frame (1 to 4). Without it, the placeholder shows. `frame` is one of `printed`, `corners`, `embossed`, `deckled`, or `none`; a missing or unknown frame (from a later version, say) reads as `printed` rather than making the file unreadable. `appearance` is the passport's edition, absent until a new passport's holder chooses one; `previousAppearance` is the one the last change replaced, for **Change back**. `colours` holds only the parts the holder recoloured (`cloth`, `foil`, `accent`, `ink`, `paper`, `text`, each `#RRGGBB`), `lines` for the rings, `"readable": false` when Keep colours readable is off, and `"exact": ["text", …]` for colours the guard may not adjust; a part left out is the edition's own (automatic, for text and rings). `nationPages` holds the nation spread's arrangement when it is not the standard one (`"swapped": true`, `"banner": false`, `"map": false`). `identityLayout` is `mirrored` or `compact` when the identity spread is not classic (an unknown one reads as classic). `background` is the pages' background when it is not the rings (`plain`, `lined`, `grid`, `dots`, `zigzag`, `waves`, or `edition`; one this version does not draw reads as the rings). `symbols` lists the placed symbols in drawing order (each with `colour` when it has its own) (later on top): which symbol, where (`cover`, or a first page: `nation-banner`, `nation-map`, `portrait`, `information`), its centre (0 to 1 across and down), its turn in radians, and its size as a share of the width; a symbol or place this version does not know is left out. An edition this version does not know is kept as written and drawn as Eorzea. Version 1 files (before editions) migrate as a chosen Gridania edition; the backup keeps the version 1 file. Version 3 added symbols; a version 2 file reads as it is, and an earlier client refuses a version 3 file (leaving it untouched) rather than saving over symbols it cannot keep. Text read back is held to the same rules as typing, so an over-long field is cut to its limit.

Both files are saved and recovered the same way as the stamps (`SafeFile`): written to a temporary file, flushed, swapped in with the previous save kept as `.bak`, unreadable files kept aside as `.unreadable-<time>`, and newer or locked files left untouched with saving off. A newly issued passport is saved straight away, so its number stays the same. A picture is written only after it decodes, so a saved picture can always be shown. Settings shows a **Passport** note only when something needs saying (recovered, unreadable, newer, locked, or a failed save with a retry).

`PassportStore` and `PassportPersistence` live in `Infrastructure/Storage/`, beside the stamp store. They share `SafeFile` (atomic writes and recovery) and `BackgroundSaver` (coalesced background saves). `PortraitPicture` (`Infrastructure/Media/`) loads and swaps the picture's texture at the start of a frame, and `GameCharacter` (`Infrastructure/Game/`) reads the character. The pages see only `IPassportPortrait`, `ICharacterSource`, and `ISavedState`.

## Echoes

The Echoes tab is an honest preview of future adventures. The left page says what Echoes will be and that it cannot be played yet, then shows a framed sample dossier (*The Lantern Keeper's Letter*, a synthetic three-chapter mystery) labelled *Preview / not playable / nothing is tracked*. The right page lists its chapters: the first is open, marked with an inked ring, with its objectives as hollow printed diamonds (never checkboxes) and one hint; the later chapters carry a wax seal, show only their title and when they would open, and reveal no objectives. A closing line says playable adventures, saved progress, and rewards arrive later. The pages have no controls, so clicking them does nothing; on a single page the chapters follow the dossier.

`EchoPreview` holds the sample and validates that a dossier opens on its first chapter and that sealed chapters carry no objectives; it has no ImGui or host state and is not a content contract. `EchoesView` draws it, with a readable fallback when no dossier is given.

- [Echoes spread](previews/passport/echoes.png)
- [Echoes on a single page](previews/passport/echoes-narrow.png)

## Studio

The Studio tab opens on a template chooser. The left page introduces Studio and says plainly that drafts stay in memory until the plugin unloads and nothing is saved, published, or sent. The right page lists **Postcard** and **Stamp Spot** with a **Start …** link each, then Riddle, Challenge, and Trail under *Coming later* as plain text that cannot be chosen.

Starting a template turns the spread into a draft: printed fields on the left (each a native one-line text input on a ruled line, in the page's own type) and a framed preview card on the right that updates as you type. The cursor starts in the first field; Tab moves between fields. Escape inside a field only leaves it and keeps the text; with no field active it closes the passport as usual. Fields are one line, stripped of control characters, and limited by user-perceived characters (the label shows how many are left near the limit). A status line names the required fields still empty. **Save draft** and **Publish** are printed but inert, with the reason beneath them, so draft, saved, and published stay distinct; the preview is marked *Draft / not saved / not published* and its stamp *a design preview, not an issued stamp*. **« All templates** returns to the chooser and keeps each template's draft (its link becomes **Continue your draft**); visiting other sections keeps the open draft too. A postcard has no picture yet: a placeholder says uploads come later. On a single page the preview follows the fields.

`StudioTemplate` (catalog and fields) and `StudioDraft` (values, limits, missing fields, and the unavailable save/publish states) hold no ImGui or host state; `StudioView` draws them, and `BookControl.Field` is the printed input. Switching between the chooser and a draft is immediate rather than a page turn.

- [Template chooser](previews/passport/studio-templates.png)
- [Postcard draft and preview](previews/passport/studio-postcard.png)
- [Stamp spot draft with a missing field](previews/passport/studio-stamp-spot.png)
- [Postcard draft on a single page](previews/passport/studio-narrow.png)

## Input ownership

The passport's window is a transparent rectangle larger than the book (room for the tabs, the plate's shadow, and the closed cover's margin). Only the parts the book actually draws take the mouse: the board of the open book (`BookSurface.Board`, the plate's opaque area), each tab with its ribbon, the closed cover's board, and, while the cover swings, the whole window. Anywhere else the window sets `NoMouseInputs`, so clicks, scrolls, and hovering pass through to the game. A press, drag, or edit that began on the book keeps the mouse until it ends, so dragging the book past its edge keeps moving it, and while a Studio field is active the first click off the book only leaves the field. These regions are rectangles, not per-pixel shapes, and they are decided from the previous frame, so there is one frame of lag as the mouse crosses an edge.

While a Studio field is active ImGui reports that it wants text input, which is what the host uses to keep typed keys from the game; leaving the field hands them back. The book's own keys (Escape, Page Up/Down, Home, Space/Enter on the cover) never act while a field is active. Whether the game still receives movement keys while the book merely has focus (no field active) depends on Dalamud and is not yet observed in game.

## Empty, missing, long, and Japanese content

Each section explains its own empty state on its pages: Discover says the sample journal is empty, Echoes says there is no sample dossier and that chapters appear once there is one, and Studio says no template can be started when none is available (later templates are still listed). Nothing in these states is fetched. `BookShellContent` carries the discoveries, dossier, and templates into the shell, so these states are exercised in the real book.

While artwork loads, or if a plate cannot load, the book draws itself instead: an edition-cloth cover board with a foil frame and the usual foil lettering, and an open book with cloth board, paper, and a soft gutter. Turning pages and the cover swing keep plain paper, and pages that need art say *Artwork is loading or unavailable*. Previously a missing cover was a flat beige rectangle.

Long text wraps inside its paper, and words too long for a line are broken anywhere (ImGui's rule). A long holder name signs smaller, down to half size, and then wraps rather than being cut off. Very long pages scroll inside their paper. Studio fields hold each field's limit; their single line scrolls sideways while typing, and the preview wraps.

Text the decorative faces cannot draw, such as Japanese, falls back to the host font; in game that is Dalamud's default font, whose coverage depends on the client's language (Japanese is expected, Korean and Chinese are not verified). The headless checks stand in local Yu Gothic for it. Japanese lines break anywhere rather than by Japanese line-breaking rules, and interface labels themselves are English only; there is no localization yet.

- [Cover without artwork](previews/passport/states-missing-art-cover.png)
- [A page turning without artwork](previews/passport/states-missing-art-turn.png)
- [Long identity text and signature](previews/passport/states-long-identity.png)
- [Japanese identity](previews/passport/states-japanese-identity.png)
- [Japanese Studio draft](previews/passport/states-japanese-studio-draft.png)
- [Echoes with no dossier](previews/passport/states-empty-echoes.png)

## Page turns (Milestone 4 experiment)

Clicking a page side (or Page Up/Down) turns the page as a paper leaf rotating about the spine in 0.5 s. It curls as it goes: its outer edge trails the motion, most while it stands upright, so the page bows into an arc and lies flat again as it lands. Perspective, light that varies across the curve, and a shadow on the paper it hangs over complete it. The leaf carries its real page content: the front shows the page being turned, the back the page it lands as, including text, portrait, stamps, and the nation banner. The two pages that stay put remain live and readable throughout. On a single page, the page swings away to the left about its outer edge and fades, revealing the next page beneath it. Clicking again while a page turns lifts the next leaf right behind it rather than cutting the first short, so quick clicks turn page after page with every leaf seen, like a short riffle ([several leaves in the air](previews/passport/turn-burst.png)). Leaves already over on the left lie with the latest on top, and those still on the right with the earliest on top. Clicking the other way sends the newest leaf back from where it is; another click sends back the one before it. At most six leaves are in the air; past that the oldest lands at once, so holding Page Down cannot pile up pages. Reduced motion turns instantly.

How it works: ImGui draws child windows above their parent and cannot transform a child's contents, so the turning leaf gets its own child window, created after the static pages so it draws above them. Its paper is drawn as perspective strips; its page content is composed flat at the page's resting place exactly as it would be on the page, then every vertex it produced is moved through the same curled hinge projection (`LeafProjection.Warp`) and its clip rectangles are widened. While on a turning leaf, large art (portrait, stamps, map, banner) and rules are drawn in 16 vertical strips (`BookSurface.Strips`) so they bend with the paper; glyphs are already small. The leaf's child window uses the same padding and scrollbar reservation as a resting page, so content wraps identically and does not shift as a turn starts or lands. No render-to-texture, renderer dependency, or GPU hook is involved.

Measured in the headless checks: about 0.12 ms of CPU and 3,272 vertices to build a turning spread frame, against 0.04 ms and 1,544 for a still spread. GPU cost and frame pacing in game are not measured.

Limitations of this approach:

- The curl is a single circular arc across the page's width; real paper also bends diagonally from a corner. That would need a 2D tessellation of the page rather than vertical strips.
- The warp is perspective-correct at each vertex but straight within each triangle. Glyphs, paper strips, and stripped art are small enough that this is not visible at normal sizes.
- Content on the leaf is not interactive while it moves, and it shows the page from its top: a scrolled page (only possible with enlarged host text) is drawn unscrolled during the turn.
- While a leaf is in motion its geometry may draw a few pixels beyond the book's window, because perspective enlarges the lifted edge; nothing is clipped at the window edge.

Recommendation: this ordinary-ImGui approach is good enough for the passport's page turns, so CrystalRenderer is not needed for them. Revisit a renderer only if curled pages or real lighting become a goal.

- [Identity page lifting with its content](previews/passport/turn-lifting.png)
- [Page curled while upright](previews/passport/turn-curl.png)
- [Stamp page landing on the left](previews/passport/turn-landing.png)
- [Single page swinging away](previews/passport/turn-single.png)

The first page is a full-height nation pennant hung from the top edge of the paper down to the lower margin, with the crest and foil lettering drawn live over the generated banner plate.

Artwork is drawn from the generated size copy closest to its on-screen width (full, half, quarter, or eighth), so plates are never shrunk more than about 2x and their edges stay smooth instead of aliasing. While a newly needed copy loads, the nearest loaded copy draws.

A turning leaf draws its page flat in a window of its own and then warps it onto the curling paper, which means lifting the window's clip. Before the warp, `LeafProjection.Crop` cuts what was drawn down to the page's window, so a page that scrolls (Discover's list, or any long page) turns with only what was showing; previously the parts scrolled out of view appeared below the book while it riffled (found in game, 19 October 2026). Rectangles, text, and images are cut exactly, with their texture coordinates moved to match.

- [Discover's scrolling page riffling away, cut at its page](previews/passport/states-scrolled-riffle.png)

## Text rendering

Each font is built at every pixel size that is actually drawn (rounded to whole pixels, at most 48 sizes), instead of one 24 px font stretched to all sizes, so text stays sharp. A new size draws once at the scaled base font while the atlas rebuilds. Cover lettering takes its sizes, positions, and letter-spacing from the design board, and page contents (text, spacing, artwork) scale with the page width, so a smaller book shows smaller contents instead of scrolling. If a page still would not fit, it shrinks itself to its paper within a couple of frames and grows back when the book is enlarged. Enlarged host text keeps its size and may scroll instead. The headless checks use fixed-size fonts, so their previews are slightly softer than in game.

## Presentation and assets

`PassportDocumentView` owns reusable live cover/page composition. `PassportPage` records supply stable IDs and presentation kinds; `BookShell` snapshots and validates a nonempty catalog with unique IDs, tracks closed/open state and reading position, and renders only visible pages. The demo provides six logical pages; catalog size is not fixed to six. Navigation clamps safely and preserves child scrolling through stable page IDs. New sessions start closed at the first page.

Every edition's cover, banner, map, crest, and open-book plates (as `<edition>-<plate>`), plus the generic cover, portrait placeholder, and stamp atlas, are embedded from `assets/passport/vector/` with their reduced copies; see [vector asset provenance](../assets/passport/vector/PROVENANCE.md). The earlier image-generated plates and Gridania studies stay in the repository for comparison and gallery checks but are not embedded. [Their provenance](../assets/passport/studies/gridania/PROVENANCE.md) still accompanies the package.

The demo signature uses bundled **Caveat**, replacing the gallery's Windows-only Segoe Script surrogate. The unmodified variable font is pinned to Google Fonts revision `5571d84c0d8c70ec1af4f64072d8c5cf1e4e9643`, renamed only from `Caveat[wght].ttf` to `Caveat.ttf`; its SIL OFL notice ships with the plugin. Six base font handles plus up to 48 per-size handles are owned and disposed, including partial-startup/lock failures; shared texture wraps retain host ownership. Draw callbacks perform no file/network access.

## Verification

The native suite exercises closed-cover pointer/keyboard opening, ordered page navigation at wide/narrow sizes, final-page bounds, backward navigation, return-to-cover focus, session reset, expanded/invalid catalogs, style isolation, and existing Discover flow. It also opens the actual built plugin assembly and verifies the selected images/font exist as manifest resources and match preview asset hashes. This directly checks the packaging boundary that caused the previous preview/runtime mismatch.
