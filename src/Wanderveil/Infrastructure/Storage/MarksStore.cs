using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wanderveil.Features;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;

namespace Wanderveil.Infrastructure.Storage;

// Everything a passport's drawing file holds: its strokes and lines of writing, in drawing order.
internal sealed record MarkBook(IReadOnlyList<InkStroke> Strokes, IReadOnlyList<PageText> Texts)
{
    internal static readonly MarkBook Empty = new([], []);

    internal static MarkBook Of(PageMarks marks) => new(marks.Strokes.ToArray(), marks.Texts.ToArray());
}

internal sealed record MarkLoad(LoadOutcome Outcome, MarkBook Book, IReadOnlyList<string> Kept);

// The drawing and writing on a passport's stamp pages, in one versioned JSON file per profile: marks-<profile>.json,
// saved and recovered as a SafeFile, apart from the stamps and the identity, so an appearance reset never touches it.
// A stroke's points are one flat list (x, y, pressure, ...) in page-width units, to four decimals.
// Blocking file I/O: call it from startup or a background task, never from a draw callback.
internal sealed class MarksStore(string directory)
{
    internal const string Format = "wanderveil.passport-marks";
    internal const int Version = 1;
    private const long MaximumBytes = 16 * 1024 * 1024;

    internal string PathFor(string profile) => Path.Combine(directory, $"marks-{StorageProfile.Checked(profile)}.json");

    internal MarkLoad Load(string profile)
    {
        var kept = new List<string>();
        var outcome = SafeFile.Load(PathFor(profile), MaximumBytes, Parse, kept, out var book);
        return new MarkLoad(outcome, book ?? MarkBook.Empty, kept);
    }

    internal void Save(string profile, MarkBook book) => SafeFile.Write(PathFor(profile), Encoding.UTF8.GetBytes(Serialize(book)));

    internal static string Serialize(MarkBook book)
    {
        var strokes = new JsonArray();
        foreach (var stroke in book.Strokes)
        {
            var points = new StringBuilder("[");
            for (var index = 0; index < stroke.Points.Length; index++)
            {
                var point = stroke.Points[index];
                if (index > 0) points.Append(',');
                points.Append(Round(point.X)).Append(',').Append(Round(point.Y)).Append(',').Append(Round(point.Pressure));
            }
            points.Append(']');
            strokes.Add(new JsonObject
            {
                ["page"] = stroke.Sheet,
                ["colour"] = stroke.Colour.ToString(),
                ["width"] = Math.Round(stroke.Width, 5),
                ["points"] = JsonNode.Parse(points.ToString()),
            });
        }
        var texts = new JsonArray();
        foreach (var text in book.Texts)
            texts.Add(new JsonObject
            {
                ["page"] = text.Sheet,
                ["text"] = text.Text,
                ["x"] = Math.Round(text.X, 4),
                ["y"] = Math.Round(text.Y, 4),
                ["size"] = Math.Round(text.Size, 4),
                ["colour"] = text.Colour.ToString(),
            });
        var file = new JsonObject { ["format"] = Format, ["version"] = Version, ["strokes"] = strokes, ["texts"] = texts };
        return file.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        static string Round(float value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }

    // Reads a file's text. Throws FormatException when it is not a valid drawing file and NotSupportedException when it
    // is a later version. Marks past the limits are left out (PageMarks keeps them within them).
    internal static MarkBook Parse(string text)
    {
        JsonObject file;
        try { file = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("A drawing file is a JSON object."); }
        catch (JsonException exception) { throw new FormatException("A drawing file is not valid JSON.", exception); }
        if (Text(file, "format") != Format) throw new FormatException("Not a Wanderveil drawing file.");
        var version = file["version"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new FormatException("A drawing file has a version.");
        if (version > Version) throw new NotSupportedException($"Drawing file version {version} is newer than {Version}.");
        if (version < 1) throw new FormatException("A drawing file version starts at 1.");
        if (file["strokes"] is not JsonArray strokes || file["texts"] is not JsonArray texts) throw new FormatException("A drawing file lists its strokes and writing.");
        var readStrokes = strokes.Select(entry =>
        {
            if (entry is not JsonObject stroke || stroke["points"] is not JsonArray flat || flat.Count % 3 != 0) throw new FormatException("A stroke lists its points in threes.");
            var values = flat.Select(item => item is JsonValue point && point.TryGetValue<double>(out var real) ? (float)real : throw new FormatException("A stroke's points are numbers.")).ToArray();
            var points = ImmutableArray.CreateBuilder<InkPoint>(values.Length / 3);
            for (var index = 0; index < values.Length; index += 3) points.Add(new InkPoint(values[index], values[index + 1], values[index + 2]));
            return new InkStroke(Page(stroke), Colour(stroke), Real(stroke, "width"), points.ToImmutable());
        }).ToList();
        var readTexts = texts.Select(entry => entry is JsonObject line
            ? new PageText(Page(line), Text(line, "text") ?? throw new FormatException("A line of writing has its text."), Real(line, "x"), Real(line, "y"), Real(line, "size"), Colour(line))
            : throw new FormatException("A line of writing is a JSON object.")).ToList();
        // Through PageMarks, so what is kept is clean and within the limits.
        var marks = new PageMarks(readStrokes, readTexts);
        return MarkBook.Of(marks);
    }

    private static int Page(JsonObject node) => node["page"] is JsonValue value && value.TryGetValue<int>(out var page) && page >= 0 ? page : throw new FormatException("A mark names its page.");
    private static Rgb Colour(JsonObject node) => Rgb.TryParse(Text(node, "colour"), out var colour) ? colour : throw new FormatException("A mark's colour is #RRGGBB.");
    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static float Real(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<double>(out var number) ? (float)number : throw new FormatException($"A mark is missing {name}.");
}

// Keeps one profile's drawing on disk: loads once when created, then saves a snapshot on a background task after each
// change. Disposing waits briefly for a save in progress.
internal sealed class MarksPersistence : ISavedState, IDisposable
{
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(3);
    private readonly BackgroundSaver<MarkBook> saver;

    internal MarksPersistence(MarksStore store, string profile, Action<Exception>? report = null)
    {
        var load = store.Load(profile);
        Loaded = load.Outcome;
        Kept = load.Kept;
        saver = new BackgroundSaver<MarkBook>(book => store.Save(profile, book), load.Outcome is LoadOutcome.Newer or LoadOutcome.Inaccessible, report);
        Marks = new PageMarks(load.Book.Strokes, load.Book.Texts);
        Marks.Changed += Queue;
    }

    internal PageMarks Marks { get; }
    internal IReadOnlyList<string> Kept { get; }
    public LoadOutcome Loaded { get; }
    public SaveState State => saver.State;

    public void Retry()
    {
        if (saver.State == SaveState.Failed) Queue();
    }

    internal bool Flush(TimeSpan timeout) => saver.Flush(timeout);

    public void Dispose()
    {
        Marks.Changed -= Queue;
        saver.Close(DisposeWait);
    }

    private void Queue()
    {
        if (saver.State != SaveState.ReadOnly) saver.Queue(MarkBook.Of(Marks));
    }
}
