using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Wanderveil.Features.Stamps;

// One issued stamp: its own identifier, the design revision it was issued with, the discovery it came
// from, and when. Instance identifiers are never design identifiers.
internal sealed record StampInstance(string Id, StampDesign Design, string DiscoveryId, DateTimeOffset CollectedAt, StampProvenance Provenance);

internal enum CollectOutcome { Collected, AlreadyCollected, Unavailable }

internal readonly record struct CollectResult(CollectOutcome Outcome, StampInstance? Stamp);

// The stamps one passport holds, in the order they were collected, starting from any restored from disk.
// Duplicate policy: a passport holds at most one stamp per discovery. Collecting again changes nothing and
// returns the stamp already held, even if that discovery's design has a newer revision since.
// Used on the UI thread only; storage saves a snapshot when Changed is raised.
internal sealed class StampCollection
{
    internal const string IdPrefix = "local-demo-";
    private readonly List<StampInstance> stamps;
    private readonly Func<DateTimeOffset> clock;
    private int issued;

    internal StampCollection(Func<DateTimeOffset>? clock = null, IEnumerable<StampInstance>? restored = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.Now);
        stamps = restored?.ToList() ?? [];
        if (stamps.Any(stamp => stamp == null) || stamps.Select(stamp => stamp.Id).Distinct(StringComparer.Ordinal).Count() != stamps.Count
            || stamps.Select(stamp => stamp.DiscoveryId).Distinct(StringComparer.Ordinal).Count() != stamps.Count)
            throw new ArgumentException("Restored stamps need unique identifiers and at most one stamp per discovery.", nameof(restored));
        // New identifiers continue after the highest one already issued.
        issued = stamps.Select(stamp => stamp.Id.StartsWith(IdPrefix, StringComparison.Ordinal)
            && int.TryParse(stamp.Id.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(0).Max();
    }

    internal IReadOnlyList<StampInstance> Stamps => stamps;

    // Raised after a stamp is added.
    internal event Action? Changed;

    internal StampInstance? For(string discoveryId) => stamps.FirstOrDefault(stamp => string.Equals(stamp.DiscoveryId, discoveryId, StringComparison.Ordinal));

    internal CollectResult Collect(string discoveryId, StampDesign? design)
    {
        if (string.IsNullOrWhiteSpace(discoveryId) || design == null || string.IsNullOrWhiteSpace(design.Id) || design.Revision < 1)
            return new CollectResult(CollectOutcome.Unavailable, null);
        if (For(discoveryId) is { } held)
            return new CollectResult(CollectOutcome.AlreadyCollected, held);
        issued++;
        var stamp = new StampInstance($"{IdPrefix}{issued:D4}", design, discoveryId, clock(), StampProvenance.LocalDemo);
        stamps.Add(stamp);
        Changed?.Invoke();
        return new CollectResult(CollectOutcome.Collected, stamp);
    }
}
