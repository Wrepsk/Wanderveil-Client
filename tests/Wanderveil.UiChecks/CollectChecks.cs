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
        Large(check);
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

    // More stamps than the spread has places: stamp pages follow it, four places each, with one left empty.
    private static void Full(Action<bool, string> check, string? artifacts)
    {
        check(PassportDocumentView.StampPages(2).Count == 0 && PassportDocumentView.StampPages(3).Count == 2 && PassportDocumentView.StampPages(10).Count == 2 && PassportDocumentView.StampPages(11).Count == 4,
            "Stamp pages are not added in pairs as the collection fills, with an empty place always left.");
        var stamps = Seeded(5);
        using var book = new Book(new Vector2(1200, 800), stamps);
        check(book.Shell.PageCount == PassportDocumentView.SamplePages.Count + 2, "Five stamps do not add one stamp spread.");
        book.Open();
        book.Key(ImGuiKey.PageDown);
        book.Key(ImGuiKey.PageDown);
        book.Frames(4);
        check(book.Shell.VisiblePage == 4, "The collection spread cannot be reached by turning pages.");
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "A full collection spread overflows its paper.");
        book.Save(artifacts, "collect-full-passport.png");
        book.Key(ImGuiKey.PageDown);
        book.Frames(4);
        check(book.Shell.VisiblePage == 6, "The stamp page does not follow the collection spread.");
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "A stamp page overflows its paper.");
        book.Save(artifacts, "collect-stamp-page.png");
        book.Key(ImGuiKey.PageDown);
        check(book.Shell.VisiblePage == 6, "Turning forward escapes the last stamp spread.");

        // A stamp collected once the spread is full lands on the next stamp page, and the link opens it.
        var content = BookShellContent.Sample with { Discoveries = [DiscoveryPreview.Postcard] };
        using var fourth = new Book(new Vector2(1200, 800), Seeded(3), content);
        fourth.Open();
        fourth.Click(fourth.Tab(1));
        fourth.OpenFirstCard();
        fourth.Probe(() => fourth.Shell.Stamps.Stamps.Count > 3);
        check(fourth.Shell.PageCount == PassportDocumentView.SamplePages.Count + 2, "A fourth stamp does not keep its stamp spread.");
        fourth.Probe(() => fourth.Shell.Section == BookSection.Passport || fourth.Shell.SelectedDiscovery == null);
        check(fourth.Shell.Section == BookSection.Passport && fourth.Shell.VisiblePage == 6, "The passport link does not open the page holding the new stamp.");
        fourth.Save(artifacts, "collect-fourth.png");
        Console.WriteLine("PASS collect-full");
    }

    // A representative larger collection: the book grows to fit and End reaches its last page, which costs
    // no more to draw than the first stamp page, since only the visible pages are drawn.
    private static void Large(Action<bool, string> check)
    {
        const int Count = 500;
        using var book = new Book(new Vector2(1200, 800), Seeded(Count));
        var expected = PassportDocumentView.SamplePages.Count + PassportDocumentView.StampPages(Count).Count;
        check(expected == 132, "500 stamps do not need 63 stamp spreads after the collection spread.");
        check(book.Shell.PageCount == expected, $"{Count} stamps give {book.Shell.PageCount} pages, not {expected}.");
        book.Open();
        for (var turn = 0; turn < 3; turn++) book.Key(ImGuiKey.PageDown);
        var first = book.Measure();
        book.Key(ImGuiKey.End);
        book.Frames(4);
        check(book.Shell.VisiblePage == expected - 1 - ((expected - 1) % 2), "End does not reach the last stamp spread.");
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "The last stamp page overflows its paper.");
        var last = book.Measure();
        check(last < first * 3 + 1, $"The last of {expected} pages costs {last:0.00} ms against {first:0.00} ms for the first stamp page.");
        book.Key(ImGuiKey.PageUp);
        check(book.Shell.VisiblePage == expected - 3 - ((expected - 1) % 2), "Page Up does not turn back from the last stamp spread.");
        Console.WriteLine($"INFO collect-large {Count} stamps on {expected} pages: first stamp spread {first:0.00} ms, last {last:0.00} ms per frame (headless CPU)");
        Console.WriteLine("PASS collect-large");
    }

    private static StampCollection Seeded(int count)
    {
        var stamps = new StampCollection(() => Issued);
        for (var index = 0; index < count; index++)
            stamps.Collect($"demo-{index}", new StampDesign($"demo-design-{index}", 1, index == 2 ? "An unusually long stamp name for a quiet evening beyond the last lantern" : $"Demo stamp {index + 1}", index % 3, StampProvenance.LocalDemo));
        return stamps;
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

        // Mean milliseconds per resting frame, after a warm-up.
        internal double Measure()
        {
            ImGui.GetIO().AddMousePosEvent(-100, -100);
            Frames(5);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Frames(30);
            return timer.Elapsed.TotalMilliseconds / 30;
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
