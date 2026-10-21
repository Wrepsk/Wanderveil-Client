using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wanderveil.Infrastructure.Online;

// Everything the sharing page shows, as last read from the server.
// Grants: who may draw on this passport, and where; Invitations: whose passports this holder may draw on (ADR-0006).
internal sealed record SharingSnapshot(bool Anyone, IReadOnlyList<ViewRequest> Incoming, IReadOnlyList<ViewRequest> Outgoing, IReadOnlyList<ListedPlayer> Viewers, IReadOnlyList<ListedPlayer> Blocks,
    IReadOnlyList<DrawingGrant> Grants, IReadOnlyList<DrawingInvitation> Invitations)
{
    internal static readonly SharingSnapshot Empty = new(false, [], [], [], [], [], []);
}

// The signed-in holder's sharing (ADR-0005): who may open their passport, requests to view it, its viewers, blocks,
// their own requests to view others', and invitations to draw (ADR-0006). Network work runs in the background; the page reads the latest snapshot and
// calls in from the UI thread. Each action refreshes the whole picture afterwards. A refused session signs out.
internal sealed class SharingCenter : IDisposable
{
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);
    private readonly OnlineAccount account;
    private readonly SavedSession session;
    private readonly Func<DateTimeOffset> clock;
    private readonly CancellationTokenSource closing = new();
    private readonly object gate = new();
    private DateTimeOffset? refreshed;
    private int busy;

    internal SharingCenter(OnlineAccount account, SavedSession session, Func<DateTimeOffset>? clock = null)
    {
        this.account = account;
        this.session = session;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal SharingSnapshot Snapshot { get; private set; } = SharingSnapshot.Empty;
    // Whether anything has been read yet.
    internal bool Loaded { get; private set; }
    internal bool Busy => Volatile.Read(ref busy) > 0;
    // Why the last step did not work, in words for the holder; null when nothing went wrong.
    internal string? Problem { get; private set; }
    // What the last step did, when that is worth saying.
    internal string? Message { get; private set; }

    // Reads everything again when the page is open and what it shows is older than half a minute.
    internal void RefreshIfStale()
    {
        DateTimeOffset? last;
        lock (gate) last = refreshed;
        if (!Busy && (last == null || clock() - last > Stale)) Refresh();
    }

    internal void Refresh() => Run(null, (_, _) => Task.FromResult<ApiResult<object>?>(null));

    internal void SetAnyone(bool anyone) => Run(anyone ? "Anyone signed in can now open your passport." : "Only the people you approve can open your passport now.",
        async (api, token) => Erase(await api.PutSharing(session.Token, anyone ? "anyone" : "approved", token).ConfigureAwait(false)));

    internal void Approve(Guid request) => Run("Approved: they can open your passport now.", async (api, token) => await api.Approve(session.Token, request, token).ConfigureAwait(false));
    internal void Decline(Guid request) => Run("Declined. They are not told.", async (api, token) => await api.Decline(session.Token, request, token).ConfigureAwait(false));
    internal void Withdraw(Guid request) => Run("Your request was withdrawn.", async (api, token) => await api.Withdraw(session.Token, request, token).ConfigureAwait(false));
    internal void RemoveViewer(Guid viewer) => Run("Removed: they can no longer open your passport.", async (api, token) => await api.RemoveViewer(session.Token, viewer, token).ConfigureAwait(false));
    internal void Block(Guid player) => Run("Blocked: neither of you can see or ask to see the other's passport.", async (api, token) => await api.Block(session.Token, player, token).ConfigureAwait(false));
    internal void Unblock(Guid player) => Run("Unblocked. Any approval the block removed stays removed.", async (api, token) => await api.Unblock(session.Token, player, token).ConfigureAwait(false));

    // Invites a viewer to draw on these stamp pages (replacing any pages they had), or ends their invitation; their
    // marks stay until removed.
    internal void LetDraw(Guid guest, IReadOnlyList<int> sheets) => Run("They can draw on those pages now, and see who else drew there.",
        async (api, token) => Erase(await api.SetDrawingGrant(session.Token, guest, sheets, token).ConfigureAwait(false)));
    internal void StopDrawing(Guid guest) => Run("They can no longer draw on your passport. What they drew stays until you remove it.",
        async (api, token) => await api.RevokeDrawingGrant(session.Token, guest, token).ConfigureAwait(false));

    // Asks to view a character's passport. The server answers the same whatever becomes of it, so the message says so.
    internal void Ask(string name, string world) =>
        Run($"Asked to see the passport of {name.Trim()} ({world.Trim()}). If they use Wanderveil they will see your request; it waits for their answer, up to 14 days.",
            async (api, token) => await api.AskToView(session.Token, name.Trim(), world.Trim(), token).ConfigureAwait(false));

    public void Dispose()
    {
        closing.Cancel();
        closing.Dispose();
    }

    private static ApiResult<object> Erase<T>(ApiResult<T> result) => new(null, result.Status, result.Code, result.Detail, result.RetryAfter);

    // One action (or none), then everything read again.
    private void Run(string? done, Func<WanderveilApi, CancellationToken, Task<ApiResult<object>?>> action)
    {
        var api = account.Api;
        if (api == null) { Problem = "No Wanderveil server is set."; return; }
        CancellationToken token;
        try { token = closing.Token; }
        catch (ObjectDisposedException) { return; }
        Interlocked.Increment(ref busy);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await action(api, token).ConfigureAwait(false);
                if (result != null && !result.Ok)
                {
                    Report(result);
                    if (result.Offline || result.SignedOut) return;
                }
                else if (result != null) { Problem = null; Message = done; }
                await Load(api, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Problem = "Something went wrong: " + exception.Message; }
            finally { Interlocked.Decrement(ref busy); }
        }, token);
    }

    private async Task Load(WanderveilApi api, CancellationToken token)
    {
        var setting = await api.GetSharing(session.Token, token).ConfigureAwait(false);
        if (!setting.Ok) { Report(setting); return; }
        var incoming = await api.ViewRequests(session.Token, true, token).ConfigureAwait(false);
        var outgoing = await api.ViewRequests(session.Token, false, token).ConfigureAwait(false);
        var viewers = await api.Viewers(session.Token, token).ConfigureAwait(false);
        var blocks = await api.Blocks(session.Token, token).ConfigureAwait(false);
        var grants = await api.DrawingGrants(session.Token, token).ConfigureAwait(false);
        var invitations = await api.DrawingInvitations(session.Token, token).ConfigureAwait(false);
        foreach (var result in new ApiResult<object>[] { Erase(incoming), Erase(outgoing), Erase(viewers), Erase(blocks), Erase(grants), Erase(invitations) })
            if (!result.Ok) { Report(result); return; }
        Snapshot = new SharingSnapshot(setting.Value?.Visibility == "anyone", incoming.Value ?? [], outgoing.Value ?? [], viewers.Value ?? [], blocks.Value ?? [],
            grants.Value ?? [], invitations.Value ?? []);
        Loaded = true;
        account.ReportOffline(false);
        lock (gate) refreshed = clock();
    }

    private void Report<T>(ApiResult<T> result)
    {
        account.ReportOffline(result.Offline);
        if (result.SignedOut) { account.SessionRefused(session); return; }
        Message = null;
        Problem = result.Offline ? "The Wanderveil server cannot be reached right now. Sharing waits until it can." : OnlineAccount.Explain(result);
    }

    // For checks: waits until nothing is in flight.
    internal bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Busy && DateTime.UtcNow < deadline) Thread.Sleep(5);
        return !Busy;
    }
}
