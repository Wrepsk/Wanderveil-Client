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
        var padding = UiMetrics.Large * context.Scale;
        var labelWidth = ImGui.CalcTextSize(label).X;
        var width = MathF.Min(UiLayout.ContentWidth, labelWidth + padding + (2f * context.Scale));
        var textWidth = MathF.Max(1f, width - padding);
        // Preserve single-line labels when they fit, including at fractional font scales.
        var wrap = labelWidth > textWidth;
        var text = UiTypography.Measure(label, TextRole.Body, context, textWidth, wrap);
        var height = MathF.Max(MathF.Max(UiMetrics.ButtonHeight * context.Scale, ImGui.GetFrameHeight()),
            text.Y + (UiMetrics.Small * context.Scale));
        ImGui.PushID(label);
        ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.Action);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiTheme.ActionHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiTheme.ActionPressed);
        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Paper);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UiMetrics.CornerRadius * context.Scale);
        try
        {
            var clicked = ImGui.Button("##Action", new Vector2(width, height));
            var origin = ImGui.GetItemRectMin() + new Vector2((width - text.X) / 2f, (height - text.Y) / 2f);
            UiTypography.Draw(ImGui.GetWindowDrawList(), origin, label, TextRole.Body, context, textWidth, UiTheme.Paper, wrap);
            return clicked;
        }
        finally
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
            ImGui.PopID();
        }
    }
}
