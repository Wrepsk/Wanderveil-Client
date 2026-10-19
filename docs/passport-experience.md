# Passport experience

Status: user-selected product/design direction, 18 October 2026. The [static runtime prototype](passport-runtime.md) now starts closed and supports explicit opening and ordered nation/identity/demo-stamp pages. Arrival, cover opening/closing, and dismissal are implemented (see [motion](passport-runtime.md#motion)), as are page turns (Milestone 4) and section riffles; broader customization remains planned. This refines the [visual-fidelity milestones](passport-visual-fidelity.md).

First edition selected: **Gridania**. Its [static review previews](passport-gridania-study.md) establish one cover/colorway and the revised spreads. The selected green edition is now connected to the client; the animated sequence below remains the intended experience.

## Arrival, opening, and dismissal

1. Summoning the passport brings a **closed book upward from the bottom center of the screen**. Its cover faces the user and remains closed after arrival.
2. The user explicitly opens the cover through a pointer or keyboard action. Arrival must not automatically open it.
3. Opening reveals the nation introduction spread. Subsequent page navigation leads to identity and then stamp pages, with page-turn animations in the intended experience.
4. Dismissing from an open spread first closes the book and visibly returns to its cover, then slides the closed book downward off-screen. This is one dismissal sequence; it does not require another click after closing. Dismissing an already closed book slides it down directly.

Intended presentation sequence:

`Hidden → Entering closed → Closed, awaiting Open → Opening → Open spreads → Closing to cover → Exiting closed → Hidden`

Page turning is a transition between logical open spreads, independent of stamp collection or storage. The close control and Escape should eventually request the same dismissal sequence rather than bypassing it. Input/focus ownership, repeated open/close requests, interruption, viewport changes, and unload must have deterministic behavior. Their exact transition policy is part of the motion prototype, not decided by this document.

Reduced motion preserves the closed arrival state, explicit Open action, page order, and dismissal result while skipping sliding, rotation, and page-turn movement. Navigation must remain usable before animation is implemented and if an experimental effect is unavailable.

Decided 19 October 2026: the first summon of a session brings the passport to the screen center; later summons in the same session return it to where it was dismissed, including after dragging, always sliding up from below the screen. Dismissing an open book closes its cover quickly so putting it away does not feel like waiting. The position is not saved across sessions. Whether reopening restores a reading position remains open. The existing movable open-book shell is an implementation baseline, not the final arrival behavior.

## Page sequence

| Order | Left page | Right page |
| --- | --- | --- |
| Closed exterior | Nation-specific, customizable cover | Not applicable |
| First open spread: nation introduction | Nation banner | Nation map |
| Second spread: identity | Prominent portrait with holder signature below | Carefully composed identity information |
| Following spreads: collection | Stampable logical pages | Stampable logical pages |

The introduction is its own spread before identity. The identity layout supersedes the current portrait-and-fields left page / notes right page prototype. Stampable pages follow identity and must expand through logical pagination; a fixed set of images must not limit future collection size. Narrow mode presents these same logical pages in reading order, with an immediate navigation alternative to animation.

The third user reference suggests character name, home world, data center, favorite job, free company, issue date, passport number, motto, and a short biography. These are candidate identity fields, not a finalized schema or authorization to read game data. Portrait and signature belong on the left even as right-page fields are refined. Signature creation/input format remains undecided.

## Nation editions and customization

Passport editions should have distinct nation identities, rather than only swapping a label on one cover. The supplied cover reference illustrates **Ishgard, Ul'dah, Gridania, Limsa Lominsa, and Garlemald**. They are five example design families, not an exhaustive list or a requirement to finish all five before reviewing the next slice. More editions can be added later.

Users should be able to choose an edition and substantially personalize its appearance, including cover colors. A nation edition supplies a recognizable cover composition and associated banner/map; user appearance preferences should layer onto it without changing identity or collected stamp data. Selection represents presentation preference, not verified nationality or a game-affiliation restriction.

Proposed engineering approach: separate edition definitions, material/artwork layers, palette overrides, and logical page content. Avoid baking dynamic text or user color choices into a complete book screenshot. Colorable regions should preserve leather grain, shadows, embossing, and readable cover lettering. Prototype material-aware recoloring on one edition before committing to a broad customization editor. Additional customization controls and their persistence can be scheduled in bounded later slices; the next static composition must already accommodate edition/color variation.

User direction (19 October 2026) extends this. The chosen starting passport can be changed at any time, and changing it resets the passport after an explicit confirmation. Beyond colors, owners can place symbols from a provided set and adjust the passport's structure. Stamp pages start empty and unlimited; when a stamp is collected, the owner chooses which page and where it goes. Owners can draw and write on pages with a pencil-like brush whose grain lets the paper show through, and later invite chosen people to draw on chosen pages. Creators can design the stamps they place for others (shape, texts, and placed symbols within a fixed size); stamps already earned never change. These are scheduled for later slices: local personalization first, sharing and co-drawing once accounts and consent exist.

## Reference interpretation and assets

The user's three supplied images establish:

- Distinct cover compositions and palettes for nation editions.
- A separate nation map spread, with the user's explicit instruction adding the banner on its left page.
- A large portrait/signature page opposite a structured identity information page.

They are composition references, not bundled runtime assets or proof of source licensing. Preserve the established tactile leather/parchment/binding direction while adopting this page structure. Nation cover treatments, banner/map illustrations, and recoloring layers need original or appropriately licensed artwork with recorded provenance. Do not substitute generic placeholder geography for a finished nation map. Initial inspectable compositions may label artwork placeholders honestly. Dynamic typography and controls remain live-rendered.

## Incremental delivery

1. **Static composition:** review one nation cover, banner/map opening spread, revised portrait/signature identity spread, and demo stamp spread. Include a palette variation to test personalization. Establish the visual target before complex motion or a full edition picker.
2. **Logical presentation:** implement closed/open state, explicit Open, ordered page routing, readable narrow layouts, and scalable collection pages using synthetic data. Keep all navigation immediate initially.
3. **Arrival and dismissal:** prototype bottom-center closed arrival, cover opening/closing, and close-to-cover-then-exit. Verify reduced motion, focus, interruption, and viewport fitting.
4. **Separate page-turn experiment:** evaluate one lightweight 2.5D turn for material quality, clipping, perspective, text treatment, and measured performance. CrystalRenderer still needs a separate technical decision.
5. **Customization expansion:** grow edition choice and personalization controls from the reviewed material/palette model. The complete editor, saved appearance, and actual stamp collection/storage require their own implementation slices.

Review previews between milestones. This design update does not implement animation, new runtime pages, nation assets, signature input, a customization editor, or persistence.
