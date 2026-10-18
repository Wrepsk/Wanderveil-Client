using System.Numerics;

namespace Wanderveil.UI.Primitives;

internal static class UiTheme
{
    internal static readonly Vector4 Ink = Rgb(25, 36, 46);
    internal static readonly Vector4 MutedInk = Rgb(67, 80, 90);
    internal static readonly Vector4 Paper = Rgb(216, 197, 163);
    internal static readonly Vector4 PaperInset = Rgb(205, 181, 142);
    internal static readonly Vector4 Gold = Rgb(185, 150, 98);
    internal static readonly Vector4 Veil = Rgb(131, 185, 184);
    internal static readonly Vector4 Action = Rgb(52, 74, 82);
    internal static readonly Vector4 ActionHovered = Rgb(58, 80, 89);
    internal static readonly Vector4 ActionPressed = Rgb(38, 57, 65);
    internal static readonly Vector4 Shadow = new(0f, 0f, 0f, 0.24f);
    // Gridania edition cover cloth and foil (tools/passport-art/editions.mjs).
    internal static readonly Vector4 Edition = Rgb(30, 58, 42);
    internal static readonly Vector4 Foil = Rgb(169, 194, 138);
    internal static readonly Vector4 FoilBright = Rgb(205, 222, 180);

    private static Vector4 Rgb(byte red, byte green, byte blue) => new(red / 255f, green / 255f, blue / 255f, 1f);
}
