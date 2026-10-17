using Dalamud.Interface.Utility;

namespace Wanderveil.UI;

// The only host-specific UI scale read. Components receive it through UiContext.
internal static class ShellStyle
{
    internal static float Scale => ImGuiHelpers.GlobalScale;
}
