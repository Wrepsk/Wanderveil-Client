using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Wanderveil.Infrastructure.Online;

// The records of the public contract (docs/api/v1.yaml). Unknown fields are ignored, so the server may add them.
internal sealed record CharacterInfo(string LodestoneId, string Name, string World, string DataCenter);
internal sealed record LodestoneChallenge(string ChallengeId, string Code, CharacterInfo Character, DateTimeOffset ExpiresAt, string ProfileUrl);
internal sealed record AccountInfo(Guid AccountId, CharacterInfo Character, string VerifiedBy, DateTimeOffset VerifiedAt);
internal sealed record SessionInfo(AccountInfo Account, DateTimeOffset ExpiresAt);
internal sealed record SessionGrant(AccountInfo Account, DateTimeOffset ExpiresAt, string Token);
internal sealed record PassportRecord(int Revision, DateTimeOffset UpdatedAt, JsonElement Document);
// A picture as saved online: its revision (from the ETag) and its bytes.
internal sealed record PictureFile(int Revision, byte[] Bytes, string ContentType);
internal sealed record PictureInfo(int Revision, DateTimeOffset UpdatedAt, string ContentType, int Width, int Height, int Bytes);

// Sharing (ADR-0005; contract 1.1.0).
internal sealed record SharingSetting(string Visibility);
internal sealed record Player(Guid AccountId, CharacterInfo Character, string VerifiedBy);
internal sealed record CharacterName(string Name, string World);
internal sealed record ViewRequest(Guid RequestId, string State, Player? From, CharacterName? To, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
internal sealed record ListedPlayer(Guid AccountId, CharacterInfo Character, string VerifiedBy, DateTimeOffset Since);
internal sealed record Visit(Player Owner, Dictionary<string, int> Revisions);
internal sealed record XivAuthFlow(string FlowId, string UserCode, Uri VerificationUri, Uri? VerificationUriComplete, int Interval, DateTimeOffset ExpiresAt)
{
    // The page to open: with the code filled in where XIVAuth gives one.
    internal Uri Page => VerificationUriComplete ?? VerificationUri;
}
// "pending" (keep polling) or "approved" (with the session).
internal sealed record XivAuthPoll(string Status, SessionGrant? Session);

// What a call came to. Offline: no answer (no network, a timeout, the server or its proxy down). Otherwise the HTTP
// status and, for a refusal, the contract's stable code. Clients branch on Code, never on Detail.
internal sealed record ApiResult<T>(T? Value, int Status, string? Code = null, string? Detail = null, TimeSpan? RetryAfter = null)
{
    internal bool Ok => Status is >= 200 and < 300;
    internal bool Offline => Status == 0;
    // The session is gone (expired, signed out, unknown): sign in again, never retry.
    internal bool SignedOut => Status == 401;

    internal static ApiResult<T> NoAnswer(string detail) => new(default, 0, Detail: detail);
}

// Talks to a Wanderveil server over HTTPS (plain HTTP only to this PC, for a server run locally). Every call
// names the plugin's version; none throws for network trouble, which comes back as Offline. Call it off the draw
// path: everything here is asynchronous network work.
internal sealed class WanderveilApi
{
    internal const string ClientHeader = "Wanderveil-Client";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient http;
    private readonly string version;

    internal WanderveilApi(HttpClient http, Uri server, string version)
    {
        if (!IsAllowed(server)) throw new ArgumentException("The server must use HTTPS, or HTTP only on this PC.", nameof(server));
        this.http = http;
        this.version = version;
        Server = server;
    }

    internal Uri Server { get; }

    // HTTPS anywhere; plain HTTP only to a loopback address, so a token never crosses a network unencrypted.
    internal static bool IsAllowed(Uri server) =>
        server.IsAbsoluteUri && server.Query.Length == 0 && server.Fragment.Length == 0 && string.IsNullOrEmpty(server.UserInfo) &&
        (server.Scheme == Uri.UriSchemeHttps || server.Scheme == Uri.UriSchemeHttp && server.IsLoopback);

    // A server address as the holder typed it, or null when it is not one Wanderveil will talk to.
    internal static Uri? ParseServer(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var server)) return null;
        return IsAllowed(server) ? server : null;
    }

    internal Task<ApiResult<LodestoneChallenge>> StartLodestone(string name, string world, CancellationToken cancellationToken) =>
        Send<LodestoneChallenge>(HttpMethod.Post, "v1/auth/lodestone/challenges", null, Body(new { name, world }), cancellationToken);

    internal Task<ApiResult<SessionGrant>> VerifyLodestone(string challengeId, CancellationToken cancellationToken) =>
        Send<SessionGrant>(HttpMethod.Post, $"v1/auth/lodestone/challenges/{Uri.EscapeDataString(challengeId)}/verify", null, null, cancellationToken);

    internal Task<ApiResult<XivAuthFlow>> StartXivAuth(string name, string world, CancellationToken cancellationToken) =>
        Send<XivAuthFlow>(HttpMethod.Post, "v1/auth/xivauth/flows", null, Body(new { name, world }), cancellationToken);

    internal Task<ApiResult<XivAuthPoll>> PollXivAuth(string flowId, CancellationToken cancellationToken) =>
        Send<XivAuthPoll>(HttpMethod.Post, $"v1/auth/xivauth/flows/{Uri.EscapeDataString(flowId)}/poll", null, null, cancellationToken);

    internal Task<ApiResult<SessionInfo>> Session(string token, CancellationToken cancellationToken) =>
        Send<SessionInfo>(HttpMethod.Get, "v1/session", token, null, cancellationToken);

    internal Task<ApiResult<object>> SignOut(string token, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, "v1/session", token, null, cancellationToken);

    // Deletes the signed-in account and everything the server keeps for it.
    internal Task<ApiResult<object>> DeleteAccount(string token, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, "v1/account", token, null, cancellationToken);

    internal Task<ApiResult<PassportRecord>> GetPassport(string token, CancellationToken cancellationToken) => GetDocument(token, "v1/passport", cancellationToken);

    // Saves over the revision named, or as the first save when there is none.
    internal Task<ApiResult<PassportRecord>> PutPassport(string token, string document, int? over, CancellationToken cancellationToken) =>
        PutDocument(token, "v1/passport", document, over, false, cancellationToken);

    // A passport part kept as a document (v1/passport, v1/passport/stamps, v1/passport/marks), compressed in transit
    // when the server offers it.
    internal Task<ApiResult<PassportRecord>> GetDocument(string token, string path, CancellationToken cancellationToken) =>
        Send<PassportRecord>(HttpMethod.Get, path, token, null, cancellationToken, request => request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip")));

    // Saves a part over the revision named (or as its first save); gzip compresses it on the way (the drawing).
    internal Task<ApiResult<PassportRecord>> PutDocument(string token, string path, string document, int? over, bool gzip, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"document\":" + document + "}");
        if (gzip)
        {
            using var compressed = new MemoryStream();
            using (var stream = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) stream.Write(bytes);
            bytes = compressed.ToArray();
        }
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        if (gzip) content.Headers.ContentEncoding.Add("gzip");
        return Send<PassportRecord>(HttpMethod.Put, path, token, content, cancellationToken, request => Over(request, over));
    }

    internal async Task<ApiResult<PictureFile>> GetPicture(string token, CancellationToken cancellationToken)
    {
        var answer = await Exchange(HttpMethod.Get, "v1/passport/picture", token, null, cancellationToken).ConfigureAwait(false);
        if (!answer.Result.Ok) return new(null, answer.Result.Status, answer.Result.Code, answer.Result.Detail, answer.Result.RetryAfter);
        if (answer.Revision is not { } revision || answer.Body.Length == 0) return ApiResult<PictureFile>.NoAnswer("The Wanderveil server's picture could not be read.");
        return new(new PictureFile(revision, answer.Body, answer.ContentType ?? "application/octet-stream"), answer.Result.Status);
    }

    internal Task<ApiResult<PictureInfo>> PutPicture(string token, byte[] bytes, string contentType, int? over, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return Send<PictureInfo>(HttpMethod.Put, "v1/passport/picture", token, content, cancellationToken, request => Over(request, over));
    }

    internal Task<ApiResult<object>> DeletePicture(string token, int over, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, "v1/passport/picture", token, null, cancellationToken, request => Over(request, over));

    internal Task<ApiResult<SharingSetting>> GetSharing(string token, CancellationToken cancellationToken) =>
        Send<SharingSetting>(HttpMethod.Get, "v1/sharing", token, null, cancellationToken);

    internal Task<ApiResult<SharingSetting>> PutSharing(string token, string visibility, CancellationToken cancellationToken) =>
        Send<SharingSetting>(HttpMethod.Put, "v1/sharing", token, Body(new { visibility }), cancellationToken);

    // Always 202 for a valid name and world: the answer never says whether anyone received it.
    internal Task<ApiResult<object>> AskToView(string token, string name, string world, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Post, "v1/view-requests", token, Body(new { name, world }), cancellationToken);

    internal Task<ApiResult<ViewRequest[]>> ViewRequests(string token, bool incoming, CancellationToken cancellationToken) =>
        Send<ViewRequest[]>(HttpMethod.Get, "v1/view-requests?direction=" + (incoming ? "incoming" : "outgoing"), token, null, cancellationToken);

    internal Task<ApiResult<object>> Approve(string token, Guid request, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Post, $"v1/view-requests/{request}/approve", token, null, cancellationToken);

    internal Task<ApiResult<object>> Decline(string token, Guid request, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Post, $"v1/view-requests/{request}/decline", token, null, cancellationToken);

    internal Task<ApiResult<object>> Withdraw(string token, Guid request, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, $"v1/view-requests/{request}", token, null, cancellationToken);

    internal Task<ApiResult<ListedPlayer[]>> Viewers(string token, CancellationToken cancellationToken) =>
        Send<ListedPlayer[]>(HttpMethod.Get, "v1/viewers", token, null, cancellationToken);

    internal Task<ApiResult<object>> RemoveViewer(string token, Guid account, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, $"v1/viewers/{account}", token, null, cancellationToken);

    internal Task<ApiResult<ListedPlayer[]>> Blocks(string token, CancellationToken cancellationToken) =>
        Send<ListedPlayer[]>(HttpMethod.Get, "v1/blocks", token, null, cancellationToken);

    internal Task<ApiResult<object>> Block(string token, Guid account, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Put, $"v1/blocks/{account}", token, null, cancellationToken);

    internal Task<ApiResult<object>> Unblock(string token, Guid account, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, $"v1/blocks/{account}", token, null, cancellationToken);

    internal Task<ApiResult<Visit>> LookUp(string token, string name, string world, CancellationToken cancellationToken) =>
        Send<Visit>(HttpMethod.Get, $"v1/visits?name={Uri.EscapeDataString(name)}&world={Uri.EscapeDataString(world)}", token, null, cancellationToken);

    // One part of a visible passport (passport, stamps, marks): JSON as the owner's own record.
    internal Task<ApiResult<PassportRecord>> VisitDocument(string token, Guid owner, string part, CancellationToken cancellationToken) =>
        Send<PassportRecord>(HttpMethod.Get, $"v1/visits/{owner}/{part}", token, null, cancellationToken, request => request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip")));

    internal async Task<ApiResult<PictureFile>> VisitPicture(string token, Guid owner, CancellationToken cancellationToken)
    {
        var answer = await Exchange(HttpMethod.Get, $"v1/visits/{owner}/picture", token, null, cancellationToken).ConfigureAwait(false);
        if (!answer.Result.Ok) return new(null, answer.Result.Status, answer.Result.Code, answer.Result.Detail, answer.Result.RetryAfter);
        if (answer.Revision is not { } revision || answer.Body.Length == 0) return ApiResult<PictureFile>.NoAnswer("The Wanderveil server's picture could not be read.");
        return new(new PictureFile(revision, answer.Body, answer.ContentType ?? "application/octet-stream"), answer.Result.Status);
    }

    internal Task<ApiResult<object>> Report(string token, Guid owner, string part, string reason, string? note, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Post, "v1/reports", token, Body(new { accountId = owner, part, reason, note }), cancellationToken);

    private static void Over(HttpRequestMessage request, int? over)
    {
        if (over is { } revision) request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{revision.ToString(CultureInfo.InvariantCulture)}\""));
        else request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
    }

    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json");

    private async Task<ApiResult<T>> Send<T>(HttpMethod method, string path, string? token, HttpContent? content, CancellationToken cancellationToken, Action<HttpRequestMessage>? prepare = null)
    {
        var answer = await Exchange(method, path, token, content, cancellationToken, prepare).ConfigureAwait(false);
        if (!answer.Result.Ok) return new(default, answer.Result.Status, answer.Result.Code, answer.Result.Detail, answer.Result.RetryAfter);
        if (answer.Body.Length == 0) return new(default, answer.Result.Status);
        try { return new(JsonSerializer.Deserialize<T>(answer.Body, Json), answer.Result.Status); }
        catch (JsonException) { return ApiResult<T>.NoAnswer("The Wanderveil server's answer could not be read."); }
    }

    private readonly record struct Answer(ApiResult<object> Result, byte[] Body, int? Revision, string? ContentType);

    // One request and its answer as bytes (expanded when gzip-compressed), with the revision from its ETag.
    private async Task<Answer> Exchange(HttpMethod method, string path, string? token, HttpContent? content, CancellationToken cancellationToken, Action<HttpRequestMessage>? prepare = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(Server, path)) { Content = content };
        request.Headers.TryAddWithoutValidation(ClientHeader, version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        prepare?.Invoke(request);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (HttpRequestException) { return new(ApiResult<object>.NoAnswer("The Wanderveil server could not be reached."), [], null, null); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(ApiResult<object>.NoAnswer("The Wanderveil server did not answer in time."), [], null, null); }
        using (response)
        {
            var status = (int)response.StatusCode;
            byte[] body;
            try
            {
                body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (response.Content.Headers.ContentEncoding.Contains("gzip"))
                {
                    using var expanded = new MemoryStream();
                    using (var stream = new GZipStream(new MemoryStream(body), CompressionMode.Decompress)) await stream.CopyToAsync(expanded, cancellationToken).ConfigureAwait(false);
                    body = expanded.ToArray();
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException) { return new(ApiResult<object>.NoAnswer("The Wanderveil server's answer was cut off."), [], null, null); }
            var revision = response.Headers.ETag is { } tag && int.TryParse(tag.Tag.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : (int?)null;
            if (response.IsSuccessStatusCode) return new(new(null, status), body, revision, response.Content.Headers.ContentType?.MediaType);
            var (code, detail) = Problem(Encoding.UTF8.GetString(body));
            // A gateway with no word from Wanderveil itself is the server being down, not a refusal.
            if (code == null && response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                return new(ApiResult<object>.NoAnswer("The Wanderveil server is not available right now."), [], null, null);
            return new(new(null, status, code, detail, response.Headers.RetryAfter?.Delta), body, revision, null);
        }
    }

    private static (string? Code, string? Detail) Problem(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            string? Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return (Read("code"), Read("detail") ?? Read("title"));
        }
        catch (JsonException) { return (null, null); }
    }
}
