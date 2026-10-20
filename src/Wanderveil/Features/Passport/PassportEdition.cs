using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Wanderveil.Features.Passport;

// A nation edition: the passport's own design, its cover lettering, colours, and the words on its nation pages.
// Its plates (cover, crest, banner, map, open book) are generated per edition by tools/passport-art, whose
// editions.mjs holds the same colours. Choosing an edition is a presentation preference, never a claim of
// nationality or allegiance in the game.
// Cloth is the cover colour, Foil its lettering, and Ink the edition's colour for printed controls on the paper
// (links, toggles); Ink always reads on the paper, even where the cloth is pale.
internal sealed record PassportEdition(string Id, string Name, string Code, string Heading, string Region, string Kicker, string Realm,
    string MapKicker, string MapTitle, string Crest, Vector4 Cloth, Vector4 Foil, Vector4 Ink)
{
    internal static readonly PassportEdition Gridania = new("gridania", "Gridania", "GRIDANIA", "EORZEA PASSPORT", "THE BLACK SHROUD", "01 / THE FOREST NATION", "REALM OF EORZEA",
        "THE TWELVESWOOD", "An atlas of quiet paths", "Wreath & sprout", Rgb(0x1E3A2A), Rgb(0xA9C28A), Rgb(0x1E3A2A));

    // In choosing order: the three city-states' neighbours side by side, the shared realm first.
    internal static readonly IReadOnlyList<PassportEdition> All = Array.AsReadOnly(new[]
    {
        new PassportEdition("eorzea", "Eorzea", "EORZEA", "EORZEA PASSPORT", "THE SIX REALMS", "01 / THE SHARED REALM", "REALM OF EORZEA",
            "ALDENARD", "An atlas of every road", "Compass star", Rgb(0x19242E), Rgb(0xB99662), Rgb(0x2A3B52)),
        Gridania,
        new PassportEdition("uldah", "Ul'dah", "ULDAH", "EORZEA PASSPORT", "THANALAN", "01 / THE DESERT JEWEL", "REALM OF EORZEA",
            "THE SANDS OF THANALAN", "An atlas of sun and stone", "Radiant sun", Rgb(0x5A1424), Rgb(0xD4A83C), Rgb(0x7A2232)),
        new PassportEdition("limsa", "Limsa Lominsa", "LIMSA", "EORZEA PASSPORT", "LA NOSCEA", "01 / THE SEAFARING NATION", "REALM OF EORZEA",
            "THE ROSTRA AND THE SEA", "An atlas of tides and harbours", "Compass rose", Rgb(0x2B5568), Rgb(0xDCE6E8), Rgb(0x1F4A60)),
        new PassportEdition("ishgard", "Ishgard", "ISHGARD", "EORZEA PASSPORT", "COERTHAS", "01 / THE HOLY SEE", "REALM OF EORZEA",
            "THE COERTHAS HIGHLANDS", "An atlas of snow and spires", "Rose window", Rgb(0x16213A), Rgb(0xC3CAD6), Rgb(0x2B3F6B)),
        new PassportEdition("garlemald", "Garlemald", "GARLEMALD", "GARLEAN PASSPORT", "ILSABARD", "01 / THE NORTHERN EMPIRE", "ILSABARD",
            "THE NORTHERN WASTES", "An atlas of iron and frost", "Opposed chevrons", Rgb(0xFFFFFF), Rgb(0x141414), Rgb(0x8E1C22)),
    });

    // The edition with this id; none yet, or an unknown id (from a later version, say), draws as the shared Eorzea edition.
    internal static PassportEdition Find(string? id) => All.FirstOrDefault(edition => edition.Id == id) ?? All[0];

    internal static bool IsKnown(string? id) => All.Any(edition => edition.Id == id);

    // The edition's second colour in its art (Gridania's leaves, Ul'dah's wine details, Limsa Lominsa's brass, Garlemald's
    // red), as tools/passport-art/editions.mjs prints it.
    internal Vector4 Accent => Rgb(Id switch
    {
        "eorzea" => 0xB99662,
        "gridania" => 0x5E8A4A,
        "uldah" => 0x9C3A4C,
        "limsa" => 0xB58A3C,
        "ishgard" => 0x7F8FB0,
        _ => 0xC0272D,
    });

    // Where the maker's mark (WANDERVEIL) sits on the cover, in board units: above the chip mark on covers that
    // carry one (tools/passport-art/svg/cover-<edition>.svg draws it at 488-512), at the foot otherwise.
    internal float MarkTop => Id is "eorzea" or "garlemald" ? 470 : 492;

    // Hovered foil: a little nearer white, or for dark foil on a pale cover, nearer its ink.
    internal Vector4 FoilBright => Vector4.Lerp(Foil, Luminance(Foil) > 0.2f ? Vector4.One : Ink, 0.35f) with { W = 1f };

    // Whether the cover is pale, so its embossing shadows must be light rather than dark.
    internal bool PaleCloth => Luminance(Cloth) > 0.5f;

    private static float Luminance(Vector4 colour) => 0.2126f * colour.X + 0.7152f * colour.Y + 0.0722f * colour.Z;

    private static Vector4 Rgb(int hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
}

// How the passport looks, apart from what its holder wrote: its edition, the holder's colours over it, and the
// symbols they placed on the cover and first pages; later also structure. Changing edition replaces the whole
// appearance (the colours return to the new edition's own, the symbols are cleared), and the one before is kept
// so it can be restored.
internal sealed record PassportAppearance(string Edition, PassportColours? Colours = null, SymbolLayer? Symbols = null)
{
    internal static readonly PassportAppearance Gridania = new(PassportEdition.Gridania.Id);

    internal PassportColours Chosen => Colours ?? PassportColours.None;

    internal SymbolLayer Placed => Symbols ?? SymbolLayer.Empty;
}
