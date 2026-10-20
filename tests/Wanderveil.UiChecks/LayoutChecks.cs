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
        Identity(check, artifacts);
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

    // The identity spread's layouts: part of the appearance and saved; chosen from Layout's sketches; each fitting its
    // pages at rest and while being written on, wide and on a single page.
    private static void Identity(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        check(profile.Identity.Appearance?.IdentityLayout == IdentityLayout.Classic && profile.SetIdentityLayout(IdentityLayout.Compact) && !profile.SetIdentityLayout((IdentityLayout)9), "Identity layouts do not start classic, or an unknown one is accepted.");
        profile.ChangeEdition("uldah");
        check(profile.Identity.Appearance?.IdentityLayout == IdentityLayout.Classic, "A change of edition keeps the identity layout.");
        profile.RestorePrevious();
        check(profile.Identity.Appearance?.IdentityLayout == IdentityLayout.Compact, "Change back does not restore the identity layout.");
        var saved = PassportIdentity.Sample with { Appearance = PassportAppearance.Gridania with { IdentityLayout = IdentityLayout.Mirrored } };
        var text = PassportStore.Serialize(saved);
        check(text.Contains("\"identityLayout\": \"mirrored\"", StringComparison.Ordinal) && PassportStore.Parse(text) == saved.Normalize() && !PassportStore.Serialize(PassportIdentity.Sample).Contains("identityLayout", StringComparison.Ordinal), "The identity layout is not saved, or classic is written.");
        static string File(string layout) => "{\"format\":\"wanderveil.passport\",\"version\":3,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"identityLayout\":" + layout + "}}";
        check(PassportStore.Parse(File("\"diagonal\"")).Appearance?.IdentityLayout == IdentityLayout.Classic, "An unknown identity layout is not read as classic.");
        try { PassportStore.Parse(File("2")); check(false, "An identity layout that is not a name is accepted."); }
        catch (FormatException) { check(true, ""); }

        var holder = new PassportProfile(PassportIdentity.Sample);
        using (var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: holder))
        {
            book.Open();
            book.Shell.ShowSettings();
            book.Frames(3);
            book.Click(book.Tab(2));
            book.Frames(3);
            check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "Layout overflows with the identity layouts.");
            book.Click(book.Shell.IdentityLayoutTile(IdentityLayout.Mirrored)!.Value);
            book.Frames(2);
            check(holder.Identity.Appearance?.IdentityLayout == IdentityLayout.Mirrored, "Clicking an identity layout's sketch does not choose it.");
            book.Save(artifacts, "layout-identity-settings.bmp");
            book.Shell.GoBack();
            for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
            book.Key(ImGuiKey.PageDown);
            foreach (var layout in Enum.GetValues<IdentityLayout>())
            {
                holder.SetIdentityLayout(layout);
                book.Frames(8);
                var name = layout.ToString().ToLowerInvariant();
                check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0 && book.Shell.FitOf("portrait") > 0.5f && book.Shell.FitOf("information") > 0.5f,
                    $"The {name} identity spread does not fit its pages (fit {book.Shell.FitOf("portrait"):0.00} / {book.Shell.FitOf("information"):0.00}).");
                book.Save(artifacts, $"layout-identity-{name}.bmp");
                // Written on in place, it still fits.
                book.Click(book.StampControl());
                book.Frames(8);
                check(book.Shell.EditingIdentity && book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, $"Editing the {name} identity spread overflows, or Edit is not offered.");
                if (layout == IdentityLayout.Compact) book.Save(artifacts, "layout-identity-compact-edit.bmp");
                book.Key(ImGuiKey.Escape);
                check(!book.Shell.EditingIdentity, "Escape does not end editing.");
            }
        }

        // On a single page, sideways: the compact card's two columns still wrap within the page.
        var narrow = new PassportProfile(PassportIdentity.Sample with { Appearance = PassportAppearance.Gridania with { IdentityLayout = IdentityLayout.Compact } });
        using var single = new CollectChecks.Book(new Vector2(520, 900), new StampCollection(), identity: narrow);
        single.Open();
        for (var page = 0; page < 3; page++) single.Key(ImGuiKey.PageDown);
        single.Frames(8);
        check(single.Shell.VisiblePage == 3 && single.Shell.PageOverflowX == 0, "The compact identity page overflows sideways on a single page.");
        single.Key(ImGuiKey.PageUp);
        single.Frames(8);
        check(single.Shell.PageOverflowX == 0, "The compact card overflows sideways on a single page.");
        single.Save(artifacts, "layout-identity-compact-narrow.bmp");
    }
}
