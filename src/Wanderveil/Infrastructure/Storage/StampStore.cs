using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wanderveil.Features;
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

internal sealed record StampLoad(LoadOutcome Outcome, StampBook Book, IReadOnlyList<string> Kept);

// A passport's stamps in one versioned JSON file per profile: stamps-<profile>.json in the given directory,
// saved and recovered as a SafeFile (the previous save kept as stamps-<profile>.json.bak).
// Blocking file I/O: call it from startup or a background task, never from a draw callback.
internal sealed class StampStore(string directory)
{
    internal const string Format = "wanderveil.passport-stamps";
    internal const int Version = 2;
    private const long MaximumBytes = 4 * 1024 * 1024;
    private const string LocalDemo = "local-demo";

    internal static bool IsProfile(string profile) => StorageProfile.IsValid(profile);

    internal string PathFor(string profile) => Path.Combine(directory, $"stamps-{StorageProfile.Checked(profile)}.json");

    internal StampLoad Load(string profile)
    {
        var kept = new List<string>();
        var outcome = SafeFile.Load(PathFor(profile), MaximumBytes, Parse, kept, out var book);
        return new StampLoad(outcome, book ?? StampBook.Empty, kept);
    }

    internal void Save(string profile, StampBook book) => SafeFile.Write(PathFor(profile), Encoding.UTF8.GetBytes(Serialize(book)));

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
}
