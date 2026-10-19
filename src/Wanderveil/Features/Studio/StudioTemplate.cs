using System.Collections.Generic;

namespace Wanderveil.Features.Studio;

internal enum StudioTemplateKind { Postcard, StampSpot, Riddle, Challenge, Trail }

// One line of a draft. Lengths count user-perceived characters, not bytes.
internal sealed record StudioField(string Id, string Label, string Hint, int MaxLength, bool Required);

// What a creator starts from. Unavailable templates are listed so the path ahead is visible, but cannot be chosen.
internal sealed record StudioTemplate(StudioTemplateKind Kind, string Name, string Summary, string Start, bool Available, IReadOnlyList<StudioField> Fields)
{
    internal const string Title = "title";
    internal const string Caption = "caption";
    internal const string Place = "place";
    internal const string Stamp = "stamp";

    internal static readonly StudioTemplate Postcard = new(StudioTemplateKind.Postcard, "Postcard",
        "A picture and a few lines about a place worth the detour.", "Start a postcard",
        true,
        [
            new(Title, "Title", "The scenic route", 48, true),
            new(Caption, "Caption", "What made you stop here?", 220, true),
            new(Place, "Place note", "An overlook above the sea", 60, false),
            new(Stamp, "Stamp name", "Scenic route", 40, false),
        ]);

    internal static readonly StudioTemplate StampSpot = new(StudioTemplateKind.StampSpot, "Stamp Spot",
        "A place where finders earn a stamp for their passport.", "Start a stamp spot",
        true,
        [
            new(Title, "Spot name", "Where the lanterns wait", 48, true),
            new(Place, "Place note", "A bend in the woodland path", 60, true),
            new(Stamp, "Stamp name", "Lantern path", 40, true),
            new(Caption, "Note for finders", "Stand still a moment and look up.", 160, false),
        ]);

    internal static readonly IReadOnlyList<StudioTemplate> Catalog =
    [
        Postcard,
        StampSpot,
        new(StudioTemplateKind.Riddle, "Riddle", "A clue that leads to a place.", "", false, []),
        new(StudioTemplateKind.Challenge, "Challenge", "Something to do once you arrive.", "", false, []),
        new(StudioTemplateKind.Trail, "Trail", "Several places, found in order.", "", false, []),
    ];
}
