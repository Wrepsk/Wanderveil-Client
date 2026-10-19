using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wanderveil.Features;
using Wanderveil.Features.Passport;

namespace Wanderveil.Infrastructure.Storage;

internal sealed record PassportLoad(LoadOutcome Outcome, PassportIdentity? Identity, IReadOnlyList<string> Kept);

// A passport's identity (what its holder wrote, its number and issue date, where their picture sits) in one
// versioned JSON file per profile: passport-<profile>.json, saved and recovered as a SafeFile. The holder's
// picture is its own file beside it, portrait-<profile>.image, a copy of the image they chose, replaced the
// same safe way. Stamps live in their own file, so a later appearance reset never touches them.
// Blocking file I/O: call it from startup or a background task, never from a draw callback.
internal sealed class PassportStore(string directory)
{
    internal const string Format = "wanderveil.passport";
    internal const int Version = 1;
    private const long MaximumBytes = 256 * 1024;
    // A chosen picture is kept as chosen up to this size; it is scaled down when shown.
    internal const long MaximumPictureBytes = 16 * 1024 * 1024;

    internal string PathFor(string profile) => Path.Combine(directory, $"passport-{StorageProfile.Checked(profile)}.json");

    internal string PicturePath(string profile) => Path.Combine(directory, $"portrait-{StorageProfile.Checked(profile)}.image");

    internal PassportLoad Load(string profile)
    {
        var kept = new List<string>();
        var outcome = SafeFile.Load(PathFor(profile), MaximumBytes, Parse, kept, out var identity);
        return new PassportLoad(outcome, identity, kept);
    }

    internal void Save(string profile, PassportIdentity identity) => SafeFile.Write(PathFor(profile), Encoding.UTF8.GetBytes(Serialize(identity)));

    internal void SavePicture(string profile, ReadOnlySpan<byte> image) => SafeFile.Write(PicturePath(profile), image);

    // The saved picture, or null when there is none or it is too large to be one this version kept.
    internal byte[]? ReadPicture(string profile)
    {
        var path = PicturePath(profile);
        if (!File.Exists(path) || new FileInfo(path).Length > MaximumPictureBytes) return null;
        return File.ReadAllBytes(path);
    }

    internal void DeletePicture(string profile)
    {
        var path = PicturePath(profile);
        SafeFile.TryDelete(path);
        SafeFile.TryDelete(path + ".bak");
    }

    internal static string Serialize(PassportIdentity identity)
    {
        var file = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["number"] = identity.Number,
        };
        if (identity.Issued is { } issued) file["issued"] = issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var field in Enum.GetValues<IdentityField>()) file[Key(field)] = identity[field];
        if (identity.Portrait is { } crop) file["portrait"] = new JsonObject { ["x"] = crop.X, ["y"] = crop.Y, ["zoom"] = crop.Zoom };
        // Readable as written (names stay as typed); the file is never embedded in HTML.
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // Reads a file's text. Throws FormatException when it is not a valid passport file and
    // NotSupportedException when it is a later version this one does not know. Text is normalized as the
    // holder would have been allowed to type it; missing fields are empty.
    internal static PassportIdentity Parse(string text)
    {
        JsonObject file;
        try { file = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("A passport file is a JSON object."); }
        catch (JsonException exception) { throw new FormatException("A passport file is not valid JSON.", exception); }
        if (Text(file, "format") != Format) throw new FormatException("Not a Wanderveil passport file.");
        var version = file["version"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new FormatException("A passport file has a version.");
        if (version > Version) throw new NotSupportedException($"Passport file version {version} is newer than {Version}.");
        if (version < 1) throw new FormatException("A passport file version starts at 1.");
        var passportNumber = Text(file, "number");
        if (string.IsNullOrWhiteSpace(passportNumber)) throw new FormatException("A passport file has its number.");
        DateOnly? issued = null;
        if (file["issued"] != null)
            issued = DateOnly.TryParseExact(Text(file, "issued"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new FormatException("A passport's issue date is a date.");
        var identity = new PassportIdentity("", "", "", "", "", passportNumber) { Issued = issued };
        foreach (var field in Enum.GetValues<IdentityField>())
            if (file[Key(field)] != null) identity = identity.With(field, Text(file, Key(field)) ?? throw new FormatException($"A passport's {Key(field)} is text."));
        if (file["portrait"] is JsonObject portrait)
        {
            var crop = new PortraitCrop(Real(portrait, "x"), Real(portrait, "y"), Real(portrait, "zoom"));
            identity = identity with { Portrait = crop.IsValid ? crop : throw new FormatException("A portrait sits inside its frame.") };
        }
        else if (file["portrait"] != null) throw new FormatException("A passport's portrait is a JSON object.");
        return identity.Normalize();
    }

    private static string Key(IdentityField field) => JsonNamingPolicy.CamelCase.ConvertName(field.ToString());

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static float Real(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<double>(out var number) ? (float)number : throw new FormatException($"A passport file is missing {name}.");
}
