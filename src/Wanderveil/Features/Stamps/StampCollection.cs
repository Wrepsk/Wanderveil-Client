using System;
using System.Collections.Generic;
using System.Linq;

namespace Wanderveil.Features.Stamps;

// One issued stamp: its own identifier, the design revision it was issued with, the discovery it came
// from, and when. Instance identifiers are never design identifiers.
internal sealed record StampInstance(string Id, StampDesign Design, string DiscoveryId, DateTimeOffset CollectedAt, StampProvenance Provenance);

internal enum CollectOutcome { Collected, AlreadyCollected, Unavailable }

internal readonly record struct CollectResult(CollectOutcome Outcome, StampInstance? Stamp);

// The stamps one passport holds, in the order they were collected. Held in memory only: nothing is saved,
// so a new session starts empty (versioned local persistence is the next slice).
// Duplicate policy: a passport holds at most one stamp per discovery. Collecting again changes nothing and
// returns the stamp already held, even if that discovery's design has a newer revision since.
internal sealed class StampCollection(Func<DateTimeOffset>? clock = null)
{
    private readonly List<StampInstance> stamps = [];
    private readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.Now);
    private int issued;

    internal IReadOnlyList<StampInstance> Stamps => stamps;

    internal StampInstance? For(string discoveryId) => stamps.FirstOrDefault(stamp => string.Equals(stamp.DiscoveryId, discoveryId, StringComparison.Ordinal));

    internal CollectResult Collect(string discoveryId, StampDesign? design)
    {
        if (string.IsNullOrWhiteSpace(discoveryId) || design == null || string.IsNullOrWhiteSpace(design.Id) || design.Revision < 1)
            return new CollectResult(CollectOutcome.Unavailable, null);
        if (For(discoveryId) is { } held)
            return new CollectResult(CollectOutcome.AlreadyCollected, held);
        issued++;
        var stamp = new StampInstance($"local-demo-{issued:D4}", design, discoveryId, clock(), StampProvenance.LocalDemo);
        stamps.Add(stamp);
        return new CollectResult(CollectOutcome.Collected, stamp);
    }
}
