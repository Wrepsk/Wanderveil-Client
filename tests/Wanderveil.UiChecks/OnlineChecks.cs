using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wanderveil.Features;
using Wanderveil.Features.Passport;
using Wanderveil.Infrastructure.Online;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.UiChecks;

// Signing in, saved sessions, and passport sync, against an in-memory server that follows the public contract
// (docs/api/v1.yaml). No network, no backend: the public client's checks stand alone.
internal static class OnlineChecks
{
    private const ulong Content = 0x0040_0000_1234_5678;
    private const ulong OtherContent = 0x0040_0000_8765_4321;

    internal static void Run(Action<bool, string> check)
    {
        CheckAddresses(check);
        var directory = Path.Combine(Path.GetTempPath(), "wanderveil-online-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckApi(check);
            CheckSessions(check, directory);
            CheckSignIn(check, Path.Combine(directory, "sign-in"));
            CheckSync(check, Path.Combine(directory, "sync"));
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS online");
    }

    private static void CheckAddresses(Action<bool, string> check)
    {
        check(WanderveilApi.ParseServer("wanderveil.example")?.ToString() == "https://wanderveil.example/", "A bare host did not become an HTTPS address.");
        check(WanderveilApi.ParseServer("http://127.0.0.1:5080")?.ToString() == "http://127.0.0.1:5080/" && WanderveilApi.ParseServer("http://localhost:5080") != null, "A server on this PC over HTTP was refused.");
        check(WanderveilApi.ParseServer("http://wanderveil.example") == null, "Plain HTTP to another machine was accepted.");
        check(WanderveilApi.ParseServer("ftp://wanderveil.example") == null && WanderveilApi.ParseServer("") == null && WanderveilApi.ParseServer("https://user:secret@wanderveil.example") == null &&
              WanderveilApi.ParseServer("https://wanderveil.example/?x=1") == null, "An unusable server address was accepted.");
        try
        {
            _ = new WanderveilApi(new HttpClient(), new Uri("http://wanderveil.example/"), "0.0.0.1");
            check(false, "The API client accepted plain HTTP to another machine.");
        }
        catch (ArgumentException) { check(true, ""); }
    }

    private static void CheckApi(Action<bool, string> check)
    {
        var server = new FakeServer();
        server.Add(1001, "Wander Veil", "Ravana");
        var api = server.Api();
        var start = api.StartLodestone("Wander Veil", "Ravana", default).Result;
        check(start.Ok && start.Value!.Code.StartsWith("WV-", StringComparison.Ordinal) && server.LastClientHeader == "0.0.0.1", "Starting a sign-in failed, or did not name the client version.");
        var missing = api.StartLodestone("No Body", "Ravana", default).Result;
        check(!missing.Ok && missing.Status == 404 && missing.Code == "character_not_found", "A refusal did not carry its code.");
        server.Down = true;
        var down = api.StartLodestone("Wander Veil", "Ravana", default).Result;
        check(down.Offline && !down.Ok, "No answer was not reported as offline.");
        server.Down = false;
        server.Gateway = true;
        check(api.StartLodestone("Wander Veil", "Ravana", default).Result.Offline, "A gateway error without Wanderveil's word was not offline.");
        server.Gateway = false;
        server.LodestoneDown = true;
        var lodestone = api.StartLodestone("Wander Veil", "Ravana", default).Result;
        check(!lodestone.Offline && lodestone.Code == "lodestone_unavailable" && lodestone.RetryAfter == TimeSpan.FromSeconds(30), "A Lodestone outage was mistaken for the server being down.");
    }

    private static void CheckSessions(Action<bool, string> check, string directory)
    {
        var account = new AccountInfo(Guid.NewGuid(), new CharacterInfo("1001", "Wander Veil", "Ravana", "Materia"), "lodestone", DateTimeOffset.UnixEpoch);
        var token = new string('A', 40) + "abc";
        var session = new SavedSession(Content, new Uri("https://wanderveil.example/"), token, account, DateTimeOffset.UnixEpoch.AddDays(30));
        using (var store = new SessionStore(directory))
        {
            check(store.Loaded == LoadOutcome.New && store.Protection == "dpapi", $"A new session store did not start empty with DPAPI ({store.Loaded}, {store.Protection}).");
            store.Put(session);
            check(store.Flush(TimeSpan.FromSeconds(5)), "The session store did not save.");
        }
        var file = File.ReadAllText(Path.Combine(directory, "sessions.json"));
        check(!file.Contains(token, StringComparison.Ordinal) && !file.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(token)), StringComparison.Ordinal) && !file.Contains("Bearer", StringComparison.Ordinal),
            "A session token is readable in the sessions file.");
        using (var store = new SessionStore(directory))
        {
            var loaded = store.For(Content);
            check(store.Loaded == LoadOutcome.Loaded && loaded == session with { } && loaded!.Profile == "lodestone-1001", "A saved session did not load as it was saved.");
            check(store.For(OtherContent) == null, "Another character found a session.");
            store.Put(session with { ContentId = OtherContent });
            store.Remove(Content);
            store.Flush(TimeSpan.FromSeconds(5));
        }
        using (var store = new SessionStore(directory))
            check(store.For(Content) == null && store.For(OtherContent) != null, "Removing a session was not saved.");

        // A token this user cannot unprotect (copied from another PC) costs only that character a sign-in.
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "sessions.json")))!;
        var entry = node["sessions"]![0]!.AsObject();
        var copy = JsonNode.Parse(entry.ToJsonString())!.AsObject();
        copy["contentId"] = Content.ToString();
        copy["token"] = Convert.ToBase64String(new byte[64]);
        node["sessions"]!.AsArray().Add(copy);
        File.WriteAllText(Path.Combine(directory, "sessions.json"), node.ToJsonString());
        using (var store = new SessionStore(directory))
            check(store.Loaded == LoadOutcome.Loaded && store.For(Content) == null && store.For(OtherContent) != null, "An unreadable token spoiled the other sessions.");

        // A newer sessions file is left alone.
        node["version"] = 9;
        File.WriteAllText(Path.Combine(directory, "sessions.json"), node.ToJsonString());
        using (var store = new SessionStore(directory))
        {
            store.Put(session);
            check(store.Loaded == LoadOutcome.Newer && store.State == SaveState.ReadOnly, "A newer sessions file was not left read-only.");
        }
        check(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "sessions.json")))!["version"]!.GetValue<int>() == 9, "A newer sessions file was overwritten.");

        // Without DPAPI, tokens are kept as they are and the file says so.
        var plain = Path.Combine(directory, "plain");
        using (var store = new SessionStore(plain, new PlainProtector()))
        {
            store.Put(session);
            store.Flush(TimeSpan.FromSeconds(5));
        }
        check(File.ReadAllText(Path.Combine(plain, "sessions.json")).Contains("\"protection\": \"none\"", StringComparison.Ordinal), "An unprotected token is not labelled as such.");
        using (var store = new SessionStore(plain, new PlainProtector()))
            check(store.For(Content)?.Token == token, "An unprotected token did not load.");
    }

    private static void CheckSignIn(Action<bool, string> check, string directory)
    {
        var server = new FakeServer();
        server.Add(1001, "Wander Veil", "Ravana");
        var address = "";
        using var store = new SessionStore(directory);
        using var account = new OnlineAccount(store, server.Client(), "0.0.0.1", () => address);
        check(account.State == AccountState.NoCharacter, "With no character logged in, the account is not NoCharacter.");
        account.SetCharacter(Content, "Wander Veil", "Ravana");
        check(account.State == AccountState.NoServer, "Without a server address, the account is not NoServer.");
        address = FakeServer.Address;
        check(account.State == AccountState.SignedOut, "A character without a session is not signed out.");

        // A code, checked before and after it is on the profile.
        account.RequestCode();
        check(Until(() => account.Step == SignInStep.Code), "Requesting a code did not show one.");
        var challenge = account.Challenge!;
        account.CheckCode();
        check(Until(() => account.Step == SignInStep.Code && account.Problem != null) && account.Problem!.Contains("not on your Lodestone profile", StringComparison.Ordinal) && account.State == AccountState.SignedOut,
            "Checking before the code is on the profile signed in, or did not say why not.");
        server.Introduce(1001, "Hello " + challenge.Code);
        account.CheckCode();
        check(Until(() => account.State == AccountState.SignedIn), "Checking with the code on the profile did not sign in.");
        check(account.Session!.Profile == "lodestone-1001" && account.Session.Account.Character.Name == "Wander Veil" && account.Problem == null, "The session is not the verified character's.");

        // Another character on this PC signs in on its own; switching back finds the first session.
        server.Add(2002, "Other Self", "Ravana");
        account.SetCharacter(OtherContent, "Other Self", "Ravana");
        check(account.State == AccountState.SignedOut, "Another character shared the first character's session.");
        account.SetCharacter(Content, "Wander Veil", "Ravana");
        check(Until(() => server.SessionChecks > 0) && account.State == AccountState.SignedIn, "Switching back lost the first session.");

        // An unknown character, and a server that does not answer.
        account.SetCharacter(OtherContent, "No Body", "Ravana");
        account.RequestCode();
        check(Until(() => account.Problem != null) && account.Step == SignInStep.Ready && account.Problem!.Contains("no character", StringComparison.Ordinal), "An unknown character was not explained.");
        server.Down = true;
        account.RequestCode();
        check(Until(() => account.Problem?.Contains("could not be reached", StringComparison.Ordinal) == true) && account.Offline, "An unreachable server was not explained.");
        server.Down = false;

        // A late answer for a character no longer logged in is dropped.
        server.Delay = TimeSpan.FromMilliseconds(300);
        account.SetCharacter(OtherContent, "Other Self", "Ravana");
        account.RequestCode();
        account.SetCharacter(Content, "Wander Veil", "Ravana");
        Thread.Sleep(600);
        check(account.Challenge == null && account.Step == SignInStep.Ready, "A code for the previous character arrived after switching.");
        server.Delay = TimeSpan.Zero;

        // Offline at login keeps the session; a definite 401 ends it.
        server.Down = true;
        account.SetCharacter(OtherContent, "Other Self", "Ravana");
        account.SetCharacter(Content, "Wander Veil", "Ravana");
        check(Until(() => account.Offline) && account.State == AccountState.SignedIn, "An unreachable server signed the character out.");
        server.Down = false;
        server.EndSessions();
        account.SetCharacter(OtherContent, "Other Self", "Ravana");
        account.SetCharacter(Content, "Wander Veil", "Ravana");
        check(Until(() => account.State == AccountState.SignedOut) && store.For(Content) == null && account.Problem?.Contains("ended", StringComparison.Ordinal) == true,
            "A refused session was kept, or the holder was not told.");

        // Signing out forgets the session here and ends it on the server.
        SignIn(server, account, 1001);
        var token = account.Session!.Token;
        account.SignOut();
        check(account.State == AccountState.SignedOut && store.For(Content) == null && Until(() => !server.HasSession(token)), "Signing out did not end the session here and on the server.");

        // A session belongs to the server it signed in to.
        SignIn(server, account, 1001);
        address = "https://elsewhere.example";
        check(account.State == AccountState.SignedOut, "A session was used with another server.");
        address = FakeServer.Address;
        check(account.State == AccountState.SignedIn, "Returning to the server lost the session.");
    }

    private static void CheckSync(Action<bool, string> check, string directory)
    {
        var server = new FakeServer();
        server.Add(1001, "Wander Veil", "Ravana");
        using var store = new SessionStore(directory);
        using var account = new OnlineAccount(store, server.Client(), "0.0.0.1", () => FakeServer.Address);
        account.SetCharacter(Content, "Wander Veil", "Ravana");
        SignIn(server, account, 1001);
        var session = account.Session!;
        var accountId = session.Account.AccountId;
        var settle = TimeSpan.FromMilliseconds(20);

        // A passport new here, none on the server: it is saved there.
        var profile = new PassportProfile(PassportIdentity.Sample with { Number = "EZ 0001 0001" });
        using (var sync = new PassportSync(account, session, profile, directory, fresh: true, settle))
        {
            sync.Start();
            check(sync.Flush(TimeSpan.FromSeconds(5)) && sync.State == SyncState.Synced && server.Passport(accountId) is { Revision: 1 } first && first.Document.Contains("EZ 0001 0001", StringComparison.Ordinal),
                "A new passport was not saved to the server.");
            // A change here is sent over the revision agreed.
            profile.Update(profile.Identity with { Motto = "Onward" });
            check(Until(() => server.Passport(accountId)?.Revision == 2) && sync.Flush(TimeSpan.FromSeconds(5)) && server.Passport(accountId)!.Value.Document.Contains("Onward", StringComparison.Ordinal), "A change here was not sent.");
            check(server.LastIfMatch == "\"1\"", $"A change was not sent over the agreed revision ({server.LastIfMatch}).");
        }

        // A change on another PC, none here: taken when the passport opens again.
        server.Edit(accountId, document => document.Replace("Onward", "From afar", StringComparison.Ordinal));
        using (var sync = new PassportSync(account, session, profile, directory, fresh: false, settle))
        {
            sync.Start();
            sync.Flush(TimeSpan.FromSeconds(5));
            sync.Poll();
            check(profile.Identity.Motto == "From afar" && sync.Revision == 3, "A change made elsewhere was not taken.");
            sync.Flush(TimeSpan.FromSeconds(1));
            check(server.Passport(accountId)!.Value.Revision == 3, "Taking the server's passport sent it back as a change.");
        }

        // Both changed: this PC's copy wins, and the holder is told.
        server.Edit(accountId, document => document.Replace("From afar", "Theirs", StringComparison.Ordinal));
        profile.Update(profile.Identity with { Motto = "Mine" });
        using (var sync = new PassportSync(account, session, profile, directory, fresh: false, settle))
        {
            sync.Start();
            sync.Flush(TimeSpan.FromSeconds(5));
            sync.Poll();
            check(profile.Identity.Motto == "Mine" && server.Passport(accountId)!.Value.Document.Contains("Mine", StringComparison.Ordinal) && sync.Notice?.Contains("another PC", StringComparison.Ordinal) == true,
                "When both changed, this PC's copy did not win, or the holder was not told.");
        }

        // A new PC (a fresh passport) takes the server's.
        var newPc = Path.Combine(directory, "new-pc");
        Directory.CreateDirectory(newPc);
        var blank = new PassportProfile(PassportIdentity.Sample with { Number = "EZ 9999 9999" });
        using (var sync = new PassportSync(account, session, blank, newPc, fresh: true, settle))
        {
            sync.Start();
            sync.Flush(TimeSpan.FromSeconds(5));
            sync.Poll();
            check(blank.Identity.Motto == "Mine" && blank.Identity.Number == "EZ 0001 0001", "A passport new on this PC did not take the server's.");
        }

        // Offline: changes wait; once the server answers they are sent.
        server.Down = true;
        using (var sync = new PassportSync(account, session, profile, directory, fresh: false, settle))
        {
            profile.Update(profile.Identity with { Motto = "Offline edit" });
            sync.Start();
            check(Until(() => sync.State == SyncState.Offline) && account.Offline, "An unreachable server was not reported while syncing.");
            server.Down = false;
            sync.Start();
            check(Until(() => server.Passport(accountId)!.Value.Document.Contains("Offline edit", StringComparison.Ordinal)) && Until(() => sync.State == SyncState.Synced), "A change made offline was not sent once the server answered.");
        }

        // A passport saved online by a newer Wanderveil is left alone.
        server.Edit(accountId, document => document.Replace("\"version\":3", "\"version\":9", StringComparison.Ordinal).Replace("Offline edit", "Future", StringComparison.Ordinal));
        using (var sync = new PassportSync(account, session, profile, directory, fresh: false, settle))
        {
            sync.Start();
            sync.Flush(TimeSpan.FromSeconds(5));
            sync.Poll();
            check(sync.State == SyncState.Held && profile.Identity.Motto == "Offline edit" && server.Passport(accountId)!.Value.Document.Contains("Future", StringComparison.Ordinal),
                "A newer passport online was taken or overwritten.");
        }

        // A refused session while syncing signs the character out.
        server.EndSessions();
        using (var sync = new PassportSync(account, session, profile, directory, fresh: false, settle))
        {
            sync.Start();
            check(Until(() => account.State == AccountState.SignedOut) && sync.State == SyncState.Off, "A refused session while syncing did not sign the character out.");
        }
    }

    private static void SignIn(FakeServer server, OnlineAccount account, long id)
    {
        account.Restart();
        account.RequestCode();
        if (!Until(() => account.Step == SignInStep.Code)) throw new InvalidOperationException("No code: " + account.Problem);
        server.Introduce(id, account.Challenge!.Code);
        account.CheckCode();
        if (!Until(() => account.State == AccountState.SignedIn)) throw new InvalidOperationException("Not signed in: " + account.Problem);
    }

    private static bool Until(Func<bool> condition, int milliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    // The contract's endpoints, in memory.
    private sealed class FakeServer : HttpMessageHandler
    {
        internal const string Address = "https://wanderveil.test";
        private readonly object gate = new();
        private readonly Dictionary<long, (string Name, string World, string Introduction)> characters = [];
        private readonly Dictionary<string, (long Id, string Code)> challenges = [];
        private readonly Dictionary<string, Guid> sessions = [];
        private readonly Dictionary<long, Guid> accounts = [];
        private readonly Dictionary<Guid, (int Revision, string Document)> passports = [];
        internal bool Down { get; set; }
        internal bool Gateway { get; set; }
        internal bool LodestoneDown { get; set; }
        internal TimeSpan Delay { get; set; }
        internal string? LastClientHeader { get; private set; }
        internal string? LastIfMatch { get; private set; }
        internal int SessionChecks { get; private set; }

        internal HttpClient Client() => new(this, disposeHandler: false);
        internal WanderveilApi Api() => new(Client(), new Uri(Address + "/"), "0.0.0.1");
        internal void Add(long id, string name, string world) { lock (gate) characters[id] = (name, world, "-"); }
        internal void Introduce(long id, string text) { lock (gate) characters[id] = characters[id] with { Introduction = text }; }
        internal void EndSessions() { lock (gate) sessions.Clear(); }
        internal bool HasSession(string token) { lock (gate) return sessions.ContainsKey(token); }
        internal (int Revision, string Document)? Passport(Guid account) { lock (gate) return passports.TryGetValue(account, out var p) ? p : null; }

        internal void Edit(Guid account, Func<string, string> change)
        {
            lock (gate) { var (revision, document) = passports[account]; passports[account] = (revision + 1, change(document)); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            if (Down) throw new HttpRequestException("Fake outage.");
            if (Gateway) return new HttpResponseMessage(HttpStatusCode.BadGateway);
            LastClientHeader = request.Headers.TryGetValues(WanderveilApi.ClientHeader, out var values) ? values.Single() : null;
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (gate) return Route(request, path, body);
        }

        private HttpResponseMessage Route(HttpRequestMessage request, string path, string body)
        {
            if (request.Method == HttpMethod.Post && path == "/v1/auth/lodestone/challenges")
            {
                if (LodestoneDown) return Problem(503, "lodestone_unavailable", retryAfter: 30);
                var asked = JsonNode.Parse(body)!;
                var found = characters.FirstOrDefault(c => c.Value.Name == (string?)asked["name"] && c.Value.World == (string?)asked["world"]);
                if (found.Value.Name == null) return Problem(404, "character_not_found");
                var id = Guid.NewGuid().ToString("N")[..22];
                var code = "WV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                challenges[id] = (found.Key, code);
                return Json(201, new { challengeId = id, code, character = Character(found.Key), expiresAt = DateTimeOffset.UtcNow.AddMinutes(15), profileUrl = "https://lodestone.test/profile" });
            }
            if (request.Method == HttpMethod.Post && path.StartsWith("/v1/auth/lodestone/challenges/", StringComparison.Ordinal) && path.EndsWith("/verify", StringComparison.Ordinal))
            {
                var id = path.Split('/')[5];
                if (!challenges.TryGetValue(id, out var challenge)) return Problem(404, "challenge_not_found");
                if (!characters[challenge.Id].Introduction.Contains(challenge.Code, StringComparison.OrdinalIgnoreCase)) return Problem(422, "code_not_found");
                challenges.Remove(id);
                if (!accounts.TryGetValue(challenge.Id, out var account)) accounts[challenge.Id] = account = Guid.NewGuid();
                var token = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43];
                sessions[token] = account;
                return Json(200, new { account = Account(account, challenge.Id), expiresAt = DateTimeOffset.UtcNow.AddDays(30), token });
            }
            var bearer = request.Headers.Authorization?.Parameter;
            if (bearer == null || !sessions.TryGetValue(bearer, out var owner)) return Problem(401, "session_invalid");
            var lodestone = accounts.Single(a => a.Value == owner).Key;
            switch (request.Method.Method, path)
            {
                case ("GET", "/v1/session"):
                    SessionChecks++;
                    return Json(200, new { account = Account(owner, lodestone), expiresAt = DateTimeOffset.UtcNow.AddDays(30) });
                case ("DELETE", "/v1/session"):
                    sessions.Remove(bearer);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case ("GET", "/v1/passport"):
                    return passports.TryGetValue(owner, out var stored) ? Record(stored) : Problem(404, "passport_not_found");
                case ("PUT", "/v1/passport"):
                    var document = JsonNode.Parse(body)!["document"]!.ToJsonString();
                    LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                    var exists = passports.TryGetValue(owner, out var current);
                    if (request.Headers.IfNoneMatch.Any())
                    {
                        if (exists) return Problem(412, "revision_conflict");
                        passports[owner] = (1, document);
                        return Record(passports[owner]);
                    }
                    if (!exists || LastIfMatch != $"\"{current.Revision}\"") return Problem(412, "revision_conflict");
                    passports[owner] = (current.Revision + 1, document);
                    return Record(passports[owner]);
            }
            return Problem(404, "not_found");
        }

        private object Character(long id) => new { lodestoneId = id.ToString(), name = characters[id].Name, world = characters[id].World, dataCenter = "Materia" };
        private object Account(Guid account, long id) => new { accountId = account, character = Character(id), verifiedBy = "lodestone", verifiedAt = DateTimeOffset.UtcNow };

        private static HttpResponseMessage Record((int Revision, string Document) record)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"revision":{{record.Revision}},"updatedAt":"2026-10-20T12:00:00Z","document":{{record.Document}}}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new($"\"{record.Revision}\"");
            return response;
        }

        private static HttpResponseMessage Json(int status, object value) =>
            new((HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problem(int status, string code, int? retryAfter = null)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { type = "about:blank", title = code, status, code }), Encoding.UTF8, "application/problem+json"),
            };
            if (retryAfter is { } seconds) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(seconds));
            return response;
        }
    }
}
