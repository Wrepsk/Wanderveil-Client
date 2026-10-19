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
    // The current edition's cover cloth, foil, and its ink for printed controls on the paper. The book sets them
    // each frame from the passport's edition (UseEdition); they start as Gridania's (tools/passport-art/editions.mjs).
    internal static Vector4 Edition { get; private set; } = Rgb(30, 58, 42);
    internal static Vector4 Foil { get; private set; } = Rgb(169, 194, 138);
    internal static Vector4 FoilBright { get; private set; } = Rgb(205, 222, 180);
    internal static Vector4 EditionInk { get; private set; } = Rgb(30, 58, 42);

    // Drawing is single-threaded, so the edition being drawn sets these for everything drawn after it.
    internal static void UseEdition(Vector4 cloth, Vector4 foil, Vector4 foilBright, Vector4 ink)
    {
        Edition = cloth;
        Foil = foil;
        FoilBright = foilBright;
        EditionInk = ink;
    }

    private static Vector4 Rgb(byte red, byte green, byte blue) => new(red / 255f, green / 255f, blue / 255f, 1f);
}
