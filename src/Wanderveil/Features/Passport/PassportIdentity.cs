using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Wanderveil.Features.Passport;

// What the holder wrote in their passport: all self-reported display text, never verified against the game.
// Number is the passport number, printed on the identity page and engraved on every stamp page; it is given
// once, when the passport is first issued, and never edited. An empty Signature signs with the name; an empty
// CoverLine leaves the cover as the edition printed it. Portrait is where the holder's own picture sits in its
// frame, or null for the edition's placeholder.
internal sealed record PassportIdentity(string Name, string HomeWorld, string FavoriteJob, string Bio, string Motto, string Number = PassportIdentity.SampleNumber)
{
    internal const string SampleNumber = "EZ 0042 1187";
    internal string DataCenter { get; init; } = "";
    internal string FreeCompany { get; init; } = "";
    internal string Signature { get; init; } = "";
    internal string CoverLine { get; init; } = "";
    internal DateOnly? Issued { get; init; }
    internal PortraitCrop? Portrait { get; init; }

    internal string SignedName => Signature.Length > 0 ? Signature : Name;

    internal static readonly PassportIdentity Sample = new(
        "Ari Vale",
        "Demo world",
        "Bard / Botanist",
        "A keeper of small stories, quiet paths, and views worth taking the long way around.",
        "Take the scenic route. There is always another story waiting.")
    { DataCenter = "Demo DC", FreeCompany = "Unassigned", Issued = new DateOnly(2026, 10, 18) };

    // A new, empty passport issued today, with its own number.
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
        var result = this with { Number = Clean(Number, 16), Portrait = Portrait?.Normalize() };
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
        var builder = new StringBuilder("P<WV<GRIDANIA<");
        foreach (var character in Name.ToUpperInvariant())
            if (character is >= 'A' and <= 'Z') builder.Append(character);
            else if (builder[^1] != '<') builder.Append('<');
        builder.Append('<');
        foreach (var character in Number) if (char.IsAsciiDigit(character)) builder.Append(character);
        while (builder.Length < 36) builder.Append('<');
        return builder.ToString(0, 36);
    }
}

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
