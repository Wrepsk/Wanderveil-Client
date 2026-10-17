using System;
using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Primitives;

internal static class UiLayout
{
    // Reserve scrollbar room before ImGui knows this frame's content height. Otherwise the previous
    // full-width content briefly overflows horizontally on the frame a vertical scrollbar appears.
    internal static float ContentWidth => MathF.Max(1f, ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize);
}
