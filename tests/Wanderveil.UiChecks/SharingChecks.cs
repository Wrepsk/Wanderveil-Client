using System.Numerics;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Online;
using Wanderveil.UI;

namespace Wanderveil.UiChecks;

// Sharing (ADR-0005): the client's SharingCenter against the in-memory server, two players at once; and the Sharing
// page in the book's Settings, driven through a stand-in.
internal static class SharingChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wanderveil-sharing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Center(check, directory);
            Visits(check, directory);
            Page(check, artifacts);
            Visitor(check, artifacts);
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS sharing");
    }

    private static void Center(Action<bool, string> check, string directory)
    {
        var server = new OnlineChecks.FakeServer();
        server.Add(1001, "Owner Self", "Ravana");
        server.Add(2002, "Visitor Self", "Ravana");
        using var ownerStore = new SessionStore(Path.Combine(directory, "owner"));
        using var visitorStore = new SessionStore(Path.Combine(directory, "visitor"));
        using var ownerAccount = new OnlineAccount(ownerStore, server.Client(), "0.0.0.1", () => OnlineChecks.FakeServer.Address);
        using var visitorAccount = new OnlineAccount(visitorStore, server.Client(), "0.0.0.1", () => OnlineChecks.FakeServer.Address);
        ownerAccount.SetCharacter(1, "Owner Self", "Ravana");
        visitorAccount.SetCharacter(2, "Visitor Self", "Ravana");
        OnlineChecks.SignIn(server, ownerAccount, 1001);
        OnlineChecks.SignIn(server, visitorAccount, 2002);
        using var owner = new SharingCenter(ownerAccount, ownerAccount.Session!);
        using var visitor = new SharingCenter(visitorAccount, visitorAccount.Session!);

        owner.RefreshIfStale();
        check(owner.Flush(TimeSpan.FromSeconds(5)) && owner.Loaded && !owner.Snapshot.Anyone && owner.Snapshot.Incoming.Count == 0, "Sharing did not load as approved-only and empty.");

        // Asking: the sender is told what the server can say, which is not whether anyone received it.
        visitor.Ask("Owner Self", "Ravana");
        visitor.Ask("No Body", "Ravana");
        check(visitor.Flush(TimeSpan.FromSeconds(5)) && visitor.Problem == null && visitor.Message?.Contains("If they use Wanderveil", StringComparison.Ordinal) == true &&
              visitor.Snapshot.Outgoing.Count == 2 && visitor.Snapshot.Outgoing.All(r => r.State == "pending"), "Asking was not received, or said more than the server knows.");
        owner.Refresh();
        owner.Flush(TimeSpan.FromSeconds(5));
        var asking = owner.Snapshot.Incoming.SingleOrDefault();
        check(asking?.From?.Character.Name == "Visitor Self", "The owner does not see the request from its verified sender.");

        // Approving makes a viewer; the sender sees it approved.
        owner.Approve(asking!.RequestId);
        owner.Flush(TimeSpan.FromSeconds(5));
        visitor.Refresh();
        visitor.Flush(TimeSpan.FromSeconds(5));
        check(owner.Snapshot.Viewers.Single().Character.Name == "Visitor Self" && owner.Snapshot.Incoming.Count == 0 &&
              visitor.Snapshot.Outgoing.Single(r => r.To?.Name == "Owner Self").State == "approved", "Approving did not make a viewer, or the sender did not see it.");

        // Blocking removes the viewer and lists the block; unblocking does not restore the approval.
        var visitorId = owner.Snapshot.Viewers.Single().AccountId;
        owner.Block(visitorId);
        owner.Flush(TimeSpan.FromSeconds(5));
        check(owner.Snapshot.Viewers.Count == 0 && owner.Snapshot.Blocks.Single().AccountId == visitorId && owner.Message?.StartsWith("Blocked", StringComparison.Ordinal) == true, "Blocking did not remove the viewer and list the block.");
        owner.Unblock(visitorId);
        owner.Flush(TimeSpan.FromSeconds(5));
        check(owner.Snapshot.Blocks.Count == 0 && owner.Snapshot.Viewers.Count == 0, "Unblocking did not lift the block, or brought the approval back.");

        // Anyone, and back.
        owner.SetAnyone(true);
        owner.Flush(TimeSpan.FromSeconds(5));
        check(owner.Snapshot.Anyone && owner.Message?.Contains("Anyone signed in", StringComparison.Ordinal) == true, "Sharing with anyone was not set.");
        owner.SetAnyone(false);
        owner.Flush(TimeSpan.FromSeconds(5));
        check(!owner.Snapshot.Anyone, "Sharing back to approved was not set.");

        // Withdrawing one's own request, and refused actions.
        var pending = visitor.Snapshot.Outgoing.Single(r => r.To?.Name == "No Body").RequestId;
        visitor.Withdraw(pending);
        visitor.Flush(TimeSpan.FromSeconds(5));
        check(visitor.Snapshot.Outgoing.All(r => r.RequestId != pending), "Withdrawing did not remove the request.");
        owner.Approve(Guid.NewGuid());
        check(owner.Flush(TimeSpan.FromSeconds(5)) && owner.Problem != null, "A refused action was not explained.");

        // Offline: the page says so and keeps what it had; a refused session signs out.
        server.Down = true;
        owner.Refresh();
        check(owner.Flush(TimeSpan.FromSeconds(5)) && owner.Problem?.Contains("cannot be reached", StringComparison.Ordinal) == true && owner.Loaded, "An unreachable server was not explained, or lost what the page had.");
        server.Down = false;
        server.EndSessions();
        owner.Refresh();
        check(owner.Flush(TimeSpan.FromSeconds(5)) && OnlineChecks.Until(() => ownerAccount.State == AccountState.SignedOut), "A refused session while sharing did not sign out.");
    }

    // Opening someone else's passport: refused alike when it is hidden or no one's, read whole when shared, and
    // reporting and blocking from it.
    private static void Visits(Action<bool, string> check, string directory)
    {
        var server = new OnlineChecks.FakeServer();
        server.Add(1001, "Owner Self", "Ravana");
        server.Add(2002, "Visitor Self", "Ravana");
        using var ownerStore = new SessionStore(Path.Combine(directory, "visit-owner"));
        using var visitorStore = new SessionStore(Path.Combine(directory, "visit-visitor"));
        using var ownerAccount = new OnlineAccount(ownerStore, server.Client(), "0.0.0.1", () => OnlineChecks.FakeServer.Address);
        using var visitorAccount = new OnlineAccount(visitorStore, server.Client(), "0.0.0.1", () => OnlineChecks.FakeServer.Address);
        ownerAccount.SetCharacter(1, "Owner Self", "Ravana");
        visitorAccount.SetCharacter(2, "Visitor Self", "Ravana");
        OnlineChecks.SignIn(server, ownerAccount, 1001);
        OnlineChecks.SignIn(server, visitorAccount, 2002);
        var api = ownerAccount.Api!;
        var token = ownerAccount.Session!.Token;
        var design = new StampDesign("demo-design-1", 1, "Demo stamp", 0, StampProvenance.LocalDemo);
        var stamps = new StampCollection();
        stamps.Place(stamps.Collect("demo-1", design).Stamp!, new StampPlacement(0, 0.5f, 0.5f, 0));
        api.PutPassport(token, Wanderveil.Infrastructure.Storage.PassportStore.Serialize(PassportIdentity.Sample with { Number = "VISIT 0001" }), null, default).Wait();
        api.PutDocument(token, "v1/passport/stamps", Wanderveil.Infrastructure.Storage.StampStore.Serialize(Wanderveil.Infrastructure.Storage.StampBook.Of(stamps)), null, false, default).Wait();
        var picture = new byte[48];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 1, 0, 0, 0, 1, 0 }.CopyTo(picture, 0);
        api.PutPicture(token, picture, "image/png", null, default).Wait();
        using var visits = new VisitCenter(visitorAccount, visitorAccount.Session!);

        bool Open(string name)
        {
            visits.Open(name, "Ravana");
            return OnlineChecks.Until(() => !visits.Opening);
        }
        check(Open("Owner Self") && visits.Problem == VisitCenter.NotVisible && visits.Take() == null, "A passport not shared was opened, or its refusal said more.");
        check(Open("No Body") && visits.Problem == VisitCenter.NotVisible, "A character without Wanderveil was refused differently.");

        using var owner = new SharingCenter(ownerAccount, ownerAccount.Session!);
        owner.SetAnyone(true);
        owner.Flush(TimeSpan.FromSeconds(5));
        check(Open("Owner Self") && visits.Problem == null, $"A shared passport did not open ({visits.Problem}).");
        var visited = visits.Take();
        check(visited?.Identity.Number == "VISIT 0001" && visited.Stamps.Placements.Count == 1 && visited.Marks.Strokes.Count == 0 &&
              visited.Picture?.SequenceEqual(picture) == true && visited.Owner.Character.Name == "Owner Self" && visits.Take() == null,
            "A visited passport did not arrive whole, once (identity, stamps, no drawing, picture).");

        visits.Report(visited!.Owner.AccountId, "picture", "harassment");
        check(OnlineChecks.Until(() => visits.Message?.StartsWith("Reported", StringComparison.Ordinal) == true) &&
              server.Reports.Single() == (visitorAccount.Session!.Account.AccountId, visited.Owner.AccountId, "picture", "harassment"), "Reporting from a visit did not reach the server.");
        visits.Block(visited.Owner.AccountId);
        check(OnlineChecks.Until(() => visits.Blocked) && Open("Owner Self") && visits.Problem == VisitCenter.NotVisible, "Blocking from a visit did not close it to the visitor.");
    }

    // The visited book: read-only (no tabs, settings, editing, stamps to place, or drawing), with Report and Block.
    private static void Visitor(Action<bool, string> check, string? artifacts)
    {
        var visit = new StubVisit();
        var stamps = new StampCollection();
        stamps.Place(stamps.Collect("demo-1", new StampDesign("demo-design-1", 1, "Demo stamp", 0, StampProvenance.LocalDemo, "Wanderveil / Demo curator")).Stamp!, new StampPlacement(0, 0.4f, 0.4f, 0));
        using var book = new CollectChecks.Book(new Vector2(1200, 820), stamps, identity: new PassportProfile(PassportIdentity.Sample), visit: visit);
        book.Open();
        book.Frames(10);
        check(book.Shell.VisitControls.ContainsKey("Report") && book.Shell.VisitControls.ContainsKey("Block"), "A visited passport does not offer Report and Block.");
        book.Save(artifacts, "visit-book.bmp");
        book.Click(book.Tab(1));
        book.Click(book.Shell.VisitControls["Block"] - new Vector2(260, 0));
        book.Click(book.StampControl());
        check(book.Shell.Section == BookSection.Passport && !book.Shell.EditingIdentity, "A visited passport left its pages for another section or settings, or opened its editor.");

        book.Click(book.Shell.VisitControls["Report"]);
        book.Frames(2);
        book.Save(artifacts, "visit-report.bmp");
        book.Click(book.Shell.VisitControls["Report Their picture"]);
        book.Click(book.Shell.VisitControls["Report Harassment"]);
        check(visit.Calls.SequenceEqual(["report picture harassment"]), $"Reporting did not ask what and why ({string.Join(", ", visit.Calls)}).");
        book.Click(book.Shell.VisitControls["Block"]);
        book.Click(book.Shell.VisitControls["Confirm block"]);
        check(visit.Calls.Last() == "block", "Blocking did not ask first, or did not block.");
    }

    private sealed class StubVisit : IBookShellVisit
    {
        internal List<string> Calls { get; } = [];
        public string Name => "Mira Vale";
        public string World => "Ravana";
        public string VerifiedBy => "lodestone";
        public string? Message => null;
        public void Report(string part, string reason) => Calls.Add($"report {part} {reason}");
        public void Block() => Calls.Add("block");
    }

    private static void Page(Action<bool, string> check, string? artifacts)
    {
        var sharing = new StubSharing();
        using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), sharing: sharing);
        book.Shell.ShowSettings();
        book.Frames(30);
        book.Click(book.Tab(4));
        book.Frames(10);
        check(book.Shell.Category == SettingsCategory.Sharing && sharing.Refreshed > 0, "The fifth Settings tab does not open Sharing, or Sharing does not refresh.");
        check(book.Shell.SharingControls.ContainsKey("Mira Vale Approve") && book.Shell.SharingControls.ContainsKey("Tam Orin Remove") &&
              book.Shell.SharingControls.ContainsKey("Ana Lys Withdraw") && book.Shell.SharingControls.ContainsKey("Bad Actor Unblock"), "The sharing lists are not all shown with their actions.");
        book.Save(artifacts, "settings-sharing.bmp");

        book.Click(book.Shell.SharingControls["Mira Vale Approve"]);
        book.Click(book.Shell.SharingControls["Mira Vale Block"]);
        book.Click(book.Shell.SharingControls["Tam Orin Remove"]);
        book.Click(book.Shell.SharingControls["Ana Lys Withdraw"]);
        book.Click(book.Shell.SharingControls["Bad Actor Unblock"]);
        check(sharing.Calls.SequenceEqual(["approve", "block " + StubSharing.Mira, "remove", "withdraw", "unblock"]), $"The sharing actions did not reach the account ({string.Join(", ", sharing.Calls)}).");
        book.Click(book.Shell.SharingControls["Anyone"]);
        check(sharing.Calls.Last() == "anyone True", "Choosing anyone did not change the setting.");
        // Asking: the target fills the fields, then asking sends them.
        book.Click(book.Shell.SharingControls["Use target"]);
        book.Click(book.Shell.SharingControls["Ask"]);
        check(sharing.Calls.Last() == "ask Lux Arden@Sophia", $"Asking from the target did not send its name and world ({sharing.Calls.Last()}).");
        book.Click(book.Shell.SharingControls["Open"]);
        check(sharing.Calls.Last() == "visit Lux Arden@Sophia", "Open it did not open the passport named.");
        book.Click(book.Shell.SharingControls["Kel Doran Open"]);
        check(sharing.Calls.Last() == "visit Kel Doran@Ravana", "An approved request did not offer to open the passport.");

        // Co-drawing (ADR-0006): inviting a viewer to draw on pages named as the book prints them, then changing,
        // ending, and clearing an invitation; and opening a passport one is invited to draw on.
        var first = book.Shell.FixedPageCount + 1;
        book.Click(book.Tab(5));
        book.Frames(10);
        check(book.Shell.Category == SettingsCategory.Drawing && book.Shell.SharingControls.ContainsKey("Kel Doran invitation Open"),
            "The sixth Settings tab does not open Drawing together, or passports one may draw on are not listed.");
        book.Click(book.Shell.SharingControls["Tam Orin Let draw"]);
        book.Frames(4);
        book.Save(artifacts, "settings-drawing-invite.bmp");
        book.Click(book.Shell.SharingControls["Draw pages"]);
        book.Type($"{first}, {first + 2}-{first + 3}");
        book.Click(book.Shell.SharingControls["Let draw"]);
        check(sharing.Calls.Last() == "let draw 0,2,3", $"Letting a viewer draw did not send the pages chosen ({sharing.Calls.Last()}).");
        sharing.Drawers = [new(StubSharing.Tam, "Tam Orin", "Phoenix", [0, 2, 3])];
        book.Frames(2);
        book.Save(artifacts, "settings-drawing.bmp");
        book.Click(book.Shell.SharingControls["Tam Orin Remove marks"]);
        book.Click(book.Shell.SharingControls["Tam Orin Stop"]);
        check(sharing.Calls.TakeLast(2).SequenceEqual(["remove marks", "stop drawing"]), "Removing a guest's marks or ending their invitation did not reach the account.");
        book.Click(book.Shell.SharingControls["Kel Doran invitation Open"]);
        check(sharing.Calls.Last() == "visit Kel Doran@Ravana", "An invitation to draw did not open that passport.");
        sharing.Message = "Asked to see the passport of Lux Arden (Sophia). If they use Wanderveil they will see your request; it waits for their answer, up to 14 days.";
        book.Frames(2);
        book.Save(artifacts, "settings-sharing-asked.bmp");
    }

    private sealed class StubSharing : IBookShellSharing
    {
        internal static readonly Guid Mira = Guid.NewGuid();
        internal List<string> Calls { get; } = [];
        internal int Refreshed { get; private set; }
        public bool Loaded => true;
        public bool Busy => false;
        public string? Problem => null;
        public string? Message { get; set; }
        public bool Anyone => false;
        public IReadOnlyList<SharingRequest> Incoming { get; } = [new(Guid.NewGuid(), Mira, "Mira Vale", "Ravana", false, DateTimeOffset.UtcNow.AddDays(10))];
        public IReadOnlyList<SharingRequest> Outgoing { get; } = [new(Guid.NewGuid(), null, "Ana Lys", "Sophia", false, DateTimeOffset.UtcNow.AddDays(3)), new(Guid.NewGuid(), null, "Kel Doran", "Ravana", true, DateTimeOffset.UtcNow.AddDays(5))];
        public IReadOnlyList<SharingPerson> Viewers { get; } = [new(Tam, "Tam Orin", "Phoenix", "xivauth", DateTimeOffset.UtcNow)];
        public IReadOnlyList<SharingPerson> Blocked { get; } = [new(Guid.NewGuid(), "Bad Actor", "Ravana", "lodestone", DateTimeOffset.UtcNow)];
        public (string Name, string World)? Target => ("Lux Arden", "Sophia");
        public void Refresh() => Refreshed++;
        public void SetAnyone(bool anyone) => Calls.Add("anyone " + anyone);
        public void Approve(Guid request) => Calls.Add("approve");
        public void Decline(Guid request) => Calls.Add("decline");
        public void Withdraw(Guid request) => Calls.Add("withdraw");
        public void RemoveViewer(Guid player) => Calls.Add("remove");
        public void Block(Guid player) => Calls.Add("block " + player);
        public void Unblock(Guid player) => Calls.Add("unblock");
        public void Ask(string name, string world) => Calls.Add($"ask {name}@{world}");
        public bool Opening => false;
        public void Visit(string name, string world) => Calls.Add($"visit {name}@{world}");
        internal static readonly Guid Tam = Guid.NewGuid();
        public IReadOnlyList<DrawingPerson> Drawers { get; set; } = [];
        public IReadOnlyList<DrawingPerson> InvitedBy { get; } = [new(Guid.NewGuid(), "Kel Doran", "Ravana", [0, 1, 2, 7])];
        public void LetDraw(Guid player, IReadOnlyList<int> sheets) => Calls.Add($"let draw {string.Join(",", sheets)}");
        public void StopDrawing(Guid player) => Calls.Add("stop drawing");
        public void RemoveMarksBy(Guid player) => Calls.Add("remove marks");
    }
}
