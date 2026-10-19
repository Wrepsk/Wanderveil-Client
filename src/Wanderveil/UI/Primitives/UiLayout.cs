using System;
using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Primitives;

internal static class UiLayout
{
    // Content always leaves room for a vertical scrollbar, whether or not one is showing, so its width never
    // depends on the scrollbar. Otherwise content near its window's height could loop every frame: a scrollbar
    // appears, narrower content (and art sized from that width) fits again, the scrollbar goes, and it overflows.
    internal static float ContentWidth
    {
        get
        {
            var padding = ImGui.GetWindowContentRegionMin().X;
            var right = MathF.Min(ImGui.GetWindowContentRegionMax().X, ImGui.GetWindowWidth() - padding - ImGui.GetStyle().ScrollbarSize);
            return MathF.Max(1f, right - (ImGui.GetCursorScreenPos().X - ImGui.GetWindowPos().X));
        }
    }
}
