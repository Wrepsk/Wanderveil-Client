using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;
using Wanderveil.UI.Components;

namespace Wanderveil.UiChecks;

// The holder's symbols: the provided set, the layer's limits and order, how they are stored and reset with the
// edition, and decorating the first pages and the closed cover in the real shell.
internal static class SymbolChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Model(check);
        Storage(check);
        Pages(check, artifacts);
        Cover(check, artifacts);
        Narrow(check, artifacts);
        Console.WriteLine("PASS symbols");
    }

    private static PlacedSymbol On(string surface, string symbol = "sparkle", float x = 0.5f, float y = 0.5f) => new(symbol, surface, x, y, 0f, PlacedSymbol.StartSize);

    private static void Model(Action<bool, string> check)
    {
        var all = PassportSymbol.All;
        check(all.Count == PassportSymbol.Columns * PassportSymbol.Rows && all.Select(symbol => symbol.Id).Distinct().Count() == all.Count && all.Select(symbol => symbol.Cell).SequenceEqual(Enumerable.Range(0, all.Count)),
            "The symbol set does not fill the plate's cells once each, in order.");
        check(PassportEdition.All.All(edition => PassportSymbol.Find("crest-" + edition.Id) != null && PassportSymbol.Find("emblem-" + edition.Id) != null), "An edition's crest or emblem is missing from the symbol set.");

        // Unknown symbols and surfaces are left out; values are kept on the surface and within the sizes.
        var layer = SymbolLayer.Of([On("cover"), On("cover", "dragon"), On("stamps-0"), new PlacedSymbol("moon", "nation-map", 2f, float.NaN, 9f, 5f)]);
        check(layer.Items.Length == 2 && layer.Items[1] is { X: 1f, Y: 0.5f, Size: PlacedSymbol.MaximumSize } moved && MathF.Abs(moved.Rotation) <= MathF.PI,
            "A layer keeps an unknown symbol or surface, or does not keep a symbol on its surface.");
        check(SymbolLayer.Of([On("cover")]) == SymbolLayer.Of([On("cover")]) && SymbolLayer.Of([On("cover")]) != SymbolLayer.Of([On("cover", "moon")]), "Layers are not equal by their symbols.");
        // Each surface holds at most its limit; the others are unaffected.
        var crowded = SymbolLayer.Of(Enumerable.Range(0, SymbolLayer.MaximumPerSurface + 3).Select(_ => On("portrait")).Append(On("cover")));
        check(crowded.CountOn("portrait") == SymbolLayer.MaximumPerSurface && crowded.CountOn("cover") == 1 && !crowded.HasRoomOn("portrait"), "A surface holds more than its limit, or the limit spills onto another.");
        // A moved symbol comes to the top.
        var ordered = SymbolLayer.Of([On("cover", "moon"), On("cover", "sun")]).Replace(0, On("cover", "moon", 0.2f));
        check(ordered.Items.Select(symbol => symbol.Symbol).SequenceEqual(["sun", "moon"]) && ordered.Items[1].X == 0.2f, "A moved symbol does not come to the top.");

        // The profile: adding raises one change; a full surface or an unknown symbol is refused.
        var profile = new PassportProfile(PassportIdentity.Sample);
        var changes = 0;
        profile.Changed += () => changes++;
        check(profile.AddSymbol(On("cover")) && changes == 1 && profile.Identity.Appearance?.Placed.Items.Length == 1, "Adding a symbol is not one change.");
        check(!profile.AddSymbol(On("cover", "dragon")) && changes == 1, "An unknown symbol is added.");
        for (var index = 0; index < SymbolLayer.MaximumPerSurface; index++) profile.AddSymbol(On("information"));
        check(!profile.AddSymbol(On("information")) && !profile.ReplaceSymbol(0, On("information")), "A full surface takes another symbol, or one carried onto it.");
        check(profile.ReplaceSymbol(1, On("information", x: 0.1f)) && profile.RemoveSymbol(0) && profile.Identity.Appearance?.Placed.CountOn("cover") == 0, "A symbol is not moved within a full surface, or not removed.");

        // Symbols are part of the appearance: a change of edition clears them, Change back restores them, and a
        // colour reset leaves them.
        var held = profile.Identity.Appearance!.Placed;
        profile.SetColour(ColourPart.Cloth, Rgb.Hex(0x5A1424));
        profile.ResetColours();
        check(profile.Identity.Appearance?.Placed == held, "Resetting colours touches the symbols.");
        profile.ChangeEdition("ishgard");
        check(profile.Identity.Appearance?.Symbols == null, "A change of edition keeps the symbols.");
        profile.RestorePrevious();
        check(profile.Identity.Appearance?.Placed == held && profile.Identity.Edition.Id == "gridania", "Change back does not restore the symbols.");
    }

    private static void Storage(Action<bool, string> check)
    {
        var symbols = SymbolLayer.Of([new PlacedSymbol("crest-uldah", "cover", 0.25f, 0.75f, 0.5f, 0.3f), On("nation-banner", "heart", 0.1f, 0.9f)]);
        var identity = PassportIdentity.Sample with
        {
            Appearance = new PassportAppearance("uldah", Symbols: symbols),
            PreviousAppearance = PassportAppearance.Gridania with { Symbols = SymbolLayer.Of([On("portrait", "moon")]) },
        };
        var text = PassportStore.Serialize(identity);
        var read = PassportStore.Parse(text);
        check(read == identity.Normalize() && read.PreviousAppearance?.Placed.Items.Single().Symbol == "moon", "The passport file does not round-trip symbols, in order, and the previous appearance's.");
        check(text.Contains("\"version\": 3", StringComparison.Ordinal) && text.Contains("\"on\": \"cover\"", StringComparison.Ordinal), "Symbols are not saved as version 3 with their surface.");
        // A version 2 file has none; a symbol or surface this version does not know is left out.
        var older = PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\"}}");
        check(older.Appearance == PassportAppearance.Gridania, "A version 2 passport does not read as it was.");
        var unknown = PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":3,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"symbols\":[{\"symbol\":\"dragon\",\"on\":\"cover\",\"x\":0.5,\"y\":0.5,\"turn\":0,\"size\":0.2},{\"symbol\":\"sun\",\"on\":\"back\",\"x\":0.5,\"y\":0.5,\"turn\":0,\"size\":0.2},{\"symbol\":\"sun\",\"on\":\"cover\",\"x\":0.5,\"y\":0.5,\"turn\":0,\"size\":0.2}]}}");
        check(unknown.Appearance?.Placed.Items.Single().Symbol == "sun", "An unknown symbol or surface is kept, or breaks the file.");
        foreach (var bad in new[] { "{}", "[{\"symbol\":\"sun\"}]", "[{\"symbol\":\"sun\",\"on\":\"cover\",\"x\":\"left\",\"y\":0,\"turn\":0,\"size\":0.2}]", "[7]" })
        {
            try
            {
                PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":3,\"number\":\"EZ 1\",\"appearance\":{\"edition\":\"gridania\",\"symbols\":" + bad + "}}");
                check(false, $"Malformed symbols {bad} are accepted.");
            }
            catch (FormatException) { check(true, ""); }
        }
        try
        {
            PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":4,\"number\":\"EZ 1\"}");
            check(false, "A later passport version is read.");
        }
        catch (NotSupportedException) { check(true, ""); }
    }

    // Decorating the nation spread: the footer's Decorate opens the sheet; a symbol picked from it is placed with a
    // click, then turned and resized by its knob, carried to the other page, and taken off by its ×.
    private static void Pages(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        var symbols = book.Shell.Symbols;
        check(book.ClickControl(book.StampControl()) && symbols.Decorating && symbols.SheetOpen, "The nation pages' footer does not offer Decorate, opening the sheet.");
        book.Frames(2);
        check(symbols.SheetCells.Count == PassportSymbol.All.Count && book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "The sheet does not show every symbol, or overflows.");
        book.Save(artifacts, "symbols-sheet.bmp");

        book.Click(symbols.SheetCells["sparkle"]);
        check(symbols.Carrying is { Symbol: "sparkle", From: -1 } && !symbols.SheetOpen, "Picking a symbol does not put it in hand and slide the sheet away.");
        var left = book.Page(false);
        book.Click(left.Position + left.Size * new Vector2(0.12f, 0.9f));
        var placed = profile.Identity.Appearance?.Placed.Items.SingleOrDefault();
        check(placed is { Symbol: "sparkle", Surface: "nation-banner" } && symbols.Carrying == null && symbols.SheetOpen && book.Shell.VisiblePage == 0,
            $"A click does not put the symbol on the page under it, bring the sheet back, and leave the page ({placed}).");
        // Where it lands is where it was clicked.
        var (center, _) = SymbolArt.Place(placed!.Value, left);
        check(Vector2.Distance(center, left.Position + left.Size * new Vector2(0.12f, 0.9f)) < 2f, $"The symbol landed at {center}, not where it was clicked.");

        // A crest on the map page, then the sheet put away (Escape) to show the symbols' knobs.
        book.Click(symbols.SheetCells["crest-gridania"]);
        var right = book.Page(true);
        book.Click(right.Position + right.Size * new Vector2(0.86f, 0.1f));
        book.Key(ImGuiKey.Escape);
        check(symbols.Decorating && !symbols.SheetOpen && symbols.Handles.Count == 2, "Escape does not put the sheet away, leaving the symbols' handles.");
        book.Save(artifacts, "symbols-decorating.bmp");

        // The knob dragged out and round: the symbol grows and turns.
        var (body, knob, _) = symbols.Handles[0];
        var before = profile.Identity.Appearance!.Placed.Items[0];
        var outward = knob - body;
        book.Drag(knob, body + new Vector2(-outward.Y, outward.X) * 1.5f);
        var after = profile.Identity.Appearance!.Placed.Items.Single(symbol => symbol.Symbol == "sparkle");
        check(after.Size > before.Size * 1.3f && MathF.Abs(MathF.Abs(after.Rotation) - MathF.PI / 2) < 0.15f, $"The knob does not resize and turn the symbol ({before.Size} to {after.Size}, turned {after.Rotation}).");

        // A drag carries the sparkle (now on top) across to the map page.
        var sparkle = profile.Identity.Appearance!.Placed.Items.Length - 1;
        book.Drag(symbols.Handles[sparkle].Body, right.Position + right.Size * new Vector2(0.3f, 0.2f));
        check(profile.Identity.Appearance!.Placed.Items.All(symbol => symbol.Surface == "nation-map") && book.Shell.VisiblePage == 0, "Dragging a symbol does not carry it to the other page.");

        // Its × takes it off.
        book.Click(symbols.Handles[profile.Identity.Appearance!.Placed.Items.Length - 1].Remove);
        check(profile.Identity.Appearance!.Placed.Items.Single().Symbol == "crest-gridania", "A symbol's × does not take it off.");

        // Done ends decorating; the symbols stay, beneath the page's printing.
        book.Key(ImGuiKey.Escape);
        check(!symbols.Decorating && symbols.Handles.Count == 0, "Escape does not end decorating once the sheet is away.");
        book.Save(artifacts, "symbols-pages.bmp");

        // The identity pages offer Decorate beside Edit; turning on to the stamp pages ends decorating.
        book.Key(ImGuiKey.PageDown);
        book.Frames(3);
        var spot = book.StampControl();
        var found = false;
        for (var x = spot.X; x > spot.X - 400 && !found; x -= 8)
        {
            book.Move(new Vector2(x, spot.Y));
            if (!ImGui.IsAnyItemHovered()) continue;
            book.Click(new Vector2(x, spot.Y));
            found = symbols.Decorating;
        }
        check(found && !book.Shell.EditingIdentity, "The identity pages do not offer Decorate beside Edit.");
        book.Key(ImGuiKey.Escape);
        book.Key(ImGuiKey.PageDown);
        book.Frames(3);
        check(!symbols.Decorating && book.Shell.VisiblePage == 4, "Turning to the stamp pages does not end decorating.");
    }

    // The closed cover: its Decorate mark, a symbol pressed in foil, and a click while decorating never opens the book.
    private static void Cover(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample with { Appearance = new PassportAppearance("uldah") });
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        var cover = PassportDocumentView.CoverSurface(book.Root);
        // The marks along the cover's lower band, from the left: Decorate comes first.
        var band = cover.Position.Y + cover.Size.Y * 542f / 560f;
        for (var x = cover.Position.X + 8; x < cover.Max.X && !book.Shell.Symbols.Decorating; x += 6)
            if (book.ClickControl(new Vector2(x, band))) break;
        var symbols = book.Shell.Symbols;
        check(symbols.Decorating && symbols.SheetOpen && !book.Shell.IsBookOpen, "The closed cover does not offer Decorate.");
        book.Save(artifacts, "symbols-cover-sheet.bmp");
        book.Click(symbols.SheetCells["moon"]);
        book.Click(cover.Position + cover.Size * new Vector2(0.78f, 0.12f));
        book.Click(symbols.SheetCells["sparkle"]);
        book.Click(cover.Position + cover.Size * new Vector2(0.2f, 0.84f));
        check(profile.Identity.Appearance?.Placed.CountOn(SymbolSurface.Cover) == 2 && !book.Shell.IsBookOpen, "Symbols are not placed on the cover, or a click opens the book.");
        book.Key(ImGuiKey.Escape);
        book.Key(ImGuiKey.Escape);
        check(!symbols.Decorating, "Escape does not end decorating the cover.");
        book.Save(artifacts, "symbols-cover.bmp");
        // Opening the cover afterwards still works.
        book.Open();
        check(book.Shell.IsBookOpen, "The cover does not open after decorating.");
    }

    // On a single page the footer's two decorating controls and the sheet still fit.
    private static void Narrow(Action<bool, string> check, string? artifacts)
    {
        using var book = new CollectChecks.Book(new Vector2(520, 900), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample));
        book.Open();
        book.Shell.Symbols.Start();
        book.Frames(3);
        var page = book.Page(false);
        check(book.Shell.Symbols.Sheet is { } sheet && sheet.Max.Y <= page.Max.Y + 1 && sheet.Size.X <= page.Size.X + 1 && book.Shell.PageOverflowX == 0, "The sheet does not fit a single page.");
        book.Save(artifacts, "symbols-narrow.bmp");
    }
}
