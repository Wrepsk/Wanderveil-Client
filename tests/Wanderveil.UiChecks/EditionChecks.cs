using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// Nation editions: their definitions, how the chosen one is stored (and older files migrated), choosing one on
// a new passport's first spread, and changing it in Settings with a confirmation and a way back, while
// everything the holder wrote stays.
internal static class EditionChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Definitions(check);
        Storage(check);
        Change(check);
        Issue(check, artifacts);
        Settings(check, artifacts);
        Gallery(check, artifacts);
    }

    private static void Definitions(Action<bool, string> check)
    {
        check(PassportEdition.All.Count == 6 && PassportEdition.All.Select(edition => edition.Id).Distinct().Count() == 6, "The six editions are not all there, or two share an id.");
        check(PassportEdition.Find(null).Id == "eorzea" && PassportEdition.Find("atlantis").Id == "eorzea" && PassportEdition.Find("uldah").Name == "Ul'dah", "Edition lookup does not fall back to Eorzea.");
        foreach (var edition in PassportEdition.All)
        {
            // Links and toggles print in the edition's ink, so it must read on the paper.
            check(Contrast(edition.Ink, UiTheme.Paper) >= 4.5, $"{edition.Name}'s ink does not read on the paper ({Contrast(edition.Ink, UiTheme.Paper):0.0}).");
            check(Contrast(edition.Foil, edition.Cloth) >= 3, $"{edition.Name}'s foil does not read on its cover ({Contrast(edition.Foil, edition.Cloth):0.0}).");
            check(edition.Code.All(character => character is >= 'A' and <= 'Z'), $"{edition.Name}'s machine-line code is not plain capitals.");
        }
        check((PassportIdentity.Sample with { Appearance = new PassportAppearance("uldah") }).MachineLine().StartsWith("P<WV<ULDAH<ARI<VALE<<", StringComparison.Ordinal), "The machine line does not name the edition.");
        Console.WriteLine("PASS edition-definitions");
    }

    private static void Storage(Action<bool, string> check)
    {
        var changed = PassportIdentity.Sample with { Appearance = new PassportAppearance("ishgard"), PreviousAppearance = PassportAppearance.Gridania };
        check(PassportStore.Parse(PassportStore.Serialize(changed)) == changed.Normalize(), "The passport file does not round-trip an edition and the one before it.");
        var issued = PassportIdentity.Issue(new DateOnly(2026, 10, 20), new Random(1));
        check(PassportStore.Parse(PassportStore.Serialize(issued)).Appearance == null, "A passport whose edition is not chosen comes back chosen.");
        // Version 1 came before editions: every such passport was Gridania, and stays so, chosen.
        var old = PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":1,\"number\":\"EZ 1\",\"name\":\"Ari\"}");
        check(old.Appearance == PassportAppearance.Gridania && old.PreviousAppearance == null && old.Name == "Ari", "A version 1 passport does not migrate to a chosen Gridania edition.");
        check(PassportStore.Serialize(old).Contains("\"version\": 2", StringComparison.Ordinal), "A migrated passport is not saved as version 2.");
        // An edition this version does not know is kept as written, drawing as Eorzea meanwhile.
        var later = PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"sharlayan\"}}");
        check(later.Appearance?.Edition == "sharlayan" && later.Edition.Id == "eorzea" && PassportStore.Serialize(later).Contains("sharlayan", StringComparison.Ordinal), "An unknown edition is lost.");
        foreach (var bad in new[] { "{\"edition\":\"\"}", "\"gridania\"", "{\"edition\":7}" })
        {
            try
            {
                PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":" + bad + "}");
                check(false, $"An appearance of {bad} was accepted.");
            }
            catch (FormatException) { check(true, ""); }
        }
        Console.WriteLine("PASS edition-storage");
    }

    // Changing edition replaces only the appearance and keeps the one replaced; details never change.
    private static void Change(Action<bool, string> check)
    {
        var profile = new PassportProfile(PassportIdentity.Sample with { CoverLine = "Ari Vale", Frame = PictureFrame.Deckled, Portrait = PortraitCrop.Centered });
        var before = profile.Identity;
        check(profile.ChangeEdition("uldah") && profile.Identity.Appearance?.Edition == "uldah" && profile.Identity.PreviousAppearance == PassportAppearance.Gridania, "Changing edition does not keep the previous one.");
        check(profile.Identity with { Appearance = before.Appearance, PreviousAppearance = before.PreviousAppearance } == before, "Changing edition changed something besides the appearance.");
        check(!profile.ChangeEdition("uldah"), "Choosing the current edition counts as a change.");
        check(profile.RestorePrevious() && profile.Identity.Appearance == PassportAppearance.Gridania && profile.Identity.PreviousAppearance?.Edition == "uldah", "Changing back does not swap the editions.");
        var fresh = new PassportProfile(PassportIdentity.Issue(new DateOnly(2026, 10, 20), new Random(2)));
        check(fresh.Identity.Edition.Id == "eorzea" && fresh.ChangeEdition("limsa") && fresh.Identity.PreviousAppearance == null && !fresh.RestorePrevious(), "A first choice keeps something to change back to.");
        Console.WriteLine("PASS edition-change");
    }

    // A new passport opens on the six editions; choosing one issues it and closes the book on its new cover.
    private static void Issue(Action<bool, string> check, string? artifacts)
    {
        foreach (var single in new[] { false, true })
        {
            var profile = new PassportProfile(PassportIdentity.Issue(new DateOnly(2026, 10, 20), new Random(4)));
            using var book = new CollectChecks.Book(single ? new Vector2(520, 900) : new Vector2(1200, 820), new StampCollection(), identity: profile);
            var name = single ? "edition-issue-single" : "edition-issue";
            book.Save(artifacts, name + "-cover.bmp");
            book.Open();
            check(book.Shell.IsBookOpen && book.Shell.PageOverflowX == 0, "A new passport does not open.");
            book.Save(artifacts, name + ".bmp");
            // Up the left column from the foot of the page, the first control is the lower row's first cover.
            var page = book.Page(!single);
            for (var y = page.Size.Y - 20; profile.Identity.Appearance == null && y > 20; y -= 6)
                if (book.ClickControl(page.Position + new Vector2(page.Size.X * 0.15f, y))) break;
            check(profile.Identity.Edition.Id == "limsa", $"Clicking the lower row's first cover chose {profile.Identity.Edition.Name}.");
            check(profile.Identity.Appearance != null, "Clicking the editions did not choose one.");
            check(!book.Shell.IsBookOpen && profile.Identity.PreviousAppearance == null, "Choosing the first edition does not close the book on its cover, or keeps a previous one.");
            book.Frames(3);
            book.Save(artifacts, name + "-chosen.bmp");
        }
        Console.WriteLine("PASS edition-issue");
    }

    private static void Settings(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample with { CoverLine = "Ari Vale" });
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        book.Shell.ShowSettings();
        book.Frames(3);
        check(book.Shell.Section == BookSection.Settings && book.Shell.PageOverflow == 0, "The Settings pages overflow with the editions.");
        // Sweeping rows from the right below the Back link, the first control is the last cover, Garlemald;
        // picking it asks first.
        book.Save(artifacts, "edition-settings.bmp");
        var page = book.Page(false);
        for (var y = page.Size.Y * 0.42f; book.Shell.PendingEdition == null && y < page.Size.Y * 0.8f; y += 8)
            for (var x = page.Size.X - 10; book.Shell.PendingEdition == null && x > 10; x -= 8)
                book.ClickControl(new Vector2(page.Position.X + x, page.Position.Y + y));
        check(book.Shell.PendingEdition == "garlemald" && profile.Identity.Appearance == PassportAppearance.Gridania, $"Picking an edition in Settings changed it at once or picked {book.Shell.PendingEdition}.");
        book.Save(artifacts, "edition-settings-confirm.bmp");
        check(book.Shell.PageOverflow == 0, "The edition confirmation overflows the Settings page.");
        // The confirmation's first link, Change to Garlemald, starts the line beneath its question.
        for (var y = page.Size.Y - 20; profile.Identity.Appearance == PassportAppearance.Gridania && book.Shell.PendingEdition != null && y > 40; y -= 4)
            book.ClickControl(new Vector2(page.Position.X + 30, page.Position.Y + y));
        check(profile.Identity.Appearance?.Edition == "garlemald" && profile.Identity.PreviousAppearance == PassportAppearance.Gridania && profile.Identity.CoverLine == "Ari Vale", "Confirming did not change the edition, keep the previous one, or keep the cover line.");
        book.Frames(2);
        book.Save(artifacts, "edition-settings-changed.bmp");
        for (var y = page.Size.Y - 20; profile.Identity.Appearance?.Edition == "garlemald" && y > 40; y -= 4) book.Click(new Vector2(page.Position.X + 30, page.Position.Y + y));
        check(profile.Identity.Appearance == PassportAppearance.Gridania, "Change back did not restore the previous edition.");
        Console.WriteLine("PASS edition-settings");
    }

    // Every edition's closed cover and opening spread, for review.
    private static void Gallery(Action<bool, string> check, string? artifacts)
    {
        foreach (var edition in PassportEdition.All)
        {
            var profile = new PassportProfile(PassportIdentity.Sample with { Appearance = new PassportAppearance(edition.Id) });
            using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
            book.Save(artifacts, $"edition-{edition.Id}-cover.bmp");
            book.Open();
            check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, $"{edition.Name}'s opening spread overflows.");
            book.Save(artifacts, $"edition-{edition.Id}-open.bmp");
            book.Key(ImGuiKey.PageDown);
            book.Save(artifacts, $"edition-{edition.Id}-identity.bmp");
        }
        Console.WriteLine("PASS edition-gallery");
    }

    private static double Contrast(Vector4 first, Vector4 second)
    {
        static double Channel(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        static double Luminance(Vector4 value) => 0.2126 * Channel(value.X) + 0.7152 * Channel(value.Y) + 0.0722 * Channel(value.Z);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
