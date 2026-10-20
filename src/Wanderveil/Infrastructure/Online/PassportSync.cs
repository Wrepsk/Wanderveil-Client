using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.Infrastructure.Online;

// Off: not running. Checking: comparing with the server. Saving: sending this PC's changes. Synced: the server has
// what this PC has. Offline: the server could not be reached; changes wait and are sent later. Held: the server
// has a part from a newer Wanderveil, so this version leaves it alone.
internal enum SyncState { Off, Checking, Saving, Synced, Offline, Held }

// What was last agreed with the server for a part: its revision and the hash of what both sides then had (empty
// right after this PC took the server's copy, until the copy is hashed as this PC writes it).
internal sealed record SyncMark(int Revision, string Hash);

// Keeps one character's passport in step with the server's private record, part by part (identity, stamps, drawing
// and writing, picture; ADR-0005). This PC's files stay the source of truth and work offline. For each part, on start
// and after each change: a side that changed since the last agreement wins; if both changed, this PC's copy wins and
// replaces the server's. A part new on this PC takes the server's when there is one. Network and disk work run on one
// background task; parts are read and changed only on the UI thread (Start, change events, Poll).
internal sealed class PassportSync : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LastRetry = TimeSpan.FromMinutes(5);
    private readonly OnlineAccount account;
    private readonly SavedSession session;
    private readonly IReadOnlyList<SyncPart> parts;
    private readonly string markPath;
    private readonly CancellationTokenSource closing = new();
    private readonly object gate = new();
    private readonly TimeSpan settle;
    private readonly Dictionary<string, SyncMark> marks;
    private readonly Dictionary<string, object?> snapshots = [];
    private readonly Dictionary<string, SyncState> states = [];
    private readonly HashSet<string> dirty = [];
    // Parts that took the server's copy, with a snapshot of the copy as applied, waiting to be hashed.
    private readonly Dictionary<string, object?> rehash = [];
    private readonly List<(SyncPart Part, object? Prepared, int Revision)> adopt = [];
    private readonly HashSet<string> fresh = [];
    private Task worker = Task.CompletedTask;
    private bool running;
    private TimeSpan retry;

    // Only the identity, as before stamps, drawing, and the picture were online.
    internal PassportSync(OnlineAccount account, SavedSession session, PassportProfile profile, string directory, bool fresh, TimeSpan? settle = null)
        : this(account, session, [new IdentityPart(profile, fresh)], directory, settle)
    {
    }

    internal PassportSync(OnlineAccount account, SavedSession session, IReadOnlyList<SyncPart> parts, string directory, TimeSpan? settle = null)
    {
        this.account = account;
        this.session = session;
        this.parts = parts;
        this.settle = settle ?? Settle;
        retry = FirstRetry;
        markPath = Path.Combine(directory, $"passport-sync-{StorageProfile.Checked(session.Profile)}.json");
        marks = ReadMarks(markPath);
        foreach (var part in parts)
        {
            if (part.Fresh) fresh.Add(part.Name);
            states[part.Name] = SyncState.Off;
            snapshots[part.Name] = part.Capture();
            part.Changed += () => Changed(part);
        }
    }

    internal SyncState State
    {
        get
        {
            lock (gate)
            {
                var all = states.Values.ToList();
                if (all.Contains(SyncState.Held)) return SyncState.Held;
                if (all.Contains(SyncState.Offline)) return SyncState.Offline;
                if (all.Contains(SyncState.Saving)) return SyncState.Saving;
                if (all.Contains(SyncState.Checking)) return SyncState.Checking;
                return all.All(state => state == SyncState.Synced) ? SyncState.Synced : SyncState.Off;
            }
        }
    }

    internal SyncState StateOf(string part)
    {
        lock (gate) return states.GetValueOrDefault(part);
    }

    // Something the holder should know about the last comparison, or null.
    internal string? Notice { get; private set; }

    // The identity's agreed revision.
    internal int? Revision => RevisionOf("passport");

    internal int? RevisionOf(string part)
    {
        lock (gate) return marks.TryGetValue(part, out var mark) ? mark.Revision : null;
    }

    // Begins: compares every part with the server now. From the UI thread.
    internal void Start()
    {
        lock (gate)
        {
            foreach (var part in parts) { snapshots[part.Name] = part.Capture(); dirty.Add(part.Name); }
        }
        Kick(TimeSpan.Zero);
    }

    // Each frame, on the UI thread: takes the server's copies the comparisons chose.
    internal void Poll()
    {
        List<(SyncPart Part, object? Prepared, int Revision)> taken;
        lock (gate)
        {
            if (adopt.Count == 0) return;
            taken = [.. adopt];
            adopt.Clear();
        }
        foreach (var (part, prepared, revision) in taken)
        {
            part.Apply(prepared);
            lock (gate)
            {
                marks[part.Name] = new SyncMark(revision, "");
                fresh.Remove(part.Name);
                // The copy as this PC now holds it, hashed on the worker, so taking it is not mistaken for a change.
                rehash[part.Name] = part.Capture();
                snapshots[part.Name] = rehash[part.Name];
                dirty.Add(part.Name);
            }
        }
        Kick(TimeSpan.Zero);
    }

    // For checks: waits until the worker is idle.
    internal bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Task current;
            lock (gate) { if (!running) return true; current = worker; }
            var left = deadline - DateTime.UtcNow;
            current.Wait(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        }
        lock (gate) return !running;
    }

    public void Dispose()
    {
        closing.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        closing.Dispose();
    }

    private void Changed(SyncPart part)
    {
        // The picture changes off the UI thread and has no snapshot to take.
        var snapshot = part.IsPicture ? null : part.Capture();
        lock (gate)
        {
            snapshots[part.Name] = snapshot;
            dirty.Add(part.Name);
        }
        Kick(settle);
    }

    private void Kick(TimeSpan delay)
    {
        lock (gate)
        {
            if (closing.IsCancellationRequested || running) return;
            running = true;
            worker = Task.Run(() => Work(delay));
        }
    }

    private async Task Work(TimeSpan delay)
    {
        var token = closing.Token;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            while (true)
            {
                SyncPart? next;
                lock (gate)
                {
                    next = parts.FirstOrDefault(part => dirty.Contains(part.Name));
                    if (next == null) { running = false; return; }
                    dirty.Remove(next.Name);
                }
                if (await Compare(next, token).ConfigureAwait(false)) continue;
                // Offline: everything waits, tried again later, waiting longer each time.
                TimeSpan wait;
                lock (gate)
                {
                    dirty.Add(next.Name);
                    wait = retry;
                    retry = retry * 2 > LastRetry ? LastRetry : retry * 2;
                    running = false;
                }
                await Task.Delay(wait, token).ConfigureAwait(false);
                Kick(TimeSpan.Zero);
                return;
            }
        }
        catch (OperationCanceledException) { lock (gate) running = false; }
        catch (Exception)
        {
            // Never leave the worker marked running; the next change or start tries again.
            lock (gate) running = false;
        }
    }

    // One part's comparison and whatever it calls for. False when it should be tried again later (offline).
    private async Task<bool> Compare(SyncPart part, CancellationToken token)
    {
        var api = account.Api;
        if (api == null || api.Server != session.Server) { SetState(part, SyncState.Off); return true; }
        SetState(part, SyncState.Checking);

        object? snapshot;
        SyncMark? agreed;
        bool isFresh;
        lock (gate)
        {
            snapshot = snapshots.GetValueOrDefault(part.Name);
            if (rehash.Remove(part.Name, out var applied) && marks.TryGetValue(part.Name, out var pending))
                marks[part.Name] = pending with { Hash = part.Encode(applied)?.Hash ?? "" };
            agreed = marks.GetValueOrDefault(part.Name);
            isFresh = fresh.Contains(part.Name);
        }
        WriteMarks();
        var local = part.Encode(snapshot);

        int revision;
        byte[] remote;
        if (part.IsPicture)
        {
            var read = await api.GetPicture(session.Token, token).ConfigureAwait(false);
            if (Settled(part, read) is { } stop) return stop;
            if (read.Status == 404) return await Absent(api, part, local, token).ConfigureAwait(false);
            (revision, remote) = (read.Value!.Revision, read.Value.Bytes);
        }
        else
        {
            var read = await api.GetDocument(session.Token, Route(part), token).ConfigureAwait(false);
            if (Settled(part, read) is { } stop) return stop;
            if (read.Status == 404) return await Absent(api, part, local, token).ConfigureAwait(false);
            (revision, remote) = (read.Value!.Revision, Encoding.UTF8.GetBytes(read.Value.Document.GetRawText()));
        }

        var remoteHash = SyncPart.Hash(remote);
        var localChanged = agreed == null ? !isFresh && local != null : (local?.Hash ?? "") != agreed.Hash;
        var remoteChanged = agreed == null || revision != agreed.Revision;
        if (agreed != null && !localChanged && !remoteChanged) { SetState(part, SyncState.Synced); return true; }
        if (local != null && local.Hash == remoteHash)
        {
            Agree(part, new SyncMark(revision, local.Hash));
            SetState(part, SyncState.Synced);
            return true;
        }
        if (!localChanged && remoteChanged)
        {
            object prepared;
            try { prepared = part.Prepare(remote); }
            catch (NotSupportedException)
            {
                SetState(part, SyncState.Held);
                Notice = "Your passport was last saved online by a newer Wanderveil. Update the plugin to keep it in step.";
                return true;
            }
            catch (FormatException) { return local == null || await Send(api, part, local, revision, token).ConfigureAwait(false); }
            lock (gate) adopt.Add((part, prepared, revision));
            SetState(part, SyncState.Synced);
            return true;
        }
        if (remoteChanged && agreed != null) Notice = "Your passport was also changed on another PC; the version on this PC was kept.";
        if (local == null) return await Remove(api, part, revision, token).ConfigureAwait(false);
        return await Send(api, part, local, revision, token).ConfigureAwait(false);
    }

    // The server has none of this part: send this PC's, or agree there is none.
    private async Task<bool> Absent(WanderveilApi api, SyncPart part, LocalPart? local, CancellationToken token)
    {
        if (local != null) return await Send(api, part, local, null, token).ConfigureAwait(false);
        lock (gate) marks.Remove(part.Name);
        WriteMarks();
        SetState(part, SyncState.Synced);
        return true;
    }

    private async Task<bool> Send(WanderveilApi api, SyncPart part, LocalPart local, int? over, CancellationToken token)
    {
        SetState(part, SyncState.Saving);
        byte[] bytes;
        string contentType;
        try { (bytes, contentType) = await local.Payload(token).ConfigureAwait(false); }
        catch (InvalidDataException exception)
        {
            // Not something to retry (a picture that cannot be made small enough): kept on this PC, and said so.
            SetState(part, SyncState.Offline);
            Notice = "Your picture stays on this PC only: " + exception.Message;
            return true;
        }
        int? saved;
        if (part.IsPicture)
        {
            var result = await api.PutPicture(session.Token, bytes, contentType, over, token).ConfigureAwait(false);
            if (result.Status == 412) { Again(part); return true; }
            if (Settled(part, result) is { } stop) return stop;
            saved = result.Value?.Revision;
        }
        else
        {
            var result = await api.PutDocument(session.Token, Route(part), Encoding.UTF8.GetString(bytes), over, part is MarksPart, token).ConfigureAwait(false);
            if (result.Status == 412) { Again(part); return true; }
            if (Settled(part, result) is { } stop) return stop;
            saved = result.Value?.Revision;
        }
        if (saved is { } revision) Agree(part, new SyncMark(revision, local.Hash));
        SetState(part, SyncState.Synced);
        return true;
    }

    // The holder removed this part here (only the picture can be removed): remove it online too.
    private async Task<bool> Remove(WanderveilApi api, SyncPart part, int over, CancellationToken token)
    {
        SetState(part, SyncState.Saving);
        var result = await api.DeletePicture(session.Token, over, token).ConfigureAwait(false);
        if (result.Status == 412) { Again(part); return true; }
        if (result.Status != 404 && Settled(part, result) is { } stop) return stop;
        lock (gate) marks.Remove(part.Name);
        WriteMarks();
        SetState(part, SyncState.Synced);
        return true;
    }

    // What an answer that is not a usable one means, or null to carry on.
    private bool? Settled<T>(SyncPart part, ApiResult<T> result)
    {
        account.ReportOffline(result.Offline);
        if (result.Offline) { SetState(part, SyncState.Offline); return false; }
        lock (gate) retry = FirstRetry;
        if (result.SignedOut) { account.SessionRefused(session); SetState(part, SyncState.Off); return true; }
        // Success, with a body or (a removal) without one.
        if (result.Ok || result.Status is 404 or 412) return null;
        // Refused for a reason that retrying will not change (too large, invalid): keep this PC's copy and say so.
        SetState(part, SyncState.Offline);
        Notice = OnlineAccount.Explain(result);
        return result.Status is 429 or >= 500 ? false : true;
    }

    private void Again(SyncPart part)
    {
        lock (gate) dirty.Add(part.Name);
    }

    private void Agree(SyncPart part, SyncMark mark)
    {
        lock (gate)
        {
            marks[part.Name] = mark;
            fresh.Remove(part.Name);
        }
        WriteMarks();
    }

    private void SetState(SyncPart part, SyncState state)
    {
        lock (gate) states[part.Name] = state;
    }

    private static string Route(SyncPart part) => part.Name == "passport" ? "v1/passport" : "v1/passport/" + part.Name;

    // The agreement file: version 2 keeps every part; version 1 held only the identity.
    private static Dictionary<string, SyncMark> ReadMarks(string path)
    {
        var result = new Dictionary<string, SyncMark>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is not JsonObject file) return result;
            static SyncMark? Read(JsonNode? node)
            {
                if (node is not JsonObject entry) return null;
                var revision = entry["revision"] is JsonValue r && r.TryGetValue<int>(out var number) ? number : 0;
                var hash = entry["hash"] is JsonValue h && h.TryGetValue<string>(out var text) ? text : null;
                return revision > 0 && hash is { Length: 64 } ? new SyncMark(revision, hash) : null;
            }
            if (file["parts"] is JsonObject saved)
            {
                foreach (var (name, node) in saved)
                    if (Read(node) is { } mark) result[name] = mark;
            }
            else if (Read(file) is { } identity) result["passport"] = identity;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        return result;
    }

    private void WriteMarks()
    {
        JsonObject saved;
        lock (gate)
        {
            saved = new JsonObject();
            foreach (var (name, mark) in marks.Where(entry => entry.Value.Hash.Length == 64))
                saved[name] = new JsonObject { ["revision"] = mark.Revision, ["hash"] = mark.Hash };
        }
        try
        {
            var file = new JsonObject { ["format"] = "wanderveil.passport-sync", ["version"] = 2, ["parts"] = saved, ["at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            SafeFile.Write(markPath, Encoding.UTF8.GetBytes(file.ToJsonString()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
