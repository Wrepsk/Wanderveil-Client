using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wanderveil.Features;
using Wanderveil.Infrastructure.Storage;

namespace Wanderveil.Infrastructure.Online;

// A signed-in character on this PC: the game's content ID (never sent anywhere), the server it signed in to, its
// session token, and the verified character the server named.
internal sealed record SavedSession(ulong ContentId, Uri Server, string Token, AccountInfo Account, DateTimeOffset ExpiresAt)
{
    // The local profile its passport files use.
    internal string Profile => "lodestone-" + Account.Character.LodestoneId;
}

// Keeps tokens unreadable to other Windows users and other PCs (DPAPI, current user). Where that is unavailable (a
// Wine prefix without it, say), tokens are kept as they are, and the file says so.
internal interface ITokenProtector
{
    string Name { get; }
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

// Windows' own CryptProtectData/CryptUnprotectData (crypt32), called directly so the plugin carries no extra
// package. Wine implements them too.
internal sealed partial class DpapiProtector : ITokenProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Wanderveil session token");
    // CRYPTPROTECT_UI_FORBIDDEN: never show a prompt.
    private const int NoPrompt = 0x1;
    public string Name => "dpapi";
    public byte[] Protect(byte[] data) => Run(data, protect: true);
    public byte[] Unprotect(byte[] data) => Run(data, protect: false);

    private static byte[] Run(byte[] data, bool protect)
    {
        var input = GCHandle.Alloc(data, GCHandleType.Pinned);
        var salt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var inBlob = new Blob { Size = data.Length, Data = input.AddrOfPinnedObject() };
            var entropyBlob = new Blob { Size = Entropy.Length, Data = salt.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref inBlob, null, ref entropyBlob, 0, 0, NoPrompt, out var output)
                : CryptUnprotectData(ref inBlob, 0, ref entropyBlob, 0, 0, NoPrompt, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastPInvokeError());
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally
        {
            input.Free();
            salt.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, nint reserved, nint prompt, int flags, out Blob output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref Blob input, nint description, ref Blob entropy, nint reserved, nint prompt, int flags, out Blob output);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}

internal sealed class PlainProtector : ITokenProtector
{
    public string Name => "none";
    public byte[] Protect(byte[] data) => data;
    public byte[] Unprotect(byte[] data) => data;
}

// The sessions saved on this PC, one per character (`sessions.json`, format wanderveil.sessions, version 1).
// Loading happens once at startup; saves run on a background task. A token that cannot be unprotected (the file
// was copied from another PC or user) is dropped, so that character signs in again.
internal sealed class SessionStore : IDisposable
{
    internal const string Format = "wanderveil.sessions";
    internal const int Version = 1;
    private const long MaximumBytes = 1024 * 1024;
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(3);
    private readonly string path;
    private readonly ITokenProtector protector;
    private readonly BackgroundSaver<IReadOnlyList<SavedSession>> saver;
    private readonly Dictionary<ulong, SavedSession> sessions = [];
    private readonly object gate = new();

    internal SessionStore(string directory, ITokenProtector? protector = null, Action<Exception>? report = null)
    {
        path = Path.Combine(directory, "sessions.json");
        this.protector = protector ?? Choose();
        var kept = new List<string>();
        Loaded = SafeFile.Load(path, MaximumBytes, Parse, kept, out var loaded);
        Kept = kept;
        foreach (var session in loaded ?? []) sessions[session.ContentId] = session;
        saver = new BackgroundSaver<IReadOnlyList<SavedSession>>(Save, Loaded is LoadOutcome.Newer or LoadOutcome.Inaccessible, report);
    }

    internal LoadOutcome Loaded { get; }
    internal IReadOnlyList<string> Kept { get; }
    internal string Protection => protector.Name;
    internal SaveState State => saver.State;

    internal SavedSession? For(ulong contentId)
    {
        lock (gate) return sessions.GetValueOrDefault(contentId);
    }

    internal void Put(SavedSession session)
    {
        lock (gate) sessions[session.ContentId] = session;
        Queue();
    }

    // Forgets a character's session (signed out, refused by the server, or expired).
    internal void Remove(ulong contentId)
    {
        bool removed;
        lock (gate) removed = sessions.Remove(contentId);
        if (removed) Queue();
    }

    internal bool Flush(TimeSpan timeout) => saver.Flush(timeout);

    public void Dispose() => saver.Close(DisposeWait);

    private void Queue()
    {
        List<SavedSession> snapshot;
        lock (gate) snapshot = [.. sessions.Values];
        saver.Queue(snapshot);
    }

    private static ITokenProtector Choose()
    {
        try
        {
            var dpapi = new DpapiProtector();
            var probe = dpapi.Unprotect(dpapi.Protect([1, 2, 3]));
            return probe is [1, 2, 3] ? dpapi : new PlainProtector();
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new PlainProtector();
        }
    }

    private void Save(IReadOnlyList<SavedSession> snapshot)
    {
        var list = new JsonArray();
        foreach (var session in snapshot.OrderBy(s => s.ContentId))
        {
            var character = session.Account.Character;
            list.Add(new JsonObject
            {
                ["contentId"] = session.ContentId.ToString(CultureInfo.InvariantCulture),
                ["server"] = session.Server.ToString(),
                ["protection"] = protector.Name,
                ["token"] = Convert.ToBase64String(protector.Protect(Encoding.UTF8.GetBytes(session.Token))),
                ["expiresAt"] = session.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
                ["account"] = new JsonObject
                {
                    ["accountId"] = session.Account.AccountId.ToString(),
                    ["verifiedBy"] = session.Account.VerifiedBy,
                    ["verifiedAt"] = session.Account.VerifiedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["lodestoneId"] = character.LodestoneId,
                    ["name"] = character.Name,
                    ["world"] = character.World,
                    ["dataCenter"] = character.DataCenter,
                },
            });
        }
        var file = new JsonObject { ["format"] = Format, ["version"] = Version, ["sessions"] = list };
        SafeFile.Write(path, Encoding.UTF8.GetBytes(file.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
    }

    private IReadOnlyList<SavedSession> Parse(string text)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (JsonException exception) { throw new FormatException("Not a sessions file.", exception); }
        if (root is not JsonObject file || Text(file, "format") != Format) throw new FormatException("Not a Wanderveil sessions file.");
        var version = file["version"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new FormatException("No version.");
        if (version > Version) throw new NotSupportedException("A newer sessions file.");
        var result = new List<SavedSession>();
        foreach (var node in file["sessions"] as JsonArray ?? [])
        {
            // One unusable entry (a token from another PC, say) costs only that character a sign-in.
            if (node is not JsonObject entry || entry["account"] is not JsonObject account) continue;
            if (!ulong.TryParse(Text(entry, "contentId"), NumberStyles.None, CultureInfo.InvariantCulture, out var contentId) || contentId == 0) continue;
            if (!Uri.TryCreate(Text(entry, "server"), UriKind.Absolute, out var server) || !WanderveilApi.IsAllowed(server)) continue;
            if (!Guid.TryParse(Text(account, "accountId"), out var accountId)) continue;
            if (!DateTimeOffset.TryParse(Text(entry, "expiresAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)) continue;
            if (!DateTimeOffset.TryParse(Text(account, "verifiedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var verified)) continue;
            var lodestone = Text(account, "lodestoneId");
            if (lodestone.Length is 0 or > 12 || !lodestone.All(char.IsAsciiDigit)) continue;
            string token;
            try
            {
                var stored = Convert.FromBase64String(Text(entry, "token"));
                ITokenProtector reader = Text(entry, "protection") == protector.Name ? protector : Text(entry, "protection") == "none" ? new PlainProtector() : protector;
                token = Encoding.UTF8.GetString(reader.Unprotect(stored));
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or PlatformNotSupportedException) { continue; }
            if (token.Length != 43) continue;
            var character = new CharacterInfo(lodestone, Text(account, "name"), Text(account, "world"), Text(account, "dataCenter"));
            result.Add(new SavedSession(contentId, server, token, new AccountInfo(accountId, character, Text(account, "verifiedBy"), verified), expires));
        }
        return result;
    }

    private static string Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
