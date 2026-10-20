using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// The Settings colour rows: for each part (cover cloth, foil, page ink, paper), the edition's own colour, a row
// of curated swatches, and a custom colour from a picker. A pick is kept at once; the plates are recoloured in
// the background and the legibility guard (PassportLook) keeps lettering and text readable whatever is picked.
internal sealed class BookShellColours(PassportProfile holder)
{
    private static readonly (ColourPart Part, string Label, (string Name, int Hex)[] Swatches)[] Rows =
    [
        (ColourPart.Cloth, "COVER", [("Forest", 0x1E3A2A), ("Navy", 0x19242E), ("Wine", 0x5A1424), ("Harbour", 0x2B5568), ("Midnight", 0x16213A), ("Plum", 0x3E2440), ("Saddle", 0x6B4226), ("Charcoal", 0x2E2E2E), ("Ivory", 0xEFEBE0)]),
        (ColourPart.Foil, "FOIL", [("Gold", 0xB99662), ("Bright gold", 0xD4A83C), ("Sage", 0xA9C28A), ("Silver", 0xC3CAD6), ("Pearl", 0xDCE6E8), ("Copper", 0xB87333), ("Rose gold", 0xC9967E), ("Black", 0x141414)]),
        (ColourPart.Ink, "PAGE INK", [("Forest", 0x1E3A2A), ("Navy", 0x2A3B52), ("Wine", 0x7A2232), ("Harbour", 0x1F4A60), ("Royal", 0x2B3F6B), ("Crimson", 0x8E1C22), ("Umber", 0x5A3A22), ("Black", 0x19242E)]),
        (ColourPart.Paper, "PAPER", [("Cream", 0xD8C5A3), ("Ivory", 0xE6DCC6), ("White", 0xECEAE4), ("Aged", 0xCDB38A), ("Grey", 0xD2D0CB), ("Rose", 0xE4D3CA), ("Sage", 0xD5DCC8), ("Sky", 0xD3DCE2)]),
    ];

    // The custom swatch's wheel of hues, before anything is picked.
    private static readonly int[] Hues = [0xD98880, 0xD9C27A, 0x8CC48C, 0x7FC4C4, 0x8C9ED9, 0xC48CC4];

    // The custom colour being picked, shown live on its swatch and kept when the picker lets go.
    private ColourPart? picking;
    private Vector3 picked;

    internal void Draw(UiContext context)
    {
        var identity = holder.Identity;
        var edition = identity.Edition;
        var chosen = identity.Appearance?.Chosen ?? PassportColours.None;
        var look = identity.Look;
        ImGui.Dummy(new Vector2(0, 22 * context.Scale));
        UiTypography.Text("COLOURS", TextRole.Caption, context, UiTheme.MutedInk);
        foreach (var (part, label, swatches) in Rows)
        {
            ImGui.Dummy(new Vector2(0, 8 * context.Scale));
            UiTypography.Text(label, TextRole.Caption, context, UiTheme.MutedInk);
            ImGui.Dummy(new Vector2(0, 3 * context.Scale));
            Row(part, swatches, Printed(edition, part), chosen[part], context);
        }
        if (look.Adjusted.Length > 0)
        {
            ImGui.Dummy(new Vector2(0, 8 * context.Scale));
            var parts = string.Join(", ", look.Adjusted.Select(Name));
            UiTypography.Text($"Adjusted to stay readable: {parts}.", TextRole.Body, context, UiTheme.MutedInk);
        }
        if (chosen.IsEmpty) return;
        ImGui.Dummy(new Vector2(0, 8 * context.Scale));
        if (BookControl.Link($"Use {edition.Name}'s own colours", context)) holder.ResetColours();
    }

    private void Row(ColourPart part, (string Name, int Hex)[] swatches, Rgb printed, Rgb? current, UiContext context)
    {
        var size = MathF.Round(20 * context.Scale);
        var gap = MathF.Round(6 * context.Scale);
        var width = UiLayout.ContentWidth;
        var origin = ImGui.GetCursorScreenPos();
        var perRow = Math.Max(1, (int)((width + gap) / (size + gap)));
        // The edition's own colour first, then the curated ones not equal to it, then the custom swatch.
        var colours = new[] { ($"{holder.Identity.Edition.Name}'s own", printed, (Rgb?)null) }
            .Concat(swatches.Select(swatch => (swatch.Name, Rgb.Hex(swatch.Hex), (Rgb?)Rgb.Hex(swatch.Hex))).Where(entry => entry.Item2 != printed))
            .ToArray();
        var custom = current is { } own && colours.All(entry => entry.Item2 != own);
        var count = colours.Length + 1;
        var list = ImGui.GetWindowDrawList();
        for (var index = 0; index < count; index++)
        {
            var at = origin + new Vector2(index % perRow * (size + gap), index / perRow * (size + gap));
            var center = at + new Vector2(size / 2);
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
                else
                    for (var slice = 0; slice < 6; slice++)
                    {
                        list.PathArcTo(center, size / 2 - 1, slice * MathF.Tau / 6, (slice + 1) * MathF.Tau / 6, 6);
                        list.PathLineTo(center);
                        list.PathFillConvex(ImGui.GetColorU32(Rgb.Hex(Hues[slice]).Vector));
                    }
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
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rows * (size + gap) - gap));
    }

    // A colour picker on paper: dragging previews on the swatch; letting go (or entering a hex) keeps it.
    private void Picker(ColourPart part, UiContext context)
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiTheme.Paper);
        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Ink);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiTheme.PaperInset);
        ImGui.PushStyleColor(ImGuiCol.Border, UiTheme.Gold);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10) * context.Scale);
        try
        {
            if (!ImGui.BeginPopup($"Custom {part}")) { if (picking == part) picking = null; return; }
            try
            {
                ImGui.TextUnformatted($"Custom {Name(part).ToLowerInvariant()}");
                ImGui.SetNextItemWidth(200 * context.Scale);
                ImGui.ColorPicker3("##Picker", ref picked, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.PickerHueBar | ImGuiColorEditFlags.DisplayHex);
                if (ImGui.IsItemDeactivatedAfterEdit()) holder.SetColour(part, Rgb.Of(new Vector4(picked, 1)));
            }
            finally { ImGui.EndPopup(); }
        }
        finally
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
        }
    }

    private static Rgb Printed(PassportEdition edition, ColourPart part) => Rgb.Of(part switch
    {
        ColourPart.Cloth => edition.Cloth,
        ColourPart.Foil => edition.Foil,
        ColourPart.Ink => edition.Ink,
        _ => PassportLook.EditionPaper,
    });

    private static string Name(ColourPart part) => part switch
    {
        ColourPart.Cloth => "Cover",
        ColourPart.Foil => "Foil",
        ColourPart.Ink => "Page ink",
        _ => "Paper",
    };
}
