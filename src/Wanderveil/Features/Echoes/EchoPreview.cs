using System;
using System.Collections.Generic;
using System.Linq;

namespace Wanderveil.Features.Echoes;

// A synthetic dossier showing how an adventure might read. It is not playable content, progress, or a
// backend contract: nothing here is tracked, unlocked, or rewarded.
internal sealed record EchoObjective(string Text, string? Hint = null);

// A sealed chapter shows only its title and summary, so the preview never reveals a later story.
internal sealed record EchoChapter(string Numeral, string Title, string Summary, bool Sealed, IReadOnlyList<EchoObjective> Objectives);

internal sealed record EchoPreview(string Title, string Byline, string Premise, IReadOnlyList<EchoChapter> Chapters)
{
    internal static readonly EchoPreview Sample = new(
        "The Lantern Keeper's Letter",
        "A sample mystery in three chapters / Wanderveil demo",
        "A letter left at a woodland inn asks whoever finds it to light the lanterns one last time.",
        [
            new("I", "The letter", "Someone left a note for the next traveller.", false,
            [
                new("Read the letter left at the inn."),
                new("Find the bench that faces the evening sky.", "Look where the lanterns end."),
                new("Answer the riddle carved into the rail."),
            ]),
            new("II", "The keeper's path", "Opens once the first chapter is complete.", true, []),
            new("III", "One last light", "Opens once the second chapter is complete.", true, []),
        ]);

    // A dossier opens on its first chapter; sealed chapters carry no objectives.
    internal static EchoPreview Validate(EchoPreview preview)
    {
        if (string.IsNullOrWhiteSpace(preview.Title) || preview.Chapters.Count == 0)
            throw new ArgumentException("A dossier needs a title and chapters.", nameof(preview));
        if (preview.Chapters[0].Sealed || preview.Chapters.Any(chapter => chapter.Sealed && chapter.Objectives.Count > 0))
            throw new ArgumentException("A dossier opens on its first chapter, and sealed chapters reveal no objectives.", nameof(preview));
        return preview;
    }
}
