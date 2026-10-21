using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wanderveil.Features.Drawing;

namespace Wanderveil.Infrastructure.Online;

// Guests' marks on one passport (ADR-0006). On the holder's own passport (Owner null): every guest's marks, and
// removing them. On a visited passport: everyone's guest marks, the pages this account is invited to, and Mine, this
// account's own marks there as an editable PageMarks whose changes become saves and erasures, one mark each. The
// server's word wins: a mark it refuses (no longer invited, over a limit) is taken back here, with the reason.
// Network work runs in the background; the book reads the latest state each frame, and Poll (on the UI thread, which
// alone changes Mine) applies what the server said about Mine.
internal sealed class CoDrawingCenter : IDisposable
{
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(20);
    private readonly OnlineAccount account;
    private readonly SavedSession session;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan retry;
    private readonly CancellationTokenSource closing = new();
    private readonly object gate = new();
    // Each of Mine's strokes and lines of writing (by reference) and its mark ID.
    private readonly Dictionary<object, Guid> known = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<(bool Add, Guid Id, string? Body)> outbox = new();
    private bool sending;
    private bool applying;
    private bool loadedMine;
    // What the server last said this account's marks are (and when it was asked), and marks to drop (refused, or
    // removed by the owner): applied to Mine by Poll. Saves are counted on the same clock, so a mark saved while a
    // read was on its way is not taken for one removed.
    private (List<GuestInk> Marks, long Asked)? reported;
    private readonly HashSet<Guid> dropped = [];
    private readonly Dictionary<Guid, long> saved = [];
    private long tick;
    private DateTimeOffset? refreshed;
    private int reading;

    internal CoDrawingCenter(OnlineAccount account, SavedSession session, Guid? owner, Func<DateTimeOffset>? clock = null, TimeSpan? retry = null)
    {
        this.account = account;
        this.session = session;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.retry = retry ?? TimeSpan.FromSeconds(10);
        Owner = owner;
        if (owner != null) Mine.Changed += MineChanged;
    }

    // The visited passport's owner, or null for the holder's own.
    internal Guid? Owner { get; }
    internal IReadOnlyList<GuestInk> Marks { get; private set; } = [];
    // The stamp pages this account may draw on (visited passports only).
    internal IReadOnlySet<int> Pages { get; private set; } = new HashSet<int>();
    internal PageMarks Mine { get; } = new();
    internal string? Problem { get; private set; }
    internal bool Loaded { get; private set; }
    // Whether Mine holds this account's marks as read from the server (before then, it takes no drawing).
    internal bool Ready { get { lock (gate) return loadedMine; } }
    internal Guid Me => session.Account.AccountId;

    internal void RefreshIfStale()
    {
        DateTimeOffset? last;
        lock (gate) last = refreshed;
        if (Volatile.Read(ref reading) == 0 && (last == null || clock() - last > Stale)) Refresh();
    }

    internal void Refresh()
    {
        var api = account.Api;
        if (api == null) return;
        Interlocked.Increment(ref reading);
        long asked;
        lock (gate) asked = ++tick;
        _ = Task.Run(async () =>
        {
            try
            {
                var marks = await api.GuestMarks(session.Token, Owner, closing.Token).ConfigureAwait(false);
                if (Refused(marks)) return;
                var ink = (marks.Value ?? []).Select(GuestInkFormat.From).OfType<GuestInk>().ToList();
                HashSet<int>? pages = null;
                if (Owner is { } owner)
                {
                    var invitations = await api.DrawingInvitations(session.Token, closing.Token).ConfigureAwait(false);
                    if (Refused(invitations)) return;
                    pages = (invitations.Value ?? []).Where(i => i.Owner.AccountId == owner).SelectMany(i => i.Pages).ToHashSet();
                }
                lock (gate)
                {
                    Marks = ink;
                    if (pages != null) Pages = pages;
                    refreshed = clock();
                    Loaded = true;
                }
                if (Owner != null) lock (gate) reported = (ink.Where(mark => mark.Author.AccountId == Me).ToList(), asked);
                account.ReportOffline(false);
            }
            catch (OperationCanceledException) { }
            finally { Interlocked.Decrement(ref reading); }
        });
    }

    // The holder removes one guest mark from their own passport, or everything one guest made.
    internal void Remove(Guid mark) => Act(api => api.RemoveGuestMark(session.Token, mark, closing.Token), () => Marks = Marks.Where(m => m.Id != mark).ToList());
    internal void RemoveBy(Guid author) => Act(api => api.RemoveGuestMarksBy(session.Token, author, closing.Token), () => Marks = Marks.Where(m => m.Author.AccountId != author).ToList());

    // A mark announced live (ADR-0006), taken before the next read.
    internal void Announced(GuestInk mark)
    {
        lock (gate) if (Marks.All(m => m.Id != mark.Id)) Marks = [.. Marks, mark];
    }

    internal void Withdrawn(Guid mark)
    {
        lock (gate)
        {
            Marks = Marks.Where(m => m.Id != mark).ToList();
            if (Owner != null) dropped.Add(mark);
        }
    }

    // On the UI thread, each frame: Mine takes what the server said (marks the owner removed, or refused ones).
    internal void Poll()
    {
        lock (gate)
        {
            if (reported is { } mine) { reported = null; Reconcile(mine.Marks, mine.Asked); }
            if (dropped.Count == 0) return;
            Take(known.Where(entry => dropped.Contains(entry.Value)).Select(entry => entry.Key).ToHashSet(ReferenceEqualityComparer.Instance));
            dropped.Clear();
        }
    }

    // For checks, on the thread that stands for the UI: waits until nothing is in flight, applying what arrives.
    internal bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Poll();
            bool idle;
            lock (gate) idle = !sending && outbox.Count == 0 && Volatile.Read(ref reading) == 0 && reported == null && dropped.Count == 0;
            if (idle) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    public void Dispose()
    {
        if (Owner != null) Mine.Changed -= MineChanged;
        closing.Cancel();
        closing.Dispose();
    }

    private void Act(Func<WanderveilApi, Task<ApiResult<object>>> call, Action done)
    {
        var api = account.Api;
        if (api == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await call(api).ConfigureAwait(false);
                if (Refused(result)) return;
                lock (gate) done();
                Problem = null;
            }
            catch (OperationCanceledException) { }
        });
    }

    // Under gate, on the UI thread. Mine as the server has it: on first read, Mine becomes this account's marks there; afterwards, marks the server no
    // longer has (removed by the owner) leave Mine too. Marks waiting to be sent stay.
    private void Reconcile(IReadOnlyList<GuestInk> mine, long asked)
    {
        if (!loadedMine)
        {
            loadedMine = true;
            applying = true;
            try
            {
                Mine.Replace(mine.Select(m => m.Stroke).OfType<InkStroke>(), mine.Select(m => m.Text).OfType<PageText>());
                known.Clear();
                // Mine keeps what it was given in order, so pair them back up the same way.
                foreach (var (ink, mark) in Mine.Strokes.Zip(mine.Where(m => m.Stroke != null))) known[ink] = mark.Id;
                foreach (var (ink, mark) in Mine.Texts.Zip(mine.Where(m => m.Text != null))) known[ink] = mark.Id;
            }
            finally { applying = false; }
            return;
        }
        var kept = mine.Select(m => m.Id).ToHashSet();
        var pending = outbox.Where(item => item.Add).Select(item => item.Id).ToHashSet();
        var gone = known.Where(entry => !kept.Contains(entry.Value) && !pending.Contains(entry.Value) && !(saved.TryGetValue(entry.Value, out var at) && at > asked)).Select(entry => entry.Key).ToHashSet(ReferenceEqualityComparer.Instance);
        if (gone.Count == 0) return;
        Take(gone);
    }

    // Under gate, on the UI thread: takes these out of Mine without sending anything.
    private void Take(ISet<object> gone)
    {
        if (gone.Count == 0) return;
        applying = true;
        try
        {
            var strokes = Mine.Strokes.Where(s => !gone.Contains(s)).ToList();
            var texts = Mine.Texts.Where(t => !gone.Contains(t)).ToList();
            var strokeIds = strokes.Select(s => known.TryGetValue(s, out var id) ? id : (Guid?)null).ToList();
            var textIds = texts.Select(t => known.TryGetValue(t, out var id) ? id : (Guid?)null).ToList();
            // Replace makes the records again, in the same order, so pair them back up by position.
            Mine.Replace(strokes, texts);
            known.Clear();
            foreach (var (ink, id) in Mine.Strokes.Zip(strokeIds)) if (id is { } kept) known[ink] = kept;
            foreach (var (ink, id) in Mine.Texts.Zip(textIds)) if (id is { } kept) known[ink] = kept;
        }
        finally { applying = false; }
    }

    // On the UI thread: what Mine gained becomes saves, what it lost becomes erasures.
    private void MineChanged()
    {
        lock (gate)
        {
            if (applying) return;
            var now = new HashSet<object>(Mine.Strokes.Cast<object>().Concat(Mine.Texts), ReferenceEqualityComparer.Instance);
            foreach (var (ink, id) in known.Where(entry => !now.Contains(entry.Key)).ToList())
            {
                known.Remove(ink);
                outbox.Enqueue((false, id, null));
            }
            foreach (var ink in now.Where(ink => !known.ContainsKey(ink)))
            {
                var id = Guid.NewGuid();
                known[ink] = id;
                outbox.Enqueue((true, id, ink is InkStroke stroke ? GuestInkFormat.Save(stroke) : GuestInkFormat.Save((PageText)ink)));
            }
            if (sending || outbox.Count == 0) return;
            sending = true;
        }
        _ = Task.Run(Send);
    }

    // One at a time, in order. No answer: wait and try again. A refusal: take the mark back here, and say why.
    private async Task Send()
    {
        var api = account.Api;
        while (true)
        {
            (bool Add, Guid Id, string? Body) next;
            lock (gate)
            {
                if (outbox.Count == 0 || api == null || Owner == null) { sending = false; return; }
                next = outbox.Peek();
            }
            ApiResult<object> result;
            try
            {
                result = next.Add
                    ? AsObject(await api.AddGuestMark(session.Token, Owner!.Value, next.Id, next.Body!, closing.Token).ConfigureAwait(false))
                    : await api.EraseGuestMark(session.Token, Owner!.Value, next.Id, closing.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { lock (gate) sending = false; return; }
            if (result.Offline || result.Status is 429 or >= 500)
            {
                account.ReportOffline(result.Offline);
                try { await Task.Delay(retry, closing.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { lock (gate) sending = false; return; }
                continue;
            }
            lock (gate)
            {
                outbox.Dequeue();
                if (next.Add && result.Ok) saved[next.Id] = ++tick;
            }
            if (result.SignedOut) { account.SessionRefused(session); lock (gate) sending = false; return; }
            if (result.Ok || !next.Add && result.Status == 404) { Problem = null; continue; }
            Problem = result.Code == "drawing_not_allowed" ? "You may no longer draw on that page." : OnlineAccount.Explain(result);
            if (next.Add) lock (gate) dropped.Add(next.Id);
        }
    }

    private static ApiResult<object> AsObject<T>(ApiResult<T> result) => new(null, result.Status, result.Code, result.Detail, result.RetryAfter);

    private bool Refused<T>(ApiResult<T> result)
    {
        account.ReportOffline(result.Offline);
        if (result.Ok) return false;
        if (result.SignedOut) { account.SessionRefused(session); return true; }
        Problem = result.Code == "passport_not_visible" ? VisitCenter.NotVisible : result.Offline ? "The Wanderveil server cannot be reached right now." : OnlineAccount.Explain(result);
        return true;
    }
}
