using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// Ink: printed on paper. Foil: stamped on the cover. Tab: a paper index tab tucked behind the pages.
internal enum BookControlStyle { Ink, Foil, Tab }

internal static class BookControl
{
    // Native button semantics under custom paint retain keyboard navigation and release-to-activate.
    internal static bool Draw(string id, string label, UiRect bounds, UiContext context, BookControlStyle style, bool selected = false, string? hint = null)
    {
        var list = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(bounds.Position);
        ImGui.PushID(id);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, style == BookControlStyle.Foil ? UiTheme.Foil : UiTheme.Ink);
        bool pressed;
        try
        {
            pressed = ImGui.Button("##Action", bounds.Size);
            var hovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();
            var disabled = ImGui.GetStyle().Alpha < 0.99f;
            if (style == BookControlStyle.Tab) Tab(list, bounds, context, selected, hovered);
            var ink = style switch
            {
                BookControlStyle.Foil => hovered ? UiTheme.FoilBright : UiTheme.Foil,
                BookControlStyle.Tab => selected || hovered ? UiTheme.Ink : UiTheme.MutedInk,
                _ => hovered ? UiTheme.Ink : UiTheme.MutedInk,
            };
            if (active) ink *= new Vector4(0.8f, 0.8f, 0.8f, 1);
            if (disabled) ink.W *= 0.45f;
            var width = bounds.Size.X - 4 * context.Scale;
            var measured = UiTypography.Measure(label, TextRole.Caption, context, width, false);
            var origin = bounds.Position + (bounds.Size - measured) / 2;
            list.PushClipRect(bounds.Position, bounds.Max, true);
            UiTypography.Draw(list, origin, label, TextRole.Caption, context, width, ink, false);
            list.PopClipRect();
            if (style != BookControlStyle.Tab && hovered && !disabled)
            {
                var y = origin.Y + measured.Y + 1 * context.Scale;
                list.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + measured.X, y), ImGui.GetColorU32(ink with { W = ink.W * 0.6f }), context.Scale);
            }
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (hint != null && hovered) ImGui.SetTooltip(hint);
        }
        finally { ImGui.PopStyleColor(4); ImGui.PopID(); }
        return pressed;
    }

    // An inline printed link inside page flow, sized to its label.
    internal static bool Link(string label, UiContext context)
    {
        var measured = UiTypography.Measure(label, TextRole.Body, context, UiLayout.ContentWidth, false);
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), measured + new Vector2(4, 6) * context.Scale);
        var list = ImGui.GetWindowDrawList();
        ImGui.PushID(label);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, UiTheme.Ink);
        try
        {
            var pressed = ImGui.Button("##Link", bounds.Size);
            var hovered = ImGui.IsItemHovered();
            var ink = hovered ? UiTheme.Ink : UiTheme.Edition;
            UiTypography.Draw(list, bounds.Position + new Vector2(2, 2) * context.Scale, label, TextRole.Body, context, measured.X + 1, ink, false);
            var y = bounds.Position.Y + 2 * context.Scale + measured.Y + context.Scale;
            list.AddLine(new Vector2(bounds.Position.X + 2 * context.Scale, y), new Vector2(bounds.Position.X + 2 * context.Scale + measured.X, y), ImGui.GetColorU32(ink with { W = hovered ? 0.9f : 0.45f }), context.Scale);
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            return pressed;
        }
        finally { ImGui.PopStyleColor(4); ImGui.PopID(); }
    }

    private static void Tab(ImDrawListPtr list, UiRect bounds, UiContext context, bool selected, bool hovered)
    {
        // The tab runs down behind the board; the book surface drawn afterwards hides its foot.
        var radius = 5 * context.Scale;
        var top = bounds.Position + new Vector2(0, selected ? 0 : 3 * context.Scale);
        var foot = new Vector2(bounds.Max.X, bounds.Max.Y + 10 * context.Scale);
        var fill = selected ? UiTheme.Paper : hovered ? UiTheme.Paper * new Vector4(0.97f, 0.97f, 0.97f, 1) : UiTheme.PaperInset;
        list.AddRectFilled(top + new Vector2(1, 2) * context.Scale, foot + new Vector2(1, 0) * context.Scale, ImGui.GetColorU32(UiTheme.Shadow), radius, ImDrawFlags.RoundCornersTop);
        list.AddRectFilled(top, foot, ImGui.GetColorU32(fill), radius, ImDrawFlags.RoundCornersTop);
        list.AddRect(top, foot, ImGui.GetColorU32(UiTheme.Ink with { W = 0.22f }), radius, ImDrawFlags.RoundCornersTop, context.Scale);
        if (selected)
            list.AddRectFilled(top + new Vector2(radius, 0), new Vector2(bounds.Max.X - radius, top.Y + 3 * context.Scale), ImGui.GetColorU32(UiTheme.Edition));
    }
}
