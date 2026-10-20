using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Wanderveil.Features.Passport;

// What the holder wrote in their passport: all self-reported display text, never verified against the game.
// Number is the passport number, printed on the identity page and engraved on every stamp page; it is given
// once, when the passport is first issued, and never edited. An empty Signature signs with the name; an empty
// CoverLine leaves the cover as the edition printed it. Portrait is where the holder's own picture sits in its
// frame, or null for the edition's placeholder; Frame is how that picture is set on the page.
internal sealed record PassportIdentity(string Name, string HomeWorld, string FavoriteJob, string Bio, string Motto, string Number = PassportIdentity.SampleNumber)
{
    internal const string SampleNumber = "EZ 0042 1187";
    internal string DataCenter { get; init; } = "";
    internal string FreeCompany { get; init; } = "";
    internal string Signature { get; init; } = "";
    internal string CoverLine { get; init; } = "";
    internal DateOnly? Issued { get; init; }
    internal PortraitCrop? Portrait { get; init; }
    // How the holder's own picture is set on the page.
    internal PictureFrame Frame { get; init; }
    // The passport's appearance (its edition), or null until a new passport's holder chooses one; until then,
    // and for an edition this version does not know, it draws as the shared Eorzea edition. PreviousAppearance is the one replaced by the last change, kept so it can be restored.
    internal PassportAppearance? Appearance { get; init; }
    internal PassportAppearance? PreviousAppearance { get; init; }

    internal PassportEdition Edition => PassportEdition.Find(Appearance?.Edition);
    // The colours it is drawn in: the edition's, with the holder's over them, kept legible.
    internal PassportLook Look => PassportLook.Of(Edition, Appearance?.Colours);

    internal string SignedName => Signature.Length > 0 ? Signature : Name;

    internal static readonly PassportIdentity Sample = new(
        "Ari Vale",
        "Demo world",
        "Bard / Botanist",
        "A keeper of small stories, quiet paths, and views worth taking the long way around.",
        "Take the scenic route. There is always another story waiting.")
    { DataCenter = "Demo DC", FreeCompany = "Unassigned", Issued = new DateOnly(2026, 10, 18), Appearance = PassportAppearance.Gridania };

    // A new, empty passport issued today, with its own number; its holder then chooses its edition.
    internal static PassportIdentity Issue(DateOnly today, Random random) =>
        new("", "", "", "", "", string.Create(CultureInfo.InvariantCulture, $"EZ {random.Next(10000):D4} {random.Next(10000):D4}")) { Issued = today };

    // The longest text each field keeps, in characters as a reader counts them.
    internal static int Limit(IdentityField field) => field switch
    {
        IdentityField.Name or IdentityField.Signature => 40,
        IdentityField.HomeWorld or IdentityField.DataCenter => 24,
        IdentityField.FavoriteJob or IdentityField.FreeCompany => 40,
        IdentityField.CoverLine => 28,
        IdentityField.Motto => 120,
        _ => 400,
    };

    internal string this[IdentityField field] => field switch
    {
        IdentityField.Name => Name,
        IdentityField.HomeWorld => HomeWorld,
        IdentityField.DataCenter => DataCenter,
        IdentityField.FavoriteJob => FavoriteJob,
        IdentityField.FreeCompany => FreeCompany,
        IdentityField.Motto => Motto,
        IdentityField.Bio => Bio,
        IdentityField.Signature => Signature,
        _ => CoverLine,
    };

    internal PassportIdentity With(IdentityField field, string value) => field switch
    {
        IdentityField.Name => this with { Name = value },
        IdentityField.HomeWorld => this with { HomeWorld = value },
        IdentityField.DataCenter => this with { DataCenter = value },
        IdentityField.FavoriteJob => this with { FavoriteJob = value },
        IdentityField.FreeCompany => this with { FreeCompany = value },
        IdentityField.Motto => this with { Motto = value },
        IdentityField.Bio => this with { Bio = value },
        IdentityField.Signature => this with { Signature = value },
        _ => this with { CoverLine = value },
    };

    // Every field as it is kept: one line of text (line breaks and other control characters become spaces,
    // runs of spaces one space), trimmed, within its limit; a portrait placement inside its frame.
    internal PassportIdentity Normalize()
    {
        var result = this with { Number = Clean(Number, 16), Portrait = Portrait?.Normalize(), Frame = Enum.IsDefined(Frame) ? Frame : PictureFrame.Printed };
        foreach (var field in Enum.GetValues<IdentityField>()) result = result.With(field, Clean(result[field], Limit(field)));
        return result;
    }

    internal static string Clean(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var space = char.IsControl(character) || char.IsWhiteSpace(character);
            if (space && (builder.Length == 0 || builder[^1] == ' ')) continue;
            builder.Append(space ? ' ' : character);
        }
        var clean = builder.ToString().TrimEnd();
        // Cut between whole characters, never inside a surrogate pair or combining sequence.
        var info = new StringInfo(clean);
        return info.LengthInTextElements <= limit ? clean : info.SubstringByTextElements(0, limit).TrimEnd();
    }

    // The machine-readable line under the identity details: name letters and number digits, padded with '<'.
    internal string MachineLine()
    {
        const int Length = 36;
        var digits = new StringBuilder();
        foreach (var character in Number) if (char.IsAsciiDigit(character)) digits.Append(character);
        var builder = new StringBuilder("P<WV<" + Edition.Code + "<");
        foreach (var character in Name.ToUpperInvariant())
            if (character is >= 'A' and <= 'Z') builder.Append(character);
            else if (builder[^1] != '<') builder.Append('<');
        // A long name is cut, never the number; name and number are set apart by a double filler.
        builder.Length = Math.Min(builder.Length, Length - 2 - digits.Length);
        builder.Append(builder[^1] == '<' ? "<" : "<<").Append(digits);
        while (builder.Length < Length) builder.Append('<');
        return builder.ToString(0, Length);
    }
}

// Printed: an ink keyline around a lighter mount, with a soft shadow, as a printed passport photo.
// Corners: dark paper photo corners holding the picture. Embossed: a mount pressed into the paper, no ink.
// Deckled: the picture's own edge torn rough. None: the picture as it is.
internal enum PictureFrame { Printed, Corners, Embossed, Deckled, None }

internal enum IdentityField { Name, HomeWorld, DataCenter, FavoriteJob, FreeCompany, Motto, Bio, Signature, CoverLine }

// Where the holder's own picture sits in the portrait frame: the picture's point at the frame's centre (0 to 1
// across and down) and how far it is zoomed past just covering the frame (1 to 4).
internal readonly record struct PortraitCrop(float X, float Y, float Zoom)
{
    internal const float MaximumZoom = 4f;
    internal static readonly PortraitCrop Centered = new(0.5f, 0.5f, 1f);

    internal bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Zoom) && X is >= 0f and <= 1f && Y is >= 0f and <= 1f && Zoom is >= 1f and <= MaximumZoom;

    internal PortraitCrop Normalize() => new(Finite(X, 0.5f, 0f, 1f), Finite(Y, 0.5f, 0f, 1f), Finite(Zoom, 1f, 1f, MaximumZoom));

    // The part of the picture (as texture coordinates) that fills a frame: a picture of imageAspect
    // (width / height) covering a frame of frameAspect, zoomed, its centre kept so the frame stays covered.
    internal (Vector2 Min, Vector2 Max) Window(float imageAspect, float frameAspect)
    {
        var span = imageAspect > frameAspect ? new Vector2(frameAspect / imageAspect, 1f) : new Vector2(1f, imageAspect / frameAspect);
        span /= Zoom;
        var center = Clamp(new Vector2(X, Y), span);
        return (center - span / 2, center + span / 2);
    }

    // The same placement with its centre moved as far as the frame allows.
    internal PortraitCrop Clamped(float imageAspect, float frameAspect)
    {
        var (min, max) = Window(imageAspect, frameAspect);
        var center = (min + max) / 2;
        return this with { X = center.X, Y = center.Y };
    }

    private static Vector2 Clamp(Vector2 center, Vector2 span) =>
        new(Math.Clamp(center.X, span.X / 2, 1 - span.X / 2), Math.Clamp(center.Y, span.Y / 2, 1 - span.Y / 2));

    private static float Finite(float value, float fallback, float min, float max) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

// The holder's identity as shown and edited. Changing it raises Changed, which the identity store saves.
internal sealed class PassportProfile(PassportIdentity identity)
{
    internal PassportIdentity Identity { get; private set; } = identity.Normalize();
    internal event Action? Changed;

    // Changes the passport's edition, replacing its whole appearance; the appearance it had is kept so it can be
    // restored. A first choice has nothing to keep. Details, picture, cover line, and stamps never change.
    internal bool ChangeEdition(string edition)
    {
        var current = Identity.Appearance;
        if (current?.Edition == edition) return false;
        return Update(Identity with { Appearance = new PassportAppearance(edition), PreviousAppearance = current });
    }

    // Recolours one part of the passport (null returns it to the edition's own). Colours are part of the
    // appearance, so a later change of edition replaces them, and change back brings them back.
    internal bool SetColour(ColourPart part, Rgb? colour)
    {
        var appearance = Identity.Appearance ?? new PassportAppearance(Identity.Edition.Id);
        var colours = appearance.Chosen.With(part, colour);
        return Update(Identity with { Appearance = appearance with { Colours = colours.IsEmpty ? null : colours } });
    }

    // Returns every part to the edition's own; the guard's settings are preferences, kept.
    internal bool ResetColours() => Identity.Appearance is { Colours: not null } appearance
        && Update(Identity with { Appearance = appearance with { Colours = new PassportColours(Free: appearance.Chosen.Free, Exact: appearance.Chosen.Exact) is { IsEmpty: false } kept ? kept : null } });

    // Lets the guard move one part, or keeps it exactly as picked even while the guard is on.
    internal bool SetAdjustable(ColourPart part, bool adjustable)
    {
        var appearance = Identity.Appearance ?? new PassportAppearance(Identity.Edition.Id);
        var colours = appearance.Chosen.WithExact(part, !adjustable);
        return Update(Identity with { Appearance = appearance with { Colours = colours.IsEmpty ? null : colours } });
    }

    // Turns the legibility guard on or off (off: every colour is used exactly as picked).
    internal bool SetReadable(bool readable)
    {
        var appearance = Identity.Appearance ?? new PassportAppearance(Identity.Edition.Id);
        var colours = appearance.Chosen with { Free = !readable };
        return Update(Identity with { Appearance = appearance with { Colours = colours.IsEmpty ? null : colours } });
    }

    // Places a symbol on top of the others on its surface; false when it is unknown or its surface is full.
    internal bool AddSymbol(PlacedSymbol symbol) =>
        symbol.IsKnown && Appearance().Placed.HasRoomOn(symbol.Surface) && SetSymbols(Appearance().Placed.Add(symbol));

    // Moves, turns, or resizes the symbol at index (in the layer's drawing order); it comes to the top.
    internal bool ReplaceSymbol(int index, PlacedSymbol symbol)
    {
        var placed = Appearance().Placed;
        if ((uint)index >= (uint)placed.Items.Length || !symbol.IsKnown) return false;
        // Carried onto another surface, it needs room there.
        if (symbol.Surface != placed.Items[index].Surface && !placed.HasRoomOn(symbol.Surface)) return false;
        return SetSymbols(placed.Replace(index, symbol));
    }

    internal bool RemoveSymbol(int index) => SetSymbols(Appearance().Placed.Remove(index));

    // Prints every page on another background; part of the appearance, so a change of edition resets it.
    internal bool SetBackground(PageBackground background) =>
        Enum.IsDefined(background) && Update(Identity with { Appearance = Appearance() with { Background = background } });

    // Takes every symbol off the cover and pages.
    internal bool ClearSymbols() => Identity.Appearance is { Symbols: not null } && SetSymbols(SymbolLayer.Empty);

    private PassportAppearance Appearance() => Identity.Appearance ?? new PassportAppearance(Identity.Edition.Id);

    private bool SetSymbols(SymbolLayer symbols) => Update(Identity with { Appearance = Appearance() with { Symbols = symbols.IsEmpty ? null : symbols } });

    // Puts back the appearance the last change replaced (and keeps the one it replaces in turn).
    internal bool RestorePrevious() =>
        Identity.PreviousAppearance is { } previous && Update(Identity with { Appearance = previous, PreviousAppearance = Identity.Appearance });

    // Keeps the identity normalized; returns whether anything changed.
    internal bool Update(PassportIdentity next)
    {
        next = next.Normalize();
        if (next == Identity) return false;
        Identity = next;
        Changed?.Invoke();
        return true;
    }
}
