using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Primitives;

internal enum TextRole
{
    Title,
    Heading,
    Body,
    Caption,
}

internal static class UiTypography
{
    internal static float Size(TextRole role, UiContext context) => role switch
    {
        TextRole.Title => context.FontSize * 1.65f,
        TextRole.Heading => context.FontSize * 1.15f,
        TextRole.Caption => MathF.Max(12f * context.Scale, context.FontSize * 0.85f),
        _ => context.FontSize,
    };

    internal static Vector2 Measure(string text, TextRole role, UiContext context, float width)
    {
        var ratio = Size(role, context) / ImGui.GetFontSize();
        // Measure and draw use the same font, size ratio, and wrap width.
        var measured = ImGui.CalcTextSize(text, false, MathF.Max(1f, width) / ratio) * ratio;
        return new Vector2(MathF.Min(width, measured.X), MathF.Max(Size(role, context), measured.Y));
    }

    internal static void Draw(ImDrawListPtr drawList, Vector2 position, string text, TextRole role,
        UiContext context, float width, Vector4 ink)
    {
        drawList.AddText(ImGui.GetFont(), Size(role, context), position, ImGui.GetColorU32(ink), text, MathF.Max(1f, width));
    }

    internal static void Text(string text, TextRole role, UiContext context, Vector4 ink)
    {
        var width = UiLayout.ContentWidth;
        var measured = Measure(text, role, context, width);
        Draw(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), text, role, context, width, ink);
        ImGui.Dummy(new Vector2(width, measured.Y));
    }
}
