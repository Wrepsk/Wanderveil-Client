using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Wanderveil.Infrastructure.Online;

// Where the logged-in character stands. NoCharacter: none is logged in. NoServer: no usable server address is set.
// SignedOut: the character must sign in before the passport opens. SignedIn: it has a session (which may be offline
// for now: the passport still opens, from this PC's copy).
internal enum AccountState { NoCharacter, NoServer, SignedOut, SignedIn }

// The sign-in, step by step. Ready: nothing asked yet. Requesting: asking for a code (or for an XIVAuth sign-in).
// Code: the Lodestone code is shown, waiting for the holder to put it on their profile. Checking: the server is
// reading the profile. Approving: waiting for the holder to approve Wanderveil on XIVAuth's page.
internal enum SignInStep { Ready, Requesting, Code, Checking, Approving }

// The logged-in character's account: its saved session, the Lodestone sign-in, sign-out, and the server's word on
// whether the session still holds. Network work runs on background tasks; the UI reads the state each frame and
// calls in from the UI or framework thread. A result for a character no longer logged in is dropped.
internal sealed class OnlineAccount : IDisposable
{
    private readonly SessionStore store;
    private readonly HttpClient http;
    private readonly string version;
    private readonly Func<string?> serverText;
    private readonly object gate = new();
    private readonly CancellationTokenSource closing = new();
    private string? serverSeen;
    private WanderveilApi? api;
    private ulong contentId;
    private string name = "";
    private string world = "";
    // Bumped whenever the character, server, or sign-in changes, so a late answer for an earlier one is dropped.
    private int generation;
    private volatile bool offline;

    internal OnlineAccount(SessionStore store, HttpClient http, string version, Func<string?> serverText)
    {
        this.store = store;
        this.http = http;
        this.version = version;
        this.serverText = serverText;
    }

    internal SignInStep Step { get; private set; }
    internal LodestoneChallenge? Challenge { get; private set; }
    // The XIVAuth sign-in waiting for approval, if any.
    internal XivAuthFlow? Flow { get; private set; }
    // Whether the server may offer XIVAuth: false once it has said it does not, until the server changes.
    internal bool XivAuthOffered { get; private set; } = true;
    // One second of the server's polling interval; shorter only in checks.
    internal TimeSpan PollUnit { get; init; } = TimeSpan.FromSeconds(1);
    // Why the last step did not work, in words for the holder; null when nothing went wrong.
    internal string? Problem { get; private set; }
    // Whether the server could not be reached on the last try (signed-in work continues from this PC's copy).
    internal bool Offline => offline;
    internal string CharacterName => name;
    internal string CharacterWorld => world;
    internal ulong ContentId => contentId;
    internal WanderveilApi? Api { get { lock (gate) return Refresh(); } }

    internal SavedSession? Session
    {
        get
        {
            lock (gate)
            {
                var server = Refresh();
                if (contentId == 0 || server == null) return null;
                var session = store.For(contentId);
                return session != null && session.Server == server.Server ? session : null;
            }
        }
    }

    internal AccountState State
    {
        get
        {
            if (contentId == 0) return AccountState.NoCharacter;
            if (Api == null) return AccountState.NoServer;
            return Session != null ? AccountState.SignedIn : AccountState.SignedOut;
        }
    }

    // The logged-in character, from the framework thread (0 when none). With a saved session, asks the server
    // whether it still holds; a definite no signs this character out, no answer keeps it (offline).
    internal void SetCharacter(ulong id, string characterName, string characterWorld)
    {
        int ticket;
        lock (gate)
        {
            if (id == contentId && characterName == name && characterWorld == world) return;
            contentId = id;
            name = characterName;
            world = characterWorld;
            ticket = Reset();
        }
        if (Session is { } session) _ = Confirm(session, ticket);
    }

    // Asks the server for a code for the logged-in character.
    internal void RequestCode()
    {
        WanderveilApi? server;
        int ticket;
        string asked, on;
        lock (gate)
        {
            server = Refresh();
            if (server == null || contentId == 0 || Step is SignInStep.Requesting or SignInStep.Checking) return;
            ticket = ++generation;
            Step = SignInStep.Requesting;
            Problem = null;
            asked = name;
            on = world;
        }
        _ = Run(ticket, async token =>
        {
            var result = await server.StartLodestone(asked, on, token).ConfigureAwait(false);
            lock (gate)
            {
                if (ticket != generation) return;
                offline = result.Offline;
                if (result.Ok && result.Value != null) { Challenge = result.Value; Step = SignInStep.Code; }
                else { Step = Challenge != null ? SignInStep.Code : SignInStep.Ready; Problem = Explain(result); }
            }
        });
    }

    // Asks the server to read the profile for the code shown; signs in when it is there.
    internal void CheckCode()
    {
        WanderveilApi? server;
        LodestoneChallenge? challenge;
        int ticket;
        ulong character;
        lock (gate)
        {
            server = Refresh();
            challenge = Challenge;
            if (server == null || challenge == null || Step != SignInStep.Code) return;
            ticket = ++generation;
            Step = SignInStep.Checking;
            Problem = null;
            character = contentId;
        }
        _ = Run(ticket, async token =>
        {
            var result = await server.VerifyLodestone(challenge.ChallengeId, token).ConfigureAwait(false);
            lock (gate)
            {
                if (ticket != generation) return;
                offline = result.Offline;
                if (result.Ok && result.Value is { } grant)
                {
                    store.Put(new SavedSession(character, server.Server, grant.Token, grant.Account, grant.ExpiresAt));
                    Challenge = null;
                    Step = SignInStep.Ready;
                    return;
                }
                // A used, expired, or vanished challenge cannot be tried again; anything else can.
                if (result.Code is "challenge_not_found" or "challenge_expired" or "character_not_found") { Challenge = null; Step = SignInStep.Ready; }
                else Step = SignInStep.Code;
                Problem = Explain(result);
            }
        });
    }

    // Starts an XIVAuth sign-in for the logged-in character, then waits for approval, asking the server at the
    // interval it gives (longer when it says to slow down, or cannot be reached) until XIVAuth answers or the sign-in
    // expires. Restart (or another character) stops it.
    internal void StartXivAuth()
    {
        WanderveilApi? server;
        int ticket;
        string asked, on;
        ulong character;
        lock (gate)
        {
            server = Refresh();
            if (server == null || contentId == 0 || Step is SignInStep.Requesting or SignInStep.Checking or SignInStep.Approving) return;
            ticket = Reset();
            Step = SignInStep.Requesting;
            asked = name;
            on = world;
            character = contentId;
        }
        _ = Run(ticket, async token =>
        {
            var started = await server.StartXivAuth(asked, on, token).ConfigureAwait(false);
            XivAuthFlow flow;
            lock (gate)
            {
                if (ticket != generation) return;
                offline = started.Offline;
                if (!started.Ok || started.Value == null)
                {
                    Step = SignInStep.Ready;
                    Problem = Explain(started);
                    // Not set up there (as opposed to XIVAuth being down for now, which comes with a time to wait).
                    if (started.Code == "xivauth_unavailable" && started.RetryAfter == null) XivAuthOffered = false;
                    return;
                }
                flow = started.Value;
                Flow = flow;
                Step = SignInStep.Approving;
            }
            var wait = TimeSpan.Zero;
            while (true)
            {
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
                wait = PollUnit * Math.Max(1, flow.Interval);
                lock (gate) if (ticket != generation) return;
                var poll = await server.PollXivAuth(flow.FlowId, token).ConfigureAwait(false);
                lock (gate)
                {
                    if (ticket != generation) return;
                    offline = poll.Offline;
                    if (poll.Ok && poll.Value is { Status: "approved", Session: { } grant })
                    {
                        store.Put(new SavedSession(character, server.Server, grant.Token, grant.Account, grant.ExpiresAt));
                        Flow = null;
                        Step = SignInStep.Ready;
                        return;
                    }
                    if (poll.Ok) continue;
                    // Polled too soon, or XIVAuth asked to slow down: wait as long as the server says.
                    if (poll.Status == 429) { wait = poll.RetryAfter is { } after ? PollUnit * Math.Max(1, after.TotalSeconds) : wait * 2; continue; }
                    // No answer, or XIVAuth down for now: keep trying, more slowly, while the sign-in lasts.
                    if (poll.Offline || poll.Code == "xivauth_unavailable" && poll.RetryAfter != null)
                    {
                        wait = PollUnit * Math.Max(flow.Interval * 2, poll.RetryAfter?.TotalSeconds ?? 0);
                        if (DateTimeOffset.UtcNow < flow.ExpiresAt) continue;
                        Problem = "The XIVAuth sign-in expired while the server could not be reached. Start again.";
                    }
                    else Problem = Explain(poll);
                    Flow = null;
                    Step = SignInStep.Ready;
                    return;
                }
            }
        });
    }

    // Back to the start of the sign-in, forgetting the code shown.
    internal void Restart()
    {
        lock (gate) Reset();
    }

    // Signs this character out: forgotten here at once, and the server told when it can be reached.
    internal void SignOut()
    {
        SavedSession? session;
        WanderveilApi? server;
        lock (gate)
        {
            session = Session;
            server = Refresh();
            if (session == null) return;
            store.Remove(session.ContentId);
            Reset();
        }
        if (server != null) _ = Run(generation, token => server.SignOut(session.Token, token));
    }

    // From any work that met a definite 401: the session is gone, so this character signs in again.
    internal void SessionRefused(SavedSession session)
    {
        lock (gate)
        {
            if (store.For(session.ContentId)?.Token != session.Token) return;
            store.Remove(session.ContentId);
            if (session.ContentId == contentId) { Reset(); Problem = "Your session has ended. Sign in again to keep your passport in step."; }
        }
    }

    internal void ReportOffline(bool value) => offline = value;

    public void Dispose()
    {
        closing.Cancel();
        closing.Dispose();
    }

    private async Task Confirm(SavedSession session, int ticket)
    {
        var server = Api;
        if (server == null) return;
        await Run(ticket, async token =>
        {
            var result = await server.Session(session.Token, token).ConfigureAwait(false);
            offline = result.Offline;
            if (result.SignedOut) SessionRefused(session);
            else if (result.Ok && result.Value is { } info)
                lock (gate)
                    if (store.For(session.ContentId)?.Token == session.Token) store.Put(session with { Account = info.Account, ExpiresAt = info.ExpiresAt });
        }).ConfigureAwait(false);
    }

    private async Task Run(int ticket, Func<CancellationToken, Task> work)
    {
        CancellationToken token;
        try { token = closing.Token; }
        catch (ObjectDisposedException) { return; }
        try { await Task.Run(() => work(token), token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            lock (gate)
                if (ticket == generation) { Problem = "Something went wrong: " + exception.Message; Flow = null; if (Step is SignInStep.Requesting or SignInStep.Checking or SignInStep.Approving) Step = Challenge != null ? SignInStep.Code : SignInStep.Ready; }
        }
    }

    // Under gate.
    private int Reset()
    {
        Challenge = null;
        Flow = null;
        Step = SignInStep.Ready;
        Problem = null;
        return ++generation;
    }

    // The client for the server address set now; under gate.
    private WanderveilApi? Refresh()
    {
        var text = serverText();
        if (text == serverSeen) return api;
        serverSeen = text;
        var server = WanderveilApi.ParseServer(text);
        api = server == null ? null : new WanderveilApi(http, server, version);
        XivAuthOffered = true;
        Reset();
        return api;
    }

    // What the holder should read when a call did not work.
    internal static string Explain<T>(ApiResult<T> result) => result.Code switch
    {
        _ when result.Offline => "The Wanderveil server could not be reached. Check the address and your connection, then try again.",
        "character_not_found" => "The Lodestone has no character with this name on this world. If you renamed or moved recently, the Lodestone may not show it yet.",
        "lodestone_unavailable" => "The Lodestone cannot be read right now (it may be under maintenance). Try again in a little while.",
        "code_not_found" => "The code is not on your Lodestone profile yet. Save it in your character profile, then check again; the Lodestone can take a minute to show it.",
        "challenge_expired" => "That code has expired. Ask for a new one.",
        "challenge_not_found" => "That sign-in has already been used or is unknown. Ask for a new code.",
        "rate_limited" => result.RetryAfter is { } wait
            ? $"Too many tries for now. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minute(s)."
            : "Too many tries for now. Try again in a few minutes.",
        "client_outdated" => "This version of Wanderveil is too old for the server. Update the plugin.",
        "session_invalid" => "Your session has ended. Sign in again.",
        "invalid_request" => "The server did not accept this character's name or world.",
        "xivauth_unavailable" => result.RetryAfter == null
            ? "This server does not offer XIVAuth sign-in yet. Sign in with a Lodestone code instead."
            : "XIVAuth cannot be reached right now. Try again in a little while, or sign in with a Lodestone code.",
        "access_denied" => "The sign-in was declined on XIVAuth.",
        "flow_expired" => "The XIVAuth sign-in expired before it was approved. Start again.",
        "flow_not_found" => "That XIVAuth sign-in is over. Start again.",
        "xivauth_character_not_verified" => "XIVAuth did not vouch for this character. On XIVAuth, verify it and share it with Wanderveil when approving, then start again.",
        _ => result.Detail ?? $"The server refused this ({result.Status}).",
    };
}
