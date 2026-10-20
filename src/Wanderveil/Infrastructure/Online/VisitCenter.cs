using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.Infrastructure.Online;

// Someone else's passport as read for a visit: its owner and every part, kept in memory only (ADR-0005: never on
// disk, so revoking access leaves no copies behind).
internal sealed record VisitedPassport(Player Owner, PassportIdentity Identity, StampBook Stamps, MarkBook Marks, byte[]? Picture);

// Opening someone else's passport, and reporting or blocking them from it. Every refusal reads the same, as the server
// answers it: no passport you may open there. Network work runs in the background; the UI takes a passport ready to
// show with Take.
internal sealed class VisitCenter : IDisposable
{
    internal const string NotVisible = "There is no passport you may open there. They may not use Wanderveil, or have not shared their passport with you.";
    private readonly OnlineAccount account;
    private readonly SavedSession session;
    private readonly CancellationTokenSource closing = new();
    private readonly object gate = new();
    private VisitedPassport? ready;
    private int generation;

    internal VisitCenter(OnlineAccount account, SavedSession session)
    {
        this.account = account;
        this.session = session;
    }

    internal bool Opening { get; private set; }
    internal string? Problem { get; private set; }
    // What the last report or block did.
    internal string? Message { get; private set; }
    // Set once a block from a visit has gone through: the visited passport should close.
    internal bool Blocked { get; private set; }

    // A passport ready to show, taken once (UI thread).
    internal VisitedPassport? Take()
    {
        lock (gate)
        {
            var taken = ready;
            ready = null;
            return taken;
        }
    }

    internal void Open(string name, string world)
    {
        var api = account.Api;
        if (api == null) return;
        int ticket;
        lock (gate) ticket = ++generation;
        Opening = true;
        Problem = null;
        Message = null;
        Blocked = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var visited = await Read(api, name.Trim(), world.Trim(), closing.Token).ConfigureAwait(false);
                lock (gate) if (ticket == generation && visited != null) ready = visited;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Problem = "The passport could not be opened: " + exception.Message; }
            finally { lock (gate) if (ticket == generation) Opening = false; }
        });
    }

    internal void Report(Guid owner, string part, string reason)
    {
        var api = account.Api;
        if (api == null) return;
        _ = Task.Run(async () =>
        {
            var result = await api.Report(session.Token, owner, part, reason, null, closing.Token).ConfigureAwait(false);
            if (Refused(result)) return;
            Message = "Reported. Thank you: people at Wanderveil will review it.";
        });
    }

    internal void Block(Guid owner)
    {
        var api = account.Api;
        if (api == null) return;
        _ = Task.Run(async () =>
        {
            var result = await api.Block(session.Token, owner, closing.Token).ConfigureAwait(false);
            if (Refused(result)) return;
            Message = "Blocked. Neither of you can see or ask to see the other's passport.";
            Blocked = true;
        });
    }

    public void Dispose()
    {
        closing.Cancel();
        closing.Dispose();
    }

    // The passport, or null with Problem saying why not.
    private async Task<VisitedPassport?> Read(WanderveilApi api, string name, string world, CancellationToken token)
    {
        var found = await api.LookUp(session.Token, name, world, token).ConfigureAwait(false);
        if (!found.Ok || found.Value is not { } visit) { Refused(found); return null; }
        var owner = visit.Owner.AccountId;
        var identity = await api.VisitDocument(session.Token, owner, "passport", token).ConfigureAwait(false);
        if (!identity.Ok) { Refused(identity); return null; }
        var stamps = await api.VisitDocument(session.Token, owner, "stamps", token).ConfigureAwait(false);
        var marks = await api.VisitDocument(session.Token, owner, "marks", token).ConfigureAwait(false);
        var picture = await api.VisitPicture(session.Token, owner, token).ConfigureAwait(false);
        foreach (var part in new ApiResult<object>[] { Erase(stamps), Erase(marks), Erase(picture) })
            if (!part.Ok && part.Status != 404) { Refused(part); return null; }
        try
        {
            return new VisitedPassport(visit.Owner,
                PassportStore.Parse(identity.Value!.Document.GetRawText()),
                stamps.Value is { } savedStamps ? StampStore.Parse(savedStamps.Document.GetRawText()) : StampBook.Empty,
                marks.Value is { } savedMarks ? MarksStore.Parse(savedMarks.Document.GetRawText()) : MarkBook.Empty,
                picture.Value?.Bytes);
        }
        catch (NotSupportedException) { Problem = "That passport was saved by a newer Wanderveil. Update the plugin to open it."; }
        catch (FormatException) { Problem = "That passport could not be read."; }
        return null;
    }

    private static ApiResult<object> Erase<T>(ApiResult<T> result) => new(null, result.Status, result.Code, result.Detail, result.RetryAfter);

    // True (with Problem set) when the answer is a refusal.
    private bool Refused<T>(ApiResult<T> result)
    {
        account.ReportOffline(result.Offline);
        if (result.Ok) return false;
        if (result.SignedOut) { account.SessionRefused(session); return true; }
        Problem = result.Code == "passport_not_visible" ? NotVisible
            : result.Offline ? "The Wanderveil server cannot be reached right now."
            : OnlineAccount.Explain(result);
        return true;
    }
}
