using System.Collections.Immutable;
using System.Numerics;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Online;
using Wanderveil.UI;

namespace Wanderveil.UiChecks;

// Invited co-drawing (ADR-0006): invitations through SharingCenter, a guest's marks saved and erased one by one from
// an editable copy, refusals taken back, the owner removing them, and the book drawing others' marks, naming them on
// hover, removing them on the owner's right-click, and taking drawing only on invited pages.
internal static class CoDrawingChecks
{
    private static readonly Rgb Graphite = Rgb.Hex(0x3A3D44);

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wanderveil-codrawing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Format(check);
            Centers(check, directory);
            OwnBook(check, artifacts);
            VisitBook(check, artifacts);
            Ranges(check);
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS co-drawing");
    }

    private static InkStroke Line(int sheet, float y, float from = 0.1f, float to = 0.9f) =>
        new(sheet, Graphite, 0.008f, Enumerable.Range(0, 40).Select(index => new InkPoint(from + (to - from) * index / 39f, y, 0.8f)).ToImmutableArray());

    // A stroke and a line of writing survive the contract's JSON.
    private static void Format(Action<bool, string> check)
    {
        var stroke = Line(3, 0.4f) with { Colour = Rgb.Hex(0xA3302A) };
        var saved = System.Text.Json.JsonDocument.Parse(GuestInkFormat.Save(stroke)).RootElement;
        var author = new Player(Guid.NewGuid(), new CharacterInfo("1", "Tam Orin", "Phoenix", "Light"), "lodestone");
        var back = GuestInkFormat.From(new GuestMark(Guid.NewGuid(), saved.GetProperty("sheet").GetInt32(), author, DateTimeOffset.UtcNow, saved.GetProperty("stroke"), null));
        check(back?.Stroke is { } read && read.Sheet == 3 && read.Colour == stroke.Colour && read.Points.Length == 40 && MathF.Abs(read.Points[10].X - stroke.Points[10].X) < 1e-4f,
            "A guest's stroke did not survive the contract's JSON.");
        var text = new PageText(2, "Hello from Phoenix", 0.2f, 0.3f, 0.05f, Rgb.Hex(0x2F4F8A));
        var written = System.Text.Json.JsonDocument.Parse(GuestInkFormat.Save(text)).RootElement;
        var line = GuestInkFormat.From(new GuestMark(Guid.NewGuid(), 2, author, DateTimeOffset.UtcNow, null, written.GetProperty("text")));
        check(line?.Text == text && line.Sheet == 2, "A guest's line of writing did not survive the contract's JSON.");
        var broken = System.Text.Json.JsonDocument.Parse("""{"colour":"red","width":0.01,"points":[[0,0,1],[1,1,1]]}""").RootElement;
        check(GuestInkFormat.From(new GuestMark(Guid.NewGuid(), 0, author, DateTimeOffset.UtcNow, broken, null)) == null, "A malformed guest mark was taken.");
    }

    private static void Centers(Action<bool, string> check, string directory)
    {
        var server = new OnlineChecks.FakeServer();
        server.Add(1001, "Owner Self", "Ravana");
        server.Add(2002, "Guest Self", "Ravana");
        using var ownerStore = new SessionStore(Path.Combine(directory, "owner"));
        using var guestStore = new SessionStore(Path.Combine(directory, "guest"));
        using var ownerAccount = new OnlineAccount(ownerStore, server.Client(), "0.0.0.1", () => OnlineChecks.FakeServer.Address);
        using var guestAccount = new OnlineAccount(guestStore, server.Client(), "0.0.0.1", () => OnlineChecks.FakeServer.Address);
        ownerAccount.SetCharacter(1, "Owner Self", "Ravana");
        guestAccount.SetCharacter(2, "Guest Self", "Ravana");
        OnlineChecks.SignIn(server, ownerAccount, 1001);
        OnlineChecks.SignIn(server, guestAccount, 2002);
        var ownerId = ownerAccount.Session!.Account.AccountId;
        var guestId = guestAccount.Session!.Account.AccountId;
        using var ownerSharing = new SharingCenter(ownerAccount, ownerAccount.Session!);
        using var guestSharing = new SharingCenter(guestAccount, guestAccount.Session!);
        var wait = TimeSpan.FromSeconds(5);

        // Only a viewer can be invited; then the invitation shows on both sides.
        ownerSharing.LetDraw(guestId, [0, 2]);
        check(ownerSharing.Flush(wait) && ownerSharing.Problem != null && ownerSharing.Snapshot.Grants.Count == 0, "Someone who cannot open the passport was invited to draw.");
        guestSharing.Ask("Owner Self", "Ravana");
        guestSharing.Flush(wait);
        ownerSharing.Refresh();
        ownerSharing.Flush(wait);
        ownerSharing.Approve(ownerSharing.Snapshot.Incoming.Single().RequestId);
        ownerSharing.Flush(wait);
        ownerSharing.LetDraw(guestId, [2, 0]);
        ownerSharing.Flush(wait);
        guestSharing.Refresh();
        check(guestSharing.Flush(wait) && ownerSharing.Snapshot.Grants.Single().Pages.SequenceEqual([0, 2]) &&
              guestSharing.Snapshot.Invitations.Single().Owner.AccountId == ownerId, "An invitation to draw was not listed for its owner and its guest.");

        // The guest's book: the invited pages, and each mark saved as it is made.
        using var guest = new CoDrawingCenter(guestAccount, guestAccount.Session!, ownerId, retry: TimeSpan.FromMilliseconds(50));
        guest.Refresh();
        check(guest.Flush(wait) && guest.Loaded && guest.Pages.SetEquals([0, 2]) && guest.Mine.Strokes.Count == 0, "A guest's center did not read the pages they are invited to.");
        guest.Mine.AddStroke(Line(0, 0.3f));
        guest.Mine.SetText(null, new PageText(2, "Hello!", 0.2f, 0.2f, 0.05f, Graphite));
        check(guest.Flush(wait) && server.GuestMarkCount(ownerId) == 2 && guest.Problem == null, "A guest's stroke and writing were not saved one by one.");

        // A page not invited: refused, taken back, and explained.
        guest.Mine.AddStroke(Line(1, 0.3f));
        check(guest.Flush(wait) && guest.Mine.StrokesOn(1).Count() == 0 && guest.Mine.Strokes.Count == 1 && server.GuestMarkCount(ownerId) == 2 &&
              guest.Problem == "You may no longer draw on that page.", "A mark on a page not invited was kept, or not explained.");

        // Undo erases: the writing goes from the server too.
        guest.Mine.SetText(0, guest.Mine.Texts[0] with { Text = "" });
        check(guest.Flush(wait) && server.GuestMarkCount(ownerId) == 1, "Taking writing away did not erase it on the server.");

        // The eraser splits a stroke: the old mark goes, its two ends come as new marks.
        guest.Mine.Erase(0, new Vector2(0.5f, 0.3f), 0.05f);
        check(guest.Flush(wait) && server.GuestMarkCount(ownerId) == 2 && guest.Mine.Strokes.Count == 2, "Rubbing out the middle of a stroke did not save its two ends in its place.");

        // Offline: the marks wait, then go when the server answers again.
        server.Down = true;
        guest.Mine.AddStroke(Line(2, 0.6f));
        Thread.Sleep(150);
        server.Down = false;
        check(guest.Flush(wait) && server.GuestMarkCount(ownerId) == 3 && guest.Mine.Strokes.Count == 3, "A mark made while offline was not saved once the server answered.");

        // The owner sees them, by author, and removes one; the guest's copy follows.
        using var owner = new CoDrawingCenter(ownerAccount, ownerAccount.Session!, null);
        owner.Refresh();
        check(owner.Flush(wait) && owner.Marks.Count == 3 && owner.Marks.All(mark => mark.Author.Character.Name == "Guest Self"), "The owner does not see guests' marks with their author.");
        var removed = owner.Marks.First(mark => mark.Sheet == 2);
        owner.Remove(removed.Id);
        check(OnlineChecks.Until(() => owner.Marks.Count == 2) && server.GuestMarkCount(ownerId) == 2, "Removing a guest's mark did not reach the server.");
        guest.Refresh();
        check(guest.Flush(wait) && guest.Mine.StrokesOn(2).Count() == 0 && guest.Mine.Strokes.Count == 2, "A guest's copy kept a mark the owner removed.");

        // Opened again (another visit), the guest's copy is read back and can still be erased.
        using (var again = new CoDrawingCenter(guestAccount, guestAccount.Session!, ownerId))
        {
            again.Refresh();
            check(again.Flush(wait) && again.Mine.Strokes.Count == 2, "A guest's own marks were not read back on a new visit.");
            again.Mine.ClearSheet(0);
            check(again.Flush(wait) && server.GuestMarkCount(ownerId) == 0, "A guest's marks read back could not be erased.");
        }

        // Everything one guest made, at once; and ending the invitation.
        guest.Refresh();
        guest.Flush(wait);
        guest.Mine.AddStroke(Line(0, 0.7f));
        guest.Flush(wait);
        owner.Refresh();
        owner.Flush(wait);
        owner.RemoveBy(guestId);
        check(OnlineChecks.Until(() => owner.Marks.Count == 0) && server.GuestMarkCount(ownerId) == 0, "Removing everything a guest made did not reach the server.");
        ownerSharing.StopDrawing(guestId);
        ownerSharing.Flush(wait);
        guestSharing.Refresh();
        check(guestSharing.Flush(wait) && ownerSharing.Snapshot.Grants.Count == 0 && guestSharing.Snapshot.Invitations.Count == 0 &&
              ownerSharing.Message?.StartsWith("They can no longer draw", StringComparison.Ordinal) == true, "Ending an invitation to draw did not take it off both lists.");
        guest.Refresh();
        check(guest.Flush(wait) && guest.Pages.Count == 0, "A guest still had pages to draw on after the invitation ended.");
    }

    // The holder's own book: guests' marks drawn beneath their own, named on hover, removed on a right-click.
    private static void OwnBook(Action<bool, string> check, string? artifacts)
    {
        var layer = new StubLayer(_ => true, remove: true);
        layer.Add(new LayerMark(Guid.NewGuid(), "Tam Orin", Line(0, 0.35f, 0.15f, 0.85f) with { Colour = Rgb.Hex(0x2F4F8A), Width = 0.012f }, null));
        layer.Add(new LayerMark(Guid.NewGuid(), "Tam Orin", null, new PageText(1, "Was here, Phoenix", 0.2f, 0.3f, 0.06f, Rgb.Hex(0xA3302A))));
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), marks: new PageMarks([Line(0, 0.6f)]), layer: layer);
        book.Open();
        book.Shell.GoToPage(book.Shell.FixedPageCount + 1, true);
        for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
        book.Frames(4);
        check(layer.Refreshed > 0, "The stamp pages do not refresh guests' marks.");
        var found = Find(book, false, mark => mark.Author == "Tam Orin");
        check(found != null, "Hovering a guest's stroke does not name its author.");
        book.Save(artifacts, "codrawing-own.bmp", keepMouse: true);
        book.RightClick(found!.Value);
        book.Frames(2);
        check(book.Shell.Drawing.RemoveChoice != null, "A right-click on a guest's mark does not offer removing it.");
        book.Click(book.Shell.Drawing.RemoveChoice!.Value);
        check(layer.Removed.SequenceEqual([layer.Marks[0].Id]), "Removing a guest's mark did not reach the account.");
    }

    // A visited book: the owner's and others' marks beneath, Draw offered on invited pages, and drawing taken only there.
    private static void VisitBook(Action<bool, string> check, string? artifacts)
    {
        var layer = new StubLayer(sheet => sheet == 1, remove: false);
        layer.Add(new LayerMark(Guid.Empty, "Mira Vale", Line(0, 0.4f), null));
        var mine = new PageMarks();
        var visit = new StubVisit();
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), marks: mine, visit: visit, layer: layer);
        book.Open();
        book.Frames(10);
        check(!book.Shell.VisitControls.ContainsKey("Draw"), "Draw is offered on pages not invited.");
        book.Shell.GoToPage(book.Shell.FixedPageCount + 1, true);
        for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
        book.Frames(4);
        check(book.Shell.VisitControls.ContainsKey("Draw"), "Draw is not offered where the visitor is invited.");
        var named = Find(book, false, mark => mark.Author == "Mira Vale");
        check(named != null, "The owner's drawing is not named on hover in a visit.");
        book.RightClick(named!.Value);
        check(book.Shell.Drawing.RemoveChoice == null, "A visitor was offered removing the owner's marks.");
        book.Click(book.Shell.VisitControls["Draw"]);
        check(book.Shell.Drawing.Drawing, "Draw does not start drawing in a visit.");
        var left = book.Page(false);
        var right = book.Page(true);
        book.Drag(left.Position + left.Size * new Vector2(0.2f, 0.2f), left.Position + left.Size * new Vector2(0.8f, 0.25f));
        book.Drag(right.Position + right.Size * new Vector2(0.2f, 0.2f), right.Position + right.Size * new Vector2(0.8f, 0.25f));
        check(mine.StrokesOn(0).Count() == 0 && mine.StrokesOn(1).Count() == 1, $"Drawing was taken on a page not invited, or not on the invited one ({mine.Strokes.Count}).");
        book.Save(artifacts, "codrawing-visit.bmp");
    }

    // Sweeps the mouse down a page (a quarter across) until the book names a mark of the layer there.
    private static Vector2? Find(CollectChecks.Book book, bool right, Func<LayerMark, bool> match)
    {
        var page = book.Page(right);
        for (var y = page.Position.Y + 4; y < page.Max.Y - 4; y += 2)
        {
            var point = new Vector2(page.Position.X + page.Size.X * 0.5f, y);
            book.Move(point);
            if (book.Shell.Drawing.Hovered is { } hovered && match(hovered)) return point;
        }
        return null;
    }

    private static void Ranges(Action<bool, string> check)
    {
        check(PageRanges.Parse("5, 7-9;12", 3, 100) is [5, 7, 8, 9, 12] && PageRanges.Parse("9-7", 3, 100) is [7, 8, 9], "Page lists are not read with ranges.");
        check(PageRanges.Parse("", 3, 100) == null && PageRanges.Parse("2", 3, 100) == null && PageRanges.Parse("101", 3, 100) == null && PageRanges.Parse("five", 3, 100) == null,
            "An empty, unreadable, or out-of-range page list was taken.");
        check(PageRanges.Format([9, 5, 7, 8, 12, 13]) == "5, 7-9, 12, 13", $"Page lists are not written with ranges ({PageRanges.Format([9, 5, 7, 8, 12, 13])}).");
    }

    private sealed class StubLayer(Func<int, bool> mayDraw, bool remove) : IBookShellLayer
    {
        private readonly List<LayerMark> marks = [];
        internal List<Guid> Removed { get; } = [];
        internal int Refreshed { get; private set; }
        internal void Add(LayerMark mark) => marks.Add(mark);
        public IReadOnlyList<LayerMark> Marks => marks;
        public bool MayDraw(int sheet) => mayDraw(sheet);
        public bool MayRemove => remove;
        public void Remove(Guid mark) => Removed.Add(mark);
        public string? Problem => null;
        public void Refresh() => Refreshed++;
    }

    private sealed class StubVisit : IBookShellVisit
    {
        public string Name => "Mira Vale";
        public string World => "Ravana";
        public string VerifiedBy => "lodestone";
        public string? Message => null;
        public void Report(string part, string reason) { }
        public void Block() { }
    }
}
