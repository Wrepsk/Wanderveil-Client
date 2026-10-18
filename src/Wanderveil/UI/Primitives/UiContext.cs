using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Primitives;

internal readonly record struct UiContext(float Scale, float FontSize)
{
    internal ImFontPtr TitleFont { get; init; }
    internal ImFontPtr HeadingFont { get; init; }
    // Body and caption text; null keeps the host font.
    internal ImFontPtr BodyFont { get; init; }
    // Cover lettering and monospaced passport numbers; only drawn where a caller opts in.
    internal ImFontPtr DisplayFont { get; init; }
    internal ImFontPtr MonoFont { get; init; }
    internal ImFontPtr SignatureFont { get; init; }
}
