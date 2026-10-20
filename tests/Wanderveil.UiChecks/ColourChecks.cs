using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
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
    }

    // The colour rows in Settings: they fit beside the editions, and a swatch click recolours at once.
    private static void Settings(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        book.Shell.ShowSettings();
        book.Frames(3);
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "The colour rows overflow the Settings page.");
        book.Save(artifacts, "colour-settings.bmp");
        // Up the right page's second swatch column from its foot, the first control is the paper row's second
        // swatch: Ivory.
        var page = book.Page(true);
        var scale = page.Size.X / 364f;
        for (var y = page.Size.Y - 20; profile.Identity.Appearance?.Colours == null && y > 40; y -= 3)
            book.ClickControl(new Vector2(page.Position.X + 36 * scale, page.Position.Y + y));
        check(profile.Identity.Appearance?.Colours?.Paper == Rgb.Hex(0xE6DCC6) && profile.Identity.Appearance.Edition == "gridania", $"Clicking a paper swatch chose {profile.Identity.Appearance?.Colours}.");
        book.Frames(3);
        check(UiTheme.Paper == Rgb.Hex(0xE6DCC6).Vector && book.Shell.PageOverflow == 0, "The book does not print on the picked paper.");
        book.Save(artifacts, "colour-settings-picked.bmp");
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
        var dark = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Cloth: Rgb.Hex(0x141414), Foil: Rgb.Hex(0x1A1A1A), Ink: Rgb.Hex(0xD8C5A3), Paper: Rgb.Hex(0x303030)));
        check(PassportLook.Contrast(dark.Foil, dark.Cloth) >= 3 && dark.Adjusted.Contains(ColourPart.Foil), "Foil that does not read on its cloth is kept.");
        check(PassportLook.Contrast(new Vector4(0x43 / 255f, 0x50 / 255f, 0x5A / 255f, 1), dark.Paper) >= 4.5 && dark.Adjusted.Contains(ColourPart.Paper), "A paper captions cannot be read on is kept.");
        check(PassportLook.Contrast(dark.Ink, dark.Paper) >= 4.5 && dark.Adjusted.Contains(ColourPart.Ink), "Ink that does not read on the paper is kept.");
        var fine = PassportLook.Of(PassportEdition.Gridania, new PassportColours(Cloth: Rgb.Hex(0x5A1424), Foil: Rgb.Hex(0xD4A83C)));
        check(fine.Adjusted.Length == 0 && Rgb.Of(fine.Cloth) == Rgb.Hex(0x5A1424) && Rgb.Of(fine.Foil) == Rgb.Hex(0xD4A83C), "Legible colours are moved.");
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
        Console.WriteLine("PASS colour-drawn");
    }
}
