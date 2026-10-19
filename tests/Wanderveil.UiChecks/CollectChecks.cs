using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// The first product loop: read a discovery, collect its local demo stamp, place it in the passport, and
// arrange it there. Stamp pages are blank and unlimited; stamps sit wherever their owner put them.
internal static class CollectChecks
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 19, 18, 30, 0, TimeSpan.FromHours(2));

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Policy(check);
        foreach (var (name, room) in new[] { ("wide", new Vector2(1200, 800)), ("narrow", new Vector2(452, 760)) })
            Loop(check, artifacts, name, room);
        Spot(check);
        Arranging(check, artifacts);
        Limit(check);
        Escape(check);
        Edges(check);
        foreach (var (name, room) in new[] { ("wide", new Vector2(1200, 800)), ("narrow", new Vector2(452, 760)) }) Jump(check, name, room);
        Holding(check, artifacts);
        ScrolledTurn(check, artifacts);
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

        var spot = stamps.Collect(DiscoveryPreview.StampSpot.Id, DiscoveryPreview.StampSpot.Stamp, new StampSource("STAMP SPOT", "Where the lanterns wait"));
        check(spot.Outcome == CollectOutcome.Collected && spot.Stamp!.Id != stamp.Id && stamps.Stamps[1] == spot.Stamp, "A second discovery's stamp is not kept in collection order.");
        check(spot.Stamp!.Source == new StampSource("STAMP SPOT", "Where the lanterns wait"), "A stamp does not keep what it was collected from.");

        // New stamps wait unplaced; placing puts them on top; moving one lifts it to the top again.
        check(stamps.Unplaced.SequenceEqual([stamp, spot.Stamp]) && stamps.LastSheet == -1, "New stamps do not wait to be placed.");
        check(stamps.Place(stamp, new StampPlacement(4, 0.2f, 0.3f, 0.1f)) && stamps.Place(spot.Stamp, new StampPlacement(4, 0.6f, 0.6f, 0f)), "Valid placements are refused.");
        check(stamps.OnSheet(4).Select(entry => entry.Stamp).SequenceEqual([stamp, spot.Stamp]) && !stamps.Unplaced.Any() && stamps.LastSheet == 4, "Placed stamps are not layered in the order they were put down.");
        check(stamps.Place(stamp, new StampPlacement(9, 1f, 0f, 7f)) && stamps.Placed[^1].Stamp == stamp && stamps.LastSheet == 9 && !stamps.OnSheet(4).Contains(new PlacedStamp(stamp, new StampPlacement(4, 0.2f, 0.3f, 0.1f))), "Moving a stamp does not move it or lift it to the top.");
        check(MathF.Abs(stamps.PlacementOf(stamp)!.Value.Rotation - (7f - MathF.Tau)) < 1e-4f, "A turn past a full circle is not kept between -pi and pi.");
        foreach (var invalid in new[] { new StampPlacement(-1, 0.5f, 0.5f, 0f), new StampPlacement(StampPlacement.MaximumSheet + 1, 0.5f, 0.5f, 0f), new StampPlacement(0, 1.1f, 0.5f, 0f), new StampPlacement(0, 0.5f, float.NaN, 0f) })
            check(!stamps.Place(stamp, invalid) && stamps.PlacementOf(stamp)!.Value.Sheet == 9, "An invalid placement was accepted or moved the stamp.");
        check(!stamps.Place(stamp with { Id = "local-demo-9999" }, new StampPlacement(0, 0.5f, 0.5f, 0f)), "A stamp the collection does not hold was placed.");
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
        var stamp = shell.Stamps.For(DiscoveryPreview.Postcard.Id);
        check(shell.Stamps.Stamps.Count == 1 && stamp != null, name + ": the postcard's Collect action does not collect its stamp.");
        check(stamp!.Source == new StampSource("POSTCARD", DiscoveryPreview.Postcard.Title) && stamp.Design.Creator != null, name + ": a collected stamp does not record its source and designer.");
        check(shell.SelectedDiscovery == DiscoveryPreview.Postcard && shell.Stamps.Unplaced.Single() == stamp, name + ": collecting leaves the postcard or places the stamp by itself.");
        book.Frames(2);
        check(shell.PageOverflowX == 0, name + ": the collected postcard's text overflows sideways.");
        book.Save(artifacts, "collect-" + name + "-collected.png");

        // Its link opens the first stamp page with the stamp in hand; a click on the paper presses it there.
        book.Probe(() => shell.Section == BookSection.Passport || shell.SelectedDiscovery == null);
        check(shell.Section == BookSection.Passport && shell.VisiblePage == shell.FixedPageCount, name + ": the place link does not open the first stamp page.");
        check(shell.Desk.Carrying?.Stamp == stamp && !shell.Desk.Arrange, name + ": the new stamp is not in hand to place.");
        book.Move(book.Spot(false, 0.5f, 0.4f));
        book.Frames(2);
        book.Save(artifacts, "collect-" + name + "-placing.png", keepMouse: true);
        book.Click(book.Spot(false, 0.5f, 0.4f));
        var placed = shell.Stamps.PlacementOf(stamp);
        check(placed is { Sheet: 0 } && MathF.Abs(placed.Value.X - 0.5f) < 0.02f && MathF.Abs(placed.Value.Y - 0.4f) < 0.02f, name + $": the stamp is not pressed where it was clicked ({placed}).");
        check(shell.Desk.Carrying == null && shell.VisiblePage == shell.FixedPageCount && shell.Phase == BookPhase.Open, name + ": placing a stamp also turned or closed the book.");
        book.Frames(3);
        check(shell.PageOverflow == 0 && shell.PageOverflowX == 0, name + ": a stamp page overflows its paper.");
        book.Save(artifacts, "collect-" + name + "-passport.png");

        // A click on the stamp opens its info: on the opposite page in a spread, over its own page on one page.
        book.Click(book.Spot(false, 0.5f, 0.4f));
        check(shell.Desk.Info == stamp && shell.Desk.InfoSide() == false, name + ": clicking a stamp does not open its info.");
        book.Save(artifacts, "collect-" + name + "-info.png");
        book.Key(ImGuiKey.Escape);
        check(shell.Desk.Info == null && shell.Phase == BookPhase.Open, name + ": Escape does not close the stamp info first.");

        // Back in Discover, the postcard holds its one stamp, and its link now shows where it is.
        book.Click(book.Tab(1));
        check(shell.SelectedDiscovery == DiscoveryPreview.Postcard, name + ": returning to Discover loses the open postcard.");
        book.Probe(() => shell.SelectedDiscovery == null || shell.Section != BookSection.Discover);
        check(shell.Stamps.Stamps.Count == 1, name + ": a collected postcard can be collected again.");
        check(shell.Section == BookSection.Passport && shell.VisiblePage == shell.FixedPageCount && shell.Desk.Carrying == null, name + ": the link of a placed stamp does not open its page.");
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
        var storage = new FakeStorage { State = SaveState.Failed };
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
        check(StampStorageText.Count(1, storage).EndsWith("NOT SAVED", StringComparison.Ordinal) && StampStorageText.Count(1, storage with { State = SaveState.Saved }).EndsWith("SAVED ON THIS PC", StringComparison.Ordinal), "The collection caption misstates whether stamps are saved.");
        check(StampStorageText.Notice(new FakeStorage()) == null && StampStorageText.Notice(new FakeStorage { Loaded = LoadOutcome.Recovered }) != null, "Storage notices appear when nothing happened, or not after a recovery.");
        Console.WriteLine("PASS collect-unsaved");
    }

    private sealed record FakeStorage : ISavedState
    {
        public LoadOutcome Loaded { get; init; } = LoadOutcome.Loaded;
        public SaveState State { get; init; } = SaveState.Saved;
        internal int Retries { get; private set; }
        public void Retry() => Retries++;
    }

    // Arrange mode: drag a stamp across the spread, turn it by its knob and by the wheel, carry one across page
    // turns (by key and by resting at the edge), and put one back with Escape.
    private static void Arranging(Action<bool, string> check, string? artifacts)
    {
        var stamps = Seeded(3);
        stamps.Place(stamps.Stamps[0], new StampPlacement(0, 0.3f, 0.3f, 0.2f));
        stamps.Place(stamps.Stamps[1], new StampPlacement(1, 0.4f, 0.8f, -0.3f));
        using var book = new Book(new Vector2(1200, 800), stamps);
        var shell = book.Shell;
        book.Open();
        book.Click(book.Tab(0));
        book.Key(ImGuiKey.End);
        check(shell.VisiblePage == shell.FixedPageCount, "End does not reach the last page with a stamp.");
        var first = stamps.Stamps[0];
        var second = stamps.Stamps[1];

        // Outside Arrange mode a drag on a stamp moves nothing; it opens nothing either.
        book.Drag(book.Spot(false, 0.3f, 0.3f), book.Spot(true, 0.5f, 0.5f));
        check(stamps.PlacementOf(first)!.Value.Sheet == 0 && shell.Desk.Info == null, "A stamp moved, or opened its info, from a drag without Arrange mode.");

        // The footer offers the stamp waiting to be placed before Arrange.
        check(stamps.Unplaced.Count() == 1, "The fixture does not leave one stamp waiting.");
        book.Click(book.StampControl());
        check(shell.Desk.Carrying?.Stamp == stamps.Stamps[2] && shell.VisiblePage == shell.FixedPageCount + 2, "Place stamp does not pick up the waiting stamp on the page after the last stamp.");
        book.Move(book.Spot(false, 0.2f, 0.8f));
        book.Wheel(-3);
        book.Click(book.Spot(false, 0.2f, 0.8f));
        var third = stamps.PlacementOf(stamps.Stamps[2]);
        book.Key(ImGuiKey.PageUp);
        check(third is { Sheet: 2 } && shell.VisiblePage == shell.FixedPageCount && MathF.Abs(third.Value.Rotation - (StampArt.Tilt(stamps.Stamps[2].Id) + 3 * 5f * MathF.PI / 180f)) < 1e-3f, $"The wheel does not turn a carried stamp ({third}).");

        book.Click(book.StampControl());
        check(shell.Desk.Arrange, "The Arrange control does not start arranging.");
        // Drag the first stamp from the left page to the right one.
        book.Drag(book.Spot(false, 0.3f, 0.3f), book.Spot(true, 0.8f, 0.15f));
        var moved = stamps.PlacementOf(first)!.Value;
        check(moved.Sheet == 1 && MathF.Abs(moved.X - 0.8f) < 0.02f && MathF.Abs(moved.Y - 0.15f) < 0.02f && MathF.Abs(moved.Rotation - 0.2f) < 1e-4f && stamps.Placed[^1].Stamp == first,
            $"Dragging a stamp across the spread does not move it there on top, keeping its turn ({moved}).");

        // Turn the second stamp by dragging its knob a quarter turn round.
        var center = book.Spot(true, 0.4f, 0.8f);
        var diameter = book.Page(true).Size.X * BookShellStamps.Size;
        var (sin, cos) = MathF.SinCos(-0.3f);
        var knob = center + new Vector2(sin, -cos) * diameter * 0.62f;
        book.Drag(knob, center + new Vector2(diameter, 0));
        check(MathF.Abs(stamps.PlacementOf(second)!.Value.Rotation - MathF.PI / 2) < 0.05f && stamps.PlacementOf(second)!.Value.Sheet == 1, $"Dragging a knob does not turn its stamp ({stamps.PlacementOf(second)}).");

        // Carry: click to pick up, Page Down twice, then put it down on a later page.
        book.Click(book.Spot(true, 0.8f, 0.15f));
        check(shell.Desk.Carrying?.Stamp == first, "A click in Arrange mode does not pick a stamp up.");
        book.Key(ImGuiKey.PageDown);
        book.Key(ImGuiKey.PageDown);
        check(shell.VisiblePage == shell.FixedPageCount + 4 && shell.Desk.Carrying?.Stamp == first, "Turning pages drops the carried stamp.");
        book.Move(book.Spot(false, 0.5f, 0.5f));
        book.Save(artifacts, "collect-carry.png", keepMouse: true);
        book.Click(book.Spot(false, 0.5f, 0.5f));
        check(stamps.PlacementOf(first)!.Value.Sheet == 4, "A carried stamp is not put down on the page it was carried to.");

        // Resting at the outer edge with a stamp in hand turns the page.
        book.Click(book.Spot(false, 0.5f, 0.5f));
        var edge = book.Layout.Book;
        book.Move(new Vector2(edge.Max.X - edge.Size.X * 0.02f, edge.Position.Y + edge.Size.Y / 2));
        book.Frames(45);
        check(shell.VisiblePage == shell.FixedPageCount + 6 && shell.Desk.Carrying?.Stamp == first, "Resting at the edge with a stamp in hand does not turn the page.");
        // Escape puts it back where it was, on page 4, and keeps the passport open.
        book.Key(ImGuiKey.Escape);
        check(shell.Desk.Carrying == null && stamps.PlacementOf(first)!.Value.Sheet == 4 && shell.Phase == BookPhase.Open, "Escape does not put a carried stamp back.");
        // The × beside the second stamp (a quarter turn from its knob, which now points right) takes it off
        // the page; it waits to be placed again and is still held.
        book.Key(ImGuiKey.PageUp);
        book.Key(ImGuiKey.PageUp);
        book.Key(ImGuiKey.PageUp);
        check(shell.VisiblePage == shell.FixedPageCount, "Page Up does not return to the first stamp spread.");
        var turned = stamps.PlacementOf(second)!.Value.Rotation;
        var (removeSin, removeCos) = MathF.SinCos(turned + MathF.PI / 2);
        book.Save(artifacts, "collect-arrange.png");
        book.Click(center + new Vector2(removeSin, -removeCos) * diameter * 0.62f);
        check(stamps.PlacementOf(second) == null && stamps.Unplaced.Single() == second && stamps.Stamps.Contains(second), "The × does not take its stamp off the page to wait.");
        book.Click(book.StampControl());
        check(shell.Desk.Carrying?.Stamp == second, "A removed stamp is not offered to place again.");
        book.Click(center);
        check(stamps.PlacementOf(second)?.Sheet == shell.VisiblePage - shell.FixedPageCount + 1 && !stamps.Unplaced.Any(), "A removed stamp cannot be placed again on the page after the last stamp.");
        book.Click(book.StampControl());
        check(!shell.Desk.Arrange, "Done does not end Arrange mode.");
        Console.WriteLine("PASS collect-arrange");
    }

    // The passport has 100 pages: turning stops at the last spread, and blank pages store nothing.
    private static void Limit(Action<bool, string> check)
    {
        using var book = new Book(new Vector2(1200, 800), new StampCollection(() => Issued));
        book.Open();
        check(book.Shell.PageCount == BookShell.TotalPages, "The passport does not have 100 pages.");
        for (var turn = 0; turn < 60; turn++) book.Key(ImGuiKey.PageDown);
        check(book.Shell.VisiblePage == BookShell.TotalPages - 2, $"Turning stops at page {book.Shell.VisiblePage + 1}, not at the last spread.");
        check(book.Shell.PageOverflow == 0 && book.Shell.Stamps.Placed.Count == 0, "Blank stamp pages overflow or create placements.");
        Console.WriteLine("PASS collect-limit");
    }

    // A stamp turned round and pushed to any page edge keeps its knob and × on the page in Arrange mode, so
    // the page never grows a scrollbar.
    private static void Edges(Action<bool, string> check)
    {
        var stamps = Seeded(4);
        var corners = new[] { (0f, 0f, 0f), (1f, 1f, MathF.PI), (1f, 0f, MathF.PI / 2), (0f, 1f, -MathF.PI / 2) };
        for (var index = 0; index < corners.Length; index++) stamps.Place(stamps.Stamps[index], new StampPlacement(index % 2, corners[index].Item1, corners[index].Item2, corners[index].Item3));
        foreach (var room in new[] { new Vector2(1200, 800), new Vector2(452, 760) })
        {
            using var book = new Book(room, stamps);
            book.Open();
            book.Key(ImGuiKey.End);
            book.Click(book.StampControl());
            book.Frames(3);
            check(book.Shell.Desk.Arrange && book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, $"A stamp at the page edge makes its page scroll in Arrange mode at {room.X:0} px.");
        }
        Console.WriteLine("PASS collect-edges");
    }

    // Clicking a page number lets you type any page (clamped to 1-100) and Enter goes there; Escape leaves it.
    private static void Jump(Action<bool, string> check, string name, Vector2 room)
    {
        using var book = new Book(room, new StampCollection(() => Issued));
        var shell = book.Shell;
        book.Open();
        void Go(string typed, bool enter = true)
        {
            book.Click(book.Layout.Folio(false));
            // Keyboard focus reaches the field a couple of frames after it appears.
            book.Frames(2);
            check(shell.EnteringPage && ImGui.GetIO().WantTextInput, name + ": clicking a page number does not open it for typing.");
            ImGui.GetIO().AddInputCharacters(typed);
            book.Frames(2);
            book.Key(enter ? ImGuiKey.Enter : ImGuiKey.Escape);
            check(!shell.EnteringPage && shell.Phase == BookPhase.Open, name + ": leaving the page number does not close it, or closes the book.");
        }
        var step = book.Layout.IsSpread ? 2 : 1;
        Go("57");
        check(shell.VisiblePage == 56 - 56 % step, name + $": typing 57 opens page {shell.VisiblePage + 1}.");
        Go("250");
        check(shell.VisiblePage == 99 - 99 % step, name + $": a page past the last opens page {shell.VisiblePage + 1}, not the last.");
        Go("0");
        check(shell.VisiblePage == 0, name + $": page 0 opens page {shell.VisiblePage + 1}, not the first.");
        Go("12", enter: false);
        check(shell.VisiblePage == 0, name + ": Escape in the page number still jumps.");

        // With motion, a jump riffles through the pages in book order, as between sections.
        using var moving = new Book(room, new StampCollection(() => Issued), motion: true);
        moving.Open();
        check(moving.Shell.Phase == BookPhase.Open, name + ": the book with motion does not open.");
        moving.Click(moving.Layout.Folio(false));
        moving.Frames(2);
        ImGui.GetIO().AddInputCharacters("57");
        moving.Frames(2);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, true); moving.Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, false); moving.Frame();
        check(moving.Shell.IsRiffling && moving.Shell.VisiblePage == 56 - 56 % step, name + ": a jump does not riffle to its page.");
        for (var frame = 0; frame < 120 && moving.Shell.IsRiffling; frame++) moving.Frame();
        check(!moving.Shell.IsRiffling && moving.Shell.VisiblePage == 56 - 56 % step, name + ": a jump's riffle does not land on its page.");
        Console.WriteLine("PASS collect-jump-" + name);
    }

    // A page that scrolls (Discover's list) turns away and back with only its visible part on the leaf: what
    // is scrolled out of its window must not appear below the book while the pages riffle (it did, in game).
    private static void ScrolledTurn(Action<bool, string> check, string? artifacts)
    {
        // Enlarged text makes Discover run well past its page, as it does in game.
        using var book = new Book(new Vector2(1200, 1200), new StampCollection(() => Issued), motion: true, textScale: 1.25f);
        var shell = book.Shell;
        book.Open();
        book.Click(book.Tab(1));
        for (var frame = 0; frame < 120 && shell.IsRiffling; frame++) book.Frame();
        book.Frames(3);
        check(shell.Section == BookSection.Discover && book.Layout.IsSpread && shell.PageOverflow > book.Layout.Book.Size.Y * 0.3f, $"Discover overflows by only {shell.PageOverflow:0} px here, or not as a spread, so this check would prove nothing.");
        var bottom = book.Layout.Book.Max.Y + 1;
        check(bottom < 1150, "The book fills the window, leaving no room below it to see content leak.");
        // Riffling forward to Echoes carries Discover's right (scrolling) page away on the first leaf; riffling
        // back brings it in on the last.
        foreach (var tab in new[] { 2, 1 })
        {
            book.Click(book.Tab(tab));
            var frames = 0;
            for (; frames < 120 && shell.IsRiffling; frames++)
            {
                book.Frame();
                var lowest = Book.LowestInk();
                check(lowest <= bottom, $"While riffling {(tab == 2 ? "away from" : "back to")} Discover, page content is drawn {lowest - bottom:0} px below the book.");
                if (tab == 2 && frames == 6) book.Save(artifacts, "states-scrolled-riffle.png", keepMouse: true);
            }
            check(frames > 3, "The riffle away from a scrolled page did not run.");
        }
        Console.WriteLine("PASS scrolled-page-turn");
    }

    // A held stamp stays on the stamp pages: no turning back past the first, no jumping before it, no
    // closing to the cover. And it keeps its size while a page turns beneath it.
    private static void Holding(Action<bool, string> check, string? artifacts)
    {
        var stamps = Seeded(1);
        using var book = new Book(new Vector2(1200, 800), stamps, motion: true);
        var shell = book.Shell;
        book.Open();
        check(shell.Phase == BookPhase.Open, "The book with motion does not open.");
        book.Click(book.StampControl());
        for (var frame = 0; frame < 120 && shell.IsRiffling; frame++) book.Frame();
        check(shell.Desk.Carrying != null && shell.VisiblePage == shell.FixedPageCount, "Place stamp does not open the first stamp page with the stamp in hand.");
        book.Key(ImGuiKey.PageUp);
        for (var frame = 0; frame < 60 && shell.IsTurning; frame++) book.Frame();
        check(shell.VisiblePage == shell.FixedPageCount, "A held stamp lets the book turn back to the identity pages.");
        book.Key(ImGuiKey.Home);
        check(shell.Phase == BookPhase.Open && shell.IsBookOpen, "Home closes the book while a stamp is held.");
        var edge = book.Layout.Book;
        book.Move(new Vector2(edge.Position.X + edge.Size.X * 0.02f, edge.Position.Y + edge.Size.Y / 2));
        book.Frames(45);
        check(shell.VisiblePage == shell.FixedPageCount && !shell.IsTurning, "Resting at the left edge turns back past the first stamp page.");
        check(shell.FirstPageNumber == shell.FixedPageCount + 1, "Jumps are not limited to the stamp pages while a stamp is held.");
        shell.GoToPage(2, true);
        for (var frame = 0; frame < 120 && shell.IsRiffling; frame++) book.Frame();
        check(shell.VisiblePage == shell.FixedPageCount && shell.Desk.Carrying != null, "A jump before the stamp pages leaves them while a stamp is held.");

        // The stamp in hand is the same size before, during, and after a page turn.
        book.Move(book.Spot(false, 0.5f, 0.5f));
        var size = shell.Desk.CarriedDiameter;
        ImGui.GetIO().AddKeyEvent(ImGuiKey.PageDown, true); book.Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.PageDown, false); book.Frame();
        var turned = false;
        for (var frame = 0; frame < 60 && shell.IsTurning; frame++)
        {
            book.Frame();
            turned = true;
            check(MathF.Abs(shell.Desk.CarriedDiameter - size) < 0.5f, $"The stamp in hand grows from {size:0} to {shell.Desk.CarriedDiameter:0} while the page turns.");
            if (frame == 8) book.Save(artifacts, "collect-carry-turning.png", keepMouse: true);
        }
        check(turned && shell.VisiblePage == shell.FixedPageCount + 2 && MathF.Abs(shell.Desk.CarriedDiameter - size) < 0.5f, "A held stamp does not keep its size through a turn.");
        shell.GoToPage(1, true);
        Console.WriteLine("PASS collect-holding");
    }

    // In game the plugin takes Escape from the game and hands it over; the book then ignores ImGui's Escape,
    // says when it wants the key, and acts on the handed-over press.
    private static void Escape(Action<bool, string> check)
    {
        var stamps = Seeded(1);
        using var book = new Book(new Vector2(1200, 800), stamps, readsEscape: false);
        var shell = book.Shell;
        book.Open();
        book.Key(ImGuiKey.End);
        check(shell.WantsEscape, "An open, focused passport does not want Escape.");
        book.Click(book.StampControl());
        check(shell.Desk.Carrying != null && shell.WantsEscape, "A carried stamp does not want Escape.");
        book.Key(ImGuiKey.Escape);
        check(shell.Desk.Carrying != null && shell.Phase == BookPhase.Open, "The book read Escape from ImGui although the plugin hands it over.");
        shell.RequestEscape();
        book.Frames(2);
        check(shell.Desk.Carrying == null && shell.Phase == BookPhase.Open && stamps.Unplaced.Count() == 1, "A handed-over Escape does not put the carried stamp back.");
        shell.RequestEscape();
        book.Frames(2);
        check(shell.Phase != BookPhase.Open, "A handed-over Escape does not close the passport once nothing is held.");
        Console.WriteLine("PASS collect-escape");
    }

    // A representative larger collection: 500 stamps over all 96 stamp pages, five or six on each. End reaches
    // the last, and it costs no more to draw than the first because only the pages on screen are drawn.
    private static void Large(Action<bool, string> check)
    {
        const int Count = 500;
        var stamps = Seeded(Count);
        var sheets = BookShell.TotalPages - PassportDocumentView.SamplePages.Count;
        for (var index = 0; index < Count; index++) stamps.Place(stamps.Stamps[index], new StampPlacement(index % sheets, index % 3 / 2f, index / sheets % 5 / 4f, index * 0.2f));
        using var book = new Book(new Vector2(1200, 800), stamps);
        book.Open();
        book.Key(ImGuiKey.PageDown);
        book.Key(ImGuiKey.PageDown);
        var first = book.Measure();
        book.Key(ImGuiKey.End);
        var last = BookShell.TotalPages - 1;
        check(book.Shell.VisiblePage == last - last % 2, $"End reaches page {book.Shell.VisiblePage + 1}, not the last stamp's.");
        check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "A stamp page overflows its paper.");
        var end = book.Measure();
        check(end < first * 3 + 1, $"The page of the last of {Count} stamps costs {end:0.00} ms against {first:0.00} ms for the first.");
        Console.WriteLine($"INFO collect-large {Count} stamps over {sheets} pages: first stamp spread {first:0.00} ms, last {end:0.00} ms per frame (headless CPU)");
        Console.WriteLine("PASS collect-large");
    }

    private static StampCollection Seeded(int count)
    {
        var stamps = new StampCollection(() => Issued);
        for (var index = 0; index < count; index++)
            stamps.Collect($"demo-{index}", new StampDesign($"demo-design-{index}", 1, $"Demo stamp {index + 1}", index % 3, StampProvenance.LocalDemo, "Wanderveil / Demo curator"), new StampSource("POSTCARD", $"Demo discovery {index + 1}"));
        return stamps;
    }

    // The production shell in a headless window, driven by mouse and keys as the plugin window is.
    internal sealed class Book : IDisposable
    {
        private readonly HeadlessUi host;
        private readonly Vector2 room;
        private readonly UiContext context;
        private UiRect root;
        internal BookShell Shell { get; }

        internal Book(Vector2 room, StampCollection stamps, BookShellContent? content = null, ISavedState? storage = null, bool readsEscape = true, bool motion = false, float textScale = 1f,
            PassportProfile? identity = null, Func<PreviewTextures, IPassportPortrait>? portrait = null, ICharacterSource? character = null)
        {
            this.room = room;
            host = new HeadlessUi(room, materials: true);
            Shell = new BookShell(host.Textures!, PassportIdentity.Sample, content: content, stamps: stamps, storage: storage, identity: identity, portrait: portrait?.Invoke(host.Textures!), character: character) { ReducedMotion = !motion, ReadsEscape = readsEscape };
            context = new UiContext(1, 18 * textScale) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
            Frames(3);
        }

        internal BookShellLayout Layout => BookShellLayout.Create(root, context, Shell.VisiblePage);

        // A page's region on screen: the right page of a spread, or its left (or only) page.
        internal UiRect Page(bool right) => right && Layout.IsSpread ? Layout.RightPage : Layout.LeftPage;

        // Where a stamp centred at (x, y) of a page sits on screen, as the stamp layer places it.
        internal Vector2 Spot(bool right, float x, float y)
        {
            var page = Page(right);
            var diameter = page.Size.X * BookShellStamps.Size;
            return page.Position + new Vector2(diameter / 2) + new Vector2(x, y) * (page.Size - new Vector2(diameter));
        }

        // The stamp control in the footer, just before the size and close marks.
        internal Vector2 StampControl()
        {
            var tools = Layout.Tool(0, context);
            return new Vector2(tools.Position.X - BookShellLayout.Gap(context) - 12, tools.Position.Y + tools.Size.Y / 2);
        }

        internal void Move(Vector2 point) { ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frames(2); }

        internal void Wheel(float notches) { ImGui.GetIO().AddMouseWheelEvent(0, notches); Frames(2); }

        internal void Drag(Vector2 from, Vector2 to)
        {
            Move(from);
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            for (var step = 1; step <= 8; step++) { var point = Vector2.Lerp(from, to, step / 8f); ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame(); }
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(3);
        }
        // Where discoveries are read: the right page of a spread, or the single page.
        private UiRect Reading => Layout.IsSpread ? Layout.RightPage : Layout.LeftPage;

        // As the plugin window does each frame: advance motion, then size and draw the book.
        internal void Frame()
        {
            Shell.Advance(1f / 60f);
            host.BookFrame(Shell.FitSize(room - new Vector2(32), context), () =>
            {
                root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                Shell.Draw(root, context);
                backdrop = BackdropHandle();
            });
        }

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

        // The lowest point the book drew anything visible at last frame (the preview backdrop excluded).
        // The preview backdrop's draw list, which LowestInk leaves out; only readable during a frame.
        private static nint backdrop;
        private static unsafe nint BackdropHandle() => (nint)ImGui.GetBackgroundDrawList().Handle;

        internal static unsafe float LowestInk()
        {
            var data = ImGui.GetDrawData();
            var lowest = float.MinValue;
            for (var listIndex = 0; listIndex < data.CmdListsCount; listIndex++)
            {
                var list = new ImDrawListPtr(data.CmdLists[listIndex]);
                if ((nint)list.Handle == backdrop) continue;
                for (var commandIndex = 0; commandIndex < list.CmdBuffer.Size; commandIndex++)
                {
                    var command = list.CmdBuffer[commandIndex];
                    for (var index = 0u; index + 2 < command.ElemCount; index += 3)
                    {
                        var a = list.VtxBuffer[(int)(command.VtxOffset + list.IdxBuffer[(int)(command.IdxOffset + index)])];
                        var b = list.VtxBuffer[(int)(command.VtxOffset + list.IdxBuffer[(int)(command.IdxOffset + index + 1)])];
                        var c = list.VtxBuffer[(int)(command.VtxOffset + list.IdxBuffer[(int)(command.IdxOffset + index + 2)])];
                        var area = MathF.Abs((b.Pos.X - a.Pos.X) * (c.Pos.Y - a.Pos.Y) - (b.Pos.Y - a.Pos.Y) * (c.Pos.X - a.Pos.X));
                        if (area < 0.01f || (a.Col >> 24) + (b.Col >> 24) + (c.Col >> 24) == 0) continue;
                        // Only what its clip rectangle lets through is drawn.
                        var top = MathF.Min(a.Pos.Y, MathF.Min(b.Pos.Y, c.Pos.Y));
                        if (top >= command.ClipRect.W) continue;
                        lowest = MathF.Max(lowest, MathF.Min(command.ClipRect.W, MathF.Max(a.Pos.Y, MathF.Max(b.Pos.Y, c.Pos.Y))));
                    }
                }
            }
            return lowest;
        }

        // Clicks at point only if a control is under the mouse there (never bare paper, which turns the page).
        internal bool ClickControl(Vector2 point)
        {
            Move(point);
            if (!ImGui.IsAnyItemHovered()) return false;
            Click(point);
            return true;
        }

        internal void Type(string text) { foreach (var character in text) ImGui.GetIO().AddInputCharacter(character); Frames(2); }

        internal void SelectAll()
        {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl, true); ImGui.GetIO().AddKeyEvent(ImGuiKey.A, true); Frame();
            ImGui.GetIO().AddKeyEvent(ImGuiKey.A, false); ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl, false); Frames(2);
        }

        internal void Key(ImGuiKey key) { ImGui.GetIO().AddKeyEvent(key, true); Frame(); ImGui.GetIO().AddKeyEvent(key, false); Frames(2); }

        internal Vector2 Tab(int index) => Layout.Bookmark(index).Position + Layout.Bookmark(index).Size / 2;

        // Opens the cover; with motion, waits for the book to arrive first and for the cover to land after.
        internal void Open()
        {
            for (var frame = 0; frame < 240 && Shell.Phase == BookPhase.Entering; frame++) Frame();
            Click(BookShellLayout.OpenPoint(root));
            for (var frame = 0; frame < 240 && Shell.Phase == BookPhase.Opening; frame++) Frame();
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

        // Moves the mouse away first, unless the picture needs it where it is (a stamp in hand).
        internal void Save(string? artifacts, string name, bool keepMouse = false)
        {
            if (artifacts == null) return;
            if (!keepMouse) ImGui.GetIO().AddMousePosEvent(-100, -100);
            Frames(2);
            SoftwarePreview.Save(Path.Combine(artifacts, name), host);
        }

        public void Dispose() => host.Dispose();
    }
}
