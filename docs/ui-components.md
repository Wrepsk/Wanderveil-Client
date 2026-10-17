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
| `Features/Passport/PassportLayout.cs` | Responsive page width, spacing, and padding |
| `PassportView.cs`, `PassportPreview.cs` | Synthetic identity/notes composition and procedural portrait placeholder |
| `Features/Discover/DiscoveryLayout.cs` | Text-size-aware two-column/stacked card and detail layouts |
| `DiscoveryCard.cs` | Measured/drawn card composition; returns an open intent |
| `DiscoverView.cs`, `DiscoveryPreview.cs` | In-memory browse/detail navigation and synthetic fixtures |
| `PostcardArtwork.cs` | Original geometric scenery; no textures or external art |

`ShellStyle.Scale` is the only host-specific global UI-scale read. Components receive `UiContext` instead of consulting game services. Typography uses the current host font and explicit role size; draw-list text reserves its measured height with a real ImGui layout item. Decorative paper backgrounds are clipped to page bounds. Text containers grow with wrapped content rather than using fixed heights.

`UiLayout.ContentWidth` conservatively reserves scrollbar room before native layout knows the current content height, preventing transient horizontal overflow when vertical scrolling first becomes necessary.

Passport uses two equal-height pages when both have enough room. Minimum readable page width increases with actual font size as well as host scale. Otherwise pages stack with native vertical scrolling. The sample is Ari Vale on Demo world, with invented notes, a portrait placeholder, and no issued collectibles. Nothing is authenticated, stored, uploaded, or inferred from game state.

The shell initially selects Passport. Explore discoveries requests the Discover tab on the following frame; other tab selections remain native. Shared buttons remain real ImGui items rather than custom pointer rectangles. Settings uses the same typography and retry-button component.

Discover presents type, title, creator, imagined location, teaser, stamp motif/name, and explicit not-collected/demo state. Cards use one native input item each; their text/stamp children are decorative, so there are no nested actions to activate twice. Width/font size determine two-column or stacked layout; each row uses the tallest measured card. Details use the same responsive rule for postcard/caption and location/reward pages. Back preserves browse scroll and opening details starts at the top with Back focused. No collect/persistence behavior is implemented.

`UiButton.Draw` constrains its width to available content, measures/wraps long labels, and grows its height. Short labels that fit use matching unwrapped measurement/drawing to avoid fractional-font wrapping. Native focus/activation and disabled state remain intact; hidden stable IDs are scoped by the visible label. `UiTypography.Measure`/`Draw` accept an optional `wrap` argument (default true); callers must use the same value for both.

## Automated evidence

Run the [native UI checks](../tests/README.md). All 322 checks pass in Debug/Release. The check project compiles the same component/Passport/Discover sources and loads the installed ImGui native library. It verifies geometry across widths/scales/font sizes, rejects invalid geometry, checks selected text/button contrast pairs, exercises wrapping/cursor reservation, mouse release/cancel and keyboard card/Back activation, disabled collection, browse-scroll restoration, and style cleanup. Preview cases cover wide, narrow, enlarged text, long content, empty discoveries, missing art, and scrolled details.

Software previews use ImGui's default test font and a simple CPU rasterizer; they validate real component draw lists, not exact game fonts or GPU appearance. No test renderer/native library is included in the plugin package. The full design system, localization glyph coverage, animations, issued-stamp/passport controls, and broader navigation/overlay controls remain future work.
