using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Stamps;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// The owner's stamps on the passport's stamp pages: drawn where they were put, with an info page for any
// stamp, and, in Arrange mode, picked up, carried (across page turns too), put down, and turned.
// Clicking a stamp opens its info on the opposite page. In Arrange mode a press-and-drag carries a stamp
// until release and a click picks it up until the next click; each round knob turns its stamp, and each ×
// takes its stamp off the page to wait among the unplaced (an earned stamp is never deleted). While a
// stamp is carried the mouse wheel turns it, resting at the book's outer edge turns the page, a click or
// release on a stamp page puts it down, and Escape or a right click puts it back where it came from.
internal sealed class BookShellStamps(StampCollection collection, IBookAssets assets)
{
    // A stamp's diameter as a share of its page's width.
    internal const float Size = 0.34f;
    private const float EdgeShare = 0.06f;
    private const float EdgeDwell = 0.6f;
    private static readonly float WheelStep = 5f * MathF.PI / 180f;

    private readonly List<Area> areas = [];
    private float edgeTime;
    private bool startedOnStamp;
    private bool draggedOnStamp;
    private (StampInstance Stamp, float Rotation)? turning;

    private readonly record struct Area(int Sheet, UiRect Bounds, bool Right);

    internal sealed record Carry(StampInstance Stamp, float Rotation, bool Held);

    internal bool Arrange { get; set; }
    internal Carry? Carrying { get; private set; }
    // The stamp whose info page is open.
    internal StampInstance? Info { get; private set; }
    // True while a stamp is carried or turned: the book neither moves nor turns from mouse input then.
    internal bool Busy => Carrying != null || turning != null;
    // True for the frame a click put a carried stamp down or cancelled it, so the book ignores that click.
    internal bool TookClick { get; private set; }

    // Takes a stamp in hand: from its page, or a waiting one to place. Arrange mode is left as it was.
    internal void PickUp(StampInstance stamp, bool held = false)
    {
        Info = null;
        Carrying = new Carry(stamp, collection.PlacementOf(stamp)?.Rotation ?? StampArt.Tilt(stamp.Id), held);
    }

    // Puts a carried stamp back where it was (or among the unplaced, if it never had a place).
    internal void Cancel()
    {
        Carrying = null;
        turning = null;
    }

    internal void CloseInfo() => Info = null;

    // Called before the pages of a frame are drawn.
    internal void BeginFrame() => areas.Clear();

    // Draws one stamp page's stamps inside its page window. live is false for turning leaves and pages
    // that are only passing by; only live pages take input and can receive a carried stamp.
    internal void DrawSheet(int sheet, bool live, bool right, UiContext context)
    {
        var area = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        if (live) areas.Add(new Area(sheet, area, right));
        var diameter = area.Size.X * Size;
        var list = ImGui.GetWindowDrawList();
        var shown = collection.OnSheet(sheet).Where(entry => entry.Stamp != Carrying?.Stamp).ToArray();
        foreach (var entry in shown)
        {
            var rotation = turning is { } held && held.Stamp == entry.Stamp ? held.Rotation : entry.Placement.Rotation;
            var bounds = Bounds(area, entry.Placement, diameter);
            StampArt.Issued(bounds, entry.Stamp, assets, context, rotation);
            if (live && Info == entry.Stamp) list.AddCircle(bounds.Position + bounds.Size / 2, diameter * 0.52f, ImGui.GetColorU32(UiTheme.Gold), 48, MathF.Max(1f, 2f * context.Scale));
        }
        // While a stamp is carried, clicks belong to it, not to the stamps beneath.
        if (!live || Carrying != null) return;

        // Hit testing runs top layer first, so the stamp drawn on top is the one a click reaches.
        var origin = ImGui.GetCursorScreenPos();
        foreach (var entry in shown.Reverse())
        {
            var bounds = Bounds(area, entry.Placement, diameter);
            var center = bounds.Position + bounds.Size / 2;
            ImGui.PushID(entry.Stamp.Id);
            try
            {
                if (Arrange) { Knob(entry, center, diameter, area, context); Remove(entry, center, diameter, area, context); }
                ImGui.SetCursorScreenPos(bounds.Position);
                ImGui.InvisibleButton("Stamp", bounds.Size);
                if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(Arrange ? ImGuiMouseCursor.ResizeAll : ImGuiMouseCursor.Hand);
                if (ImGui.IsItemHovered() && Arrange) list.AddCircle(center, diameter * 0.52f, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.6f }), 48, MathF.Max(1f, context.Scale));
                if (ImGui.IsItemActivated()) { startedOnStamp = true; draggedOnStamp = false; }
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) draggedOnStamp = true;
                if (Arrange && ImGui.IsItemActive() && draggedOnStamp) PickUp(entry.Stamp, held: true);
                else if (ImGui.IsItemDeactivated() && startedOnStamp && Carrying == null)
                {
                    startedOnStamp = false;
                    // A click, not a drag: pick it up in Arrange mode, otherwise open or close its info.
                    if (!draggedOnStamp && Arrange) PickUp(entry.Stamp);
                    else if (!draggedOnStamp) Info = Info == entry.Stamp ? null : entry.Stamp;
                }
            }
            finally { ImGui.PopID(); }
        }
        ImGui.SetCursorScreenPos(origin);
    }

    // The round knob above a stamp in Arrange mode: drag it around the stamp to turn the stamp.
    private void Knob(PlacedStamp entry, Vector2 center, float diameter, UiRect area, UiContext context)
    {
        var rotation = turning is { } held && held.Stamp == entry.Stamp ? held.Rotation : entry.Placement.Rotation;
        var (sin, cos) = MathF.SinCos(rotation);
        var reach = diameter * 0.62f;
        var radius = MathF.Max(5f, diameter * 0.06f);
        var knob = Inside(center + new Vector2(sin * reach, -cos * reach), radius, area);
        var list = ImGui.GetWindowDrawList();
        list.AddLine(center + new Vector2(sin, -cos) * diameter * 0.5f, knob, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.5f }), MathF.Max(1f, context.Scale));
        list.AddCircleFilled(knob, radius, ImGui.GetColorU32(UiTheme.Paper));
        list.AddCircle(knob, radius, ImGui.GetColorU32(UiTheme.Ink), 24, MathF.Max(1f, 1.5f * context.Scale));
        ImGui.SetCursorScreenPos(knob - new Vector2(radius * 1.6f));
        ImGui.InvisibleButton("Turn", new Vector2(radius * 3.2f));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemActive())
        {
            var toward = ImGui.GetIO().MousePos - center;
            if (toward.LengthSquared() > 1f) turning = (entry.Stamp, MathF.Atan2(toward.X, -toward.Y));
        }
        if (ImGui.IsItemDeactivated() && turning is { } done && done.Stamp == entry.Stamp)
        {
            collection.Place(entry.Stamp, entry.Placement with { Rotation = done.Rotation });
            turning = null;
        }
    }

    // The × beside a stamp in Arrange mode, a quarter turn round from its knob so the two never meet.
    private void Remove(PlacedStamp entry, Vector2 center, float diameter, UiRect area, UiContext context)
    {
        var rotation = turning is { } held && held.Stamp == entry.Stamp ? held.Rotation : entry.Placement.Rotation;
        var (sin, cos) = MathF.SinCos(rotation + MathF.PI / 2);
        var radius = MathF.Max(5f, diameter * 0.06f);
        var mark = Inside(center + new Vector2(sin, -cos) * diameter * 0.62f, radius, area);
        var list = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(mark - new Vector2(radius * 1.6f));
        ImGui.InvisibleButton("Remove", new Vector2(radius * 3.2f));
        var hovered = ImGui.IsItemHovered();
        var ink = ImGui.GetColorU32(hovered ? UiTheme.Action : UiTheme.Ink);
        list.AddCircleFilled(mark, radius, ImGui.GetColorU32(UiTheme.Paper));
        list.AddCircle(mark, radius, ink, 24, MathF.Max(1f, 1.5f * context.Scale));
        var arm = radius * 0.45f;
        var stroke = MathF.Max(1f, 1.5f * context.Scale);
        list.AddLine(mark - new Vector2(arm), mark + new Vector2(arm), ink, stroke);
        list.AddLine(mark + new Vector2(-arm, arm), mark + new Vector2(arm, -arm), ink, stroke);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Take this stamp off the page. It waits to be placed again.");
        }
        if (ImGui.IsItemClicked()) collection.Unplace(entry.Stamp);
    }

    // A knob or × whose stamp sits by the page edge is kept on the page; past it, its button would make the
    // page scroll.
    private static Vector2 Inside(Vector2 point, float radius, UiRect area)
    {
        var margin = new Vector2(radius * 1.6f + 1f);
        return Vector2.Clamp(point, area.Position + margin, Vector2.Max(area.Position + margin, area.Max - margin));
    }

    // After the pages are drawn: moves, turns, and puts down a carried stamp. Returns -1 or 1 when resting at
    // the book's outer edge should turn the page that way, otherwise 0.
    internal int Update(UiRect window, UiRect book, UiContext context, float seconds)
    {
        TookClick = false;
        if (Carrying is not { } carry) { edgeTime = 0f; return 0; }
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        if (io.MouseWheel != 0f && book.Contains(mouse)) Carrying = carry = carry with { Rotation = StampPlacement.Normalize(carry.Rotation - io.MouseWheel * WheelStep) };
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { Cancel(); TookClick = true; return 0; }
        var over = areas.Any(area => area.Bounds.Contains(mouse));
        var released = carry.Held ? !ImGui.IsMouseDown(ImGuiMouseButton.Left) : ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (released)
        {
            TookClick = !carry.Held;
            if (over) { Put(carry, areas.First(area => area.Bounds.Contains(mouse)), mouse); return 0; }
            // A drag let go off any stamp page returns the stamp; a click there keeps it in hand.
            if (carry.Held) { Cancel(); return 0; }
        }

        // The stamp in hand, in a window of its own above the pages, with a soft shadow beneath it.
        var page = over ? areas.First(area => area.Bounds.Contains(mouse)).Bounds : areas.Count > 0 ? areas[0].Bounds : book;
        var diameter = page.Size.X * Size;
        ImGui.SetCursorScreenPos(window.Position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        try
        {
            if (ImGui.BeginChild("Carried stamp", window.Size, false, ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings))
            {
                // The shadow is the stamp's own shape, so it lines up with art that is not centred in its image.
                StampArt.Issued(new UiRect(mouse + new Vector2(3, 5) * context.Scale - new Vector2(diameter / 2), new Vector2(diameter)), carry.Stamp, assets, context, carry.Rotation, tint: new Vector4(0.1f, 0.07f, 0.03f, 0.22f));
                StampArt.Issued(new UiRect(mouse - new Vector2(diameter / 2), new Vector2(diameter)), carry.Stamp, assets, context, carry.Rotation, 0.92f);
            }
        }
        finally { ImGui.EndChild(); ImGui.PopStyleVar(); }
        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        // Resting at the outer edge of the book turns the page, again and again while it stays there.
        var edge = book.Size.X * EdgeShare;
        var side = !book.Contains(mouse) ? 0 : mouse.X < book.Position.X + edge ? -1 : mouse.X > book.Max.X - edge ? 1 : 0;
        if (side == 0) { edgeTime = 0f; return 0; }
        edgeTime += seconds;
        if (edgeTime < EdgeDwell) return 0;
        edgeTime = 0f;
        return side;
    }

    private void Put(Carry carry, Area area, Vector2 mouse)
    {
        var diameter = area.Bounds.Size.X * Size;
        var room = Vector2.Max(Vector2.One, area.Bounds.Size - new Vector2(diameter));
        var at = Vector2.Clamp((mouse - area.Bounds.Position - new Vector2(diameter / 2)) / room, Vector2.Zero, Vector2.One);
        collection.Place(carry.Stamp, new StampPlacement(area.Sheet, at.X, at.Y, carry.Rotation));
        Carrying = null;
        startedOnStamp = false;
    }

    // Where the stamp with the open info page sits: its page side, or null when it is not on these pages.
    internal bool? InfoSide()
    {
        if (Info is not { } stamp || collection.PlacementOf(stamp) is not { } placement) return null;
        foreach (var area in areas)
            if (area.Sheet == placement.Sheet) return area.Right;
        return null;
    }

    // The open stamp's info, laid like a card over a page: what it is, when and from what it was collected,
    // who designed it, and where it sits. folio is the printed number of the page the stamp is on.
    internal void DrawInfo(UiRect region, int folio, UiContext context)
    {
        if (Info is not { } stamp) return;
        ImGui.SetCursorScreenPos(region.Position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        try
        {
            if (!ImGui.BeginChild("Stamp info", region.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar)) return;
            var list = ImGui.GetWindowDrawList();
            var card = new UiRect(region.Position, region.Size);
            PageSurface.Draw(list, card, context);
            var padding = 20 * context.Scale;
            var width = card.Size.X - padding * 2;
            var flow = new PageFlow(region.Position + new Vector2(padding), width, context, true);
            flow.Text("STAMP / LOCAL DEMO", TextRole.Caption, true);
            flow.Gap(8);
            flow.Text(stamp.Design.Name, TextRole.Title);
            flow.Gap(10);
            var art = flow.Reserve(MathF.Min(width * 0.4f, 110 * context.Scale));
            StampArt.Issued(art, stamp, assets, context, collection.PlacementOf(stamp)?.Rotation ?? 0f);
            flow.Rule();
            Field(ref flow, "COLLECTED", $"{StampArt.Date(stamp.CollectedAt)}, on page {folio.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            Field(ref flow, "FROM", stamp.Source is { } source ? $"{Title(source.Kind)}: {source.Title}" : "Not recorded");
            Field(ref flow, "DESIGNED BY", stamp.Design.Creator ?? "Not recorded");
            flow.Text("A local demo stamp issued on this PC, not an official achievement.", TextRole.Caption, true);
            flow.Gap(12);
            ImGui.SetCursorScreenPos(region.Position + new Vector2(padding, padding + flow.Height));
            if (BookControl.Link("Close", context)) Info = null;
            ImGui.Dummy(new Vector2(0, padding));
        }
        finally { ImGui.EndChild(); ImGui.PopStyleVar(); }

        static void Field(ref PageFlow flow, string label, string value)
        {
            flow.Text(label, TextRole.Caption, true);
            flow.Gap(3);
            flow.Text(value);
            flow.Gap(10);
        }

        static string Title(string kind) => kind.Length == 0 ? kind : kind[..1].ToUpperInvariant() + kind[1..].ToLowerInvariant();
    }

    // A stamp's centre is kept far enough from every edge that the whole stamp stays on its page.
    private static UiRect Bounds(UiRect area, StampPlacement placement, float diameter)
    {
        var room = area.Size - new Vector2(diameter);
        return new UiRect(area.Position + new Vector2(placement.X * room.X, placement.Y * room.Y), new Vector2(diameter));
    }
}
