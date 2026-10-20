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
            Page(check, artifacts);
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
        public IReadOnlyList<SharingPerson> Viewers { get; } = [new(Guid.NewGuid(), "Tam Orin", "Phoenix", "xivauth", DateTimeOffset.UtcNow)];
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
    }
}
