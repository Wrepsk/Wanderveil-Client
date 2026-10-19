# Gridania passport study

Status: static review slice delivered, then connected to the [runtime prototype](passport-runtime.md), 18 October 2026. The user selected Gridania as the first edition for the [intended passport experience](passport-experience.md). Visual acceptance remains pending; original gallery-only scope below is superseded by the runtime connection.

## Inspectable previews

- [Forest-green cover](previews/passport/gridania-cover.png): botanical blind embossing, separate gold heraldic artwork, live lettering.
- [Plum cover variation](previews/passport/gridania-cover-plum.png): the same edition/composition in a user-personalized colorway study.
- [Nation introduction](previews/passport/gridania-nation.png): textile banner left, overhead map study right.
- [Identity spread](previews/passport/gridania-identity.png): prominent portrait and demo signature left; composed information, motto, and biography right.

The previews run native ImGui draw lists against a borderless transparent host and an authored backdrop. `PassportDocumentView`, `BookSurface`, `PageFlow`, typography, portrait, and stamp primitives are shared with the plugin. All lettering remains live. The original Segoe Script surrogate has been replaced by bundled Caveat for the fictional signature in both runtime and regenerated gallery previews; this does not establish eventual signature input. Existing open-book materials are retained.

Review the cover proportions/materials, botanical embossing, gold treatment, portrait size, signature placement, information hierarchy, and stamp spacing. The two cover colorways are distinct generated material plates sharing live lettering/emblem composition. They demonstrate visual direction, not an implemented color picker, recoloring mask, or arbitrary palette pipeline.

The introduction banner and emblem are original heraldic interpretations. The selected map is an illustrative overhead atlas study, explicitly labeled as unvalidated geography. It must not be presented as a finished accurate Gridania/Twelveswood map; geography and exact heraldry still need review before final nation artwork. Sources, generation/edit lineage, and notices are in [asset provenance](../assets/passport/studies/gridania/PROVENANCE.md), with the [complete prompt set](../assets/passport/studies/gridania/generation-prompts.md). The initial perspective vignette was refined into the selected overhead map and is retained only as source lineage.

## Engineering and evidence

`GridaniaTargets`/`GridaniaChecks` are test-only gallery sources around production `PassportDocumentView`. Art remains under `assets/passport/studies/gridania/`; four selected assets are now explicitly embedded for the runtime prototype. Plum/intermediate assets, gallery code, and PNG previews stay outside the plugin package. Discover behavior is preserved.

The native suite includes both covers, nation/identity spreads, narrow portrait/information pages, and enlarged long identity text. Checks cover stationary host bounds, default page fit without scrolling, font stack restoration, illustration aspect/bounds, and bounded long-page scrolling. Existing shell input and Discover checks remain. Software sampling and local body/signature fonts do not establish exact GPU/game-host output.

## Next bounded step

Review these compositions through the connected static runtime prototype. Refine map/heraldic artwork and the recoloring layer model as needed. Entrance/dismissal and the separate 2.5D turn experiment remain subsequent milestones; no animation, CrystalRenderer, signature input, customization editor, or collection persistence is implemented.
