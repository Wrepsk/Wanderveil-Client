using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Wanderveil.Features;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.Infrastructure.Online;

// Off: not running. Checking: comparing with the server. Saving: sending this PC's changes. Synced: the server has
// what this PC has. Offline: the server could not be reached; changes wait and are sent later. Held: the server
// has a passport from a newer Wanderveil, so this version leaves it alone.
internal enum SyncState { Off, Checking, Saving, Synced, Offline, Held }

// What was last agreed with the server: its revision and the hash of the document both sides then had.
internal sealed record SyncMark(int Revision, string Hash);

// Keeps one character's passport identity (the identity file's document, no picture) in step with the server's
// private record. This PC's file stays the source of truth and works offline. On start and after each change it
// compares: a side that changed since the last agreement wins; if both changed, this PC's copy wins and replaces
// the server's. A passport new on this PC takes the server's when there is one. Network and disk work run on one
// background task; the identity is read and changed only on the UI thread (Snapshot, Poll).
internal sealed class PassportSync : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LastRetry = TimeSpan.FromMinutes(5);
    private readonly OnlineAccount account;
    private readonly SavedSession session;
    private readonly PassportProfile profile;
    private readonly string markPath;
    private readonly CancellationTokenSource closing = new();
    private readonly object gate = new();
    private readonly TimeSpan settle;
    private bool fresh;
    private SyncMark? mark;
    private string document;
    private Task worker = Task.CompletedTask;
    private bool running;
    private bool again;
    private TimeSpan retry;
    // A server passport to take, handed to the UI thread.
    private (PassportIdentity Identity, SyncMark Mark)? adopt;
    private bool profileChanging;

    internal PassportSync(OnlineAccount account, SavedSession session, PassportProfile profile, string directory, bool fresh, TimeSpan? settle = null)
    {
        this.account = account;
        this.session = session;
        this.profile = profile;
        this.fresh = fresh;
        this.settle = settle ?? Settle;
        retry = FirstRetry;
        markPath = Path.Combine(directory, $"passport-sync-{StorageProfile.Checked(session.Profile)}.json");
        mark = ReadMark(markPath);
        document = PassportStore.Serialize(profile.Identity);
        profile.Changed += Changed;
    }

    internal SyncState State { get; private set; } = SyncState.Off;
    // Something the holder should know about the last comparison, or null.
    internal string? Notice { get; private set; }
    internal int? Revision => mark?.Revision;

    // Begins: compares with the server now. From the UI thread.
    internal void Start() => Kick(TimeSpan.Zero);

    // Each frame, on the UI thread: takes a server passport the comparison chose.
    internal void Poll()
    {
        (PassportIdentity Identity, SyncMark Mark)? taken;
        lock (gate) { taken = adopt; adopt = null; }
        if (taken is not { } value) return;
        profileChanging = true;
        try { profile.Update(value.Identity); }
        finally { profileChanging = false; }
        lock (gate)
        {
            document = PassportStore.Serialize(profile.Identity);
            // The document as this PC writes it, so taking it is not mistaken for a change here.
            mark = value.Mark with { Hash = Hash(document) };
            fresh = false;
        }
        // Off the UI thread: a small file, but still disk work.
        var written = mark;
        _ = Task.Run(() => WriteMark(written));
    }

    // For checks: waits until the worker is idle.
    internal bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Task current;
            lock (gate) { if (!running) return true; current = worker; }
            current.Wait(deadline - DateTime.UtcNow > TimeSpan.Zero ? deadline - DateTime.UtcNow : TimeSpan.Zero);
        }
        lock (gate) return !running;
    }

    public void Dispose()
    {
        profile.Changed -= Changed;
        closing.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        closing.Dispose();
    }

    private void Changed()
    {
        var text = PassportStore.Serialize(profile.Identity);
        lock (gate) document = text;
        if (!profileChanging) Kick(settle);
    }

    private void Kick(TimeSpan delay)
    {
        lock (gate)
        {
            if (closing.IsCancellationRequested) return;
            if (running) { again = true; return; }
            running = true;
            again = false;
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
                var done = await Compare(token).ConfigureAwait(false);
                lock (gate)
                {
                    if (!done) { running = false; break; }
                    if (!again) { running = false; return; }
                    again = false;
                }
            }
            // Not done (offline): try again later, waiting longer each time.
            TimeSpan wait;
            lock (gate) { wait = retry; retry = retry * 2 > LastRetry ? LastRetry : retry * 2; }
            await Task.Delay(wait, token).ConfigureAwait(false);
            Kick(TimeSpan.Zero);
        }
        catch (OperationCanceledException) { lock (gate) running = false; }
    }

    // One comparison and whatever it calls for. False when it should be tried again later.
    private async Task<bool> Compare(CancellationToken token)
    {
        var api = account.Api;
        if (api == null || api.Server != session.Server) { State = SyncState.Off; return true; }
        State = SyncState.Checking;
        var read = await api.GetPassport(session.Token, token).ConfigureAwait(false);
        if (Settled(read) is { } stop) return stop;

        string local;
        SyncMark? agreed;
        bool isFresh;
        lock (gate) { local = document; agreed = mark; isFresh = fresh; }
        var localHash = Hash(local);
        if (read.Status == 404) return await Send(api, local, null, token).ConfigureAwait(false);

        var record = read.Value!;
        var remote = record.Document.GetRawText();
        var remoteHash = Hash(remote);
        var localChanged = agreed == null ? !isFresh : localHash != agreed.Hash;
        var remoteChanged = agreed == null || record.Revision != agreed.Revision;
        if (agreed != null && !localChanged && !remoteChanged) { State = SyncState.Synced; return true; }
        if (localHash == remoteHash || !localChanged && remoteChanged)
        {
            // Take the server's (or note the agreement when both already match).
            PassportIdentity identity;
            try { identity = PassportStore.Parse(remote); }
            catch (NotSupportedException) { State = SyncState.Held; Notice = "Your passport was last saved online by a newer Wanderveil. Update the plugin to keep it in step."; return true; }
            catch (FormatException) { return await Send(api, local, record.Revision, token).ConfigureAwait(false); }
            lock (gate) adopt = (identity, new SyncMark(record.Revision, remoteHash));
            State = SyncState.Synced;
            return true;
        }
        if (remoteChanged && agreed != null) Notice = "This passport was also changed on another PC; the version on this PC was kept.";
        return await Send(api, local, record.Revision, token).ConfigureAwait(false);
    }

    private async Task<bool> Send(WanderveilApi api, string local, int? over, CancellationToken token)
    {
        State = SyncState.Saving;
        var saved = await api.PutPassport(session.Token, local, over, token).ConfigureAwait(false);
        if (saved.Status == 412) { lock (gate) again = true; return true; }
        if (Settled(saved) is { } stop) return stop;
        var agreed = new SyncMark(saved.Value!.Revision, Hash(local));
        lock (gate) { mark = agreed; fresh = false; }
        WriteMark(agreed);
        State = SyncState.Synced;
        return true;
    }

    // What an answer that is not a record means, or null to carry on.
    private bool? Settled<T>(ApiResult<T> result)
    {
        account.ReportOffline(result.Offline);
        if (result.Offline) { State = SyncState.Offline; return false; }
        lock (gate) retry = FirstRetry;
        if (result.SignedOut) { account.SessionRefused(session); State = SyncState.Off; return true; }
        if (result.Ok && result.Value != null || result.Status is 404 or 412) return null;
        // Refused for a reason that retrying will not change (too large, invalid): keep this PC's copy and say so.
        State = SyncState.Offline;
        Notice = OnlineAccount.Explain(result);
        return result.Status is 429 or >= 500 ? false : true;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static SyncMark? ReadMark(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is not JsonObject file) return null;
            var revision = file["revision"] is JsonValue r && r.TryGetValue<int>(out var number) ? number : 0;
            var hash = file["hash"] is JsonValue h && h.TryGetValue<string>(out var text) ? text : null;
            return revision > 0 && hash is { Length: 64 } ? new SyncMark(revision, hash) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void WriteMark(SyncMark agreed)
    {
        try
        {
            var file = new JsonObject { ["format"] = "wanderveil.passport-sync", ["version"] = 1, ["revision"] = agreed.Revision, ["hash"] = agreed.Hash, ["at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            SafeFile.Write(markPath, Encoding.UTF8.GetBytes(file.ToJsonString()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
