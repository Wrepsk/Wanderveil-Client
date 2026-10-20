using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// The Settings colour rows: for each part (cover cloth, foil, page ink, paper, text), the edition's own colour (for
// text, Automatic), a row of curated swatches, and a custom colour from a picker, each row named beside its
// swatches. A pick is kept at once; the plates are recoloured in the background. Keep colours readable (on by
// default) lets the legibility guard (PassportLook) move picks that would not read; off, every pick is exact.
internal sealed class BookShellColours(PassportProfile holder)
{
    private static readonly (ColourPart Part, string Label, (string Name, int Hex)[] Swatches)[] Rows =
    [
        (ColourPart.Cloth, "COVER", [("Forest", 0x1E3A2A), ("Navy", 0x19242E), ("Wine", 0x5A1424), ("Harbour", 0x2B5568), ("Midnight", 0x16213A), ("Plum", 0x3E2440), ("Saddle", 0x6B4226), ("Charcoal", 0x2E2E2E), ("Ivory", 0xEFEBE0)]),
        (ColourPart.Foil, "FOIL", [("Gold", 0xB99662), ("Bright gold", 0xD4A83C), ("Sage", 0xA9C28A), ("Silver", 0xC3CAD6), ("Pearl", 0xDCE6E8), ("Copper", 0xB87333), ("Rose gold", 0xC9967E), ("Black", 0x141414)]),
        (ColourPart.Accent, "ACCENT", [("Sage", 0x5E8A4A), ("Wine", 0x9C3A4C), ("Brass", 0xB58A3C), ("Steel", 0x7F8FB0), ("Crimson", 0xC0272D), ("Gold", 0xB99662), ("Teal", 0x2B7A78), ("Plum", 0x6B3E6E)]),
        (ColourPart.Ink, "PAGE INK", [("Forest", 0x1E3A2A), ("Navy", 0x2A3B52), ("Wine", 0x7A2232), ("Harbour", 0x1F4A60), ("Royal", 0x2B3F6B), ("Crimson", 0x8E1C22), ("Umber", 0x5A3A22), ("Black", 0x19242E)]),
        (ColourPart.Paper, "PAPER", [("Cream", 0xD8C5A3), ("Ivory", 0xE6DCC6), ("White", 0xECEAE4), ("Aged", 0xCDB38A), ("Grey", 0xD2D0CB), ("Rose", 0xE4D3CA), ("Sage", 0xD5DCC8), ("Sky", 0xD3DCE2), ("Black", 0x141414)]),
        (ColourPart.Text, "TEXT", [("Ink", 0x19242E), ("Black", 0x000000), ("Sepia", 0x4A3426), ("Navy", 0x1F2A44), ("Forest", 0x1E3A2A), ("Wine", 0x5A1424), ("Parchment", 0xEFE8DA), ("White", 0xFFFFFF)]),
        (ColourPart.Lines, "RINGS", [("Black", 0x000000), ("White", 0xFFFFFF), ("Gold", 0xB99662), ("Sage", 0x5E8A4A), ("Wine", 0x7A2232), ("Navy", 0x2A3B52), ("Rose", 0xC9967E), ("Sky", 0x7FA6C4)]),
    ];


    // Where each swatch was drawn last frame (row part, index from the left), for checks that click a given one.
    private readonly System.Collections.Generic.Dictionary<(ColourPart, int), Vector2> drawn = [];

    internal Vector2? Swatch(ColourPart part, int index) => drawn.TryGetValue((part, index), out var centre) ? centre : null;

    // The custom colour being picked, shown live on its swatch and kept when the picker lets go.
    private ColourPart? picking;
    private Vector3 picked;

    internal void Draw(UiContext context)
    {
        var identity = holder.Identity;
        var edition = identity.Edition;
        var chosen = identity.Appearance?.Chosen ?? PassportColours.None;
        var look = identity.Look;
        ImGui.Dummy(new Vector2(0, 8 * context.Scale));
        UiTypography.Text("COLOURS", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 4 * context.Scale));
        // Each row's name sits beside its swatches, in a column as wide as the longest name.
        var column = Rows.Max(row => UiTypography.Measure(row.Label, TextRole.Caption, context, float.MaxValue, false).X) + 10 * context.Scale;
        foreach (var (part, label, swatches) in Rows)
        {
            // Text and rings start from Automatic: what they would be without a pick of their own.
            var printed = part switch
            {
                ColourPart.Text => Rgb.Of(PassportLook.Of(edition, chosen with { Text = null }).Text),
                ColourPart.Lines => Rgb.Of(PassportLook.Of(edition, chosen with { Lines = null }).Lines),
                _ => Printed(edition, part),
            };
            Row(part, label, column, swatches, printed, chosen[part], context);
        }
        ImGui.Dummy(new Vector2(0, 6 * context.Scale));
        if (BookControl.Toggle("Keep colours readable", !chosen.Free, context)) holder.SetReadable(chosen.Free);
        if (!chosen.Free) Adjustable(chosen, context);
        var note = chosen.Free ? "Colours are used exactly as picked, even where text is hard to read."
            : look.Adjusted.Length > 0 ? Adjusted(look.Adjusted)
            : chosen.Text == null && look.LightText ? "On this dark paper, text prints light." : null;
        if (note != null)
        {
            ImGui.Dummy(new Vector2(0, 4 * context.Scale));
            UiTypography.Text(note, TextRole.Body, context, UiTheme.MutedInk);
        }
        if (chosen with { Free = false, Exact = 0 } == PassportColours.None) return;
        ImGui.Dummy(new Vector2(0, 6 * context.Scale));
        if (BookControl.Link($"Use {edition.Name}'s own colours", context)) holder.ResetColours();
    }

    // With the guard on: one small checkbox per colour on a single caption-sized line, ticked where the guard may
    // move it (all, at first); a narrow page wraps it.
    private void Adjustable(PassportColours chosen, UiContext context)
    {
        ImGui.Dummy(new Vector2(0, 2 * context.Scale));
        var list = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = UiLayout.ContentWidth;
        var gap = 7 * context.Scale;
        var height = UiTypography.Size(TextRole.Caption, context) + 4 * context.Scale;
        var lead = UiTypography.Measure("MAY ADJUST", TextRole.Caption, context, float.MaxValue, false);
        UiTypography.Draw(list, start + new Vector2(0, 2 * context.Scale), "MAY ADJUST", TextRole.Caption, context, width, UiTheme.MutedInk, false);
        var at = start + new Vector2(lead.X + gap, 0);
        foreach (var part in PassportColours.Guarded)
        {
            // Short names, so the five fit on one line.
            var label = part == ColourPart.Ink ? "Ink" : Name(part);
            var size = UiTypography.Measure(label, TextRole.Caption, context, float.MaxValue, false);
            var box = MathF.Round(size.Y * 0.75f);
            var item = new Vector2(box + 4 * context.Scale + size.X, height);
            if (at.X + item.X > start.X + width) at = new Vector2(start.X + lead.X + gap, at.Y + height);
            ImGui.SetCursorScreenPos(at);
            ImGui.PushID("May adjust " + part);
            var clicked = ImGui.InvisibleButton("##Adjust", item);
            drawn[(part, -1)] = at + item / 2;
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();
            var on = chosen.Adjustable(part);
            var ink = ImGui.GetColorU32(hovered ? UiTheme.Ink : UiTheme.EditionInk);
            var boxAt = at + new Vector2(0, (height - box) / 2);
            list.AddRect(boxAt, boxAt + new Vector2(box), ink, 1.5f * context.Scale, ImDrawFlags.None, MathF.Max(1, context.Scale));
            if (on)
            {
                list.AddLine(boxAt + new Vector2(box * 0.2f, box * 0.52f), boxAt + new Vector2(box * 0.42f, box * 0.76f), ink, MathF.Max(1, 1.4f * context.Scale));
                list.AddLine(boxAt + new Vector2(box * 0.42f, box * 0.76f), boxAt + new Vector2(box * 0.82f, box * 0.22f), ink, MathF.Max(1, 1.4f * context.Scale));
            }
            UiTypography.Draw(list, at + new Vector2(box + 4 * context.Scale, 2 * context.Scale), label, TextRole.Caption, context, size.X + 1, hovered ? UiTheme.Ink : UiTheme.MutedInk, false);
            if (hovered) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip(on ? part == ColourPart.Lines ? "Rings may be adjusted to stay faintly visible" : $"{Name(part)} may be adjusted to stay readable" : $"{Name(part)} stays exactly as picked"); }
            if (clicked) holder.SetAdjustable(part, !on);
            at += new Vector2(item.X + gap, 0);
        }
        ImGui.SetCursorScreenPos(new Vector2(start.X, at.Y + height));
        ImGui.Dummy(Vector2.Zero);
    }

    // A small preview in the holder's exact colours (even where the Settings pages keep themselves readable):
    // the closed cover beside an open page with a name, a caption, a line of text, and a link.
    internal void Preview(UiContext context)
    {
        var identity = holder.Identity;
        var look = identity.Look;
        var edition = identity.Edition;
        ImGui.Dummy(new Vector2(0, 14 * context.Scale));
        UiTypography.Text("HOW IT LOOKS", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 8 * context.Scale));
        var list = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = UiLayout.ContentWidth;
        // As tall as the page's lines need (with room under the link), and never shorter than the cover reads well at.
        var name0 = identity.Name.Length > 0 ? identity.Name : "Ari Vale";
        float Tall(string text, TextRole role, float scale) => UiTypography.Measure(text, role, context with { FontSize = context.FontSize * scale }, width * 0.6f, false).Y;
        var needed = (9 + 4 + 1 + 6 + 14) * context.Scale + Tall(name0, TextRole.Title, 0.75f) + Tall("HOME WORLD", TextRole.Caption, 1f) + Tall("Demo world", TextRole.Body, 1f) + Tall("Explore discoveries »", TextRole.Body, 1f);
        var height = MathF.Round(MathF.Max(100 * context.Scale, needed));
        var cover = new UiRect(origin, new Vector2(MathF.Round(100 * context.Scale) * 0.75f, height));
        var corner = 5 * context.Scale;
        list.AddRectFilled(cover.Position + new Vector2(2, 3) * context.Scale, cover.Max + new Vector2(2, 3) * context.Scale, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.25f)), corner);
        list.AddRectFilled(cover.Position, cover.Max, ImGui.GetColorU32(look.Cloth), corner);
        list.AddRect(cover.Position + new Vector2(6 * context.Scale), cover.Max - new Vector2(6 * context.Scale), ImGui.GetColorU32(look.Foil with { W = 0.85f }), corner * 0.6f, ImDrawFlags.None, MathF.Max(1, context.Scale));
        list.AddCircle(cover.Position + new Vector2(cover.Size.X / 2, cover.Size.Y * 0.38f), cover.Size.X * 0.17f, ImGui.GetColorU32(look.Foil), 24, MathF.Max(1, 1.4f * context.Scale));
        var name = edition.Name.ToUpperInvariant();
        var type = context with { FontSize = MathF.Max(9, 11 * context.Scale) / 1.65f, TitleFont = context.DisplayFont };
        var measured = UiTypography.Measure(name, TextRole.Title, type, cover.Size.X, false);
        UiTypography.Draw(list, new Vector2(cover.Position.X + (cover.Size.X - MathF.Min(measured.X, cover.Size.X - 12 * context.Scale)) / 2, cover.Position.Y + cover.Size.Y * 0.62f), name, TextRole.Title, type, cover.Size.X - 12 * context.Scale, look.Foil, false);

        var page = new UiRect(new Vector2(cover.Max.X + 14 * context.Scale, origin.Y), new Vector2(MathF.Max(40, width - cover.Size.X - 14 * context.Scale), height));
        list.AddRectFilled(page.Position + new Vector2(2, 3) * context.Scale, page.Max + new Vector2(2, 3) * context.Scale, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.18f)), 2 * context.Scale);
        list.AddRectFilled(page.Position, page.Max, ImGui.GetColorU32(look.Paper), 2 * context.Scale);
        // A faint edge, so a page the colour of the Settings paper still stands out from it.
        list.AddRect(page.Position, page.Max, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.45f }), 2 * context.Scale, ImDrawFlags.None, 1);
        // The guilloche, a little stronger than printed so it shows at this size.
        list.PushClipRect(page.Position, page.Max, true);
        var centre = page.Position + new Vector2(page.Size.X * 0.55f, page.Size.Y * 0.62f);
        for (var radius = 6f; radius < page.Size.X; radius += 7 * context.Scale) list.AddCircle(centre, radius, ImGui.GetColorU32(look.Lines with { W = 0.12f }), 48, 1);
        list.PopClipRect();
        var inset = page.Position + new Vector2(10, 9) * context.Scale;
        var inner = page.Size.X - 20 * context.Scale;
        var y = 0f;
        Line(identity.Name.Length > 0 ? identity.Name : "Ari Vale", TextRole.Title, look.Text, 0.75f);
        Line("HOME WORLD", TextRole.Caption, look.Caption, 1f, 4);
        Line(identity.HomeWorld.Length > 0 ? identity.HomeWorld : "Demo world", TextRole.Body, look.Text, 1f, 1);
        var link = "Explore discoveries »";
        var linkSize = UiTypography.Measure(link, TextRole.Body, context, inner, false);
        var linkAt = inset + new Vector2(0, y + 6 * context.Scale);
        UiTypography.Draw(list, linkAt, link, TextRole.Body, context, inner, look.Ink, false);
        BookSurface.Line(list, linkAt + new Vector2(0, linkSize.Y), linkAt + linkSize, ImGui.GetColorU32(look.Ink with { W = 0.6f }), MathF.Max(1, context.Scale));
        ImGui.Dummy(new Vector2(width, height + 3 * context.Scale));

        void Line(string text, TextRole role, Vector4 colour, float scale, float before = 0)
        {
            var type = context with { FontSize = context.FontSize * scale };
            y += before * context.Scale;
            UiTypography.Draw(list, inset + new Vector2(0, y), text, role, type, inner, colour, false);
            y += UiTypography.Measure(text, role, type, inner, false).Y;
        }
    }

    private void Row(ColourPart part, string label, float column, (string Name, int Hex)[] swatches, Rgb printed, Rgb? current, UiContext context)
    {
        var size = MathF.Round(18 * context.Scale);
        var gap = MathF.Round(5 * context.Scale);
        // The selection ring reaches past a swatch's edge: keep the row in from its edges so it is never cut.
        var ring = MathF.Ceiling(4 * context.Scale);
        var start = ImGui.GetCursorScreenPos();
        var labelSize = UiTypography.Measure(label, TextRole.Caption, context, column, false);
        UiTypography.Draw(ImGui.GetWindowDrawList(), start + new Vector2(0, ring + (size - labelSize.Y) / 2), label, TextRole.Caption, context, column, UiTheme.MutedInk, false);
        var width = UiLayout.ContentWidth - column - ring * 2;
        var origin = start + new Vector2(column + ring, ring);
        var perRow = Math.Max(1, (int)((width + gap) / (size + gap)));
        // The edition's own colour (for text, Automatic) first, then the curated ones not equal to it, then custom.
        var first = part is ColourPart.Text or ColourPart.Lines ? "Automatic" : $"{holder.Identity.Edition.Name}'s own";
        var colours = new[] { (first, printed, (Rgb?)null) }
            .Concat(swatches.Select(swatch => (swatch.Name, Rgb.Hex(swatch.Hex), (Rgb?)Rgb.Hex(swatch.Hex))).Where(entry => entry.Item2 != printed))
            .ToArray();
        var custom = current is { } own && colours.All(entry => entry.Item2 != own);
        var count = colours.Length + 1;
        var list = ImGui.GetWindowDrawList();
        for (var index = 0; index < count; index++)
        {
            var at = origin + new Vector2(index % perRow * (size + gap), index / perRow * (size + gap));
            var center = at + new Vector2(size / 2);
            drawn[(part, index)] = center;
            ImGui.SetCursorScreenPos(at);
            ImGui.PushID($"{part} swatch {index}");
            var clicked = ImGui.InvisibleButton("##Swatch", new Vector2(size));
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();
            bool selected;
            if (index < colours.Length)
            {
                var (name, colour, value) = colours[index];
                selected = value == null ? current == null : current == value;
                list.AddCircleFilled(center, size / 2 - 1, ImGui.GetColorU32(colour.Vector), 24);
                list.AddCircle(center, size / 2 - 1, ImGui.GetColorU32(UiTheme.Ink with { W = 0.35f }), 24, 1);
                if (value == null) list.AddCircleFilled(center, MathF.Max(1.5f, size * 0.09f), ImGui.GetColorU32(PassportLook.Contrast(colour.Vector, Vector4.One) > 3 ? Vector4.One : UiTheme.Ink), 8);
                if (hovered) ImGui.SetTooltip($"{name} ({colour})");
                if (clicked) holder.SetColour(part, value);
            }
            else
            {
                // The custom swatch: the picked colour, or a wheel of hues until one is picked.
                selected = custom;
                var shown = picking == part ? Rgb.Of(new Vector4(picked, 1)) : custom ? current : null;
                if (shown is { } colour) list.AddCircleFilled(center, size / 2 - 1, ImGui.GetColorU32(colour.Vector), 24);
                else PaperColours.Wheel(list, center, size / 2 - 1);
                list.AddCircle(center, size / 2 - 1, ImGui.GetColorU32(UiTheme.Ink with { W = 0.35f }), 24, 1);
                if (hovered) ImGui.SetTooltip("Custom colour");
                if (clicked)
                {
                    picking = part;
                    picked = new Vector3((current ?? printed).Vector.X, (current ?? printed).Vector.Y, (current ?? printed).Vector.Z);
                    ImGui.OpenPopup($"Custom {part}");
                }
                Picker(part, context);
            }
            if (selected) list.AddCircle(center, size / 2 + 2 * context.Scale, ImGui.GetColorU32(UiTheme.Ink), 24, MathF.Max(1, 1.5f * context.Scale));
            else if (hovered) list.AddCircle(center, size / 2 + 2 * context.Scale, ImGui.GetColorU32(UiTheme.Gold), 24, MathF.Max(1, 1.5f * context.Scale));
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var rows = (count + perRow - 1) / perRow;
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(UiLayout.ContentWidth, rows * (size + gap) - gap + ring * 2));
    }

    // A colour picker on paper: dragging previews on the swatch; letting go (or entering a hex) keeps it.
    private void Picker(ColourPart part, UiContext context)
    {
        if (!PaperColours.Begin($"Custom {part}", context)) { if (picking == part) picking = null; return; }
        try
        {
            ImGui.TextUnformatted($"Custom {Name(part).ToLowerInvariant()}");
            if (PaperColours.Picker(ref picked, context)) holder.SetColour(part, Rgb.Of(new Vector4(picked, 1)));
        }
        finally { PaperColours.End(); }
    }

    private static Rgb Printed(PassportEdition edition, ColourPart part) => Rgb.Of(part switch
    {
        ColourPart.Cloth => edition.Cloth,
        ColourPart.Foil => edition.Foil,
        ColourPart.Ink => edition.Ink,
        ColourPart.Accent => edition.Accent,
        _ => PassportLook.EditionPaper,
    });

    // What the guard moved: colours to stay readable, rings to stay visible.
    private static string Adjusted(ColourPart[] parts)
    {
        var readable = parts.Where(part => part != ColourPart.Lines).Select(Name).ToArray();
        var note = readable.Length > 0 ? $"Adjusted to stay readable: {string.Join(", ", readable)}." : "";
        if (parts.Contains(ColourPart.Lines)) note += (note.Length > 0 ? " " : "") + "Rings adjusted to stay visible.";
        return note;
    }

    private static string Name(ColourPart part) => part switch
    {
        ColourPart.Cloth => "Cover",
        ColourPart.Foil => "Foil",
        ColourPart.Ink => "Page ink",
        ColourPart.Paper => "Paper",
        ColourPart.Text => "Text",
        ColourPart.Accent => "Accent",
        _ => "Rings",
    };
}
