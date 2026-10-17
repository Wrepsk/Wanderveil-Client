# Shared UI components

Status: implemented foundations for the static Passport identity preview, 18 October 2026.

| Source area | Responsibility |
| --- | --- |
| `UI/Primitives/UiContext.cs` | Host scale and actual font size passed explicitly |
| `UiTheme.cs`, `UiMetrics.cs` | Semantic ink/paper/action colors and common dimensions |
| `UiTypography.cs` | Title/Heading/Body/Caption sizing; matching text measurement/drawing and layout reservation |
| `UI/Components/PageSurface.cs` | Paper, decorative trim, and shadow |
| `PageFlow.cs` | One composition for measuring and drawing variable-height content |
| `UiButton.cs` | Native ImGui button with scoped theme; keyboard/mouse behavior retained |
| `Features/Passport/PassportLayout.cs` | Responsive page width, spacing, and padding |
| `PassportView.cs`, `PassportPreview.cs` | Synthetic identity/notes composition and procedural portrait placeholder |

`ShellStyle.Scale` is the only host-specific global UI-scale read. Components receive `UiContext` instead of consulting game services. Typography uses the current host font and explicit role size; draw-list text reserves its measured height with a real ImGui layout item. Decorative paper backgrounds are clipped to page bounds. Text containers grow with wrapped content rather than using fixed heights.

`UiLayout.ContentWidth` conservatively reserves scrollbar room before native layout knows the current content height, preventing transient horizontal overflow when vertical scrolling first becomes necessary.

Passport uses two equal-height pages when both have enough room. Minimum readable page width increases with actual font size as well as host scale. Otherwise pages stack with native vertical scrolling. The sample is Ari Vale on Demo world, with invented notes, a portrait placeholder, and no issued collectibles. Nothing is authenticated, stored, uploaded, or inferred from game state.

The shell initially selects Passport. Explore discoveries requests the Discover tab on the following frame; other tab selections remain native. Shared buttons remain real ImGui items rather than custom pointer rectangles. Settings uses the same typography and retry-button component.

## Automated evidence

Run the [native UI checks](../tests/README.md). The check project compiles the same component/Passport source files and loads the installed ImGui native library. It verifies geometry across widths/scales/font sizes, rejects invalid geometry, checks selected text/button contrast pairs, exercises wrapping/cursor reservation, mouse release and keyboard activation, and style cleanup. Four preview cases cover wide, narrow, enlarged text, and long content.

Software previews use ImGui's default test font and a simple CPU rasterizer; they validate real component draw lists, not exact game fonts or GPU appearance. No test renderer/native library is included in the plugin package. The full design system, localization glyph coverage, animations, discovery/stamp components, and broader navigation/overlay controls remain future work.
