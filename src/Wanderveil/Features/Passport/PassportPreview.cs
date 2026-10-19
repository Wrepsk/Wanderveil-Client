namespace Wanderveil.Features.Passport;

// Synthetic presentation data, deliberately separate from any future character identity or stored passport.
// Number is the passport number, printed on the identity page and engraved on every stamp page.
internal sealed record PassportPreview(string Name, string HomeWorld, string FavoriteJobs, string Bio, string Motto, string Number = "EZ 0042 1187")
{
    internal static readonly PassportPreview Sample = new(
        "Ari Vale",
        "Demo world",
        "Bard / Botanist",
        "A keeper of small stories, quiet paths, and views worth taking the long way around.",
        "Take the scenic route. There is always another story waiting.");
}
