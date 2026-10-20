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
    internal const int Version = 3;
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
        file["frame"] = JsonNamingPolicy.CamelCase.ConvertName(identity.Frame.ToString());
        if (identity.Appearance is { } appearance) file["appearance"] = Appearance(appearance);
        if (identity.PreviousAppearance is { } previous) file["previousAppearance"] = Appearance(previous);
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
        // Migrations: each step upgrades the object one version in memory; the next save writes the current
        // version, and the backup keeps the old one.
        if (version == 1) Migrate1To2(file);
        // Version 3 added placed symbols; a version 2 file has none, so it reads as it is. (An earlier client
        // refuses a version 3 file rather than saving over symbols it cannot keep.)
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
        // A frame this version does not draw falls back to the printed one rather than losing the passport.
        if (Text(file, "frame") is { } frame) identity = identity with { Frame = Enum.TryParse<PictureFrame>(frame, true, out var style) && Enum.IsDefined(style) && !char.IsDigit(frame[0]) ? style : PictureFrame.Printed };
        identity = identity with { Appearance = ReadAppearance(file, "appearance"), PreviousAppearance = ReadAppearance(file, "previousAppearance") };
        return identity.Normalize();
    }

    // Version 1 came before editions, when every passport was Gridania: it keeps that edition, chosen.
    private static void Migrate1To2(JsonObject file)
    {
        file["appearance"] = Appearance(PassportAppearance.Gridania);
        file["version"] = 2;
    }

    private static JsonObject Appearance(PassportAppearance appearance)
    {
        var node = new JsonObject { ["edition"] = appearance.Edition };
        if (appearance.Colours is { IsEmpty: false } colours)
        {
            var parts = new JsonObject();
            foreach (var part in Enum.GetValues<ColourPart>())
                if (colours[part] is { } colour) parts[JsonNamingPolicy.CamelCase.ConvertName(part.ToString())] = colour.ToString();
            if (colours.Free) parts["readable"] = false;
            if (colours.Exact != 0)
            {
                var exact = new JsonArray();
                foreach (var part in PassportColours.Guarded) if ((colours.Exact & (1 << (int)part)) != 0) exact.Add(JsonNamingPolicy.CamelCase.ConvertName(part.ToString()));
                parts["exact"] = exact;
            }
            node["colours"] = parts;
        }
        if (appearance.Background != PageBackground.Guilloche) node["background"] = JsonNamingPolicy.CamelCase.ConvertName(appearance.Background.ToString());
        if (appearance.Symbols is { IsEmpty: false } symbols)
        {
            var list = new JsonArray();
            foreach (var symbol in symbols.Items)
                list.Add(new JsonObject { ["symbol"] = symbol.Symbol, ["on"] = symbol.Surface, ["x"] = symbol.X, ["y"] = symbol.Y, ["turn"] = symbol.Rotation, ["size"] = symbol.Size });
            node["symbols"] = list;
        }
        return node;
    }

    // An edition this version does not know is kept as written (it draws as Gridania), so nothing is lost.
    private static PassportAppearance? ReadAppearance(JsonObject file, string name)
    {
        if (file[name] == null) return null;
        if (file[name] is not JsonObject appearance || Text(appearance, "edition") is not { Length: > 0 } edition || edition.Length > 32)
            throw new FormatException($"A passport's {name} names its edition.");
        PassportColours? colours = null;
        if (appearance["colours"] is JsonObject parts)
        {
            colours = PassportColours.None;
            foreach (var part in Enum.GetValues<ColourPart>())
            {
                var key = JsonNamingPolicy.CamelCase.ConvertName(part.ToString());
                if (parts[key] == null) continue;
                colours = Rgb.TryParse(Text(parts, key), out var colour) ? colours.With(part, colour) : throw new FormatException($"A passport's {key} colour is #RRGGBB.");
            }
            if (parts["readable"] is JsonValue readable && readable.TryGetValue<bool>(out var guarded)) colours = colours with { Free = !guarded };
            else if (parts["readable"] != null) throw new FormatException("A passport's readable setting is true or false.");
            // Parts kept exact even while the guard is on; a name this version does not know is ignored.
            if (parts["exact"] is JsonArray exact)
                foreach (var entry in exact)
                {
                    var exactName = entry is JsonValue exactValue && exactValue.TryGetValue<string>(out var exactText) ? exactText : throw new FormatException("A passport's exact colours are names.");
                    foreach (var part in PassportColours.Guarded) if (JsonNamingPolicy.CamelCase.ConvertName(part.ToString()) == exactName) colours = colours.WithExact(part, true);
                }
            else if (parts["exact"] != null) throw new FormatException("A passport's exact colours are a list.");
            if (colours.IsEmpty) colours = null;
        }
        else if (appearance["colours"] != null) throw new FormatException("A passport's colours are a JSON object.");
        // Placed symbols in drawing order. One this version does not know, or on a surface it does not have, is
        // left out (a later version's file is refused before this), as are any past a surface's limit.
        SymbolLayer? symbols = null;
        if (appearance["symbols"] is JsonArray placed)
        {
            var read = new List<PlacedSymbol>();
            foreach (var entry in placed)
            {
                if (entry is not JsonObject symbol || Text(symbol, "symbol") is not { } id || Text(symbol, "on") is not { } surface)
                    throw new FormatException("A passport's placed symbol names its symbol and where it is.");
                read.Add(new PlacedSymbol(id, surface, Real(symbol, "x"), Real(symbol, "y"), Real(symbol, "turn"), Real(symbol, "size")));
            }
            symbols = SymbolLayer.Of(read) is { IsEmpty: false } layer ? layer : null;
        }
        else if (appearance["symbols"] != null) throw new FormatException("A passport's symbols are a list.");
        // A background this version does not draw falls back to the rings rather than losing the passport.
        var background = PageBackground.Guilloche;
        if (appearance["background"] is JsonValue backgroundValue && backgroundValue.TryGetValue<string>(out var backgroundName))
            background = Enum.TryParse<PageBackground>(backgroundName, true, out var parsed) && Enum.IsDefined(parsed) && !char.IsDigit(backgroundName[0]) ? parsed : PageBackground.Guilloche;
        else if (appearance["background"] != null) throw new FormatException("A passport's background is a name.");
        return new PassportAppearance(edition, colours, symbols, background);
    }

    private static string Key(IdentityField field) => JsonNamingPolicy.CamelCase.ConvertName(field.ToString());

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static float Real(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue<double>(out var number) ? (float)number : throw new FormatException($"A passport file is missing {name}.");
}
