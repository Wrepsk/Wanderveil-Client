using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal static class CompassMark
{
    private const float Size = 32f;
    private const float Radius = 13f;
    private const float NeedleLength = 10f;
    private const float NeedleHalfWidth = 3f;

    internal static void Draw()
    {
        var scale = ShellStyle.Scale;
        var extent = new Vector2(Size * scale);
        var center = ImGui.GetCursorScreenPos() + (extent / 2f);
        var drawList = ImGui.GetWindowDrawList();
        var gold = ImGui.GetColorU32(UiTheme.Gold);
        var paper = ImGui.GetColorU32(UiTheme.Paper);
        var left = center - new Vector2(NeedleHalfWidth * scale, 0f);
        var right = center + new Vector2(NeedleHalfWidth * scale, 0f);

        drawList.AddCircle(center, Radius * scale, gold, 32, scale);
        drawList.AddTriangleFilled(center - new Vector2(0f, NeedleLength * scale), left, right, gold);
        drawList.AddTriangleFilled(center + new Vector2(0f, NeedleLength * scale), right, left, paper);
        ImGui.Dummy(extent);
    }
}
