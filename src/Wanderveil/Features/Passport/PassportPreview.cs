namespace Wanderveil.Features.Passport;

// Synthetic presentation data, deliberately separate from any future character identity or stored passport.
internal sealed record PassportPreview(string Name, string HomeWorld, string FavoriteJobs, string Bio, string Motto)
{
    internal static readonly PassportPreview Sample = new(
        "Ari Vale",
        "Demo world",
        "Bard / Botanist",
        "A keeper of small stories, quiet paths, and views worth taking the long way around.",
        "Take the scenic route. There is always another story waiting.");
}
