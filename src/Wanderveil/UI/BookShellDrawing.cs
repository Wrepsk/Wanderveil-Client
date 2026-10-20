using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal enum DrawingTool { Pencil, Eraser, Text, Eyedropper }

// Drawing and writing on the stamp pages. In Draw mode a toolbar takes the footer: Pencil, Eraser, and Text, a small
// palette, three sizes, and Undo (also Ctrl+Z). The pencil draws while the mouse is held on a page (smoothed, a point
// every couple of pixels, pressed harder when slower); the eraser rubs out only what it passes over; Text writes a
// line where the page is clicked (or rewrites a line clicked on), kept on Enter or clicking away, Escape leaving it as
// it was. The page under the mouse takes its clicks while drawing, so it never turns or moves the book; Page Up/Down
// still turn. Marks lie above the stamps and never change them.
internal sealed class BookShellDrawing(PageMarks marks, IBookAssets assets)
{
    // Graphite, then six inks; the second is the passport's own page ink.
    internal static readonly (string Name, int Hex)[] Palette = [("Graphite", 0x3A3D44), ("Page ink", -1), ("Sepia", 0x6B4A2E), ("Red", 0xA3302A), ("Blue", 0x2F4F8A), ("Green", 0x3D6B3A), ("White", 0xF2EEE6)];
    // Fine, medium, bold: a stroke's width, an eraser's radius, and writing's height, in page-width units.
    private static readonly float[] StrokeWidths = [0.005f, 0.009f, 0.016f];
    private static readonly float[] EraserRadii = [0.012f, 0.025f, 0.05f];
    private static readonly float[] TextSizes = [0.04f, 0.055f, 0.08f];
    private const float Spacing = 2f;

    private readonly List<Area> areas = [];
    private readonly List<InkPoint> live = [];
    private int liveSheet;
    private Vector2 smoothed;
    private float pressure = 0.8f;
    private bool erasing;
    // The line of writing being typed: its page, its line (or null for a new one), where it starts, and its text.
    private (int Sheet, int? Index, Vector2 At, float Size, Rgb Colour)? writing;
    private string typed = "";
    private bool focusWriting;
    private bool full;

    // Frame: the page's box, which marks are measured from; Surface: where they may be drawn (the paper, in from its
    // edges, down to the page's foot).
    private readonly record struct Area(int Sheet, UiRect Frame, UiRect Surface);
    // The custom colour (the swatch after the palette), and the tool the eyedropper hands back to.
    private Rgb custom = Rgb.Hex(0x7A4B9A);
    private Vector3 customPicked;
    private DrawingTool before = DrawingTool.Pencil;

    internal bool Drawing { get; private set; }
    internal DrawingTool Tool { get; set; } = DrawingTool.Pencil;
    // A palette index, or Palette.Length for the custom colour.
    internal int Colour { get; set; }
    internal Rgb Custom => custom;
    internal int Size { get; set; } = 1;
    // True while a stroke or eraser drag is in progress: the book takes no mouse input then.
    internal bool Busy => live.Count > 0 || erasing;
    internal bool Writing => writing != null;
    internal PageMarks Marks => marks;
    // For checks: where each toolbar control was drawn last frame, by name.
    internal Dictionary<string, Vector2> Controls { get; } = new(StringComparer.Ordinal);

    internal void Start() => Drawing = true;

    internal void Stop()
    {
        Finish();
        Drawing = false;
        live.Clear();
        if (erasing) { marks.EndChange(); erasing = false; }
    }

    // Escape: leaves the line being written as it was, else ends Draw mode.
    internal void Escape()
    {
        if (writing != null) { writing = null; typed = ""; return; }
        Stop();
    }

    internal void BeginFrame()
    {
        areas.Clear();
        Controls.Clear();
    }

    // A stamp page whose marks are drawn this frame, measured from the current window (the page's box) and shown as
    // far as surface reaches; live pages take the drawing while in Draw mode.
    internal void DrawSheet(int sheet, bool live, UiContext context, Vector4 pageInk, UiRect? surface = null)
    {
        var page = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        var paper = surface ?? page;
        var list = ImGui.GetWindowDrawList();
        list.PushClipRect(paper.Position, paper.Max, false);
        foreach (var stroke in marks.StrokesOn(sheet)) InkArt.Stroke(list, assets, page, stroke);
        foreach (var (index, text) in marks.TextsOn(sheet))
            if (writing is not { Index: { } editing } || editing != index || writing.Value.Sheet != sheet) InkArt.Text(list, page, text, context);
        if (live && Drawing) areas.Add(new Area(sheet, page, paper));
        if (this.live.Count > 1 && liveSheet == sheet) InkArt.Stroke(list, assets, page, this.live, StrokeWidths[Size], Ink(pageInk).Vector);
        list.PopClipRect();
    }

    // After the pages: each live page in a window of its own above its stamps, taking the mouse for the tool in hand.
    internal void Interact(UiContext context, Vector4 pageInk)
    {
        full = false;
        if (!Drawing) return;
        var mouse = ImGui.GetIO().MousePos;
        foreach (var area in areas)
        {
            ImGui.SetCursorScreenPos(area.Surface.Position);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            try
            {
                if (!ImGui.BeginChild("Drawing " + area.Sheet, area.Surface.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings)) continue;
                var list = ImGui.GetWindowDrawList();
                var page = area.Frame;
                if (writing is { } line && line.Sheet == area.Sheet) Write(page, area.Surface, line, context);
                ImGui.SetCursorScreenPos(area.Surface.Position);
                ImGui.InvisibleButton("Page", area.Surface.Size);
                var hovered = ImGui.IsItemHovered();
                var at = InkArt.FromPage(page, mouse);
                switch (Tool)
                {
                    case DrawingTool.Pencil:
                        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (ImGui.IsItemActivated()) { live.Clear(); liveSheet = area.Sheet; smoothed = at; pressure = 0.8f; live.Add(new InkPoint(at.X, at.Y, pressure)); }
                        if (ImGui.IsItemActive() && liveSheet == area.Sheet) Follow(at, page);
                        if (ImGui.IsItemDeactivated() && liveSheet == area.Sheet) EndStroke(pageInk);
                        full = hovered && !marks.HasRoomOn(area.Sheet, 1);
                        break;
                    case DrawingTool.Eraser:
                        var radius = EraserRadii[Size] * page.Size.X;
                        if (hovered || ImGui.IsItemActive()) list.AddCircle(mouse, radius, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.7f }), 32, MathF.Max(1f, context.Scale));
                        if (ImGui.IsItemActivated()) { marks.BeginChange(); erasing = true; }
                        if (ImGui.IsItemActive()) marks.Erase(area.Sheet, at, EraserRadii[Size], text => InkArt.Box(page, text, context));
                        if (ImGui.IsItemDeactivated() && erasing) { marks.EndChange(); erasing = false; }
                        break;
                    case DrawingTool.Eyedropper:
                        // Shows the colour of the mark under it; a click takes that colour and hands back the tool.
                        var found = ColourAt(area.Sheet, at, page, context);
                        if (hovered)
                        {
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            var swatch = mouse + new Vector2(14, -14) * MathF.Max(1f, context.Scale);
                            if (found is { } under) list.AddCircleFilled(swatch, 7 * MathF.Max(1f, context.Scale), ImGui.GetColorU32(under.Vector), 16);
                            list.AddCircle(swatch, 7 * MathF.Max(1f, context.Scale), ImGui.GetColorU32(UiTheme.Ink), 16, 1f);
                            if (found == null) ImGui.SetTooltip("Pick up the colour of a drawn line or writing");
                        }
                        if (ImGui.IsItemClicked() && found is { } taken)
                        {
                            Choose(taken, pageInk);
                            Tool = before;
                        }
                        break;
                    default:
                        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
                        if (ImGui.IsItemClicked())
                        {
                            Finish();
                            // A line clicked on is rewritten; elsewhere a new one starts there.
                            var hit = marks.TextsOn(area.Sheet).Select(entry => ((int, PageText)?)entry).LastOrDefault(entry =>
                            {
                                var (min, max) = InkArt.Box(page, entry!.Value.Item2, context);
                                return at.X >= min.X && at.Y >= min.Y && at.X <= max.X && at.Y <= max.Y;
                            });
                            if (hit is { } existing)
                            {
                                writing = (area.Sheet, existing.Item1, new Vector2(existing.Item2.X, existing.Item2.Y), existing.Item2.Size, existing.Item2.Colour);
                                typed = existing.Item2.Text;
                            }
                            else
                            {
                                var size = TextSizes[Size];
                                writing = (area.Sheet, null, at - new Vector2(0, size * 0.6f), size, Ink(pageInk));
                                typed = "";
                            }
                            focusWriting = true;
                        }
                        break;
                }
            }
            finally { ImGui.EndChild(); ImGui.PopStyleVar(); }
        }
        if (full) ImGui.SetTooltip("This page holds as much drawing as it may");
        // Undo from the keyboard, unless writing.
        var io = ImGui.GetIO();
        if (writing == null && io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z, false) && !Busy) marks.Undo();
    }

    // The colour of the mark nearest under a page point: a line within a short reach, or writing it lies on.
    private Rgb? ColourAt(int sheet, Vector2 at, UiRect page, UiContext context)
    {
        Rgb? nearest = null;
        var best = 0.015f * 0.015f;
        foreach (var stroke in marks.StrokesOn(sheet))
            foreach (var point in stroke.Points)
            {
                var distance = Vector2.DistanceSquared(new Vector2(point.X, point.Y), at);
                if (distance <= best + stroke.Width * stroke.Width / 4) { best = distance; nearest = stroke.Colour; }
            }
        foreach (var (_, text) in marks.TextsOn(sheet))
        {
            var (min, max) = InkArt.Box(page, text, context);
            if (at.X >= min.X && at.Y >= min.Y && at.X <= max.X && at.Y <= max.Y) nearest = text.Colour;
        }
        return nearest;
    }

    // Makes a colour the pencil's: its palette swatch when it is one, else the custom colour.
    private void Choose(Rgb colour, Vector4 pageInk)
    {
        for (var index = 0; index < Palette.Length; index++)
            if ((Palette[index].Hex < 0 ? Rgb.Of(pageInk) : Rgb.Hex(Palette[index].Hex)) == colour) { Colour = index; return; }
        custom = colour;
        Colour = Palette.Length;
    }

    // Adds the mouse to the stroke: smoothed, a point every couple of pixels, pressed harder when moving slowly.
    private void Follow(Vector2 at, UiRect page)
    {
        var unit = 1f / MathF.Max(1f, page.Size.X);
        smoothed = Vector2.Lerp(smoothed, at, 0.55f);
        var last = live[^1];
        var distance = Vector2.Distance(smoothed, new Vector2(last.X, last.Y));
        if (distance < Spacing * unit || live.Count >= PageMarks.MaximumStrokeSamples) return;
        var speed = distance / unit / MathF.Max(1e-3f, ImGui.GetIO().DeltaTime);
        pressure = pressure + (Math.Clamp(1.1f - speed / 2200f, 0.45f, 1f) - pressure) * 0.3f;
        var steps = (int)(distance / (Spacing * unit));
        for (var step = 1; step <= steps; step++)
        {
            var point = Vector2.Lerp(new Vector2(last.X, last.Y), smoothed, step / (float)steps);
            live.Add(new InkPoint(point.X, point.Y, pressure));
        }
    }

    private void EndStroke(Vector4 pageInk)
    {
        // A click without a drag leaves a dot.
        if (live.Count == 1) live.Add(live[0] with { X = live[0].X + 0.0015f });
        marks.AddStroke(new InkStroke(liveSheet, Ink(pageInk), StrokeWidths[Size], [.. live]));
        live.Clear();
    }

    // The line being written: an input over the page in the handwriting face, kept on Enter or leaving it.
    private void Write(UiRect page, UiRect surface, (int Sheet, int? Index, Vector2 At, float Size, Rgb Colour) line, UiContext context)
    {
        var type = InkArt.Type(page, line.Size, context);
        var font = UiTypography.ResolveFont(typed.Length > 0 ? typed : "Aa", TextRole.Heading, type);
        ImGui.PushFont(font);
        ImGui.SetWindowFontScale(UiTypography.Size(TextRole.Heading, type) / ImGui.GetFontSize());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiTheme.PaperInset with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.Text, line.Colour.Vector);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, UiTheme.Gold with { W = 0.4f });
        try
        {
            var at = InkArt.OnPage(page, line.At.X, line.At.Y);
            ImGui.SetCursorScreenPos(at);
            ImGui.SetNextItemWidth(MathF.Max(40f, surface.Max.X - at.X - 4f));
            if (focusWriting) { ImGui.SetKeyboardFocusHere(); focusWriting = false; }
            var entered = ImGui.InputText("##Writing", ref typed, PageMarks.TextLimit * 4, ImGuiInputTextFlags.EnterReturnsTrue);
            // Escape leaves the line as it was; Enter, or clicking away, keeps it.
            if (ImGui.IsItemDeactivated() && ImGui.IsKeyPressed(ImGuiKey.Escape, false)) { writing = null; typed = ""; }
            else if (entered || ImGui.IsItemDeactivated()) Finish();
        }
        finally
        {
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(2);
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
        }
    }

    // Keeps the line being written (empty, it is taken away).
    private void Finish()
    {
        if (writing is not { } line) return;
        writing = null;
        marks.SetText(line.Index, new PageText(line.Sheet, typed, line.At.X, line.At.Y, line.Size, line.Colour));
        typed = "";
    }

    private Rgb Ink(Vector4 pageInk) => Colour >= Palette.Length ? custom : Palette[Colour].Hex < 0 ? Rgb.Of(pageInk) : Rgb.Hex(Palette[Colour].Hex);

    // How wide the toolbar is at this height: tools, palette, sizes, and Undo, with a gap between each group.
    internal static float ToolbarWidth(float height) => height * (4 * 1.2f + (Palette.Length + 1) * 0.72f + 3 * 0.9f + 1.2f + 3 * 0.4f);

    // The toolbar across the footer, from left to right of bar: the three tools, the palette, the sizes, and Undo.
    // raised: the toolbar lies over the page (a narrow page), so it gets a paper backing of its own.
    internal void Toolbar(UiRect bar, UiContext context, Vector4 pageInk, bool raised = false)
    {
        // Narrower than it needs, its controls shrink to fit, centred on the bar's height.
        var fit = MathF.Min(1f, bar.Size.X / ToolbarWidth(bar.Size.Y));
        var height = bar.Size.Y * fit;
        var x = bar.Position.X;
        var gap = height * 0.4f;
        var list = ImGui.GetWindowDrawList();
        if (raised)
        {
            var width = MathF.Min(bar.Size.X, ToolbarWidth(height)) + 4;
            list.AddRectFilled(bar.Position - new Vector2(4, 2), bar.Position + new Vector2(width, bar.Size.Y + 2), ImGui.GetColorU32(UiTheme.Paper), 4 * context.Scale);
            list.AddRect(bar.Position - new Vector2(4, 2), bar.Position + new Vector2(width, bar.Size.Y + 2), ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.35f }), 4 * context.Scale);
        }
        bar = new UiRect(bar.Position + new Vector2(0, (bar.Size.Y - height) / 2), new Vector2(bar.Size.X, height));
        foreach (var tool in Enum.GetValues<DrawingTool>())
        {
            var cell = new UiRect(new Vector2(x, bar.Position.Y), new Vector2(height * 1.2f, height));
            x += cell.Size.X;
            if (BookControl.Draw("Tool " + tool, "", cell, context, BookControlStyle.Ink, hint: tool switch
            {
                DrawingTool.Pencil => "Pencil",
                DrawingTool.Eraser => "Eraser: rubs out what it passes over",
                DrawingTool.Text => "Write: click a page to type a line",
                _ => "Eyedropper: pick up the colour of a drawn line or writing",
            }))
            {
                Finish();
                if (tool == DrawingTool.Eyedropper && Tool != DrawingTool.Eyedropper) before = Tool;
                Tool = tool;
            }
            var hovered = ImGui.IsItemHovered();
            var selected = Tool == tool;
            var ink = ImGui.GetColorU32(selected || hovered ? UiTheme.Ink : UiTheme.MutedInk);
            if (selected) list.AddRectFilled(cell.Position + new Vector2(1), cell.Max - new Vector2(1), ImGui.GetColorU32(UiTheme.PaperInset with { W = 0.8f }), 3 * context.Scale);
            Icon(list, cell, tool, ink, context);
            Controls[tool.ToString()] = cell.Position + cell.Size / 2;
        }
        x += gap;
        for (var index = 0; index < Palette.Length; index++)
        {
            var radius = height * 0.26f;
            var cell = new UiRect(new Vector2(x, bar.Position.Y), new Vector2(height * 0.72f, height));
            if (BookControl.Draw("Colour " + index, "", cell, context, BookControlStyle.Ink, hint: Palette[index].Name)) Colour = index;
            var centre = cell.Position + cell.Size / 2;
            var colour = Palette[index].Hex < 0 ? pageInk : Rgb.Hex(Palette[index].Hex).Vector;
            list.AddCircleFilled(centre, radius, ImGui.GetColorU32(colour), 16);
            list.AddCircle(centre, radius, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.5f }), 16, 1f);
            if (Colour == index) list.AddCircle(centre, radius + 2.5f, ImGui.GetColorU32(UiTheme.Ink), 20, MathF.Max(1f, 1.5f * context.Scale));
            Controls["Colour " + index] = centre;
            x += cell.Size.X;
        }
        // The custom colour: picked (and shown) here, or taken up by the eyedropper.
        {
            var cell = new UiRect(new Vector2(x, bar.Position.Y), new Vector2(height * 0.72f, height));
            var centre = cell.Position + cell.Size / 2;
            var chosen = Colour == Palette.Length;
            if (PaperColours.Swatch("Custom colour", centre, height * 0.52f, chosen ? custom.Vector : null, chosen, chosen ? $"Your colour ({custom}); click to change it" : "Any colour", context))
            {
                Colour = Palette.Length;
                customPicked = new Vector3(custom.Vector.X, custom.Vector.Y, custom.Vector.Z);
                ImGui.OpenPopup("Pencil colour");
            }
            if (PaperColours.Begin("Pencil colour", context))
            {
                try
                {
                    ImGui.TextUnformatted("Pencil colour");
                    PaperColours.Picker(ref customPicked, context);
                    custom = Rgb.Of(new Vector4(customPicked, 1));
                }
                finally { PaperColours.End(); }
            }
            Controls["Custom colour"] = centre;
            x += cell.Size.X;
        }
        x += gap;
        for (var index = 0; index < 3; index++)
        {
            var cell = new UiRect(new Vector2(x, bar.Position.Y), new Vector2(height * 0.9f, height));
            if (BookControl.Draw("Size " + index, "", cell, context, BookControlStyle.Ink, hint: index switch { 0 => "Fine", 1 => "Medium", _ => "Bold" })) Size = index;
            var centre = cell.Position + cell.Size / 2;
            list.AddCircleFilled(centre, height * (0.08f + 0.07f * index), ImGui.GetColorU32(Size == index ? UiTheme.Ink : UiTheme.MutedInk), 12);
            if (Size == index) list.AddCircle(centre, height * 0.34f, ImGui.GetColorU32(UiTheme.Ink), 16, MathF.Max(1f, context.Scale));
            Controls["Size " + index] = centre;
            x += cell.Size.X;
        }
        x += gap;
        var undo = new UiRect(new Vector2(x, bar.Position.Y), new Vector2(height * 1.2f, height));
        ImGui.BeginDisabled(!marks.CanUndo || Busy);
        if (BookControl.Draw("Undo", "", undo, context, BookControlStyle.Ink, hint: "Undo (Ctrl+Z)")) { Finish(); marks.Undo(); }
        var undoInk = !marks.CanUndo || Busy ? UiTheme.MutedInk with { W = 0.4f } : ImGui.IsItemHovered() ? UiTheme.Ink : UiTheme.MutedInk;
        ImGui.EndDisabled();
        // A curved arrow turning back.
        var turn = undo.Position + undo.Size / 2;
        var r = height * 0.26f;
        var stroke = MathF.Max(1f, 1.4f * context.Scale);
        list.PathArcTo(turn + new Vector2(0, r * 0.2f), r, MathF.PI * 1.1f, MathF.PI * 2.15f, 12);
        list.PathStroke(ImGui.GetColorU32(undoInk), ImDrawFlags.None, stroke);
        var tip = turn + new Vector2(-r * 0.95f, r * 0.05f);
        list.AddTriangleFilled(tip + new Vector2(-r * 0.4f, -r * 0.1f), tip + new Vector2(r * 0.4f, -r * 0.1f), tip + new Vector2(0, r * 0.55f), ImGui.GetColorU32(undoInk));
        Controls["Undo"] = undo.Position + undo.Size / 2;
    }

    // A pencil (a slanted shaft with a tip), an eraser (a slanted block), or writing (a small "Aa").
    private static void Icon(ImDrawListPtr list, UiRect cell, DrawingTool tool, uint ink, UiContext context)
    {
        var centre = cell.Position + cell.Size / 2;
        var r = cell.Size.Y * 0.32f;
        var line = MathF.Max(1f, 1.4f * context.Scale);
        switch (tool)
        {
            case DrawingTool.Pencil:
                var from = centre + new Vector2(r, -r);
                var to = centre + new Vector2(-r * 0.6f, r * 0.6f);
                list.AddLine(from, to, ink, line * 2.2f);
                list.AddTriangleFilled(to + new Vector2(-r * 0.15f, -r * 0.25f), to + new Vector2(r * 0.25f, r * 0.15f), centre + new Vector2(-r, r), ink);
                break;
            case DrawingTool.Eraser:
                var (sin, cos) = MathF.SinCos(-MathF.PI / 4);
                Vector2 Turn(float x, float y) => centre + new Vector2(x * cos - y * sin, x * sin + y * cos);
                list.AddQuad(Turn(-r, -r * 0.45f), Turn(r, -r * 0.45f), Turn(r, r * 0.45f), Turn(-r, r * 0.45f), ink, line);
                list.AddLine(Turn(-r * 0.2f, -r * 0.45f), Turn(-r * 0.2f, r * 0.45f), ink, line);
                break;
            case DrawingTool.Eyedropper:
                // A dropper: a slanted tube with a bulb at its top and a drop at its tip.
                var top = centre + new Vector2(r * 0.7f, -r * 0.7f);
                var tip = centre + new Vector2(-r * 0.75f, r * 0.75f);
                list.AddLine(top, tip, ink, line * 1.8f);
                list.AddCircleFilled(top, r * 0.32f, ink, 10);
                list.AddCircleFilled(tip + new Vector2(-r * 0.05f, r * 0.25f), r * 0.14f, ink, 8);
                break;
            default:
                var size = UiTypography.Measure("Aa", TextRole.Caption, context, float.MaxValue, false);
                UiTypography.Draw(list, centre - size / 2, "Aa", TextRole.Caption, context, size.X + 2, ImGui.ColorConvertU32ToFloat4(ink), false);
                break;
        }
    }
}
