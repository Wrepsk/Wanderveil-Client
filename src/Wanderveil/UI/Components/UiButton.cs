using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

internal static class UiButton
{
    internal static bool Draw(string label, UiContext context)
    {
        // A real ImGui item retains native focus, keyboard activation, hover, and release behavior.
        ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.Action);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiTheme.ActionHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiTheme.ActionPressed);
        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Paper);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UiMetrics.CornerRadius * context.Scale);
        try
        {
            var width = ImGui.CalcTextSize(label).X + (UiMetrics.Large * context.Scale);
            var height = MathF.Max(UiMetrics.ButtonHeight * context.Scale, ImGui.GetFrameHeight());
            return ImGui.Button(label, new Vector2(width, height));
        }
        finally
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
        }
    }
}
