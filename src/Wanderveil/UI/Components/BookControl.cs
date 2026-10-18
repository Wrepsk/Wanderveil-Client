using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

internal static class BookControl
{
    // Native button semantics under custom leather/ribbon paint retain keyboard navigation.
    internal static bool Draw(string id, string label, UiRect bounds, UiContext context, bool selected = false, bool bookmark = false, string? hint = null, IBookAssets? assets = null)
    {
        var list = ImGui.GetWindowDrawList();
        var fill = ImGui.GetColorU32(UiTheme.Action);
        if (bookmark)
        {
            var cut = 5 * context.Scale;
            list.AddRectFilled(bounds.Position, bounds.Max - new Vector2(0, cut), fill);
            list.AddTriangleFilled(new Vector2(bounds.Position.X, bounds.Max.Y - cut), new Vector2(bounds.Max.X, bounds.Max.Y - cut), new Vector2(bounds.Position.X, bounds.Max.Y), fill);
        }
        else list.AddRectFilled(bounds.Position, bounds.Max, fill, 3 * context.Scale);
        if (assets != null && assets.TryGet(BookAsset.Cover, out var leather))
            list.AddImage(leather.Texture, bounds.Position, bounds.Max - new Vector2(0, bookmark ? 5 * context.Scale : 0), new Vector2(0.25f, 0.35f), new Vector2(0.75f, 0.46f));
        if (selected) list.AddLine(bounds.Position + new Vector2(6 * context.Scale, 2 * context.Scale), new Vector2(bounds.Max.X - 6 * context.Scale, bounds.Position.Y + 2 * context.Scale), ImGui.GetColorU32(UiTheme.Gold), 2 * context.Scale);
        ImGui.SetCursorScreenPos(bounds.Position);
        ImGui.PushID(id);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 1, 1, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, UiTheme.Veil);
        bool pressed;
        try
        {
            pressed = ImGui.Button("##Action", bounds.Size);
            var width = bounds.Size.X - 8 * context.Scale;
            var measured = UiTypography.Measure(label, TextRole.Caption, context, width, false);
            list.PushClipRect(bounds.Position, bounds.Max, true);
            UiTypography.Draw(list, bounds.Position + (bounds.Size - measured) / 2, label, TextRole.Caption, context, width, UiTheme.Paper, false);
            list.PopClipRect();
            if (hint != null && ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
        }
        finally { ImGui.PopStyleColor(4); ImGui.PopID(); }
        return pressed;
    }
}
