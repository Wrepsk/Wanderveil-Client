using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wanderveil.Features.Stamps;

namespace Wanderveil.Infrastructure.Storage;

internal sealed record StampLoad(StampLoadOutcome Outcome, IReadOnlyList<StampInstance> Stamps, IReadOnlyList<string> Kept);

// A passport's stamps in one versioned JSON file per profile: stamps-<profile>.json in the given directory.
// Saving writes a temporary file, flushes it to disk, then replaces the old file in one step, keeping the
// previous save as stamps-<profile>.json.bak. A crash mid-save leaves the old file whole.
// Loading never deletes anything it cannot read: unreadable files are renamed aside and kept.
// Blocking file I/O: call it from startup or a background task, never from a draw callback.
internal sealed partial class StampStore(string directory)
{
    internal const string Format = "wanderveil.passport-stamps";
    internal const int Version = 1;
    private const long MaximumBytes = 4 * 1024 * 1024;
    private const string LocalDemo = "local-demo";

    // Profiles name files, so they are restricted to a safe, stable spelling.
    internal static bool IsProfile(string profile) => ProfilePattern().IsMatch(profile);

    internal string PathFor(string profile) => Path.Combine(directory, $"stamps-{Checked(profile)}.json");

    internal StampLoad Load(string profile)
    {
        var path = PathFor(profile);
        var backup = path + ".bak";
        var kept = new List<string>();
        try { return Recover(path, backup, kept); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Setting a file aside or restoring the backup failed: touch nothing more and save nothing.
            return new StampLoad(StampLoadOutcome.Inaccessible, [], kept);
        }
    }

    private static StampLoad Recover(string path, string backup, List<string> kept)
    {
        // A leftover temporary file is a save interrupted before its final step. If it is complete it is the
        // newest save, so finish that step; otherwise drop it, since the file it was replacing is intact.
        var temporary = path + ".tmp";
        var interrupted = Read(temporary).Kind;
        if (interrupted == ReadKind.Ok)
        {
            if (File.Exists(path)) File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        else if (interrupted == ReadKind.Unreadable) TryDelete(temporary);
        var main = Read(path);
        if (main.Kind == ReadKind.Ok) return new StampLoad(StampLoadOutcome.Loaded, main.Stamps, kept);
        if (main.Kind is ReadKind.Newer or ReadKind.Inaccessible) return new StampLoad(Outcome(main.Kind), [], kept);
        var previous = Read(backup);
        if (previous.Kind is ReadKind.Newer or ReadKind.Inaccessible) return new StampLoad(Outcome(previous.Kind), [], kept);
        if (main.Kind == ReadKind.Missing && previous.Kind == ReadKind.Missing) return new StampLoad(StampLoadOutcome.New, [], kept);
        if (main.Kind == ReadKind.Unreadable) kept.Add(SetAside(path));
        if (previous.Kind == ReadKind.Ok)
        {
            // Put the backup back as the saved file, so the next save backs it up in turn.
            File.Copy(backup, path, overwrite: false);
            return new StampLoad(StampLoadOutcome.Recovered, previous.Stamps, kept);
        }
        if (previous.Kind == ReadKind.Unreadable) kept.Add(SetAside(backup));
        return new StampLoad(StampLoadOutcome.Unreadable, [], kept);
    }

    internal void Save(string profile, IReadOnlyList<StampInstance> stamps)
    {
        var path = PathFor(profile);
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        var bytes = Encoding.UTF8.GetBytes(Serialize(stamps));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(temporary, path);
    }

    internal static string Serialize(IReadOnlyList<StampInstance> stamps)
    {
        var records = new JsonArray();
        foreach (var stamp in stamps)
            records.Add(new JsonObject
            {
                ["id"] = stamp.Id,
                ["discoveryId"] = stamp.DiscoveryId,
                ["collectedAt"] = stamp.CollectedAt.ToString("O", CultureInfo.InvariantCulture),
                ["provenance"] = LocalDemo,
                ["design"] = new JsonObject
                {
                    ["id"] = stamp.Design.Id,
                    ["revision"] = stamp.Design.Revision,
                    ["name"] = stamp.Design.Name,
                    ["artwork"] = stamp.Design.Artwork,
                    ["provenance"] = LocalDemo,
                },
            });
        var file = new JsonObject { ["format"] = Format, ["version"] = Version, ["stamps"] = records };
        // Readable as written (names stay as typed); the file is never embedded in HTML.
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // Reads a file's text into stamps. Throws FormatException when it is not a valid stamp file and
    // NotSupportedException when it is a later version this one does not know.
    internal static IReadOnlyList<StampInstance> Parse(string text)
    {
        JsonObject file;
        try { file = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("A stamp file is a JSON object."); }
        catch (JsonException exception) { throw new FormatException("A stamp file is not valid JSON.", exception); }
        if (Text(file, "format") != Format) throw new FormatException("Not a Wanderveil stamp file.");
        var version = Number(file, "version");
        if (version > Version) throw new NotSupportedException($"Stamp file version {version} is newer than {Version}.");
        if (version < 1) throw new FormatException("A stamp file version starts at 1.");
        // Migrations go here: each step upgrades the object one version in memory, and the next save writes
        // the current version (the backup keeps the old one). Version 1 is the first, so there are none yet.
        if (file["stamps"] is not JsonArray records) throw new FormatException("A stamp file lists its stamps.");
        var stamps = records.Select(record => record as JsonObject ?? throw new FormatException("A stamp is a JSON object.")).Select(Stamp).ToList();
        try { _ = new StampCollection(null, stamps); }
        catch (ArgumentException exception) { throw new FormatException(exception.Message, exception); }
        return stamps;
    }

    private static StampInstance Stamp(JsonObject record)
    {
        var design = record["design"] as JsonObject ?? throw new FormatException("A stamp records its design.");
        if (Text(record, "provenance") != LocalDemo || Text(design, "provenance") != LocalDemo) throw new FormatException("Only local demo stamps can be stored.");
        if (!DateTimeOffset.TryParseExact(Text(record, "collectedAt"), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var collected))
            throw new FormatException("A stamp records when it was collected.");
        var revision = Number(design, "revision");
        if (revision < 1) throw new FormatException("A design revision starts at 1.");
        return new StampInstance(Required(record, "id"), new StampDesign(Required(design, "id"), revision, Required(design, "name"), Number(design, "artwork"), StampProvenance.LocalDemo),
            Required(record, "discoveryId"), collected, StampProvenance.LocalDemo);
    }

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string Required(JsonObject node, string name) => Text(node, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : throw new FormatException($"A stamp file is missing {name}.");
    private static int Number(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new FormatException($"A stamp file is missing {name}.");

    private enum ReadKind { Missing, Ok, Unreadable, Newer, Inaccessible }

    private readonly record struct ReadResult(ReadKind Kind, IReadOnlyList<StampInstance> Stamps);

    private static ReadResult Read(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return new ReadResult(ReadKind.Missing, []);
            if (new FileInfo(path).Length > MaximumBytes) return new ReadResult(ReadKind.Unreadable, []);
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ReadResult(ReadKind.Inaccessible, []);
        }
        try { return new ReadResult(ReadKind.Ok, Parse(text)); }
        catch (NotSupportedException) { return new ReadResult(ReadKind.Newer, []); }
        catch (FormatException) { return new ReadResult(ReadKind.Unreadable, []); }
    }

    private static StampLoadOutcome Outcome(ReadKind kind) => kind == ReadKind.Newer ? StampLoadOutcome.Newer : StampLoadOutcome.Inaccessible;

    // Renames an unreadable file aside, never over another, and returns where it went.
    private static string SetAside(string path)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.unreadable-{stamp}";
        for (var index = 2; File.Exists(target); index++) target = $"{path}.unreadable-{stamp}-{index}";
        File.Move(path, target);
        return target;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string Checked(string profile) => IsProfile(profile) ? profile : throw new ArgumentException("A profile is 1-64 lowercase letters, digits, or hyphens.", nameof(profile));

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex ProfilePattern();
}
