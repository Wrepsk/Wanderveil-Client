namespace Wanderveil.Features.Stamps;

// Where a stamp design or an issued stamp came from. Only local demo content exists so far; creator,
// curated, and social provenance arrive with accounts. Nothing here is a game achievement.
internal enum StampProvenance { LocalDemo }

// A design is what a stamp looks like; issuing one makes a separate StampInstance. Editing a design means
// a new revision, so a stamp already issued keeps the revision it was issued with and never changes.
// Artwork indexes the bundled impression atlas; without that art the procedural motif stands in.
// Creator is who designed it, as shown on the stamp's info page; null when not recorded.
internal sealed record StampDesign(string Id, int Revision, string Name, int Artwork, StampProvenance Provenance, string? Creator = null);
