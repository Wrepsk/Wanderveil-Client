using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;
using Wanderveil.UI.Components;
using Wanderveil.UiChecks;

namespace Wanderveil.UiChecks;

// The holder's identity: field rules, the passport file and its recovery, a stable issued number, and writing
// on the identity pages in the real shell (typing, wrapping, limits, the picture frame, the character fill,
// the cover line, and how editing ends).
internal static class IdentityChecks
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private const string Profile = "local";

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Fields(check);
        Crop(check);
        Storage(check);
        Issue(check);
        Edit(check, artifacts);
        Picture(check, artifacts);
        Character(check, artifacts);
        Cover(check, artifacts);
    }

    private static void Fields(Action<bool, string> check)
    {
        check(PassportIdentity.Clean("  Ari \n\t Vale\u0007  ", 40) == "Ari Vale", "Identity text keeps line breaks, control characters, or extra spaces.");
        check(PassportIdentity.Clean(new string('a', 60), 40).Length == 40, "A field keeps more than its limit.");
        // Ten emoji (twenty UTF-16 units) cut at five whole characters, never inside a pair.
        var cut = PassportIdentity.Clean(string.Concat(Enumerable.Repeat("😀", 10)), 5);
        check(cut == string.Concat(Enumerable.Repeat("😀", 5)), "A field limit splits a character.");
        var normalized = (PassportIdentity.Sample with { Bio = new string('b', 900), CoverLine = "  my   line " }).Normalize();
        check(normalized.Bio.Length == PassportIdentity.Limit(IdentityField.Bio) && normalized.CoverLine == "my line", "Normalizing an identity misses a field.");
        check(PassportIdentity.Sample.MachineLine() == "P<WV<GRIDANIA<ARI<VALE<<00421187<<<<", $"The machine line reads {PassportIdentity.Sample.MachineLine()}.");
        check(new PassportIdentity("", "", "", "", "", "EZ 1 2").MachineLine().Length == 36, "The machine line is not 36 characters.");
        var long_ = (PassportIdentity.Sample with { Name = "Wren Halloway of the Many Roads" }).MachineLine();
        check(long_.Length == 36 && long_.EndsWith("<<00421187", StringComparison.Ordinal), $"A long name pushes the number off the machine line ({long_}).");
        check((PassportIdentity.Sample with { Signature = "" }).SignedName == "Ari Vale" && (PassportIdentity.Sample with { Signature = "A. V." }).SignedName == "A. V.", "An empty signature does not sign with the name.");
        var profile = new PassportProfile(PassportIdentity.Sample);
        var changes = 0;
        profile.Changed += () => changes++;
        check(!profile.Update(PassportIdentity.Sample with { Name = "Ari Vale " }) && changes == 0, "Trailing space counts as a change.");
        check(profile.Update(PassportIdentity.Sample with { Name = "Wren" }) && changes == 1, "A change was not raised.");
        Console.WriteLine("PASS identity-fields");
    }

    private static void Crop(Action<bool, string> check)
    {
        // A wide picture (2:1) in the 3:4 frame shows its full height and a 0.375-wide slice.
        var (min, max) = PortraitCrop.Centered.Window(2f, 0.75f);
        check(Near(min.Y, 0) && Near(max.Y, 1) && Near(max.X - min.X, 0.375f) && Near((min.X + max.X) / 2, 0.5f), "A wide picture does not cover the frame by its height.");
        // A tall picture (1:3) shows its full width.
        (min, max) = PortraitCrop.Centered.Window(1f / 3f, 0.75f);
        check(Near(min.X, 0) && Near(max.X, 1) && Near(max.Y - min.Y, (1f / 3f) / 0.75f), "A tall picture does not cover the frame by its width.");
        // Moved past the edge, the window stops at it; zoomed, it narrows.
        var edge = new PortraitCrop(1f, 0.5f, 2f).Clamped(2f, 0.75f);
        check(Near(edge.X, 1 - 0.375f / 4), $"A moved picture leaves the frame ({edge.X}).");
        check(new PortraitCrop(0.5f, 0.5f, 9f).Normalize().Zoom == PortraitCrop.MaximumZoom && !new PortraitCrop(float.NaN, 0, 1).IsValid, "Portrait placement accepts impossible values.");
        Console.WriteLine("PASS identity-crop");
    }

    private static void Storage(Action<bool, string> check)
    {
        var full = PassportIdentity.Sample with { Signature = "A. Vale", CoverLine = "Ari Vale", FavoriteJob = "Bard + Botanist", Portrait = new PortraitCrop(0.4f, 0.6f, 1.5f), Frame = PictureFrame.Deckled };
        var parsed = PassportStore.Parse(PassportStore.Serialize(full));
        check(parsed == full.Normalize(), "The passport file does not round-trip an identity.");
        check(PassportStore.Serialize(full).Contains("Bard + Botanist", StringComparison.Ordinal), "Saved names are escaped instead of readable.");
        Throws<FormatException>(check, "{\"format\":\"other\",\"version\":1,\"number\":\"EZ\"}", "A file of another kind");
        Throws<FormatException>(check, "{\"format\":\"wanderveil.passport\",\"version\":1}", "A file without a number");
        Throws<FormatException>(check, "{\"format\":\"wanderveil.passport\",\"version\":1,\"number\":\"EZ\",\"name\":7}", "A name that is not text");
        Throws<FormatException>(check, "{\"format\":\"wanderveil.passport\",\"version\":1,\"number\":\"EZ\",\"portrait\":{\"x\":3,\"y\":0,\"zoom\":1}}", "A portrait outside its frame");
        Throws<NotSupportedException>(check, "{\"format\":\"wanderveil.passport\",\"version\":2,\"number\":\"EZ\"}", "A newer version");
        foreach (var unknown in new[] { "sparkly", "3", "" })
            check(PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":1,\"number\":\"EZ\",\"frame\":\"" + unknown + "\"}").Frame == PictureFrame.Printed, $"An unknown frame '{unknown}' is not read as the printed one.");
        check(PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":1,\"number\":\"EZ\"}").Frame == PictureFrame.Printed, "A passport without a frame is not printed.");
        var lenient = PassportStore.Parse("{\"format\":\"wanderveil.passport\",\"version\":1,\"number\":\"EZ 1\",\"bio\":\"" + new string('x', 900) + "\"}");
        check(lenient.Bio.Length == 400 && lenient.Name == "", "A saved identity is not held to the typing rules.");

        using (var folder = new Folder())
        {
            var store = new PassportStore(folder.Path);
            using (var first = new PassportPersistence(store, Profile, Clock, new Random(7)))
            {
                check(first.Loaded == LoadOutcome.New && first.Profile.Identity.Issued == new DateOnly(2026, 10, 20), "A new passport is not issued today.");
                check(first.Flush(Wait) && File.Exists(store.PathFor(Profile)), "A new passport is not saved straight away.");
                first.Profile.Update(first.Profile.Identity with { Name = "Wren Halloway", Portrait = PortraitCrop.Centered });
                check(first.Flush(Wait) && first.State == SaveState.Saved, "An identity change is not saved.");
            }
            using (var second = new PassportPersistence(store, Profile, Clock, new Random(99)))
            {
                check(second.Loaded == LoadOutcome.Loaded && second.Profile.Identity.Name == "Wren Halloway" && second.Profile.Identity.Portrait == PortraitCrop.Centered, "A saved identity does not come back.");
                check(second.Profile.Identity.Number == new PassportPersistence(new PassportStore(folder.Path), "other", Clock, new Random(7)).Profile.Identity.Number, "Numbers are not drawn from the given randomness.");
                second.Profile.Update(second.Profile.Identity with { Motto = "Onwards" });
                second.Flush(Wait);
            }
            // The newest save unreadable: the previous one (its backup) comes back and the bad file is kept.
            var number = PassportStore.Parse(File.ReadAllText(store.PathFor(Profile))).Number;
            File.WriteAllText(store.PathFor(Profile), "{ not json");
            using (var third = new PassportPersistence(store, Profile, Clock))
            {
                check(third.Loaded == LoadOutcome.Recovered && third.Profile.Identity.Name == "Wren Halloway" && third.Profile.Identity.Number == number, "An unreadable passport is not recovered from its backup.");
                check(third.Kept.Count == 1 && File.Exists(third.Kept[0]), "The unreadable passport file was not kept.");
            }
            // A newer file is left alone: shown fresh, never saved over.
            var newer = "{\"format\":\"wanderveil.passport\",\"version\":9,\"number\":\"EZ\"}";
            File.WriteAllText(store.PathFor(Profile), newer);
            using (var fourth = new PassportPersistence(store, Profile, Clock))
            {
                fourth.Profile.Update(fourth.Profile.Identity with { Name = "Overwrite?" });
                check(fourth.Loaded == LoadOutcome.Newer && fourth.State == SaveState.ReadOnly && fourth.Flush(Wait), "A newer passport file is not read-only.");
                check(IdentityStorageText.Notice(fourth)?.Contains("newer", StringComparison.Ordinal) == true, "A newer passport file is not explained.");
            }
            check(File.ReadAllText(store.PathFor(Profile)) == newer, "A newer passport file was overwritten.");
            // The picture file is replaced safely and removed with its backup.
            store.SavePicture(Profile, [1, 2, 3]);
            store.SavePicture(Profile, [4, 5]);
            check(store.ReadPicture(Profile) is [4, 5] && File.Exists(store.PicturePath(Profile) + ".bak"), "A picture is not replaced with a backup.");
            store.DeletePicture(Profile);
            check(store.ReadPicture(Profile) == null && !File.Exists(store.PicturePath(Profile) + ".bak"), "A removed picture stays on disk.");
        }
        Console.WriteLine("PASS identity-storage");
    }

    // A fresh passport's number has the printed shape and differs between passports.
    private static void Issue(Action<bool, string> check)
    {
        var random = new Random(3);
        var numbers = Enumerable.Range(0, 20).Select(_ => PassportIdentity.Issue(new DateOnly(2026, 10, 20), random).Number).ToArray();
        check(numbers.All(number => System.Text.RegularExpressions.Regex.IsMatch(number, "^EZ [0-9]{4} [0-9]{4}$")), "An issued number does not look like EZ 0000 0000.");
        check(numbers.Distinct().Count() > 15, "Issued numbers repeat.");
        Console.WriteLine("PASS identity-issue");
    }

    private static void Edit(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample with { Bio = string.Concat(Enumerable.Repeat("A keeper of quiet paths and small stories. ", 9)) });
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile);
        book.Open();
        book.Key(ImGuiKey.PageDown);
        check(book.Shell.VisiblePage == 2, "The identity spread did not open.");
        book.Frames(30);
        var before = book.Shell.FitOf("information");
        book.Click(book.StampControl());
        check(book.Shell.EditingIdentity, "Edit did not start editing the identity.");
        check(book.Shell.PageOverflowX == 0, "An edited field overflows its page sideways.");
        book.Frames(30);
        // The edit fields need a little more room than print (the portrait page fits at about 0.84), never
        // the half size a miscounted page extent once gave.
        check(book.Shell.FitOf("portrait") > 0.75f && book.Shell.FitOf("information") > before - 0.1f, $"Editing shrinks the identity pages ({book.Shell.FitOf("portrait"):0.00}, {book.Shell.FitOf("information"):0.00}).");
        book.Save(artifacts, "identity-edit.bmp");

        // The first field on the information page is the name: click into it, select all, and type over it.
        var page = book.Page(true);
        for (var y = 20f; !book.Shell.TypingIdentity && y < page.Size.Y / 2; y += 6) book.Click(page.Position + new Vector2(page.Size.X * 0.3f, y));
        check(book.Shell.TypingIdentity, "No field took the click.");
        book.SelectAll();
        book.Type("Wren Halloway");
        check(profile.Identity.Name == "Wren Halloway", $"Typing over the name kept '{profile.Identity.Name}'.");
        // Past its limit a field stops taking characters; a long name wraps instead of running off the page.
        book.Type(new string('w', 60));
        check(profile.Identity.Name.Length == PassportIdentity.Limit(IdentityField.Name), $"The name took {profile.Identity.Name.Length} characters.");
        check(book.Shell.PageOverflowX == 0 && book.Shell.PageOverflow == 0, "A long name overflows the page.");
        // A click on the paper beside the fields only leaves the field; it never turns the page.
        book.Click(page.Position + new Vector2(page.Size.X - 4, page.Size.Y - 30));
        check(book.Shell.VisiblePage == 2 && book.Shell.EditingIdentity, "A click on the paper while editing turned the page or stopped editing.");
        // Escape outside a field ends editing and leaves the book open.
        book.Key(ImGuiKey.Escape);
        check(!book.Shell.EditingIdentity && book.Shell.IsBookOpen, "Escape did not just stop editing.");
        // Turning away also ends it.
        book.Click(book.StampControl());
        book.Key(ImGuiKey.PageDown);
        check(!book.Shell.EditingIdentity, "Turning away from the identity pages kept editing.");

        book.Frame();
        ImGui.NewFrame();
        try
        {
            var text = "A keeper of quiet paths and small stories, whose map is mostly margins. Supercalifragilisticexpialidocious!";
            var wrapped = IdentityEditor.Wrap(text, 120);
            check(wrapped.Replace("\n", "", StringComparison.Ordinal) == text, "Soft wrapping changed the text.");
            check(wrapped.Split('\n').All(line => ImGui.CalcTextSize(line.TrimEnd()).X <= 120), "A wrapped line is wider than the field.");
        }
        finally { ImGui.EndFrame(); }
        Console.WriteLine("PASS identity-edit");
    }

    private static void Picture(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Sample);
        FakePortrait? picture = null;
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile, portrait: textures => picture = new FakePortrait(profile, Sketch(textures)));
        book.Open();
        book.Key(ImGuiKey.PageDown);
        book.Click(book.StampControl());
        var page = book.Page(false);
        for (var y = 20f; picture!.Chosen == 0 && y < page.Size.Y; y += 6) book.Click(page.Position + new Vector2(24, y));
        check(picture.Chosen == 1 && profile.Identity.Portrait == PortraitCrop.Centered, "Choose a picture was not offered.");
        book.Frames(2);
        // The holder's picture fills the frame; dragging moves it there and the wheel zooms.
        var frame = page.Position + new Vector2(page.Size.X / 2, page.Size.Y * 0.32f);
        book.Drag(frame, frame + new Vector2(-60, 0));
        check(profile.Identity.Portrait is { } moved && moved.X > 0.5f, "Dragging the picture did not move it.");
        book.Move(frame);
        book.Wheel(3);
        check(profile.Identity.Portrait is { Zoom: > 1.2f }, "Scrolling over the picture did not zoom it.");
        check(book.Shell.EditingIdentity && book.Shell.VisiblePage == 2, "Moving the picture turned or moved the book.");
        book.Save(artifacts, "identity-picture.bmp");
        // The frame row: starting from None, clicking the first word (Printed) chooses it. Probed upward from
        // the signature, so the picture links above the row are never reached.
        profile.Update(profile.Identity with { Frame = PictureFrame.None });
        book.Frames(2);
        for (var y = page.Size.Y * 0.8f; profile.Identity.Frame == PictureFrame.None && y > page.Size.Y * 0.5f; y -= 4) book.Click(page.Position + new Vector2(30, y));
        check(profile.Identity.Frame == PictureFrame.Printed && profile.Identity.Portrait != null, "Clicking a frame did not choose it, or reached the picture links.");
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "The frame row overflows the page.");
        book.Key(ImGuiKey.Escape);
        book.Save(artifacts, "identity-picture-done.bmp");
        // Every frame keeps the picture inside its area and draws without overflow.
        foreach (var style in Enum.GetValues<PictureFrame>())
        {
            profile.Update(profile.Identity with { Frame = style });
            book.Frames(3);
            check(book.Shell.PageOverflow == 0, $"The {style} frame makes the page overflow.");
            book.Save(artifacts, $"identity-frame-{style.ToString().ToLowerInvariant()}.bmp");
        }
        Console.WriteLine("PASS identity-picture");
    }

    // A freshly issued, empty passport, then filled in from the character.
    private static void Character(Action<bool, string> check, string? artifacts)
    {
        var profile = new PassportProfile(PassportIdentity.Issue(new DateOnly(2026, 10, 20), new Random(5)) with { Motto = "Onwards" });
        var source = new FakeCharacter();
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: profile, character: source);
        book.Open();
        book.Key(ImGuiKey.PageDown);
        check(book.Shell.PageOverflow == 0, "An empty passport overflows.");
        book.Save(artifacts, "identity-blank.bmp");
        book.Click(book.StampControl());
        var page = book.Page(true);
        for (var y = 10f; source.Reads == 0 && y < page.Size.Y / 2; y += 4) book.Click(page.Position + new Vector2(10, y));
        book.Frames(2);
        check(source.Reads == 1 && profile.Identity is { Name: "Wren Halloway", HomeWorld: "Faerie", DataCenter: "Light" }, "Fill in from my character did not copy the character.");
        book.Save(artifacts, "identity-filled.bmp");
        check(profile.Identity.Motto == "Onwards", "Filling in from the character changed other fields.");
        Console.WriteLine("PASS identity-character");
    }

    // The cover line is embossed on the closed cover; an empty one leaves the cover as printed.
    private static void Cover(Action<bool, string> check, string? artifacts)
    {
        foreach (var line in new[] { "", "Wren Halloway", "Of the Many Winding Roads Far" })
        {
            var profile = new PassportProfile(PassportIdentity.Sample with { CoverLine = line });
            using var book = new CollectChecks.Book(new Vector2(900, 900), new StampCollection(), identity: profile);
            book.Frames(2);
            var vertices = ImGui.GetDrawData().TotalVtxCount;
            check(vertices > 0, "The cover did not draw.");
            if (line.Length == 13) book.Save(artifacts, "cover-line.bmp");
            if (line.Length > 20) book.Save(artifacts, "cover-line-long.bmp");
        }
        Console.WriteLine("PASS identity-cover");
    }

    // A square stand-in for a holder's drawing: red line work on white running out to the edges, the case
    // where a bare picture meets the paper most harshly.
    private static BookImage Sketch(PreviewTextures textures)
    {
        const int Size = 512;
        var pixels = new byte[Size * Size * 4];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var dx = x - Size / 2f;
                var dy = y - Size * 0.42f;
                var ring = MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - 120) < 3 || MathF.Abs(MathF.Sqrt(dx * dx + (y - Size * 1.1f) * (y - Size * 1.1f)) - 260) < 3;
                var hatch = (x + y) % 23 < 2 && y > Size * 0.7f;
                if (!ring && !hatch) continue;
                var at = (y * Size + x) * 4;
                pixels[at + 1] = pixels[at + 2] = 30;
            }
        textures.Register(9000, Size, Size, pixels);
        return new BookImage(new ImTextureID(9000), Size, Size);
    }

    private static DateTimeOffset Clock() => new(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(2));

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

    private static void Throws<T>(Action<bool, string> check, string text, string what) where T : Exception
    {
        try { PassportStore.Parse(text); }
        catch (T) { check(true, ""); return; }
        catch (Exception exception) { check(false, $"{what} threw {exception.GetType().Name}."); return; }
        check(false, $"{what} was accepted.");
    }

    private sealed class FakePortrait(PassportProfile profile, BookImage image) : IPassportPortrait
    {
        internal int Chosen { get; private set; }
        public PortraitState State => profile.Identity.Portrait != null ? PortraitState.Shown : PortraitState.None;
        public string? Problem => null;

        public bool TryGet(out BookImage shown)
        {
            shown = image;
            return profile.Identity.Portrait != null;
        }

        // As if the picker returned a picture at once.
        public void Choose()
        {
            Chosen++;
            profile.Update(profile.Identity with { Portrait = PortraitCrop.Centered });
        }

        public void Remove() => profile.Update(profile.Identity with { Portrait = null });
    }

    private sealed class FakeCharacter : ICharacterSource
    {
        internal int Reads { get; private set; }

        public Task<CharacterDetails?> Read()
        {
            Reads++;
            return Task.FromResult<CharacterDetails?>(new CharacterDetails("Wren Halloway", "Faerie", "Light"));
        }
    }

    private sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wanderveil-identity-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
        }
    }
}
