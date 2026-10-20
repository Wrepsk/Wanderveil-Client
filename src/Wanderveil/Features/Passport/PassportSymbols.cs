using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Wanderveil.Features.Passport;

// A symbol from the provided set. Id is what the passport file keeps; Cell is its place in the symbol plate
// (tools/passport-art writes symbols.png in this order: the six nations' crests, their emblems, then motifs).
internal sealed record PassportSymbol(string Id, string Name, int Cell)
{
    internal const int Columns = 6;
    internal const int Rows = 4;

    internal static readonly IReadOnlyList<PassportSymbol> All = Array.AsReadOnly(new[]
    {
        new PassportSymbol("crest-eorzea", "Eorzea crest", 0), new PassportSymbol("crest-gridania", "Gridania crest", 1),
        new PassportSymbol("crest-uldah", "Ul'dah crest", 2), new PassportSymbol("crest-limsa", "Limsa Lominsa crest", 3),
        new PassportSymbol("crest-ishgard", "Ishgard crest", 4), new PassportSymbol("crest-garlemald", "Garlemald crest", 5),
        new PassportSymbol("emblem-eorzea", "Crystal", 6), new PassportSymbol("emblem-gridania", "Sprig", 7),
        new PassportSymbol("emblem-uldah", "Scales", 8), new PassportSymbol("emblem-limsa", "Anchor", 9),
        new PassportSymbol("emblem-ishgard", "Lance and pennon", 10), new PassportSymbol("emblem-garlemald", "Cog", 11),
        new PassportSymbol("sparkle", "Sparkle", 12), new PassportSymbol("crystal", "Shard", 13),
        new PassportSymbol("moon", "Moon", 14), new PassportSymbol("sun", "Sun", 15),
        new PassportSymbol("leaf", "Leaf", 16), new PassportSymbol("waves", "Waves", 17),
        new PassportSymbol("snowflake", "Snowflake", 18), new PassportSymbol("key", "Key", 19),
        new PassportSymbol("flower", "Flower", 20), new PassportSymbol("feather", "Feather", 21),
        new PassportSymbol("mountains", "Mountains", 22), new PassportSymbol("heart", "Heart", 23),
    });

    internal static PassportSymbol? Find(string? id) => All.FirstOrDefault(symbol => symbol.Id == id);
}

// Where symbols go: the front cover, and the pages before the stamps (by page id). Symbols on a page lie on
// the paper beneath everything printed there; on the cover they are pressed in foil.
internal static class SymbolSurface
{
    internal const string Cover = "cover";
    internal static readonly IReadOnlyList<string> Pages = Array.AsReadOnly(new[] { "nation-banner", "nation-map", "portrait", "information" });

    internal static bool IsKnown(string? surface) => surface == Cover || Pages.Contains(surface);
}

// One placed symbol: on a surface, its centre (0 to 1 across and down the surface), how far it is turned
// (radians), its size as a share of the surface's width, and its own colour (null follows the passport: foil on the
// cover, page ink on the pages).
internal readonly record struct PlacedSymbol(string Symbol, string Surface, float X, float Y, float Rotation, float Size, Rgb? Colour = null)
{
    internal const float MinimumSize = 0.05f;
    internal const float MaximumSize = 0.6f;
    internal const float StartSize = 0.18f;

    internal bool IsKnown => PassportSymbol.Find(Symbol) != null && SymbolSurface.IsKnown(Surface);

    // Kept on its surface, turned within one turn, and within the sizes allowed.
    internal PlacedSymbol Normalize() => this with
    {
        X = Finite(X, 0.5f, 0f, 1f),
        Y = Finite(Y, 0.5f, 0f, 1f),
        Rotation = float.IsFinite(Rotation) ? Stamps.StampPlacement.Normalize(Rotation) : 0f,
        Size = Finite(Size, StartSize, MinimumSize, MaximumSize),
    };

    private static float Finite(float value, float fallback, float min, float max) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

// The holder's placed symbols in drawing order (later ones on top). Equal when they hold the same symbols in the
// same order, so an unchanged layer never counts as a change.
internal sealed class SymbolLayer : IEquatable<SymbolLayer>
{
    // Enough to decorate freely while keeping every surface quick to draw and the passport file small.
    internal const int MaximumPerSurface = 24;
    internal static readonly SymbolLayer Empty = new(ImmutableArray<PlacedSymbol>.Empty);

    internal ImmutableArray<PlacedSymbol> Items { get; }

    private SymbolLayer(ImmutableArray<PlacedSymbol> items) => Items = items;

    // Only known symbols on known surfaces, each normalized, at most MaximumPerSurface to a surface (the first ones).
    internal static SymbolLayer Of(IEnumerable<PlacedSymbol> symbols)
    {
        var builder = ImmutableArray.CreateBuilder<PlacedSymbol>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!symbol.IsKnown) continue;
            var count = counts.GetValueOrDefault(symbol.Surface);
            if (count >= MaximumPerSurface) continue;
            counts[symbol.Surface] = count + 1;
            builder.Add(symbol.Normalize());
        }
        return builder.Count == 0 ? Empty : new SymbolLayer(builder.ToImmutable());
    }

    internal bool IsEmpty => Items.IsEmpty;

    internal int CountOn(string surface) => Items.Count(symbol => symbol.Surface == surface);

    internal bool HasRoomOn(string surface) => CountOn(surface) < MaximumPerSurface;

    // A symbol added on top of the others.
    internal SymbolLayer Add(PlacedSymbol symbol) => Of(Items.Add(symbol));

    // The symbol at index moved, turned, or resized; it comes to the top, as a sheet laid down last would.
    internal SymbolLayer Replace(int index, PlacedSymbol symbol) => (uint)index < (uint)Items.Length ? Of(Items.RemoveAt(index).Add(symbol)) : this;

    internal SymbolLayer Remove(int index) => (uint)index < (uint)Items.Length ? Of(Items.RemoveAt(index)) : this;

    public bool Equals(SymbolLayer? other) => other != null && Items.SequenceEqual(other.Items);
    public override bool Equals(object? obj) => Equals(obj as SymbolLayer);
    public static bool operator ==(SymbolLayer? left, SymbolLayer? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(SymbolLayer? left, SymbolLayer? right) => !(left == right);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items) hash.Add(item);
        return hash.ToHashCode();
    }
}
