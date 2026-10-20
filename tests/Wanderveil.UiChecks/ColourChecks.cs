using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// The holder's colours: their model and legibility guard, how they are stored and reset with the edition, the
// plate recolouring maths and PNG reader, and the passport drawn recoloured.
internal static class ColourChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Model(check);
        Storage(check);
        Recolouring(check);
        Png(check);
        Drawn(check, artifacts);
        Settings(check, artifacts);
        Accent(check, artifacts);
    }

    // The edition's accent: its own until picked, never guarded, saved, recolouring the plates that print it (and
    // only where they do), and picked from its row in Settings.
    private static void Accent(Action<bool, string> check, string? artifacts)
    {
        var gridania = PassportEdition.Gridania;
        var look = PassportLook.Of(gridania);
        check(look.Accent == gridania.Accent && !look.Recoloured(gridania), "The edition's own accent counts as recoloured.");
        var teal = PassportLook.Of(gridania, new PassportColours(Accent: Rgb.Hex(0x2B7A78)));
        check(Rgb.Of(teal.Accent) == Rgb.Hex(0x2B7A78) && teal.Recoloured(gridania) && teal.Adjusted.Length == 0 && teal.Cloth == look.Cloth, "A picked accent is not drawn as picked, or moves other colours.");
        var identity = PassportIdentity.Sample with { Appearance = PassportAppearance.Gridania with { Colours = new PassportColours(Accent: Rgb.Hex(0x2B7A78)) } };
        var text = PassportStore.Serialize(identity);
        check(text.Contains("\"accent\": \"#2B7A78\"", StringComparison.Ordinal) && PassportStore.Parse(text) == identity.Normalize(), "The accent is not saved.");

        // Gridania's cover prints its leaves in the accent: a teal accent changes some of the cover, not all of it.
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", "gridania");
        Stream? Open(string name) => File.Exists(Path.Combine(folder, name)) ? File.OpenRead(Path.Combine(folder, name)) : null;
        var printed = new PlateColours(gridania.Cloth.AsVector3(), gridania.Foil.AsVector3(), PassportLook.EditionPaper.AsVector3(), Vector3.Zero, gridania.Accent.AsVector3());
        var before = EditionRecolour.Levels(Open, "cover", new PlateTint(printed, printed))[0];
        var after = EditionRecolour.Levels(Open, "cover", new PlateTint(printed, printed with { Accent = Rgb.Hex(0x2B7A78).Vector.AsVector3() }))[0];
        var changed = 0;
        for (var index = 0; index < before.Rgba.Length; index += 4) if (Math.Abs(before.Rgba[index] - after.Rgba[index]) + Math.Abs(before.Rgba[index + 2] - after.Rgba[index + 2]) > 6) changed++;
        check(changed > 2000 && changed < before.Width * before.Height / 10, $"A new accent recolours {changed} cover pixels.");
        // Every crest follows its colours across the whole plate: Ul'dah's sun takes a new foil, Gridania's leaves a new
        // accent. (Crest weight maps were once rendered at the crest's 128-unit size and barely recoloured anything.)
        foreach (var (edition, part) in new[] { ("uldah", "foil"), ("gridania", "accent") })
        {
            var crestFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", edition);
            Stream? OpenCrest(string name) => File.Exists(Path.Combine(crestFolder, name)) ? File.OpenRead(Path.Combine(crestFolder, name)) : null;
            var shown = PassportEdition.Find(edition);
            var crestPrinted = new PlateColours(shown.Cloth.AsVector3(), shown.Foil.AsVector3(), PassportLook.EditionPaper.AsVector3(), Vector3.Zero, shown.Accent.AsVector3());
            var crestWanted = part == "foil" ? crestPrinted with { Foil = new Vector3(0.86f, 0.9f, 0.91f) } : crestPrinted with { Accent = Rgb.Hex(0x2B7A78).Vector.AsVector3() };
            var plain = EditionRecolour.Levels(OpenCrest, "crest", new PlateTint(crestPrinted, crestPrinted))[0];
            var recoloured = EditionRecolour.Levels(OpenCrest, "crest", new PlateTint(crestPrinted, crestWanted))[0];
            var moved = 0;
            for (var index = 0; index < plain.Rgba.Length; index += 4) if (Math.Abs(plain.Rgba[index] - recoloured.Rgba[index]) + Math.Abs(plain.Rgba[index + 2] - recoloured.Rgba[index + 2]) > 6) moved++;
            check(moved > 50000, $"A new {part} recolours only {moved} pixels of {edition}'s crest.");
        }

        // The Accent row in Settings: its curated swatch 6 is Teal; Customization still fits.
        var profile = new PassportProfile(PassportIdentity.Sample);
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        book.Shell.ShowSettings();
        book.Frames(3);
        book.Click(book.Tab(1));
        book.Frames(3);
        book.Click(book.Shell.ColourSwatch(ColourPart.Accent, 6)!.Value);
        book.Frames(3);
        check(profile.Identity.Appearance?.Colours?.Accent == Rgb.Hex(0x2B7A78) && book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, $"The Accent row does not pick teal, or Customization overflows ({profile.Identity.Appearance?.Colours?.Accent}).");
        book.Save(artifacts, "colour-settings-accent.bmp");
        book.Shell.GoBack();
        for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
        book.Save(artifacts, "colour-accent-pages.bmp");
        Console.WriteLine("PASS colour-accent");
    }

    // The colour rows in Settings: they fit beside the editions, and a swatch click recolours at once.
    private static void Settings(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        book.Shell.ShowSettings();
        book.Frames(3);
        // The colours are under Customization, the second category tab.
        book.Click(book.Tab(1));
        check(book.Shell.Category == SettingsCategory.Customization, "The second category tab does not open Customization.");
        book.Frames(3);
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "The colour rows overflow the Settings page.");
        book.Save(artifacts, "colour-settings.bmp");
        // The paper row's second swatch (after the edition's own) is Ivory.
        book.Click(book.Shell.ColourSwatch(ColourPart.Paper, 1)!.Value);
        check(profile.Identity.Appearance?.Colours?.Paper == Rgb.Hex(0xE6DCC6) && profile.Identity.Appearance.Edition == "gridania", $"Clicking a paper swatch chose {profile.Identity.Appearance?.Colours}.");
        book.Frames(3);
        check(UiTheme.Paper == Rgb.Hex(0xE6DCC6).Vector && book.Shell.PageOverflow == 0, $"The book does not print on the picked paper ({Rgb.Of(UiTheme.Paper)}, overflow {book.Shell.PageOverflow}).");
        book.Save(artifacts, "colour-settings-picked.bmp");
        // Black paper (the paper row's last curated swatch), then wine text: on black it does not read, so the guard
        // moves it; unticking Keep colours readable (the first control under the text row) prints it exactly.
        book.Click(book.Shell.ColourSwatch(ColourPart.Paper, 8)!.Value);
        check(profile.Identity.Appearance?.Colours?.Paper == Rgb.Hex(0x141414), $"The paper row's last curated swatch is not black ({profile.Identity.Appearance?.Colours?.Paper}).");
        book.Frames(2);
        book.Click(book.Shell.ColourSwatch(ColourPart.Text, 6)!.Value);
        check(profile.Identity.Appearance?.Colours?.Text == Rgb.Hex(0x5A1424) && profile.Identity.Look.Adjusted.Contains(ColourPart.Text), $"Wine text on black paper is not picked and guarded ({profile.Identity.Appearance?.Colours?.Text}).");
        book.Frames(2);
        // Keeping the text exact (its May adjust box) moves the paper instead.
        book.Click(book.Shell.ColourSwatch(ColourPart.Text, -1)!.Value);
        check(profile.Identity.Appearance?.Colours?.Adjustable(ColourPart.Text) == false && Rgb.Of(profile.Identity.Look.Text) == Rgb.Hex(0x5A1424) && profile.Identity.Look.Adjusted.Contains(ColourPart.Paper), "Unticking the text's May adjust box does not move the paper instead.");
        book.Frames(2);
        // On Settings the page plates are printed on the same readable paper the text is set on.
        var plates = book.Shell.PlateTint!.Value.Wanted.Paper;
        check(plates == new Vector3(UiTheme.Paper.X, UiTheme.Paper.Y, UiTheme.Paper.Z) && PassportLook.Contrast(UiTheme.Ink, UiTheme.Paper) >= 4.5, "The Settings pages print their readable text on another paper than their plates.");
        // The busiest the colour section gets (checkboxes, a note, the reset link) still fits the page.
        check(book.Shell.PageOverflow == 0, $"The colour section overflows with its checkboxes and note ({book.Shell.PageOverflow}).");
        book.Save(artifacts, "colour-settings-exact-text.bmp");
        book.Click(book.Shell.ColourSwatch(ColourPart.Text, -1)!.Value);
        book.Frames(2);
        // The Rings box sits on the same line and keeps the rings exact when unticked.
        book.Click(book.Shell.ColourSwatch(ColourPart.Lines, -1)!.Value);
        check(profile.Identity.Appearance?.Colours?.Adjustable(ColourPart.Lines) == false, "The Rings box does not keep the rings exact.");
        book.Frames(2);
        check(book.Shell.PageOverflow == 0, "The six May adjust boxes overflow the page.");
        book.Save(artifacts, "colour-settings-rings.bmp");
        book.Click(book.Shell.ColourSwatch(ColourPart.Lines, -1)!.Value);
        book.Frames(2);
        var page = book.Page(true);
        var below = book.Shell.ColourSwatch(ColourPart.Text, 0)!.Value;
        for (var y = below.Y + 12; profile.Identity.Appearance?.Colours?.Free != true && y < page.Max.Y; y += 3)
            book.ClickControl(new Vector2(page.Position.X + 30, y));
        check(profile.Identity.Appearance?.Colours?.Free == true && profile.Identity.Look.Adjusted.Length == 0 && Rgb.Of(profile.Identity.Look.Text) == Rgb.Hex(0x5A1424), "Unticking Keep colours readable does not keep the text exactly as picked.");
        // The Settings pages themselves stay readable, so the checkbox can always be found again.
        check(PassportLook.Contrast(UiTheme.Ink, UiTheme.Paper) >= 4.5, "With the guard off, the Settings pages become unreadable.");
        book.Save(artifacts, "colour-settings-free.bmp");
        check(book.Shell.PageOverflow == 0, $"The colour rows with the checkbox overflow the Settings page ({book.Shell.PageOverflow}).");
        // Elsewhere the book prints the colours exactly.
        book.Shell.GoBack();
        book.Frames(3);
        check(Rgb.Of(UiTheme.Ink) == Rgb.Hex(0x5A1424), "With the guard off, the passport pages do not print the text exactly as picked.");
        book.Save(artifacts, "colour-free-pages.bmp");
        Console.WriteLine("PASS colour-settings");
    }

    private static void Model(Action<bool, string> check)
    {
        check(Rgb.TryParse("#1E3A2A", out var green) && green == new Rgb(0x1E, 0x3A, 0x2A) && green.ToString() == "#1E3A2A", "A colour does not round-trip as #RRGGBB.");
        foreach (var bad in new[] { "", "1E3A2A", "#1E3A2", "#1E3A2AFF", "#GGGGGG", null })
            check(!Rgb.TryParse(bad, out _), $"'{bad}' was read as a colour.");
        var printed = PassportLook.Of(PassportEdition.Gridania);
        check(printed.Adjusted.Length == 0 && printed.Cloth == PassportEdition.Gridania.Cloth && printed.Paper == PassportLook.EditionPaper, "An edition's own colours are adjusted.");
        // Each guard: foil on its cloth 3:1, captions on the paper 4.5:1, printed controls on the paper 4.5:1.
        var dark = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Cloth: Rgb.Hex(0x141414), Foil: Rgb.Hex(0x1A1A1A), Ink: Rgb.Hex(0x303030), Paper: Rgb.Hex(0x303030)));
        check(PassportLook.Contrast(dark.Foil, dark.Cloth) >= 3 && dark.Adjusted.Contains(ColourPart.Foil), "Foil that does not read on its cloth is kept.");
        // Dark paper stays as picked; the text turns light and reads on it.
        check(Rgb.Of(dark.Paper) == Rgb.Hex(0x303030) && !dark.Adjusted.Contains(ColourPart.Paper) && dark.LightText, "A dark paper is changed instead of the text.");
        check(PassportLook.Contrast(dark.Text, dark.Paper) >= 4.5 && PassportLook.Contrast(dark.Caption, dark.Paper) >= 4.5, "Text or captions do not read on a dark paper.");
        foreach (var hex in new[] { 0x000000, 0x777777, 0x767676, 0x808080, 0x5A1424, 0xFFFFFF, 0xD8C5A3, 0x2B5568 })
        {
            var on = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Paper: Rgb.Hex(hex)));
            check(PassportLook.Contrast(on.Text, on.Paper) >= 4.5 && PassportLook.Contrast(on.Caption, on.Paper) >= 4.5 && PassportLook.Contrast(on.Ink, on.Paper) >= 4.5, $"Text, captions, or page ink do not read on paper {Rgb.Hex(hex)}.");
        }
        check(!printed.LightText && Rgb.Of(printed.Text) == Rgb.Hex(0x19242E), "The usual paper does not print dark text.");
        check(PassportLook.Contrast(dark.Ink, dark.Paper) >= 4.5 && dark.Adjusted.Contains(ColourPart.Ink), "Ink that does not read on the paper is kept.");
        var fine = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Cloth: Rgb.Hex(0x5A1424), Foil: Rgb.Hex(0xD4A83C)));
        check(fine.Adjusted.Length == 0 && Rgb.Of(fine.Cloth) == Rgb.Hex(0x5A1424) && Rgb.Of(fine.Foil) == Rgb.Hex(0xD4A83C), "Legible colours are moved.");
        // Text picked: exact when it reads, captions a softer shade of it, the paper's rings following it.
        var sepia = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Text: Rgb.Hex(0x4A3426)));
        check(Rgb.Of(sepia.Text) == Rgb.Hex(0x4A3426) && sepia.Lines == sepia.Text && !sepia.LightText && PassportLook.Contrast(sepia.Caption, sepia.Paper) >= 4.5 && sepia.Caption != sepia.Text, "Picked text, its captions, or its rings are not as picked.");
        var white = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Paper: Rgb.Hex(0x000000), Text: Rgb.Hex(0xFFFFFF)));
        check(white.LightText && white.Lines == Vector4.One, "White text on black paper does not light the rings.");
        // A pick that does not read is moved, unless the guard is off; then every colour is exact.
        var faint = new PassportColours(Foil: Rgb.Hex(0x1E3A2A), Text: Rgb.Hex(0xD0BE9C));
        var guarded = PassportLook.Of(PassportEdition.Gridania, faint);
        check(guarded.Adjusted.Contains(ColourPart.Text) && guarded.Adjusted.Contains(ColourPart.Foil) && PassportLook.Contrast(guarded.Text, guarded.Paper) >= 4.5, "An unreadable text or foil pick is kept with the guard on.");
        var free = PassportLook.Of(PassportEdition.Gridania, faint with { Free = true });
        check(free.Adjusted.Length == 0 && Rgb.Of(free.Text) == Rgb.Hex(0xD0BE9C) && Rgb.Of(free.Foil) == Rgb.Hex(0x1E3A2A), "With the guard off a pick is still moved.");
        // The guard moves what the holder lets it: text kept exact, the paper moves instead; both exact, neither.
        var wine = new PassportColours(Paper: Rgb.Hex(0x141414), Text: Rgb.Hex(0x5A1424));
        var paperMoves = PassportLook.Of(PassportEdition.Gridania, wine.WithExact(ColourPart.Text, true));
        check(Rgb.Of(paperMoves.Text) == Rgb.Hex(0x5A1424) && paperMoves.Adjusted.Contains(ColourPart.Paper) && PassportLook.Contrast(paperMoves.Text, paperMoves.Paper) >= 4.5, "With text kept exact, the paper does not move for it.");
        var neither = PassportLook.Of(PassportEdition.Gridania, wine.WithExact(ColourPart.Text, true).WithExact(ColourPart.Paper, true));
        check(!neither.Adjusted.Contains(ColourPart.Text) && !neither.Adjusted.Contains(ColourPart.Paper) && Rgb.Of(neither.Paper) == Rgb.Hex(0x141414) && Rgb.Of(neither.Text) == Rgb.Hex(0x5A1424), "With text and paper both kept exact, one still moves.");
        var foilExact = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Foil: Rgb.Hex(0x1E3A2A)).WithExact(ColourPart.Foil, true));
        check(Rgb.Of(foilExact.Foil) == Rgb.Hex(0x1E3A2A) && foilExact.Adjusted.Contains(ColourPart.Cloth) && PassportLook.Contrast(foilExact.Foil, foilExact.Cloth) >= 3, "With foil kept exact, the cloth does not move for it.");
        // Rings: their own colour when picked, else following the text.
        var gold = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Lines: Rgb.Hex(0xB99662)));
        check(Rgb.Of(gold.Lines) != Rgb.Hex(0xB99662) && gold.Adjusted.Contains(ColourPart.Lines) && PassportLook.Contrast(gold.Lines, gold.Paper) >= PassportColours.RingsContrast && gold.Paper == PassportLook.EditionPaper,
            "Gold rings on cream are not moved (only the rings) until they show.");
        var goldExact = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Lines: Rgb.Hex(0xB99662)).WithExact(ColourPart.Lines, true));
        check(Rgb.Of(goldExact.Lines) == Rgb.Hex(0xB99662) && !goldExact.Adjusted.Contains(ColourPart.Lines), "Rings kept exact are still moved.");
        var navy = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Lines: Rgb.Hex(0x2A3B52)));
        check(Rgb.Of(navy.Lines) == Rgb.Hex(0x2A3B52) && !navy.Adjusted.Contains(ColourPart.Lines) && Rgb.Of(sepia.Lines) == Rgb.Hex(0x4A3426), "Rings that show are moved, or rings stop following the text.");
        foreach (var hex in new[] { 0x000000, 0x777777, 0xFFFFFF, 0xD8C5A3 })
        {
            var on = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Paper: Rgb.Hex(hex)));
            check(PassportLook.Contrast(on.Lines, on.Paper) >= PassportColours.RingsContrast, $"Automatic rings do not show on paper {Rgb.Hex(hex)}.");
        }
        Console.WriteLine("PASS colour-model");
    }

    private static void Storage(Action<bool, string> check)
    {
        var colours = new PassportColours(Cloth: Rgb.Hex(0x5A1424), Paper: Rgb.Hex(0xECEAE4));
        var identity = PassportIdentity.Sample with { Appearance = new PassportAppearance("gridania", colours), PreviousAppearance = new PassportAppearance("uldah", new PassportColours(Foil: Rgb.Hex(0xC3CAD6))) };
        var text = PassportStore.Serialize(identity);
        check(PassportStore.Parse(text) == identity.Normalize() && text.Contains("\"cloth\": \"#5A1424\"", StringComparison.Ordinal), "Colours do not round-trip through the passport file.");
        check(!PassportStore.Serialize(PassportIdentity.Sample).Contains("colours", StringComparison.Ordinal), "A passport in its edition's colours writes colours.");
        foreach (var bad in new[] { "{\"cloth\":\"red\"}", "{\"foil\":7}", "\"#FFFFFF\"" })
        {
            try
            {
                PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"colours\":" + bad + "}}");
                check(false, $"Colours {bad} were accepted.");
            }
            catch (FormatException) { check(true, ""); }
        }
        check(PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"colours\":{}}}").Appearance?.Colours == null, "Empty colours are kept as colours.");

        // Colours belong to the appearance: changing edition returns to the new edition's own, change back brings them back.
        var profile = new PassportProfile(PassportIdentity.Sample);
        check(profile.SetColour(ColourPart.Cloth, Rgb.Hex(0x3E2440)) && profile.Identity.Appearance?.Colours?.Cloth == Rgb.Hex(0x3E2440), "Setting a colour does not keep it.");
        check(profile.SetColour(ColourPart.Paper, Rgb.Hex(0xECEAE4)) && profile.Identity.Appearance?.Colours?.Cloth == Rgb.Hex(0x3E2440), "Setting a second colour loses the first.");
        profile.ChangeEdition("ishgard");
        check(profile.Identity.Appearance?.Colours == null && profile.Identity.PreviousAppearance?.Colours?.Cloth == Rgb.Hex(0x3E2440), "Changing edition does not reset the colours, or loses them for change back.");
        profile.RestorePrevious();
        check(profile.Identity.Appearance?.Colours?.Paper == Rgb.Hex(0xECEAE4), "Change back does not bring the colours back.");
        check(profile.SetColour(ColourPart.Cloth, null) && profile.Identity.Appearance?.Colours?.Cloth == null && profile.ResetColours() && profile.Identity.Appearance?.Colours == null, "Colours do not return to the edition's.");
        var exact = PassportIdentity.Sample with { Appearance = new PassportAppearance("gridania", new PassportColours(Text: Rgb.Hex(0x4A3426), Free: true)) };
        var stored = PassportStore.Serialize(exact);
        check(PassportStore.Parse(stored) == exact.Normalize() && stored.Contains("\"text\": \"#4A3426\"", StringComparison.Ordinal) && stored.Contains("\"readable\": false", StringComparison.Ordinal), "Text colour and the guard being off do not round-trip.");
        try
        {
            PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"colours\":{\"readable\":\"no\"}}}");
            check(false, "A readable setting that is not true or false was accepted.");
        }
        catch (FormatException) { check(true, ""); }
        var parts = PassportIdentity.Sample with { Appearance = new PassportAppearance("gridania", new PassportColours(Lines: Rgb.Hex(0xB99662)).WithExact(ColourPart.Paper, true).WithExact(ColourPart.Text, true)) };
        var partsText = PassportStore.Serialize(parts);
        check(PassportStore.Parse(partsText) == parts.Normalize() && partsText.Contains("\"lines\": \"#B99662\"", StringComparison.Ordinal) && partsText.Contains("\"exact\"", StringComparison.Ordinal), "Rings and parts kept exact do not round-trip.");
        check(PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"colours\":{\"exact\":[\"paper\",\"sparkle\"]}}}").Appearance?.Colours?.Adjustable(ColourPart.Paper) == false, "Parts kept exact are not read, or an unknown one breaks the file.");
        var guard = new PassportProfile(PassportIdentity.Sample);
        check(guard.SetReadable(false) && guard.Identity.Appearance?.Colours?.Free == true && guard.SetColour(ColourPart.Text, Rgb.Hex(0xFFFFFF)), "Turning the guard off is not kept.");
        check(guard.ResetColours() && guard.Identity.Appearance?.Colours == new PassportColours(Free: true), "Using the edition's own colours turns the guard back on or keeps a colour.");
        check(guard.SetReadable(true) && guard.Identity.Appearance?.Colours == null, "Turning the guard back on leaves colours behind.");
        check(guard.SetAdjustable(ColourPart.Paper, false) && guard.Identity.Appearance?.Colours?.Adjustable(ColourPart.Paper) == false && guard.SetAdjustable(ColourPart.Paper, true) && guard.Identity.Appearance?.Colours == null, "Keeping one part exact is not kept, or not undone.");
        Console.WriteLine("PASS colour-storage");
    }

    private static void Recolouring(Action<bool, string> check)
    {
        // Two pixels: one fully the cloth (weight 255), one half (128); the cloth moves +50 red, -20 blue.
        var plate = new PlatePixels(2, 1, [100, 100, 100, 255, 100, 100, 100, 128]);
        var moved = PlateRecolour.Apply(plate, [new PlateRecolour.Shift([255, 128], 50, 0, -20)]);
        check(moved.Rgba is [150, 100, 80, 255, 125, 100, 90, 128], $"Recolouring is not plate + weight x shift ({string.Join(",", moved.Rgba)}).");
        check(plate.Rgba[0] == 100, "Recolouring changed the printed plate.");
        // Halving weights colour by coverage: a transparent pixel's colour does not bleed into its opaque neighbour.
        var edge = PlateRecolour.Halve(new PlatePixels(2, 2, [255, 0, 0, 255, 0, 0, 255, 0, 255, 0, 0, 255, 0, 0, 255, 0]));
        check(edge is { Width: 1, Height: 1 } && edge.Rgba is [255, 0, 0, 128], $"Halving bleeds transparent colour ({string.Join(",", edge.Rgba)}).");
        Console.WriteLine("PASS colour-recolouring");
    }

    // The plugin's PNG reader against Windows' own decoder, on real plates and a weight map.
    private static void Png(Action<bool, string> check)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", "gridania");
        foreach (var name in new[] { "crest.quarter.png", "banner.eighth.png", "book-open.eighth.png" })
        {
            using var stream = File.OpenRead(Path.Combine(folder, name));
            var mine = PlatePng.ReadRgba(stream);
            using var bitmap = new Bitmap(Path.Combine(folder, name));
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var differs = 0;
            try
            {
                var row = new byte[bitmap.Width * 4];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var at = (y * bitmap.Width + x) * 4;
                        // GDI is BGRA; transparent pixels carry no colour to compare.
                        if (row[x * 4 + 3] != mine.Rgba[at + 3] || row[x * 4 + 3] > 0 && (row[x * 4 + 2] != mine.Rgba[at] || row[x * 4 + 1] != mine.Rgba[at + 1] || row[x * 4] != mine.Rgba[at + 2])) differs++;
                    }
                }
            }
            finally { bitmap.UnlockBits(data); }
            check(mine.Width == bitmap.Width && mine.Height == bitmap.Height && differs == 0, $"The PNG reader misreads {name} ({differs} pixels).");
        }
        using var weight = File.OpenRead(Path.Combine(folder, "cover.w-cover.png"));
        var (width, height, grey) = PlatePng.ReadGrey(weight);
        check(width == 1024 && height == 1536 && grey.Any(value => value == 255) && grey.Any(value => value == 0), "A weight map does not read as a full cover-sized grey map.");
        try
        {
            PlatePng.ReadRgba(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9]));
            check(false, "Something that is not a PNG was read.");
        }
        catch (InvalidDataException) { check(true, ""); }
        Console.WriteLine("PASS colour-png");
    }

    // A recoloured Gridania: wine cloth, silver foil, white paper. Its plates are recoloured, its pages print on
    // the new paper, and nothing overflows.
    private static void Drawn(Action<bool, string> check, string? artifacts)
    {
        var colours = new PassportColours(Cloth: Rgb.Hex(0x5A1424), Foil: Rgb.Hex(0xC3CAD6), Ink: Rgb.Hex(0x7A2232), Paper: Rgb.Hex(0xECEAE4));
        var profile = new PassportProfile(PassportIdentity.Sample with { Appearance = new PassportAppearance("gridania", colours) });
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        var textures = book.Textures;
        book.Save(artifacts, "colour-cover.bmp");
        book.Open();
        check(textures.Recolourings >= 3 && book.Shell.PageOverflow == 0, $"The recoloured passport does not recolour its plates ({textures.Recolourings}).");
        book.Save(artifacts, "colour-open.bmp");
        book.Key(ImGuiKey.PageDown);
        book.Save(artifacts, "colour-identity.bmp");
        check(UiTheme.Paper == PassportLook.Of(PassportEdition.Gridania, colours).Paper, "The pages do not print on the chosen paper.");
        // Black paper with light text, as a holder might want it.
        var night = new PassportProfile(PassportIdentity.Sample with { Appearance = new PassportAppearance("ishgard", new PassportColours(Paper: Rgb.Hex(0x141414))) });
        using (var dark = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: night))
        {
            dark.Open();
            dark.Key(ImGuiKey.PageDown);
            check(UiTheme.Paper == Rgb.Hex(0x141414).Vector && PassportLook.Contrast(UiTheme.Ink, UiTheme.Paper) >= 4.5 && dark.Shell.PageOverflow == 0, "Black paper does not print light, readable text.");
            dark.Save(artifacts, "colour-dark-paper.bmp");
            // The guilloche rings turn light on it: the open page plate is lighter than plain black paper where
            // its rings run, not darker.
            var plate = EditionRecolour.Levels(name => File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", "ishgard", name)) ? File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Editions", "ishgard", name)) : null, "book-open",
                new PlateTint(new PlateColours(PassportEdition.Find("ishgard").Cloth.AsVector3(), PassportEdition.Find("ishgard").Foil.AsVector3(), PassportLook.EditionPaper.AsVector3(), Vector3.Zero),
                    new PlateColours(PassportEdition.Find("ishgard").Cloth.AsVector3(), PassportEdition.Find("ishgard").Foil.AsVector3(), new Vector3(0x14 / 255f), Vector3.One)))[0];
            var lighter = 0;
            var darker = 0;
            // Along a row through the right page's rings (centre 1119, 601), away from the gutter shadow.
            for (var x = 1200; x < 1440; x++)
            {
                var value = plate.Rgba[(601 * plate.Width + x) * 4];
                if (value > 0x14 + 2) lighter++;
                if (value < 0x14 - 2) darker++;
            }
            check(lighter > 20 && darker == 0, $"The rings do not turn light on dark paper ({lighter} lighter, {darker} darker).");
        }
        Console.WriteLine("PASS colour-drawn");
    }
}
