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

// Everything a passport's stamp file holds: the stamps in collection order and their placements, bottom
// layer first. Only placed stamps have placements, so empty pages are never stored.
internal sealed record StampBook(IReadOnlyList<StampInstance> Stamps, IReadOnlyList<PlacedStamp> Placements)
{
    internal static readonly StampBook Empty = new([], []);

    internal static StampBook Of(StampCollection collection) => new(collection.Stamps.ToArray(), collection.Placed.ToArray());

    internal bool SameAs(StampBook other) => Stamps.SequenceEqual(other.Stamps) && Placements.SequenceEqual(other.Placements);
}

internal sealed record StampLoad(StampLoadOutcome Outcome, StampBook Book, IReadOnlyList<string> Kept);

// A passport's stamps in one versioned JSON file per profile: stamps-<profile>.json in the given directory.
// Saving writes a temporary file, flushes it to disk, then replaces the old file in one step, keeping the
// previous save as stamps-<profile>.json.bak. A crash mid-save leaves the old file whole.
// Loading never deletes anything it cannot read: unreadable files are renamed aside and kept.
// Blocking file I/O: call it from startup or a background task, never from a draw callback.
internal sealed partial class StampStore(string directory)
{
    internal const string Format = "wanderveil.passport-stamps";
    internal const int Version = 2;
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
            return new StampLoad(StampLoadOutcome.Inaccessible, StampBook.Empty, kept);
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
        if (main.Kind == ReadKind.Ok) return new StampLoad(StampLoadOutcome.Loaded, main.Book, kept);
        if (main.Kind is ReadKind.Newer or ReadKind.Inaccessible) return new StampLoad(Outcome(main.Kind), StampBook.Empty, kept);
        var previous = Read(backup);
        if (previous.Kind is ReadKind.Newer or ReadKind.Inaccessible) return new StampLoad(Outcome(previous.Kind), StampBook.Empty, kept);
        if (main.Kind == ReadKind.Missing && previous.Kind == ReadKind.Missing) return new StampLoad(StampLoadOutcome.New, StampBook.Empty, kept);
        if (main.Kind == ReadKind.Unreadable) kept.Add(SetAside(path));
        if (previous.Kind == ReadKind.Ok)
        {
            // Put the backup back as the saved file, so the next save backs it up in turn.
            File.Copy(backup, path, overwrite: false);
            return new StampLoad(StampLoadOutcome.Recovered, previous.Book, kept);
        }
        if (previous.Kind == ReadKind.Unreadable) kept.Add(SetAside(backup));
        return new StampLoad(StampLoadOutcome.Unreadable, StampBook.Empty, kept);
    }

    internal void Save(string profile, StampBook book)
    {
        var path = PathFor(profile);
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        var bytes = Encoding.UTF8.GetBytes(Serialize(book));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(temporary, path);
    }

    internal static string Serialize(StampBook book)
    {
        var records = new JsonArray();
        foreach (var stamp in book.Stamps)
        {
            var design = new JsonObject
            {
                ["id"] = stamp.Design.Id,
                ["revision"] = stamp.Design.Revision,
                ["name"] = stamp.Design.Name,
                ["artwork"] = stamp.Design.Artwork,
                ["provenance"] = LocalDemo,
            };
            if (stamp.Design.Creator != null) design["creator"] = stamp.Design.Creator;
            var record = new JsonObject
            {
                ["id"] = stamp.Id,
                ["discoveryId"] = stamp.DiscoveryId,
                ["collectedAt"] = stamp.CollectedAt.ToString("O", CultureInfo.InvariantCulture),
                ["provenance"] = LocalDemo,
                ["design"] = design,
            };
            if (stamp.Source is { } source) record["source"] = new JsonObject { ["kind"] = source.Kind, ["title"] = source.Title };
            records.Add(record);
        }
        var placements = new JsonArray();
        foreach (var entry in book.Placements)
            placements.Add(new JsonObject
            {
                ["stamp"] = entry.Stamp.Id,
                ["page"] = entry.Placement.Sheet,
                ["x"] = entry.Placement.X,
                ["y"] = entry.Placement.Y,
                ["rotation"] = entry.Placement.Rotation,
            });
        var file = new JsonObject { ["format"] = Format, ["version"] = Version, ["stamps"] = records, ["placements"] = placements };
        // Readable as written (names stay as typed); the file is never embedded in HTML.
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // Reads a file's text. Throws FormatException when it is not a valid stamp file and
    // NotSupportedException when it is a later version this one does not know.
    internal static StampBook Parse(string text)
    {
        JsonObject file;
        try { file = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("A stamp file is a JSON object."); }
        catch (JsonException exception) { throw new FormatException("A stamp file is not valid JSON.", exception); }
        if (Text(file, "format") != Format) throw new FormatException("Not a Wanderveil stamp file.");
        var version = Number(file, "version");
        if (version > Version) throw new NotSupportedException($"Stamp file version {version} is newer than {Version}.");
        if (version < 1) throw new FormatException("A stamp file version starts at 1.");
        // Migrations: each step upgrades the object one version in memory; the next save writes the current
        // version, and the backup keeps the old one.
        if (version == 1) Migrate1To2(file);
        if (file["stamps"] is not JsonArray records) throw new FormatException("A stamp file lists its stamps.");
        if (file["placements"] is not JsonArray placements) throw new FormatException("A stamp file lists its placements.");
        var stamps = records.Select(record => record as JsonObject ?? throw new FormatException("A stamp is a JSON object.")).Select(Stamp).ToList();
        var byId = new Dictionary<string, StampInstance>(StringComparer.Ordinal);
        foreach (var stamp in stamps) byId.TryAdd(stamp.Id, stamp);
        var placed = placements.Select(entry => entry as JsonObject ?? throw new FormatException("A placement is a JSON object.")).Select(entry =>
        {
            if (!byId.TryGetValue(Required(entry, "stamp"), out var stamp)) throw new FormatException("A placement names a stamp the file does not hold.");
            var placement = new StampPlacement(Number(entry, "page"), Real(entry, "x"), Real(entry, "y"), Real(entry, "rotation"));
            return placement.IsValid ? new PlacedStamp(stamp, placement) : throw new FormatException("A placement is outside its page.");
        }).ToList();
        try { _ = new StampCollection(null, stamps, placed); }
        catch (ArgumentException exception) { throw new FormatException(exception.Message, exception); }
        return new StampBook(stamps, placed);
    }

    // Version 1 had no placements, creators, or sources. Its stamps become unplaced, waiting for the owner
    // to press them onto a page; nothing is placed for them.
    private static void Migrate1To2(JsonObject file)
    {
        file["placements"] = new JsonArray();
        file["version"] = 2;
    }

    private static StampInstance Stamp(JsonObject record)
    {
        var design = record["design"] as JsonObject ?? throw new FormatException("A stamp records its design.");
        if (Text(record, "provenance") != LocalDemo || Text(design, "provenance") != LocalDemo) throw new FormatException("Only local demo stamps can be stored.");
        if (!DateTimeOffset.TryParseExact(Text(record, "collectedAt"), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var collected))
            throw new FormatException("A stamp records when it was collected.");
        var revision = Number(design, "revision");
        if (revision < 1) throw new FormatException("A design revision starts at 1.");
        StampSource? source = null;
        if (record["source"] is JsonObject from) source = new StampSource(Required(from, "kind"), Required(from, "title"));
        else if (record["source"] != null) throw new FormatException("A stamp's source is a JSON object.");
        var creator = design["creator"] == null ? null : Required(design, "creator");
        return new StampInstance(Required(record, "id"), new StampDesign(Required(design, "id"), revision, Required(design, "name"), Number(design, "artwork"), StampProvenance.LocalDemo, creator),
            Required(record, "discoveryId"), collected, StampProvenance.LocalDemo, source);
    }

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string Required(JsonObject node, string name) => Text(node, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : throw new FormatException($"A stamp file is missing {name}.");
    private static int Number(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new FormatException($"A stamp file is missing {name}.");
    private static float Real(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<double>(out var number) ? (float)number : throw new FormatException($"A stamp file is missing {name}.");

    private enum ReadKind { Missing, Ok, Unreadable, Newer, Inaccessible }

    private readonly record struct ReadResult(ReadKind Kind, StampBook Book);

    private static ReadResult Read(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return new ReadResult(ReadKind.Missing, StampBook.Empty);
            if (new FileInfo(path).Length > MaximumBytes) return new ReadResult(ReadKind.Unreadable, StampBook.Empty);
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ReadResult(ReadKind.Inaccessible, StampBook.Empty);
        }
        try { return new ReadResult(ReadKind.Ok, Parse(text)); }
        catch (NotSupportedException) { return new ReadResult(ReadKind.Newer, StampBook.Empty); }
        catch (FormatException) { return new ReadResult(ReadKind.Unreadable, StampBook.Empty); }
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
