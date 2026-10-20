using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// Colour choosing on paper, shared by Settings' colour rows, a symbol's colour, and the pencil: a round swatch (a
// colour, or a wheel of hues standing for "pick any colour"), and a popup in the paper's colours holding a picker
// (hue bar and hex). A picker previews while dragged and keeps the colour when it lets go.
internal static class PaperColours
{
    // The wheel's hues, standing for a custom colour not yet picked.
    private static readonly int[] Hues = [0xD98880, 0xD9C27A, 0x8CC48C, 0x7FC4C4, 0x8C9ED9, 0xC48CC4];

    // Draws a swatch at center (colour null: the wheel) and returns whether it was clicked. Selected draws a ring
    // just outside it; hovering, a gold one and the name.
    internal static bool Swatch(string id, Vector2 center, float size, Vector4? colour, bool selected, string name, UiContext context)
    {
        var list = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(center - new Vector2(size / 2));
        ImGui.PushID(id);
        var clicked = ImGui.InvisibleButton("##Swatch", new Vector2(size));
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();
        if (colour is { } shown) list.AddCircleFilled(center, size / 2 - 1, ImGui.GetColorU32(shown with { W = 1f }), 24);
        else Wheel(list, center, size / 2 - 1);
        list.AddCircle(center, size / 2 - 1, ImGui.GetColorU32(UiTheme.Ink with { W = 0.35f }), 24, 1);
        if (selected) list.AddCircle(center, size / 2 + 2 * context.Scale, ImGui.GetColorU32(UiTheme.Ink), 24, MathF.Max(1, 1.5f * context.Scale));
        else if (hovered) list.AddCircle(center, size / 2 + 2 * context.Scale, ImGui.GetColorU32(UiTheme.Gold), 24, MathF.Max(1, 1.5f * context.Scale));
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(name);
        }
        return clicked;
    }

    internal static void Wheel(ImDrawListPtr list, Vector2 center, float radius)
    {
        for (var slice = 0; slice < Hues.Length; slice++)
        {
            list.PathArcTo(center, radius, slice * MathF.Tau / Hues.Length, (slice + 1) * MathF.Tau / Hues.Length, 6);
            list.PathLineTo(center);
            var hex = Hues[slice];
            list.PathFillConvex(ImGui.GetColorU32(new Vector4(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f)));
        }
    }

    // Opens a popup in the paper's colours (open it with ImGui.OpenPopup(id) first); false when it is not showing.
    // A true return must be followed by End.
    internal static bool Begin(string id, UiContext context)
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiTheme.Paper);
        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Ink);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiTheme.PaperInset);
        ImGui.PushStyleColor(ImGuiCol.Border, UiTheme.Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10) * context.Scale);
        if (ImGui.BeginPopup(id)) return true;
        Pop();
        return false;
    }

    internal static void End()
    {
        ImGui.EndPopup();
        Pop();
    }

    // The picker: picked changes live while dragged; returns true when it lets go (or a hex is entered), to keep it.
    internal static bool Picker(ref Vector3 picked, UiContext context)
    {
        ImGui.SetNextItemWidth(200 * context.Scale);
        ImGui.ColorPicker3("##Picker", ref picked, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.PickerHueBar | ImGuiColorEditFlags.DisplayHex);
        return ImGui.IsItemDeactivatedAfterEdit();
    }

    private static void Pop()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
    }
}
