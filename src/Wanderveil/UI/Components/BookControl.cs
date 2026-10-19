using System;
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
            if (style == BookControlStyle.Tab && selected)
            {
                var ribbonLeft = RibbonBounds(bounds, context).Position.X - 3 * context.Scale;
                if (origin.X + measured.X > ribbonLeft) origin.X = MathF.Max(bounds.Position.X + 2 * context.Scale, (bounds.Position.X + ribbonLeft - measured.X) / 2);
            }
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
            var ink = hovered ? UiTheme.Ink : UiTheme.EditionInk;
            UiTypography.Draw(list, bounds.Position + new Vector2(2, 2) * context.Scale, label, TextRole.Body, context, measured.X + 1, ink, false);
            var y = bounds.Position.Y + 2 * context.Scale + measured.Y + context.Scale;
            BookSurface.Line(list, new Vector2(bounds.Position.X + 2 * context.Scale, y), new Vector2(bounds.Position.X + 2 * context.Scale + measured.X, y), ImGui.GetColorU32(ink with { W = hovered ? 0.9f : 0.45f }), context.Scale);
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            return pressed;
        }
        finally { ImGui.PopStyleColor(4); ImGui.PopID(); }
    }

    // A printed checkbox: an inked square, ticked when on, followed by its label. Native button semantics.
    internal static bool Toggle(string label, bool value, UiContext context)
    {
        var measured = UiTypography.Measure(label, TextRole.Body, context, UiLayout.ContentWidth, false);
        var box = MathF.Round(measured.Y * 0.62f);
        var gap = 10 * context.Scale;
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), new Vector2(box + gap + measured.X + 4 * context.Scale, measured.Y + 6 * context.Scale));
        var list = ImGui.GetWindowDrawList();
        ImGui.PushID(label);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, UiTheme.Ink);
        try
        {
            var pressed = ImGui.Button("##Toggle", bounds.Size);
            var hovered = ImGui.IsItemHovered();
            var ink = ImGui.GetColorU32(hovered ? UiTheme.Ink : UiTheme.EditionInk);
            var min = new Vector2(bounds.Position.X + 2 * context.Scale, bounds.Position.Y + (bounds.Size.Y - box) / 2);
            var max = min + new Vector2(box);
            list.AddRect(min, max, ink, 2 * context.Scale, ImDrawFlags.None, MathF.Max(1, 1.5f * context.Scale));
            if (value)
            {
                // A hand-ticked check rather than a font glyph.
                var weight = MathF.Max(1.5f, 2.2f * context.Scale);
                list.AddLine(min + new Vector2(box * 0.2f, box * 0.52f), min + new Vector2(box * 0.42f, box * 0.76f), ink, weight);
                list.AddLine(min + new Vector2(box * 0.42f, box * 0.76f), min + new Vector2(box * 0.82f, box * 0.22f), ink, weight);
            }
            UiTypography.Draw(list, new Vector2(max.X + gap, bounds.Position.Y + 3 * context.Scale), label, TextRole.Body, context, measured.X + 1, hovered ? UiTheme.Ink : UiTheme.EditionInk, false);
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            return pressed;
        }
        finally { ImGui.PopStyleColor(4); ImGui.PopID(); }
    }

    // A labelled line to write on: a native single-line text input with no box, inked on a ruled line.
    // The page's body font is drawn at the page's size, so typed text matches the printed text around it.
    // maxLength is in characters; the caller keeps the value within it. id stays stable while the label changes.
    internal static bool Field(string id, string label, string hint, ref string value, int maxLength, UiContext context, bool focus = false)
    {
        UiTypography.Text(label, TextRole.Caption, context, UiTheme.MutedInk);
        var width = UiLayout.ContentWidth;
        var list = ImGui.GetWindowDrawList();
        ImGui.PushID(id);
        ImGui.PushFont(UiTypography.ResolveFont(value + hint, TextRole.Body, context));
        ImGui.SetWindowFontScale(UiTypography.Size(TextRole.Body, context) / ImGui.GetFontSize());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 3) * context.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiTheme.PaperInset with { W = 0.35f });
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, UiTheme.PaperInset with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Ink);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, UiTheme.MutedInk with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, UiTheme.Gold with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, UiTheme.Ink);
        try
        {
            ImGui.SetNextItemWidth(width);
            if (focus) ImGui.SetKeyboardFocusHere();
            // The native limit is in UTF-8 bytes; four per character leaves room for any script.
            var before = value;
            var changed = ImGui.InputTextWithHint("##Field", hint, ref value, maxLength * 4 + 1);
            // Native Escape reverts the whole edit; here it only leaves the field, keeping what was written.
            if (ImGui.IsItemDeactivated() && ImGui.IsKeyPressed(ImGuiKey.Escape, false)) { value = before; changed = false; }
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var ink = ImGui.IsItemActive() ? UiTheme.Ink : UiTheme.EditionInk with { W = ImGui.IsItemHovered() ? 0.7f : 0.4f };
            BookSurface.Line(list, new Vector2(min.X, max.Y), max, ImGui.GetColorU32(ink), MathF.Max(1, context.Scale));
            return changed;
        }
        finally
        {
            ImGui.PopStyleColor(7);
            ImGui.PopStyleVar(2);
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
            ImGui.PopID();
        }
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
    }

    // The selected tab's ribbon: full length, before any extension is applied.
    internal static UiRect RibbonBounds(UiRect tab, UiContext context)
    {
        var width = MathF.Round(MathF.Max(7 * context.Scale, tab.Size.Y * 0.36f));
        var right = tab.Max.X - 12 * context.Scale;
        return new UiRect(new Vector2(right - width, tab.Position.Y + context.Scale), new Vector2(width, tab.Size.Y + MathF.Max(6 * context.Scale, tab.Size.Y * 0.28f)));
    }

    // A small edition-colored ribbon hanging from the selected tab past its foot, over the book's edge.
    // Drawn after the book surface so it overlaps it. extension (0-1) is how far it has unrolled, for later
    // entrance/exit motion; 0 draws nothing.
    internal static void Ribbon(UiRect tab, UiContext context, float extension = 1f)
    {
        if (extension <= 0f) return;
        var full = RibbonBounds(tab, context);
        var length = full.Size.Y * MathF.Min(1f, extension);
        var notch = MathF.Min(full.Size.X * 0.5f, length * 0.4f);
        var list = ImGui.GetWindowDrawList();
        var shadow = new Vector2(1.5f, 2f) * context.Scale;
        Shape(full.Position + shadow, ImGui.GetColorU32(UiTheme.Shadow));
        Shape(full.Position, ImGui.GetColorU32(UiTheme.Edition));
        // A faint foil edge keeps the ribbon legible where it crosses the same-colored board.
        var edge = ImGui.GetColorU32(UiTheme.Foil with { W = 0.45f });
        var foot = full.Position.Y + length;
        var cut = foot - notch;
        var thin = MathF.Max(1, context.Scale);
        list.AddLine(full.Position, new Vector2(full.Position.X, foot), edge, thin);
        list.AddLine(new Vector2(full.Max.X, full.Position.Y), new Vector2(full.Max.X, foot), edge, thin);
        list.AddLine(new Vector2(full.Position.X, foot), new Vector2(full.Position.X + full.Size.X / 2, cut), edge, thin);
        list.AddLine(new Vector2(full.Position.X + full.Size.X / 2, cut), new Vector2(full.Max.X, foot), edge, thin);
        // A foil thread down the middle and a darker fold on the left edge.
        var middle = full.Position.X + full.Size.X / 2;
        list.AddLine(new Vector2(middle, full.Position.Y + 2 * context.Scale), new Vector2(middle, full.Position.Y + length - notch - 2 * context.Scale), ImGui.GetColorU32(UiTheme.Foil with { W = 0.55f }), MathF.Max(1, context.Scale));
        list.AddRectFilled(full.Position, new Vector2(full.Position.X + MathF.Max(1, context.Scale * 1.5f), full.Position.Y + length - notch), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.22f)));

        void Shape(Vector2 origin, uint color)
        {
            var left = origin.X;
            var right = origin.X + full.Size.X;
            var center = origin.X + full.Size.X / 2;
            var body = origin.Y + length - notch;
            list.AddRectFilled(origin, new Vector2(right, body), color);
            // Swallow-tailed foot: two triangles leave a V notch.
            list.AddTriangleFilled(new Vector2(left, body), new Vector2(center, body), new Vector2(left, origin.Y + length), color);
            list.AddTriangleFilled(new Vector2(center, body), new Vector2(right, body), new Vector2(right, origin.Y + length), color);
        }
    }
}
