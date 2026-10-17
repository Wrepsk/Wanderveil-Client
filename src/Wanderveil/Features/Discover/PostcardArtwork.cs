using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Discover;

internal static class PostcardArtwork
{
    // Original geometric scenery, with no texture loads or borrowed game art.
    internal static void Draw(UiRect bounds)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = bounds.Position;
        var size = bounds.Size;
        drawList.PushClipRect(origin, bounds.Max, true);
        try
        {
            drawList.AddRectFilled(origin, bounds.Max, ImGui.GetColorU32(UiTheme.PaperInset));
            drawList.AddCircleFilled(origin + (size * new Vector2(0.76f, 0.3f)), size.Y * 0.12f, ImGui.GetColorU32(UiTheme.Gold));
            drawList.AddRectFilled(origin + new Vector2(0f, size.Y * 0.56f), bounds.Max, ImGui.GetColorU32(UiTheme.Veil));
            drawList.AddTriangleFilled(origin + (size * new Vector2(-0.1f, 1f)),
                origin + (size * new Vector2(0.24f, 0.34f)), origin + (size * new Vector2(0.68f, 1f)), ImGui.GetColorU32(UiTheme.Action));
            drawList.AddTriangleFilled(origin + (size * new Vector2(0.38f, 1f)),
                origin + (size * new Vector2(0.86f, 0.7f)), origin + (size * new Vector2(1.2f, 1f)), ImGui.GetColorU32(UiTheme.Ink));
        }
        finally
        {
            drawList.PopClipRect();
        }
    }
}
