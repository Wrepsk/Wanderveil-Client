# Shared UI components

Status: implemented foundations for static Passport and Discover previews, 18 October 2026.

| Source area | Responsibility |
| --- | --- |
| `UI/Primitives/UiContext.cs` | Host scale and actual font size passed explicitly |
| `UiTheme.cs`, `UiMetrics.cs` | Semantic ink/paper/action colors and common dimensions |
| `UiMotion.cs` | Motion tokens: every duration, the shared cubic easing, and the longest frame motion may advance; Reduced motion makes each instant |
| `UiTypography.cs` | Title/Heading/Body/Caption sizing; matching text measurement/drawing and layout reservation |
| `UI/Components/PageSurface.cs` | Paper, decorative trim, and shadow |
| `PageFlow.cs` | One composition for measuring and drawing variable-height content |
| `UiButton.cs` | Native ImGui button with scoped theme; keyboard/mouse behavior retained |
| `UiCard.cs` | Whole-card native action with scoped parchment/hover styling and keyboard focus |
| `StampPreview.cs` | Original decorative compass-stamp motif; no issued collectible state |
| `BookSurface.cs`, `BookAssets.cs` | Asset-backed cover/spread/page-half painter with a drawn cloth/paper fallback while plates load, safe content bounds, and texture port |
| `UI/BookShellContent.cs` | The discoveries, Echoes dossier, and Studio templates the shell shows; empty or missing values are valid states |
| `UI/BookShell.cs`, `BookShellLayout.cs`, `Components/BookControl.cs` | Borderless stationary host composition, viewport fitting, bounded live pages, attached native actions, and shell intents |
| `Infrastructure/Media/PassportAssets.cs` | Embedded shared textures and plugin-owned managed heading fonts |
| `Features/Passport/PassportLayout.cs` | Responsive page width, spacing, and padding |
| `PassportView.cs` | Earlier identity/notes composition and procedural portrait placeholder (gallery and checks) |
| `PassportIdentity.cs` | The holder's identity (self-reported fields, number, issue date, portrait placement) with field limits and normalization, `PortraitCrop`, and the `PassportProfile` the pages edit |
| `PassportEdition.cs` | The six nation editions (words, cloth, foil, page ink) and `PassportAppearance`, the part a change of edition replaces |
| `PassportColours.cs` | The holder's colours (`Rgb`, `PassportColours`) and `PassportLook`: the colours drawn, kept legible |
| `PassportSymbols.cs` | The symbol set (`PassportSymbol`), the surfaces that take symbols, a placed symbol, and `SymbolLayer`: symbols in drawing order, at most 24 to a surface |
| `EditionShelf.cs` | The editions as small real covers to choose from, with or without names beneath |
| `PictureFrames.cs` | The five frames for the holder's picture (printed, corners, embossed, deckled, none), as draw-list geometry |
| `IdentityEditor.cs` | Writing on the identity pages in place: soft-wrapped fields over a faint inset, picture placement, the character fill, and the cover line |
| `PassportServices.cs` | What the host provides the identity pages (`IPassportPortrait`, `ICharacterSource`) and the sentences about the saved identity |
| `Features/Discover/DiscoveryLayout.cs` | Text-size-aware two-column/stacked card and detail layouts |
| `DiscoveryCard.cs` | Measured/drawn card composition; returns an open intent |
| `DiscoverView.cs`, `DiscoveryPreview.cs` | In-memory browse/detail navigation, the collect action, and synthetic fixtures naming each discovery's stamp design |
| `Features/Stamps/StampDesign.cs`, `StampCollection.cs` | Stamp designs and issued instances with local demo provenance and source, sparse placements (page, place, turn) in layer order, and the collection with its one-per-discovery and placement rules |
| `UI/BookShellStamps.cs` | Stamps on the stamp pages: drawing, the info page opposite, Arrange mode with drag, turning knobs, and × to take a stamp off its page, and carrying a stamp across page turns |
| `StampStorage.cs` | What loading found and whether saving works (`ISavedState`), with the sentences the pages print about it |
| `Infrastructure/Storage/StampStore.cs`, `StampPersistence.cs` | Versioned stamp file per profile; startup load and background saves |
| `Infrastructure/Storage/PassportStore.cs`, `PassportPersistence.cs` | Versioned identity file and picture copy per profile; issue on first load, startup load and background saves |
| `Infrastructure/Storage/SafeFile.cs`, `BackgroundSaver.cs` | Shared atomic save with backup and recovery, profile names, and coalesced background saving |
| `UI/Components/PlateRecolour.cs` | Recolouring plates through their weight maps, coverage-weighted reduced copies, and a small PNG reader for the plugin's own plates |
| `UI/BookShellColours.cs` | The Settings colour rows (swatches, custom picker, May adjust boxes, reset) and the How it looks preview |
| `UI/BookShellSymbols.cs`, `Components/SymbolArt.cs` | Decorating the cover and first pages: the symbol sheet, a symbol in hand, knobs that turn and resize, × to take one off; and drawing a symbol tinted, turned, and carried on a turning leaf |
| `Infrastructure/Media/PortraitPicture.cs`, `Infrastructure/Game/GameCharacter.cs` | The holder's picture (file picker, background decode and copy, texture swap) and the logged-in character, in game only |
| `StampArt.cs` | A design as a faint square proof or an issued stamp turned as placed, with the procedural motif as fallback |
| `PostcardArtwork.cs` | Original geometric scenery; no textures or external art |
| `Features/Studio/StudioTemplate.cs`, `StudioDraft.cs` | Template catalog (Postcard and Stamp Spot available, later templates listed) and in-memory drafts with character limits, missing fields, and unavailable save/publish |
| `Features/Echoes/EchoPreview.cs`, `EchoesView.cs` | Synthetic sample dossier with chapter/objective rules, drawn as printed, non-interactive Echoes pages |
| `StudioView.cs` | Studio's chooser/introduction and fields/preview pages; `BookControl.Field` is the printed one-line input |

`ShellStyle.Scale` is the only host-specific global UI-scale read. Components receive `UiContext` instead of consulting game services. Typography uses the current host font and explicit role size; draw-list text reserves its measured height with a real ImGui layout item. Decorative paper backgrounds are clipped to page bounds. Text containers grow with wrapped content rather than using fixed heights.

`UiLayout.ContentWidth` always leaves room for a vertical scrollbar, whether or not one is showing, so content width never depends on the scrollbar. This prevents transient horizontal overflow when scrolling first becomes necessary, and a feedback loop the user saw in game as a twitching Studio preview at one passport size: a scrollbar narrowed the page, art sized from that width shrank until the content fit, the scrollbar went away, and the content overflowed again every frame.

`BookSurface.Illustration` fits arbitrary artwork within a reserved rectangle without changing its aspect. Production `PassportDocumentView` and the gallery reuse it for banner/crest/map art alongside portrait/stamp painters. Stable logical-page records drive the runtime catalog; see [runtime composition and controls](passport-runtime.md).

The runtime book shell uses the SVG-generated cloth/paper plates at their natural aspect. Each summon starts closed; clicking the right side of the cover opens nation introduction, identity, then blank stamp pages up to page 100, each engraved with the passport number. Font size and viewport determine spread or single-page reading. Page contents scale with the page and shrink to fit their paper; only enlarged host text can make a page scroll. Ari Vale and the signature are synthetic, and stamp pages show only the stamps their owner placed; nothing is authenticated, stored, uploaded, or inferred from game state. Missing artwork has readable fallbacks.

Title, heading, body, display (cover), mono, and signature fonts are passed through `UiContext`; measure/draw select the same font and unsupported decorative glyphs fall back to the host font. An optional `IUiFonts` resolver swaps in a font built at the exact drawn pixel size, so text is never stretched. `UiTypography.DrawTracked` draws letter-spaced single-line caps. Discover accepts paper-appropriate outer text colors inside the book. See [visual targets and milestone boundaries](passport-visual-fidelity.md).

The book is its own control surface. Releasing a press on its right side opens the cover or turns forward; the left side turns back, and from the first page returns to the cover. A press that moves past the drag threshold moves the book instead and never turns. Real items keep their own clicks: `BookControl` draws paper index tabs (sections), printed ink controls on pages, and foil-stamped controls on the cover, all native buttons with keyboard focus. Space/Enter open the cover, Page Up/Down turn, Home returns to the cover, and directional/Space navigation still reaches the tabs. Identity's Explore link selects Discover. Choosing another tab riffles several pages in book order (`BookShellRiffle`). Settings are pages of the book reached from the printed Settings mark, with a printed checkbox (`BookControl.Toggle`) and a Back link; `IBookShellSettings` supplies and saves the values.

Discover presents type, title, creator, imagined location, teaser, stamp motif/name, and explicit not-collected/demo state. Cards use one native input item each; their text/stamp children are decorative, so there are no nested actions to activate twice. Width/font size determine two-column or stacked layout; each row uses the tallest measured card. Details use the same responsive rule for postcard/caption and location/reward pages. Back preserves browse scroll and opening details starts at the top with Back focused. A postcard's details end with **Collect demo stamp**; once collected they show the issued stamp, whether it is saved (with **Try saving again** after a failure), and a link that places the stamp in the passport, or shows it once placed.

`UiButton.Draw` constrains its width to available content, measures/wraps long labels, and grows its height. Short labels that fit use matching unwrapped measurement/drawing to avoid fractional-font wrapping. Native focus/activation and disabled state remain intact; hidden stable IDs are scoped by the visible label. `UiTypography.Measure`/`Draw` accept an optional `wrap` argument (default true); callers must use the same value for both.

## Automated evidence

Run the [native UI checks](../tests/README.md). All checks pass (5209 in Debug, 5329 in Release with previews). The check project compiles the same shell/component/Passport/Discover/Studio/Echoes sources and loads the installed ImGui native library. It verifies geometry across widths/scales/font sizes, rejects invalid geometry, checks selected text/button contrast pairs, exercises wrapping/cursor reservation, mouse release/cancel and keyboard card/Back activation, disabled collection, browse-scroll restoration, and style cleanup. Book checks cover material bounds, assets/fallbacks, font stack/fallback, long-page keyboard activation, complete borderless shell input/layout through viewport and size changes, and Gridania static composition/illustration/narrow-page behavior.

Baseline software previews use ImGui's default font. Material previews add the bundled heading fonts and local Windows Segoe UI as a body-font surrogate; it is not redistributed. The CPU rasterizer supports asset RGBA and multi-page font atlases. These validate real component draw lists, not exact game fonts or GPU appearance. No test renderer/native library is included in the plugin package. The full design system, localization glyph coverage, animations, issued-stamp/passport controls, and broader navigation/overlay controls remain future work.
