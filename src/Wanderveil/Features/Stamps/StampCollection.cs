using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Wanderveil.Features.Stamps;

// What a stamp was collected from, kept with the stamp so its info page reads the same even if that
// discovery is later gone: its kind (POSTCARD) and title.
internal sealed record StampSource(string Kind, string Title);

// One issued stamp: its own identifier, the design revision it was issued with, the discovery it came
// from, and when. Instance identifiers are never design identifiers.
internal sealed record StampInstance(string Id, StampDesign Design, string DiscoveryId, DateTimeOffset CollectedAt, StampProvenance Provenance, StampSource? Source = null);

// Where the owner put a stamp: a stamp page (0 is the first page after the identity pages; there is no
// last one), the stamp's centre across and down that page from 0 to 1, and its turn in radians.
internal readonly record struct StampPlacement(int Sheet, float X, float Y, float Rotation)
{
    // Far beyond any real collection; it only keeps page numbers from overflowing.
    internal const int MaximumSheet = 99_999;

    internal bool IsValid => Sheet is >= 0 and <= MaximumSheet && float.IsFinite(X) && float.IsFinite(Y) && X is >= 0f and <= 1f && Y is >= 0f and <= 1f
        && float.IsFinite(Rotation) && MathF.Abs(Rotation) <= MathF.PI + 1e-4f;

    // The same turn expressed between -pi and pi, so turning a stamp round and round never grows.
    internal static float Normalize(float rotation)
    {
        return float.IsFinite(rotation) ? MathF.IEEERemainder(rotation, MathF.Tau) : 0f;
    }
}

internal sealed record PlacedStamp(StampInstance Stamp, StampPlacement Placement);

internal enum CollectOutcome { Collected, AlreadyCollected, Unavailable }

internal readonly record struct CollectResult(CollectOutcome Outcome, StampInstance? Stamp);

// The stamps one passport holds, in the order they were collected, and where the owner put each one.
// A new stamp starts unplaced until the owner presses it onto a page. Only placements are kept, so empty
// pages cost nothing. Placed stamps are layered in the order they were put down, the latest on top.
// Duplicate policy: a passport holds at most one stamp per discovery. Collecting again changes nothing and
// returns the stamp already held, even if that discovery's design has a newer revision since.
// Used on the UI thread only; storage saves a snapshot when Changed is raised.
internal sealed class StampCollection
{
    internal const string IdPrefix = "local-demo-";
    private readonly List<StampInstance> stamps;
    private readonly List<PlacedStamp> placed;
    // Each placed stamp's place, by stamp identifier, so looking one up never walks the whole book.
    private readonly Dictionary<string, StampPlacement> places = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> clock;
    private int issued;

    internal StampCollection(Func<DateTimeOffset>? clock = null, IEnumerable<StampInstance>? restored = null, IEnumerable<PlacedStamp>? placements = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.Now);
        stamps = restored?.ToList() ?? [];
        if (stamps.Any(stamp => stamp == null) || stamps.Select(stamp => stamp.Id).Distinct(StringComparer.Ordinal).Count() != stamps.Count
            || stamps.Select(stamp => stamp.DiscoveryId).Distinct(StringComparer.Ordinal).Count() != stamps.Count)
            throw new ArgumentException("Restored stamps need unique identifiers and at most one stamp per discovery.", nameof(restored));
        placed = placements?.ToList() ?? [];
        if (placed.Any(entry => entry == null || !entry.Placement.IsValid || !stamps.Contains(entry.Stamp))
            || placed.Select(entry => entry.Stamp.Id).Distinct(StringComparer.Ordinal).Count() != placed.Count)
            throw new ArgumentException("Restored placements need valid places for known stamps, one each.", nameof(placements));
        foreach (var entry in placed) places[entry.Stamp.Id] = entry.Placement;
        // New identifiers continue after the highest one already issued.
        issued = stamps.Select(stamp => stamp.Id.StartsWith(IdPrefix, StringComparison.Ordinal)
            && int.TryParse(stamp.Id.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(0).Max();
    }

    internal IReadOnlyList<StampInstance> Stamps => stamps;

    // Placed stamps from the bottom layer to the top.
    internal IReadOnlyList<PlacedStamp> Placed => placed;

    // Stamps waiting to be pressed onto a page, oldest first.
    internal IEnumerable<StampInstance> Unplaced => stamps.Where(stamp => PlacementOf(stamp) == null);

    // The last stamp page with a stamp on it, or -1 when none is placed.
    internal int LastSheet => placed.Count == 0 ? -1 : placed.Max(entry => entry.Placement.Sheet);

    // Raised after a stamp is collected or placed.
    internal event Action? Changed;

    internal StampInstance? For(string discoveryId) => stamps.FirstOrDefault(stamp => string.Equals(stamp.DiscoveryId, discoveryId, StringComparison.Ordinal));

    internal StampPlacement? PlacementOf(StampInstance stamp) => places.TryGetValue(stamp.Id, out var place) ? place : null;

    internal IEnumerable<PlacedStamp> OnSheet(int sheet) => placed.Where(entry => entry.Placement.Sheet == sheet);

    internal CollectResult Collect(string discoveryId, StampDesign? design, StampSource? source = null)
    {
        if (string.IsNullOrWhiteSpace(discoveryId) || design == null || string.IsNullOrWhiteSpace(design.Id) || design.Revision < 1)
            return new CollectResult(CollectOutcome.Unavailable, null);
        if (For(discoveryId) is { } held)
            return new CollectResult(CollectOutcome.AlreadyCollected, held);
        issued++;
        var stamp = new StampInstance($"{IdPrefix}{issued:D4}", design, discoveryId, clock(), StampProvenance.LocalDemo, source);
        stamps.Add(stamp);
        Changed?.Invoke();
        return new CollectResult(CollectOutcome.Collected, stamp);
    }

    // Presses a held stamp onto a page, or moves it there; it lands on top of the others. False when the
    // stamp is not in this collection or the place is not a valid one.
    internal bool Place(StampInstance stamp, StampPlacement placement)
    {
        placement = placement with { Rotation = StampPlacement.Normalize(placement.Rotation) };
        if (!stamps.Contains(stamp) || !placement.IsValid) return false;
        if (places.ContainsKey(stamp.Id)) placed.RemoveAll(entry => entry.Stamp.Id == stamp.Id);
        placed.Add(new PlacedStamp(stamp, placement));
        places[stamp.Id] = placement;
        Changed?.Invoke();
        return true;
    }
}
