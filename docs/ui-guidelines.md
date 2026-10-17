# UI guidelines

Status: initial design specification with [implemented foundations](ui-components.md): shared tokens/typography, page surfaces/flow, native actions/cards, stamp motif, synthetic Passport identity preview, and static Discover cards/details.

Wanderveil is a warm atlas and physical passport with subtle magical moments. Primary destinations are Discover, Passport, Echoes, and Studio; Settings is secondary. Use paper, ink, brass, stamps, and bookmarks as cues. Keep body text readable and compositions responsive.

## Starter semantic colors

| Role | Sample |
| --- | --- |
| Midnight ink | `#19242E` |
| Deep teal | `#344A52` |
| Parchment | `#D8C5A3` |
| Antique gold | `#B99662` |
| Veil glow | `#83B9B8` |

These are seeds, not approved contrast pairs. Centralize semantic text/surface/action roles, typography, spacing, borders, and motion. Validate actual text/icon contrast. Measure and wrap strings; increased text size must resize containers. Keep a readable single-page mode at narrow widths and an explicit keyboard focus path. Reduced motion preserves all actions.

Shared components should cover discovery cards, passport pages/spreads, stamp views, tabs, buttons, dialogs, and feedback. They receive data and emit intents; they do not fetch/save/collect directly. Native text input and IME behavior must survive custom styling. Overlay input blocking and child-card actions need deliberate testing.

## Primary reference

[Aetherphone UI toolkit](https://github.com/XeldarAlz/FFXIV-Aetherphone/blob/315898ec9885016ab3e74a3b389311de7cd041f6/docs/ui-toolkit.md), [architecture](https://github.com/XeldarAlz/FFXIV-Aetherphone/blob/315898ec9885016ab3e74a3b389311de7cd041f6/docs/architecture.md), and [accent design](https://github.com/XeldarAlz/FFXIV-Aetherphone/blob/315898ec9885016ab3e74a3b389311de7cd041f6/docs/design-accents.md) were inspected at `315898ec9885016ab3e74a3b389311de7cd041f6`. Adapt the consistency and component discipline to Wanderveil's book/atlas format. Upstream component types are not installed APIs in this repo.

## Secondary reference

Partial [LightlessClient](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/commit/dc2113b0f1fe9f9bf48f830d9b0f505b594ab908) source inspection covered style options, community-board cards, and list virtualization. Use it for utility/layout organization where useful. No reference implementation was live-tested or copied into this scaffold.

## Visual acceptance

Check narrow/default/wide windows, long/CJK strings, increased text size, empty/loading/error states, missing media, nested clicks, overlays, and reduced motion in Dalamud. Use static fixtures first. Studio publication is unavailable in the mock, and Echoes is an honest future preview.
