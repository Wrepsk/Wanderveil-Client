using System;
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

    internal Task<ApiResult<SessionInfo>> Session(string token, CancellationToken cancellationToken) =>
        Send<SessionInfo>(HttpMethod.Get, "v1/session", token, null, cancellationToken);

    internal Task<ApiResult<object>> SignOut(string token, CancellationToken cancellationToken) =>
        Send<object>(HttpMethod.Delete, "v1/session", token, null, cancellationToken);

    internal Task<ApiResult<PassportRecord>> GetPassport(string token, CancellationToken cancellationToken) =>
        Send<PassportRecord>(HttpMethod.Get, "v1/passport", token, null, cancellationToken);

    // Saves over the revision named, or as the first save when there is none.
    internal Task<ApiResult<PassportRecord>> PutPassport(string token, string document, int? over, CancellationToken cancellationToken)
    {
        var content = new StringContent("{\"document\":" + document + "}", Encoding.UTF8, "application/json");
        return Send<PassportRecord>(HttpMethod.Put, "v1/passport", token, content, cancellationToken, request =>
        {
            if (over is { } revision) request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{revision}\""));
            else request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        });
    }

    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json");

    private async Task<ApiResult<T>> Send<T>(HttpMethod method, string path, string? token, HttpContent? content, CancellationToken cancellationToken, Action<HttpRequestMessage>? prepare = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(Server, path)) { Content = content };
        request.Headers.TryAddWithoutValidation(ClientHeader, version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        prepare?.Invoke(request);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (HttpRequestException) { return ApiResult<T>.NoAnswer("The Wanderveil server could not be reached."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return ApiResult<T>.NoAnswer("The Wanderveil server did not answer in time."); }
        using (response)
        {
            var status = (int)response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Delta;
            string text;
            try { text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); }
            catch (HttpRequestException) { return ApiResult<T>.NoAnswer("The Wanderveil server's answer was cut off."); }
            if (response.IsSuccessStatusCode)
            {
                if (text.Length == 0) return new(default, status);
                try { return new(JsonSerializer.Deserialize<T>(text, Json), status); }
                catch (JsonException) { return ApiResult<T>.NoAnswer("The Wanderveil server's answer could not be read."); }
            }
            var (code, detail) = Problem(text);
            // A gateway with no word from Wanderveil itself is the server being down, not a refusal.
            if (code == null && response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                return ApiResult<T>.NoAnswer("The Wanderveil server is not available right now.");
            return new(default, status, code, detail, retryAfter);
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
