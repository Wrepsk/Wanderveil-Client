using Wanderveil.Features.Stamps;

namespace Wanderveil.Features.Discover;

// A discovery and the stamp design it awards, if any. The samples are synthetic local demo content:
// not location coordinates, backend contracts, or game achievements.
// CollectOnReading: its stamp is collected by choosing to after reading it here. A stamp spot instead asks
// the reader to be at the place, which needs location awareness Wanderveil does not have yet.
internal sealed record DiscoveryPreview(
    string Id, string Kind, string Title, string Creator, string Location,
    string Teaser, string Caption, StampDesign? Stamp, bool HasIllustration, bool CollectOnReading)
{
    internal static readonly DiscoveryPreview Postcard = new(
        "demo-scenic-route", "POSTCARD", "The scenic route", "Ari Vale / Demo creator",
        "An imagined coastal overlook",
        "A quiet path, a salt-touched breeze, and a horizon worth the detour.",
        "I took the long way home and found a place to pause. Below the path, the sea held the last light of the day. " +
        "This little postcard is a reminder to leave room for the unexpected view.",
        new StampDesign("demo-scenic-route-stamp", 1, "Scenic route", 0, StampProvenance.LocalDemo), true, true);

    internal static readonly DiscoveryPreview StampSpot = new(
        "demo-lantern-path", "STAMP SPOT", "Where the lanterns wait", "Wanderveil / Demo curator",
        "An imagined woodland path",
        "A small light at the bend in the road. Some stories begin by standing still.",
        "Imagine a lantern-lined path beneath the trees, with one empty bench facing the evening sky. " +
        "No photograph accompanies this sample; the journal keeps the story readable without it.",
        new StampDesign("demo-lantern-path-stamp", 1, "Lantern path", 1, StampProvenance.LocalDemo), false, false);

    internal static readonly DiscoveryPreview[] Samples = [Postcard, StampSpot];
}
