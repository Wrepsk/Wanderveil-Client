using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// The passport's structure: the pages' background, as part of the appearance, saved, and chosen in Settings' Layout
// category; its pattern drawn on every page and its rings inked out of the plate.
internal static class LayoutChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Model(check);
        Storage(check);
        Drawn(check, artifacts);
        Console.WriteLine("PASS layout");
    }

    private static void Model(Action<bool, string> check)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        check(profile.Identity.Appearance?.Background == PageBackground.Guilloche, "A passport does not start on its rings.");
        check(profile.SetBackground(PageBackground.Waves) && !profile.SetBackground(PageBackground.Waves) && !profile.SetBackground((PageBackground)99), "Setting a background is not one change, or an unknown one is accepted.");
        profile.SetColour(ColourPart.Cloth, Rgb.Hex(0x5A1424));
        profile.ResetColours();
        check(profile.Identity.Appearance?.Background == PageBackground.Waves, "Resetting colours resets the background.");
        profile.ChangeEdition("limsa");
        check(profile.Identity.Appearance?.Background == PageBackground.Guilloche, "A change of edition keeps the background.");
        profile.RestorePrevious();
        check(profile.Identity.Appearance?.Background == PageBackground.Waves, "Change back does not restore the background.");
    }

    private static void Storage(Action<bool, string> check)
    {
        var identity = PassportIdentity.Sample with { Appearance = PassportAppearance.Gridania with { Background = PageBackground.Edition } };
        var text = PassportStore.Serialize(identity);
        check(text.Contains("\"background\": \"edition\"", StringComparison.Ordinal) && PassportStore.Parse(text) == identity.Normalize(), "The background is not saved.");
        check(!PassportStore.Serialize(PassportIdentity.Sample).Contains("background", StringComparison.Ordinal), "The rings are written as a background.");
        static string File(string background) => "{\"format\":\"wanderveil.passport\",\"version\":3,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"background\":" + background + "}}";
        check(PassportStore.Parse(File("\"tartan\"")).Appearance?.Background == PageBackground.Guilloche && PassportStore.Parse(File("\"3\"")).Appearance?.Background == PageBackground.Guilloche,
            "An unknown background is not read as the rings.");
        try { PassportStore.Parse(File("7")); check(false, "A background that is not a name is accepted."); }
        catch (FormatException) { check(true, ""); }
    }

    private static void Drawn(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        using (var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile))
        {
            book.Open();
            book.Shell.ShowSettings();
            book.Frames(3);
            book.Click(book.Tab(2));
            book.Frames(3);
            check(book.Shell.Category == SettingsCategory.Layout && book.Shell.BackgroundTile(PageBackground.Waves) != null, "The Layout category does not show the backgrounds.");
            book.Click(book.Shell.BackgroundTile(PageBackground.Waves)!.Value);
            book.Frames(3);
            check(profile.Identity.Appearance?.Background == PageBackground.Waves && book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "Clicking a background does not choose it, or Layout overflows.");
            // The rings are inked out to the paper; the pattern is drawn in their colour instead.
            check(book.Shell.PlateTint is { } tint && tint.Wanted.Lines == tint.Wanted.Paper && UiTheme.Lines == profile.Identity.Look.Lines, "The plate keeps its rings under another background.");
            book.Save(artifacts, "layout-settings.bmp");
            book.Shell.GoBack();
            for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
            book.Key(ImGuiKey.PageDown);
            book.Save(artifacts, "layout-waves.bmp");
            foreach (var background in new[] { PageBackground.Grid, PageBackground.Edition, PageBackground.Plain })
            {
                profile.SetBackground(background);
                book.Frames(2);
                book.Save(artifacts, $"layout-{background.ToString().ToLowerInvariant()}.bmp");
            }
            check(book.Shell.PlateTint is { } plain && plain.Wanted.Lines == plain.Wanted.Paper, "Plain paper keeps its rings.");
            profile.SetBackground(PageBackground.Guilloche);
            book.Frames(2);
            check(book.Shell.PlateTint is { } rings && rings.Wanted.Lines != rings.Wanted.Paper, "The rings do not come back.");
        }

        // A turning leaf carries the pattern.
        var zigzag = new PassportProfile(PassportIdentity.Sample with { Appearance = PassportAppearance.Gridania with { Background = PageBackground.Zigzag } });
        using var turning = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: zigzag, motion: true);
        turning.Open();
        turning.Key(ImGuiKey.PageDown);
        for (var frame = 0; frame < 120 && turning.Shell.IsTurning; frame++)
        {
            if (frame == 6) turning.Save(artifacts, "layout-turn.bmp", keepMouse: true);
            turning.Frame();
        }
        check(!turning.Shell.IsTurning, "A page with a background does not finish turning.");
    }
}
