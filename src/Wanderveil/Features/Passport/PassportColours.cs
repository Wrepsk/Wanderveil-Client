using System;
using System.Globalization;
using System.Numerics;

namespace Wanderveil.Features.Passport;

// A colour as the holder picks it and the passport file stores it (#RRGGBB).
internal readonly record struct Rgb(byte R, byte G, byte B)
{
    internal Vector4 Vector => new(R / 255f, G / 255f, B / 255f, 1f);

    internal static Rgb Of(Vector4 colour) => new(Byte(colour.X), Byte(colour.Y), Byte(colour.Z));

    internal static Rgb Hex(int hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    internal static bool TryParse(string? text, out Rgb colour)
    {
        colour = default;
        if (text is not { Length: 7 } || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
        colour = Hex(value);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    private static byte Byte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0, 255);
}

// The parts of the passport the holder can recolour. Cloth: the cover, spine, book edges, and banner. Foil: the
// cover lettering, crest, decoration, and banner trim. Ink: printed links and controls on the paper. Paper: the
// pages themselves.
internal enum ColourPart { Cloth, Foil, Ink, Paper }

// The holder's colours over an edition: each part null keeps the edition's own.
internal sealed record PassportColours(Rgb? Cloth = null, Rgb? Foil = null, Rgb? Ink = null, Rgb? Paper = null)
{
    internal static readonly PassportColours None = new();

    internal bool IsEmpty => Cloth == null && Foil == null && Ink == null && Paper == null;

    internal Rgb? this[ColourPart part] => part switch
    {
        ColourPart.Cloth => Cloth,
        ColourPart.Foil => Foil,
        ColourPart.Ink => Ink,
        _ => Paper,
    };

    internal PassportColours With(ColourPart part, Rgb? colour) => part switch
    {
        ColourPart.Cloth => this with { Cloth = colour },
        ColourPart.Foil => this with { Foil = colour },
        ColourPart.Ink => this with { Ink = colour },
        _ => this with { Paper = colour },
    };
}

// The colours the passport is drawn in: the edition's, with the holder's over them, then kept legible. The paper
// is always as picked; text and captions print dark on a light paper and light on a dark one (LightText), and
// must read on it (4.5:1). Foil must read on its cloth (3:1) and printed controls on the paper (4.5:1); a colour
// that would not is moved toward black or white just far enough, and Adjusted says which were moved.
internal sealed record PassportLook(Vector4 Cloth, Vector4 Foil, Vector4 Ink, Vector4 Paper, Vector4 Text, Vector4 Caption, bool LightText, ColourPart[] Adjusted)
{
    // The paper the editions are printed on (tools/passport-art/editions.mjs, shared.paper).
    internal static readonly Vector4 EditionPaper = Rgb.Hex(0xD8C5A3).Vector;
    // The text inks printed on every page (UiTheme.Ink and MutedInk): the paper must keep them readable.
    private static readonly Vector4 TextInk = Rgb.Hex(0x19242E).Vector;
    private static readonly Vector4 CaptionInk = Rgb.Hex(0x43505A).Vector;
    // Their light counterparts, for dark paper: warm off-white text, a quieter caption.
    private static readonly Vector4 LightTextInk = Rgb.Hex(0xEFE8DA).Vector;
    private static readonly Vector4 LightCaptionInk = Rgb.Hex(0xC2BAA9).Vector;

    internal static PassportLook Of(PassportEdition edition, PassportColours? colours = null)
    {
        colours ??= PassportColours.None;
        var adjusted = new System.Collections.Generic.List<ColourPart>();
        var cloth = colours.Cloth?.Vector ?? edition.Cloth;
        var paper = colours.Paper?.Vector ?? EditionPaper;
        // Text turns light where dark ink would read worse than light; black or white always reaches 4.5:1.
        var light = Contrast(LightTextInk, paper) > Contrast(TextInk, paper);
        var text = Keep(null, light ? LightTextInk : TextInk, paper, 4.5f);
        var caption = Keep(null, light ? LightCaptionInk : CaptionInk, paper, 4.5f);
        var foil = Keep(ColourPart.Foil, colours.Foil?.Vector ?? edition.Foil, cloth, 3f);
        var ink = Keep(ColourPart.Ink, colours.Ink?.Vector ?? edition.Ink, paper, 4.5f);
        return new PassportLook(cloth, foil, ink, paper, text, caption, light, adjusted.ToArray());

        // Moves colour until it reads against other: toward white on a dark background, black on a light one.
        Vector4 Keep(ColourPart? part, Vector4 colour, Vector4 other, float ratio)
        {
            if (Contrast(colour, other) >= ratio) return colour;
            var target = Contrast(Vector4.One, other) > Contrast(new Vector4(0, 0, 0, 1), other) ? Vector4.One : new Vector4(0, 0, 0, 1);
            var moved = colour;
            for (var step = 1; step <= 40 && Contrast(moved, other) < ratio; step++) moved = Vector4.Lerp(colour, target, step / 40f) with { W = 1f };
            if (part is { } adjustedPart) adjusted.Add(adjustedPart);
            return moved;
        }
    }

    // How far the holder's colours differ from the edition's on the plates that are recoloured.
    internal bool Recoloured(PassportEdition edition) => !Near(Cloth, edition.Cloth) || !Near(Foil, edition.Foil) || !Near(Paper, EditionPaper) || LightText;

    // Hovered foil: a little nearer white, or for dark foil, nearer its ink.
    internal Vector4 FoilBright => Vector4.Lerp(Foil, Luminance(Foil) > 0.2f ? Vector4.One : Ink, 0.35f) with { W = 1f };

    // Whether the cover is pale, so its embossing shadows must be light rather than dark.
    internal bool PaleCloth => Luminance(Cloth) > 0.5f;

    // A slightly darker paper for insets (fields, mounts).
    internal Vector4 PaperInset => new(Paper.X * 0.95f, Paper.Y * 0.92f, Paper.Z * 0.87f, 1f);

    internal static double Contrast(Vector4 first, Vector4 second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    // WCAG relative luminance.
    internal static float Luminance(Vector4 colour)
    {
        static float Channel(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Channel(colour.X) + 0.7152f * Channel(colour.Y) + 0.0722f * Channel(colour.Z);
    }

    private static bool Near(Vector4 a, Vector4 b) => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) + MathF.Abs(a.Z - b.Z) < 1.5f / 255f;

    // Equality by colour (the adjusted list is derived from them).
    public bool Equals(PassportLook? other) => other != null && Cloth == other.Cloth && Foil == other.Foil && Ink == other.Ink && Paper == other.Paper;

    public override int GetHashCode() => HashCode.Combine(Cloth, Foil, Ink, Paper);
}
