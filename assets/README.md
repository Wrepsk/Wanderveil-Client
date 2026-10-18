# Asset reservation

| Directory | Intended content |
| --- | --- |
| `icons/` | Plugin/section/action icons |
| `textures/` | Subtle book/paper/surface textures |
| `stamps/` | Original or licensed curated demo stamp art |
| `fonts/` | Only fonts with documented distribution rights |

`passport/` now contains original generated open-book/cover material plates, an invented portrait, and a transparent three-stamp atlas. See [provenance and exact prompts](passport/PROVENANCE.md). Dynamic text/layout/controls remain separate. `fonts/` contains pinned unmodified Source Serif 4 Regular/Semibold and its SIL OFL license. Assets are embedded in the plugin; no external image service is needed at runtime/build time. Keep unlicensed reference screenshots out of shipped assets and record later additions in [third-party notices](../THIRD_PARTY_NOTICES.md).

`passport/vector/` holds the plates currently embedded in the plugin, generated from SVG by [tools/passport-art](../tools/passport-art/README.md) ([provenance](passport/vector/PROVENANCE.md)). The image-generated plates above and the studies below remain for comparison and gallery checks only.

`passport/studies/gridania/` contains the [Gridania visual studies and generation lineage](passport/studies/gridania/PROVENANCE.md). The selected green cover/banner/overhead map/crest are explicitly embedded for runtime review; plum/intermediate studies stay excluded. The map is schematic rather than validated game geography, and the banner/crest are original interpretations. `fonts/Caveat.ttf` supplies the demo signature with its bundled SIL OFL notice.
