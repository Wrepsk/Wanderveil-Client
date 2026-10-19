using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// The same composition calculates height and paints, so wrapping cannot leave fixed-height fields behind.
internal struct PageFlow(Vector2 origin, float width, UiContext context, bool isDrawing)
{
    private float offset;

    internal readonly bool IsDrawing => isDrawing;
    internal readonly float Height => offset;

    internal void Text(string text, TextRole role = TextRole.Body, bool muted = false)
    {
        var size = UiTypography.Measure(text, role, context, width);
        if (isDrawing)
            UiTypography.Draw(ImGui.GetWindowDrawList(), origin + new Vector2(0f, offset), text, role,
                context, width, muted ? UiTheme.MutedInk : UiTheme.Ink);
        offset += size.Y;
    }

    internal void Gap(float units = UiMetrics.Small) => offset += units * context.Scale;

    internal UiRect Reserve(float height)
    {
        var bounds = new UiRect(origin + new Vector2(0f, offset), new Vector2(width, height));
        offset += height;
        return bounds;
    }

    internal void Rule()
    {
        Gap();
        if (isDrawing)
        {
            var start = origin + new Vector2(0f, offset);
            BookSurface.Line(ImGui.GetWindowDrawList(), start, start + new Vector2(width, 0f), ImGui.GetColorU32(UiTheme.Gold));
        }
        offset += context.Scale;
        Gap();
    }
}
