using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;

namespace Wanderveil.Infrastructure.Online;

// A guest's mark as the book draws it: a stroke or a line of writing, with its ID and author.
internal sealed record GuestInk(Guid Id, Player Author, InkStroke? Stroke, PageText? Text)
{
    internal int Sheet => Stroke?.Sheet ?? Text!.Sheet;
}

// Guests' marks between the contract's JSON and the book's ink (ADR-0006): colours as #rrggbb, points as
// [x, y, pressure] in page units, as in the marks file.
internal static class GuestInkFormat
{
    internal static GuestInk? From(GuestMark mark)
    {
        try
        {
            if (mark.Stroke is { } stroke)
            {
                var points = stroke.GetProperty("points").EnumerateArray()
                    .Select(point => new InkPoint(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle())).ToImmutableArray();
                return new GuestInk(mark.MarkId, mark.Author, new InkStroke(mark.Sheet, Colour(stroke), stroke.GetProperty("width").GetSingle(), points), null);
            }
            if (mark.Text is { } text)
                return new GuestInk(mark.MarkId, mark.Author, null, new PageText(mark.Sheet, text.GetProperty("text").GetString() ?? "",
                    text.GetProperty("x").GetSingle(), text.GetProperty("y").GetSingle(), text.GetProperty("size").GetSingle(), Colour(text)));
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException) { }
        return null;
    }

    // The contract's GuestMarkSave for a stroke or a line of writing.
    internal static string Save(InkStroke stroke)
    {
        var points = new JsonArray(stroke.Points.Select(point => (JsonNode)new JsonArray(Round(point.X), Round(point.Y), Round(point.Pressure))).ToArray());
        return new JsonObject { ["sheet"] = stroke.Sheet, ["stroke"] = new JsonObject { ["colour"] = Hex(stroke.Colour), ["width"] = Round(stroke.Width), ["points"] = points } }.ToJsonString();
    }

    internal static string Save(PageText text) =>
        new JsonObject { ["sheet"] = text.Sheet, ["text"] = new JsonObject { ["text"] = text.Text, ["x"] = Round(text.X), ["y"] = Round(text.Y), ["size"] = Round(text.Size), ["colour"] = Hex(text.Colour) } }.ToJsonString();

    internal static string Hex(Rgb colour) => "#" + colour.R.ToString("x2", CultureInfo.InvariantCulture) + colour.G.ToString("x2", CultureInfo.InvariantCulture) + colour.B.ToString("x2", CultureInfo.InvariantCulture);

    private static Rgb Colour(JsonElement element) =>
        Rgb.TryParse(element.GetProperty("colour").GetString() ?? "", out var colour) ? colour : throw new FormatException("Not a colour.");

    // Four decimals: a hundredth of a pixel on any page, and short to send.
    private static double Round(float value) => Math.Round(value, 4);
}
