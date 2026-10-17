using System.Numerics;
using Dalamud.Interface.Utility;

namespace Wanderveil.UI;

// Initial shell tokens; the full shared design system is a separate UI phase.
internal static class ShellStyle
{
    internal static readonly Vector4 Parchment = new(216f / 255f, 197f / 255f, 163f / 255f, 1f);
    internal static readonly Vector4 Gold = new(185f / 255f, 150f / 255f, 98f / 255f, 1f);
    internal static readonly Vector4 Veil = new(131f / 255f, 185f / 255f, 184f / 255f, 1f);

    internal static float Scale => ImGuiHelpers.GlobalScale;
}
