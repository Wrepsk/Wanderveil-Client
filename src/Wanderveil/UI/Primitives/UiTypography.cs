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
    // Whole pixels keep glyphs on the atlas grid; sized fonts are built for exactly these values.
    internal static float Size(TextRole role, UiContext context) => MathF.Round(role switch
    {
        TextRole.Title => context.FontSize * 1.65f,
        TextRole.Heading => context.FontSize * 1.15f,
        TextRole.Caption => MathF.Max(12f * context.Scale, context.FontSize * 0.8f),
        _ => context.FontSize,
    });

    internal static Vector2 Measure(string text, TextRole role, UiContext context, float width, bool wrap = true)
    {
        ImGui.PushFont(Font(text, role, context));
        try
        {
            var ratio = Size(role, context) / ImGui.GetFontSize();
            // Measure and draw use the same font, size ratio, and wrap width.
            var measured = ImGui.CalcTextSize(text, false, wrap ? MathF.Max(1f, width) / ratio : -1f) * ratio;
            return new Vector2(MathF.Min(width, measured.X), MathF.Max(Size(role, context), measured.Y));
        }
        finally { ImGui.PopFont(); }
    }

    internal static void Draw(ImDrawListPtr drawList, Vector2 position, string text, TextRole role,
        UiContext context, float width, Vector4 ink, bool wrap = true)
    {
        drawList.AddText(Font(text, role, context), Size(role, context), Snap(position), ImGui.GetColorU32(ink), text, wrap ? MathF.Max(1f, width) : 0f);
    }

    // Single-line lettering with extra space between glyphs, for engraved/foil caps.
    internal static float MeasureTracked(string text, TextRole role, UiContext context, float tracking)
    {
        ImGui.PushFont(Font(text, role, context));
        try
        {
            var ratio = Size(role, context) / ImGui.GetFontSize();
            var width = 0f;
            foreach (var character in text) width += ImGui.CalcTextSize(character.ToString()).X * ratio + tracking;
            return MathF.Max(0, width - tracking);
        }
        finally { ImGui.PopFont(); }
    }

    internal static void DrawTracked(ImDrawListPtr drawList, Vector2 position, string text, TextRole role, UiContext context, float tracking, Vector4 ink)
    {
        var font = Font(text, role, context);
        var size = Size(role, context);
        var color = ImGui.GetColorU32(ink);
        ImGui.PushFont(font);
        try
        {
            var ratio = size / ImGui.GetFontSize();
            var x = position.X;
            foreach (var character in text)
            {
                var glyph = character.ToString();
                drawList.AddText(font, size, Snap(new Vector2(x, position.Y)), color, glyph);
                x += ImGui.CalcTextSize(glyph).X * ratio + tracking;
            }
        }
        finally { ImGui.PopFont(); }
    }

    private static Vector2 Snap(Vector2 position) => new(MathF.Round(position.X), MathF.Round(position.Y));

    private static unsafe ImFontPtr Font(string text, TextRole role, UiContext context)
    {
        var font = role switch { TextRole.Title => context.TitleFont, TextRole.Heading => context.HeadingFont, _ => context.BodyFont };
        if (font.IsNull)
            return ImGui.GetFont();
        // A decorative font must never replace an unsupported name with missing-glyph boxes.
        foreach (var character in text)
            if (!char.IsWhiteSpace(character) && font.FindGlyphNoFallback(character) == null)
                return ImGui.GetFont();
        return context.Fonts?.Sized(font, Size(role, context)) ?? font;
    }

    internal static void Text(string text, TextRole role, UiContext context, Vector4 ink)
    {
        var width = UiLayout.ContentWidth;
        var measured = Measure(text, role, context, width);
        Draw(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), text, role, context, width, ink);
        ImGui.Dummy(new Vector2(width, measured.Y));
    }
}
