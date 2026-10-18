# Third-party notices

## Existing template

This client was initialized from [goatcorp/SamplePlugin](https://github.com/goatcorp/SamplePlugin). Its existing [LICENSE.md](LICENSE.md) remains unchanged. The shell/lifecycle source was adapted from the template during migration; the original sample screens and goat image were removed. The new procedural compass in `src/Wanderveil/UI/CompassMark.cs` is original project artwork. Current package/tooling dependencies are recorded in `src/Wanderveil/Wanderveil.csproj`, its `packages.lock.json`, and `.github/workflows/pr-build.yml`. SDK and locked package versions were preserved.

## Design references only

Discover's geometric postcard scenery (`Features/Discover/PostcardArtwork.cs`) and decorative stamp motif (`UI/Components/StampPreview.cs`) are original project artwork. Demo names, locations, and captions are synthetic; no reference screenshots, game textures, fonts, or upstream implementation were copied for this slice.

[Aetherphone](https://github.com/XeldarAlz/FFXIV-Aetherphone) is the primary UI reference; its repository declares AGPL-3.0-or-later. [LightlessClient](https://git.lightless-sync.org/Lightless-Sync/LightlessClient) is secondary; its repository API reports AGPL-3.0. Neither project is an installed dependency, and no code or art was copied from them in this scaffold.

[CrystalRenderer](https://github.com/Wrepsk/crystal-renderer) is an optional future integration candidate. Its project metadata declares AGPL-3.0-or-later and retains Pictomancy provenance/notices. It has not been added as a dependency.

## Passport material milestone

The four PNGs in `assets/passport/` are original outputs generated for this project with the built-in OpenAI image tool, without reference images or copied game/upstream assets. Exact prompts, intended use, and distribution notes are recorded in [provenance](assets/passport/PROVENANCE.md). These outputs are project assets under the existing repository license; no exclusive copyright claim is made in generated imagery.

Source Serif 4 Regular/Semibold are unmodified Adobe font files from [revision 80d3f8894c09c937bebfa9011247d2e1c79fd6f4](https://github.com/adobe-fonts/source-serif/tree/80d3f8894c09c937bebfa9011247d2e1c79fd6f4/TTF). Copyright 2014–2023 Adobe, reserved font name “Source”; licensed under [SIL Open Font License 1.1](assets/fonts/SourceSerif4-LICENSE.md). The license is copied into release output/packages. The fonts retain their separate license. Segoe UI is used only from the local Windows installation in test previews; no Segoe font file is redistributed.

## When adding material

Record the exact source/revision, files or assets reused, author, license, retained notices, modifications, and redistribution requirements. Asset/font licenses may differ from repository code licenses. Keep applicable license text with the distributed material; this page is not a substitute for it. Do not assume a reference screenshot grants rights to ship its artwork.
