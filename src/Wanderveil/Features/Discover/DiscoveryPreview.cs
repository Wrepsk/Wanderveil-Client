namespace Wanderveil.Features.Discover;

// Synthetic presentation fixtures. These are not location coordinates, backend contracts, or collectibles.
internal sealed record DiscoveryPreview(
    string Id, string Kind, string Title, string Creator, string Location,
    string Teaser, string Caption, string StampName, bool HasIllustration)
{
    internal static readonly DiscoveryPreview Postcard = new(
        "demo-scenic-route", "POSTCARD", "The scenic route", "Ari Vale / Demo creator",
        "An imagined coastal overlook",
        "A quiet path, a salt-touched breeze, and a horizon worth the detour.",
        "I took the long way home and found a place to pause. Below the path, the sea held the last light of the day. " +
        "This little postcard is a reminder to leave room for the unexpected view.",
        "Scenic route / Demo stamp", true);

    internal static readonly DiscoveryPreview StampSpot = new(
        "demo-lantern-path", "STAMP SPOT", "Where the lanterns wait", "Wanderveil / Demo curator",
        "An imagined woodland path",
        "A small light at the bend in the road. Some stories begin by standing still.",
        "Imagine a lantern-lined path beneath the trees, with one empty bench facing the evening sky. " +
        "No photograph accompanies this sample; the journal keeps the story readable without it.",
        "Lantern path / Demo stamp", false);

    internal static readonly DiscoveryPreview[] Samples = [Postcard, StampSpot];
}
