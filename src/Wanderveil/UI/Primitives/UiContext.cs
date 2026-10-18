using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Primitives;

internal readonly record struct UiContext(float Scale, float FontSize)
{
    internal ImFontPtr TitleFont { get; init; }
    internal ImFontPtr HeadingFont { get; init; }
    internal ImFontPtr SignatureFont { get; init; }
}
