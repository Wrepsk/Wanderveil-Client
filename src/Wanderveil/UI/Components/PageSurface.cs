using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

internal static class PageSurface
{
    internal static void Draw(ImDrawListPtr drawList, UiRect bounds, UiContext context)
    {
        var shadow = new Vector2(3f * context.Scale);
        var radius = UiMetrics.CornerRadius * context.Scale;
        drawList.AddRectFilled(bounds.Position + shadow, bounds.Max + shadow, ImGui.GetColorU32(UiTheme.Shadow), radius);
        drawList.AddRectFilled(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.Paper), radius);
        drawList.AddRect(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.Gold), radius);
    }
}
