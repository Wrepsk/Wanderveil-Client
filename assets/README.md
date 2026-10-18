# Asset reservation

| Directory | Intended content |
| --- | --- |
| `icons/` | Plugin/section/action icons |
| `textures/` | Subtle book/paper/surface textures |
| `stamps/` | Original or licensed curated demo stamp art |
| `fonts/` | Only fonts with documented distribution rights |

`passport/` now contains original generated open-book/cover material plates, an invented portrait, and a transparent three-stamp atlas. See [provenance and exact prompts](passport/PROVENANCE.md). Dynamic text/layout/controls remain separate. `fonts/` contains pinned unmodified Source Serif 4 Regular/Semibold and its SIL OFL license. Assets are embedded in the plugin; no external image service is needed at runtime/build time. Keep unlicensed reference screenshots out of shipped assets and record later additions in [third-party notices](../THIRD_PARTY_NOTICES.md).

`passport/studies/gridania/` contains the separate [Gridania visual studies and generation lineage](passport/studies/gridania/PROVENANCE.md). These are gallery review resources, excluded from runtime embedding and plugin packaging. The map is schematic rather than validated game geography, and the banner/crest are original interpretations. Only the root passport PNGs and bundled fonts are currently embedded.
