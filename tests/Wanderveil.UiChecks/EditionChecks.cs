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
        Footer(check);
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
        check(PassportStore.Serialize(old).Contains("\"version\": 3", StringComparison.Ordinal), "A migrated passport is not saved as the current version.");
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
            check(!book.Shell.IsBookOpen && book.Shell.Phase == BookPhase.Closed && profile.Identity.PreviousAppearance == null, "Choosing the first edition does not bring the new passport, closed, or keeps a previous one.");
            book.Frames(3);
            book.Save(artifacts, name + "-chosen.bmp");
        }
        Console.WriteLine("PASS edition-issue");
    }

    // Picking an edition in Settings asks first; confirming exchanges the book (the old one is put away, the new
    // one summoned closed, showing its cover); change back exchanges it again. With motion and without.
    private static void Settings(Action<bool, string> check, string? artifacts)
    {
        foreach (var motion in new[] { false, true })
        {
            var profile = new PassportProfile(PassportIdentity.Sample with { CoverLine = "Ari Vale" });
            using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile, motion: motion);
            void ToSettings()
            {
                book.Open();
                book.Shell.ShowSettings();
                for (var frame = 0; frame < 120 && (book.Shell.IsRiffling || book.Shell.TabSwap < 1f); frame++) book.Frame();
                // The editions are under Customization, the second category tab.
                book.Click(book.Tab(1));
                for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
                book.Frames(3);
            }
            ToSettings();
            if (!motion) book.Save(artifacts, "edition-settings.bmp");
            check(book.Shell.Section == BookSection.Settings && book.Shell.PageOverflow == 0, $"The Settings pages overflow with the editions ({book.Shell.PageOverflow}).");
            // The editions are on Customization's left page: sweeping rows from the right, the first control is the last cover,
            // Garlemald. Below the covers, the first control going down is the confirmation's Change link.
            var page = book.Page(false);
            var below = 0f;
            // Below the Back link, which heads the page.
            for (var y = page.Size.Y * 0.5f; book.Shell.PendingEdition == null && y < page.Size.Y * 0.8f; y += 6)
                for (var x = page.Size.X - 10; book.Shell.PendingEdition == null && x > 10; x -= 8)
                    if (book.ClickControl(new Vector2(page.Position.X + x, page.Position.Y + y))) below = y;
            // Down past the leftmost cover, so the next control met is beneath the shelf.
            for (var hovering = true; hovering && below < page.Size.Y; below += 4) { book.Move(new Vector2(page.Position.X + 30, page.Position.Y + below)); hovering = ImGui.IsAnyItemHovered(); }
            check(book.Shell.PendingEdition == "garlemald" && profile.Identity.Appearance == PassportAppearance.Gridania, $"Picking an edition in Settings changed it at once or picked {book.Shell.PendingEdition}.");
            check(book.Shell.PageOverflow == 0, "The edition confirmation overflows the Settings page.");
            if (!motion) book.Save(artifacts, "edition-settings-confirm.bmp");
            // The confirmation's first link, Change to Garlemald, starts the line beneath its question.
            for (var y = below; !book.Shell.Exchanging && profile.Identity.Appearance == PassportAppearance.Gridania && y < page.Size.Y - 20; y += 4)
                book.ClickControl(new Vector2(page.Position.X + 30, page.Position.Y + y));
            if (motion)
            {
                // The old passport leaves first, unchanged; the new one arrives once it is gone.
                check(book.Shell.Exchanging && profile.Identity.Appearance == PassportAppearance.Gridania && book.Shell.Phase is BookPhase.Closing or BookPhase.Exiting, $"Confirming does not put the old passport away first ({book.Shell.Phase}).");
                var sawHidden = false;
                var entered = false;
                var saved = false;
                for (var frame = 0; frame < 600 && book.Shell.Phase != BookPhase.Closed; frame++)
                {
                    book.Frame();
                    if (book.Shell.Phase == BookPhase.Exiting && book.Shell.Lift < 0.05f) sawHidden = true;
                    if (book.Shell.Phase == BookPhase.Entering) { entered = true; check(profile.Identity.Appearance?.Edition == "garlemald", "The passport arriving is not the new edition."); }
                    if (entered && !saved && artifacts != null && book.Shell.Lift > 0.5f) { saved = true; book.Save(artifacts, "edition-exchange-arriving.bmp", keepMouse: true); }
                }
                check(sawHidden && entered && !book.Shell.Exchanging, "The exchange does not slide the old passport away and bring the new one up.");
            }
            check(profile.Identity.Appearance?.Edition == "garlemald" && profile.Identity.PreviousAppearance == PassportAppearance.Gridania && profile.Identity.CoverLine == "Ari Vale", "Confirming did not change the edition, keep the previous one, or keep the cover line.");
            check(book.Shell.Phase == BookPhase.Closed && !book.Shell.IsBookOpen, "The new passport does not arrive closed, showing its cover.");
            if (!motion) book.Save(artifacts, "edition-settings-changed-cover.bmp");
            // Opened again, the book is still at Settings, where Change back exchanges it once more.
            ToSettings();
            check(book.Shell.Section == BookSection.Settings, "The new passport does not open where the change was made.");
            if (!motion) book.Save(artifacts, "edition-settings-changed.bmp");
            for (var y = below; !book.Shell.Exchanging && profile.Identity.Appearance?.Edition == "garlemald" && y < page.Size.Y - 20; y += 4) book.ClickControl(new Vector2(page.Position.X + 30, page.Position.Y + y));
            for (var frame = 0; frame < 600 && book.Shell.Exchanging; frame++) book.Frame();
            check(profile.Identity.Appearance == PassportAppearance.Gridania, "Change back did not restore the previous edition.");
        }
        Console.WriteLine("PASS edition-settings");
    }

    // With no stamp waiting, the nation pages' footer offers only Decorate (no Arrange); the identity pages offer Edit.
    private static void Footer(Action<bool, string> check)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        check(book.ClickControl(book.StampControl()) && book.Shell.Symbols.Decorating && !book.Shell.Desk.Arrange, "The nation pages' footer offers Arrange, or not Decorate.");
        book.Key(ImGuiKey.Escape);
        book.Key(ImGuiKey.Escape);
        book.Key(ImGuiKey.PageDown);
        book.Click(book.StampControl());
        check(book.Shell.EditingIdentity, "The identity pages' footer does not offer Edit.");
        Console.WriteLine("PASS edition-footer");
    }

    // Every edition's closed cover and opening spread, for review.
    private static void Gallery(Action<bool, string> check, string? artifacts)
    {
        foreach (var edition in PassportEdition.All)
        {
            var profile = new PassportProfile(PassportIdentity.Sample with { Appearance = new PassportAppearance(edition.Id) });
            using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
            book.Save(artifacts, $"edition-{edition.Id}-cover.bmp");
            // The open hint: a soft shadow down the cover's whole right edge.
            book.HoverCover();
            book.Save(artifacts, $"edition-{edition.Id}-cover-hint.bmp", keepMouse: true);
            ImGui.GetIO().AddMousePosEvent(-100, -100);
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
