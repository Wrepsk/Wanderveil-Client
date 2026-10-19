using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// The first product loop: read a discovery, collect its local demo stamp, and find it in the passport.
internal static class CollectChecks
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 19, 18, 30, 0, TimeSpan.FromHours(2));

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Policy(check);
        foreach (var (name, room) in new[] { ("wide", new Vector2(1200, 800)), ("narrow", new Vector2(452, 760)) })
            Loop(check, artifacts, name, room);
        Spot(check);
        Full(check, artifacts);
        Unsaved(check);
    }

    // Duplicate policy and identity, with no ImGui involved.
    private static void Policy(Action<bool, string> check)
    {
        var stamps = new StampCollection(() => Issued);
        var postcard = DiscoveryPreview.Postcard;
        check(stamps.Stamps.Count == 0 && stamps.For(postcard.Id) == null, "A new collection is not empty.");
        var first = stamps.Collect(postcard.Id, postcard.Stamp);
        check(first.Outcome == CollectOutcome.Collected && stamps.Stamps.Count == 1, "Collecting a discovery's stamp does not add it.");
        var stamp = first.Stamp!;
        check(stamp.DiscoveryId == postcard.Id && stamp.Design == postcard.Stamp && stamp.Provenance == StampProvenance.LocalDemo, "An issued stamp does not record its discovery, design, and demo provenance.");
        check(stamp.Id != stamp.Design.Id && stamp.CollectedAt == Issued, "An issued stamp shares its design's identifier or loses its date.");
        check(StampArt.Date(stamp.CollectedAt) == "19 OCT 2026", "The issue date is not printed in the offset it was collected in.");

        // Collecting again, even after the design is revised, keeps the one stamp already issued.
        var again = stamps.Collect(postcard.Id, postcard.Stamp! with { Revision = 2, Name = "Scenic route, revised" });
        check(again.Outcome == CollectOutcome.AlreadyCollected && ReferenceEquals(again.Stamp, stamp) && stamps.Stamps.Count == 1, "Collecting twice issues a duplicate.");
        check(stamps.Stamps[0].Design.Revision == 1, "A revised design changes a stamp already issued.");

        foreach (var (id, design) in new (string, StampDesign?)[] { ("", postcard.Stamp), ("   ", postcard.Stamp), ("demo-other", null), ("demo-other", postcard.Stamp! with { Id = "" }), ("demo-other", postcard.Stamp! with { Revision = 0 }) })
            check(stamps.Collect(id, design).Outcome == CollectOutcome.Unavailable && stamps.Stamps.Count == 1, "An invalid discovery or design was collected.");

        var spot = stamps.Collect(DiscoveryPreview.StampSpot.Id, DiscoveryPreview.StampSpot.Stamp);
        check(spot.Outcome == CollectOutcome.Collected && spot.Stamp!.Id != stamp.Id && stamps.Stamps[1] == spot.Stamp, "A second discovery's stamp is not kept in collection order.");
        Console.WriteLine("PASS collect-policy");
    }

    private static void Loop(Action<bool, string> check, string? artifacts, string name, Vector2 room)
    {
        using var book = new Book(room, new StampCollection(() => Issued));
        var shell = book.Shell;
        book.Open();
        book.Click(book.Tab(1));
        check(shell.Section == BookSection.Discover, name + ": the Discover tab does not open Discover.");

        // Open the postcard (the first card) and collect its stamp from the foot of its page.
        book.OpenFirstCard();
        check(shell.SelectedDiscovery == DiscoveryPreview.Postcard, name + ": the postcard cannot be opened from its card.");
        book.Probe(() => shell.Stamps.Stamps.Count > 0 || shell.SelectedDiscovery == null);
        check(shell.Stamps.Stamps.Count == 1 && shell.Stamps.For(DiscoveryPreview.Postcard.Id) != null, name + ": the postcard's Collect action does not collect its stamp.");
        check(shell.SelectedDiscovery == DiscoveryPreview.Postcard, name + ": collecting leaves the postcard.");
        book.Frames(2);
        check(shell.PageOverflowX == 0, name + ": the collected postcard's text overflows sideways.");
        book.Save(artifacts, "collect-" + name + "-collected.png");

        // The postcard now offers its passport page; following it lands on the collection spread.
        book.Probe(() => shell.Section == BookSection.Passport || shell.SelectedDiscovery == null);
        check(shell.Section == BookSection.Passport, name + ": the collected postcard does not lead to the passport.");
        var collection = PassportDocumentView.SamplePages.ToList().FindIndex(page => page.Kind == PassportPageKind.CollectionLeft);
        check(shell.VisiblePage == collection, name + ": the passport does not open on its collection page.");
        check(shell.Stamps.Stamps.Count == 1, name + ": visiting the passport changes the collection.");
        book.Frames(3);
        if (book.Layout.IsSpread) check(shell.PageOverflow == 0 && shell.PageOverflowX == 0, name + ": the collection spread overflows its paper.");
        book.Save(artifacts, "collect-" + name + "-passport.png");

        // Back in Discover, the postcard still holds its one stamp: there is nothing left to collect.
        book.Click(book.Tab(1));
        check(shell.SelectedDiscovery == DiscoveryPreview.Postcard, name + ": returning to Discover loses the open postcard.");
        book.Probe(() => shell.SelectedDiscovery == null || shell.Section != BookSection.Discover);
        check(shell.Stamps.Stamps.Count == 1, name + ": a collected postcard can be collected again.");
        Console.WriteLine("PASS collect-" + name);
    }

    // A stamp spot is collected on site, which Wanderveil cannot detect yet: its action stays inert.
    private static void Spot(Action<bool, string> check)
    {
        var content = BookShellContent.Sample with { Discoveries = [DiscoveryPreview.StampSpot] };
        using var book = new Book(new Vector2(1200, 800), new StampCollection(() => Issued), content);
        book.Open();
        book.Click(book.Tab(1));
        book.OpenFirstCard();
        check(book.Shell.SelectedDiscovery == DiscoveryPreview.StampSpot, "The stamp spot cannot be opened from its card.");
        book.Probe(() => book.Shell.SelectedDiscovery == null);
        check(book.Shell.Stamps.Stamps.Count == 0, "A stamp spot's stamp was collected without being on site.");
        Console.WriteLine("PASS collect-stamp-spot");
    }

    // A save that failed says so under the stamp and offers to try again; the stamp stays collected.
    private static void Unsaved(Action<bool, string> check)
    {
        var storage = new FakeStorage { State = StampSaveState.Failed };
        using var book = new Book(new Vector2(1200, 800), new StampCollection(() => Issued), storage: storage);
        book.Open();
        book.Click(book.Tab(1));
        book.OpenFirstCard();
        book.Probe(() => book.Shell.Stamps.Stamps.Count > 0);
        check(book.Shell.Stamps.Stamps.Count == 1, "A stamp cannot be collected while saving fails.");
        // The passport link sits below the retry link: if the probe reaches it first, come back and go on above it.
        float? start = null;
        for (var pass = 0; pass < 12 && storage.Retries == 0; pass++)
        {
            if (book.Shell.Section != BookSection.Discover) book.Click(book.Tab(1));
            start = book.Probe(() => storage.Retries > 0 || book.Shell.Section != BookSection.Discover, start - 6);
        }
        check(storage.Retries == 1, "A failed save offers no way to try again.");
        check(StampStorageText.Count(1, storage).EndsWith("NOT SAVED", StringComparison.Ordinal) && StampStorageText.Count(1, storage with { State = StampSaveState.Saved }).EndsWith("SAVED ON THIS PC", StringComparison.Ordinal), "The collection caption misstates whether stamps are saved.");
        check(StampStorageText.Notice(new FakeStorage()) == null && StampStorageText.Notice(new FakeStorage { Loaded = StampLoadOutcome.Recovered }) != null, "Storage notices appear when nothing happened, or not after a recovery.");
        Console.WriteLine("PASS collect-unsaved");
    }

    private sealed record FakeStorage : IStampStorage
    {
        public StampLoadOutcome Loaded { get; init; } = StampLoadOutcome.Loaded;
        public StampSaveState State { get; init; } = StampSaveState.Saved;
        internal int Retries { get; private set; }
        public void Retry() => Retries++;
    }

    // More stamps than the spread has places: the first three are printed and the rest are counted.
    private static void Full(Action<bool, string> check, string? artifacts)
    {
        var stamps = new StampCollection(() => Issued);
        for (var index = 0; index < 5; index++)
            stamps.Collect($"demo-{index}", new StampDesign($"demo-design-{index}", 1, index == 2 ? "An unusually long stamp name for a quiet evening beyond the last lantern" : $"Demo stamp {index + 1}", index % 4, StampProvenance.LocalDemo));
        check(stamps.Stamps.Count == 5, "Five different discoveries do not give five stamps.");
        using var book = new Book(new Vector2(1200, 800), stamps);
        book.Open();
        book.Key(ImGuiKey.PageDown);
        book.Key(ImGuiKey.PageDown);
        book.Frames(4);
        check(book.Shell.VisiblePage == 4, "The collection spread cannot be reached by turning pages.");
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "A full collection spread overflows its paper.");
        book.Save(artifacts, "collect-full-passport.png");
        Console.WriteLine("PASS collect-full");
    }

    // The production shell in a headless window, driven by mouse and keys as the plugin window is.
    private sealed class Book : IDisposable
    {
        private readonly HeadlessUi host;
        private readonly Vector2 room;
        private readonly UiContext context;
        private UiRect root;
        internal BookShell Shell { get; }

        internal Book(Vector2 room, StampCollection stamps, BookShellContent? content = null, IStampStorage? storage = null)
        {
            this.room = room;
            host = new HeadlessUi(room, materials: true);
            Shell = new BookShell(host.Textures!, PassportPreview.Sample, content: content, stamps: stamps, storage: storage) { ReducedMotion = true };
            context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            Frames(3);
        }

        internal BookShellLayout Layout => BookShellLayout.Create(root, context, 0);
        // Where discoveries are read: the right page of a spread, or the single page.
        private UiRect Reading => Layout.IsSpread ? Layout.RightPage : Layout.LeftPage;

        internal void Frame() => host.BookFrame(Shell.FitSize(room - new Vector2(32), context), () =>
        {
            root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            Shell.Draw(root, context);
        });

        internal void Frames(int count) { for (var index = 0; index < count; index++) Frame(); }

        internal void Click(Vector2 point)
        {
            ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(2);
        }

        internal void Key(ImGuiKey key) { ImGui.GetIO().AddKeyEvent(key, true); Frame(); ImGui.GetIO().AddKeyEvent(key, false); Frames(2); }

        internal Vector2 Tab(int index) => Layout.Bookmark(index).Position + Layout.Bookmark(index).Size / 2;

        internal void Open()
        {
            Click(BookShellLayout.OpenPoint(root));
            Frames(2);
        }

        internal void OpenFirstCard()
        {
            for (var y = 60f; Shell.SelectedDiscovery == null && y < Reading.Size.Y; y += 24)
                Click(Reading.Position + new Vector2(Reading.Size.X * 0.3f, y));
        }

        // Scrolls the reading page to its end, then clicks up its left edge (from start, if given) until
        // done() holds; returns the height it stopped at.
        internal float Probe(Func<bool> done, float? start = null)
        {
            var page = Reading;
            var middle = page.Position + page.Size / 2;
            ImGui.GetIO().AddMousePosEvent(middle.X, middle.Y); Frame();
            ImGui.GetIO().AddMouseWheelEvent(0, -40); Frames(2);
            var y = start ?? page.Size.Y - 12;
            for (; !done() && y > 12; y -= 6)
                Click(page.Position + new Vector2(22, y));
            return y + 6;
        }

        internal void Save(string? artifacts, string name)
        {
            if (artifacts == null) return;
            ImGui.GetIO().AddMousePosEvent(-100, -100);
            Frames(2);
            SoftwarePreview.Save(Path.Combine(artifacts, name), host);
        }

        public void Dispose() => host.Dispose();
    }
}
