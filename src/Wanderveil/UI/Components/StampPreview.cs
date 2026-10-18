using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

internal static class StampPreview
{
    // Original procedural motif, not an issued stamp or game achievement.
    internal static void Draw(UiRect bounds, UiContext context, Vector4? color = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var center = bounds.Position + (bounds.Size / 2f);
        var radius = MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.42f;
        var ink = ImGui.GetColorU32(color ?? UiTheme.Action);
        var stroke = MathF.Max(1f, context.Scale);
        drawList.AddCircle(center, radius, ink, 48, stroke * 2f);
        drawList.AddCircle(center, radius * 0.84f, ink, 48, stroke);
        var top = center - new Vector2(0f, radius * 0.57f);
        var right = center + new Vector2(radius * 0.4f, 0f);
        var bottom = center + new Vector2(0f, radius * 0.57f);
        var left = center - new Vector2(radius * 0.4f, 0f);
        drawList.AddTriangleFilled(top, right, center, ink);
        drawList.AddTriangleFilled(bottom, left, center, ink);
        drawList.AddLine(top, left, ink, stroke);
        drawList.AddLine(right, bottom, ink, stroke);
    }
}
