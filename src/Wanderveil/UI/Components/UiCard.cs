using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

internal static class UiCard
{
    // One native item owns the whole card. Decorative children never create competing hit targets.
    internal static bool Draw(string id, Vector2 size, UiContext context)
    {
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), size);
        PageSurface.Draw(ImGui.GetWindowDrawList(), bounds, context);
        ImGui.PushID(id);
        ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.Paper);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiTheme.PaperInset);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiTheme.PaperInset);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UiMetrics.CornerRadius * context.Scale);
        try
        {
            return ImGui.Button("##Open", size);
        }
        finally
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            ImGui.PopID();
        }
    }
}
