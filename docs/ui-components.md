# Shared UI components

Status: implemented foundations for static Passport and Discover previews, 18 October 2026.

| Source area | Responsibility |
| --- | --- |
| `UI/Primitives/UiContext.cs` | Host scale and actual font size passed explicitly |
| `UiTheme.cs`, `UiMetrics.cs` | Semantic ink/paper/action colors and common dimensions |
| `UiTypography.cs` | Title/Heading/Body/Caption sizing; matching text measurement/drawing and layout reservation |
| `UI/Components/PageSurface.cs` | Paper, decorative trim, and shadow |
| `PageFlow.cs` | One composition for measuring and drawing variable-height content |
| `UiButton.cs` | Native ImGui button with scoped theme; keyboard/mouse behavior retained |
| `UiCard.cs` | Whole-card native action with scoped parchment/hover styling and keyboard focus |
| `StampPreview.cs` | Original decorative compass-stamp motif; no issued collectible state |
| `BookSurface.cs`, `BookAssets.cs` | Asset-backed cover/spread/page-half painter, safe content bounds, and texture port |
| `UI/BookShell.cs`, `BookShellLayout.cs`, `Components/BookControl.cs` | Borderless stationary host composition, viewport fitting, bounded live pages, attached native actions, and shell intents |
| `Infrastructure/Media/PassportAssets.cs` | Embedded shared textures and plugin-owned managed heading fonts |
| `Features/Passport/PassportLayout.cs` | Responsive page width, spacing, and padding |
| `PassportView.cs`, `PassportPreview.cs` | Synthetic identity/notes composition and procedural portrait placeholder |
| `Features/Discover/DiscoveryLayout.cs` | Text-size-aware two-column/stacked card and detail layouts |
| `DiscoveryCard.cs` | Measured/drawn card composition; returns an open intent |
| `DiscoverView.cs`, `DiscoveryPreview.cs` | In-memory browse/detail navigation and synthetic fixtures |
| `PostcardArtwork.cs` | Original geometric scenery; no textures or external art |

`ShellStyle.Scale` is the only host-specific global UI-scale read. Components receive `UiContext` instead of consulting game services. Typography uses the current host font and explicit role size; draw-list text reserves its measured height with a real ImGui layout item. Decorative paper backgrounds are clipped to page bounds. Text containers grow with wrapped content rather than using fixed heights.

`UiLayout.ContentWidth` conservatively reserves scrollbar room before native layout knows the current content height, preventing transient horizontal overflow when vertical scrolling first becomes necessary.

`BookSurface.Illustration` fits arbitrary artwork within a reserved rectangle without changing its aspect. Production `PassportDocumentView` and the gallery reuse it for banner/crest/map art alongside portrait/stamp painters. Stable logical-page records drive the runtime catalog; see [runtime composition and controls](passport-runtime.md).

The runtime book shell uses the SVG-generated cloth/paper plates at their natural aspect. Each summon starts closed; clicking the right side of the cover opens nation introduction, identity, then demo collection pages. Font size and viewport determine spread or single-page reading. Page contents scale with the page and shrink to fit their paper; only enlarged host text can make a page scroll. Ari Vale, signature, and stamp specimens are synthetic; nothing is authenticated, stored, uploaded, or inferred from game state. Missing artwork has readable fallbacks.

Title, heading, body, display (cover), mono, and signature fonts are passed through `UiContext`; measure/draw select the same font and unsupported decorative glyphs fall back to the host font. An optional `IUiFonts` resolver swaps in a font built at the exact drawn pixel size, so text is never stretched. `UiTypography.DrawTracked` draws letter-spaced single-line caps. Discover accepts paper-appropriate outer text colors inside the book. See [visual targets and milestone boundaries](passport-visual-fidelity.md).

The book is its own control surface. Releasing a press on its right side opens the cover or turns forward; the left side turns back, and from the first page returns to the cover. A press that moves past the drag threshold moves the book instead and never turns. Real items keep their own clicks: `BookControl` draws paper index tabs (sections), printed ink controls on pages, and foil-stamped controls on the cover, all native buttons with keyboard focus. Space/Enter open the cover, Page Up/Down turn, Home returns to the cover, and directional/Space navigation still reaches the tabs. Identity's Explore link selects Discover. Settings remains separate.

Discover presents type, title, creator, imagined location, teaser, stamp motif/name, and explicit not-collected/demo state. Cards use one native input item each; their text/stamp children are decorative, so there are no nested actions to activate twice. Width/font size determine two-column or stacked layout; each row uses the tallest measured card. Details use the same responsive rule for postcard/caption and location/reward pages. Back preserves browse scroll and opening details starts at the top with Back focused. No collect/persistence behavior is implemented.

`UiButton.Draw` constrains its width to available content, measures/wraps long labels, and grows its height. Short labels that fit use matching unwrapped measurement/drawing to avoid fractional-font wrapping. Native focus/activation and disabled state remain intact; hidden stable IDs are scoped by the visible label. `UiTypography.Measure`/`Draw` accept an optional `wrap` argument (default true); callers must use the same value for both.

## Automated evidence

Run the [native UI checks](../tests/README.md). All 1333 checks pass in Debug/Release. The check project compiles the same shell/component/Passport/Discover sources and loads the installed ImGui native library. It verifies geometry across widths/scales/font sizes, rejects invalid geometry, checks selected text/button contrast pairs, exercises wrapping/cursor reservation, mouse release/cancel and keyboard card/Back activation, disabled collection, browse-scroll restoration, and style cleanup. Book checks cover material bounds, assets/fallbacks, font stack/fallback, long-page keyboard activation, complete borderless shell input/layout through viewport and size changes, and Gridania static composition/illustration/narrow-page behavior.

Baseline software previews use ImGui's default font. Material previews add the bundled heading fonts and local Windows Segoe UI as a body-font surrogate; it is not redistributed. The CPU rasterizer supports asset RGBA and multi-page font atlases. These validate real component draw lists, not exact game fonts or GPU appearance. No test renderer/native library is included in the plugin package. The full design system, localization glyph coverage, animations, issued-stamp/passport controls, and broader navigation/overlay controls remain future work.
