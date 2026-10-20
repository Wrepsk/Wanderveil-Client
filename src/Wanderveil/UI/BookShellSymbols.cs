using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Decorating the cover and the first pages with symbols. A sticker sheet of the provided set lies over the book;
// picking a symbol slides the sheet away and puts the symbol in hand, a click puts it down, and the sheet comes
// back for the next. Placed symbols are picked up by a click or carried by a drag; a knob turns and resizes each
// (its angle turns, its distance sizes), and a × takes it off. While a symbol is carried the mouse wheel turns it,
// and Escape or a right click puts it back (a new one returns to the sheet).
internal sealed class BookShellSymbols(PassportProfile holder, IBookAssets assets)
{
    private static readonly float WheelStep = 5f * MathF.PI / 180f;
    // How far a symbol's knob and × sit from its centre, as a share of its width.
    private const float HandleReach = 0.62f;

    private readonly List<Area> areas = [];
    // The symbol being turned, resized, or recoloured, as it shows until the change is kept.
    private (int Index, PlacedSymbol Symbol)? adjusting;
    // The symbol whose colour popup is open, and the custom colour being picked for it.
    private int colourTarget = -1;
    private Vector3 picked;
    private bool startedOnSymbol;
    private bool draggedOnSymbol;
    private float sheetLift;
    // A surface's width last seen, which sizes the symbol in hand while no surface is under it.
    private float lastWidth = 400f;

    private readonly record struct Area(string Surface, UiRect Bounds);

    // From: the index of a placed symbol being moved, or -1 for a new one from the sheet.
    internal sealed record Carry(string Symbol, int From, float Rotation, float Size, bool Held);

    internal bool Decorating { get; private set; }
    internal bool SheetOpen { get; private set; }
    internal Carry? Carrying { get; private set; }
    // True while a symbol is carried or adjusted: the book neither moves nor turns from mouse input then.
    internal bool Busy => Carrying != null || adjusting != null;
    // True for the frame a click put a carried symbol down or cancelled it, so the book ignores that click.
    internal bool TookClick { get; private set; }
    // The placed symbol in hand, left out where it was.
    internal int Hidden => Carrying?.From ?? -1;
    // A placed symbol being changed, drawn as it is changing (its knob dragged, or its colour being picked).
    internal (int Index, PlacedSymbol Symbol)? Changing => adjusting;
    // Where the sheet lay last frame, while it showed.
    internal UiRect? Sheet { get; private set; }

    // For checks: where each sheet symbol, and each placed symbol's body, knob, and ×, were last drawn.
    internal Dictionary<string, Vector2> SheetCells { get; } = new(StringComparer.Ordinal);
    internal Dictionary<int, (Vector2 Body, Vector2 Knob, Vector2 Remove, Vector2 Colour)> Handles { get; } = [];
    // For checks: where each swatch of the open colour popup was drawn (0 the passport's colour, then the inks).
    internal Dictionary<int, Vector2> ColourSwatches { get; } = [];

    internal void Start()
    {
        Decorating = true;
        SheetOpen = true;
    }

    internal void Stop()
    {
        Decorating = SheetOpen = false;
        Carrying = null;
        adjusting = null;
        colourTarget = -1;
        sheetLift = 0f;
    }

    internal void ToggleSheet() => SheetOpen = !SheetOpen && Carrying == null;

    internal void CloseSheet() => SheetOpen = false;

    // Puts a carried symbol back: a placed one where it was, a new one onto the sheet.
    internal void Cancel()
    {
        if (Carrying is { From: < 0 }) SheetOpen = true;
        Carrying = null;
        adjusting = null;
    }

    // Called before the surfaces of a frame are drawn.
    internal void BeginFrame()
    {
        areas.Clear();
        Handles.Clear();
        ColourSwatches.Clear();
        SheetCells.Clear();
        Sheet = null;
    }

    // A surface that takes symbols this frame: a resting first page, or the closed cover.
    internal void Register(string surface, UiRect bounds)
    {
        if (!Decorating) return;
        areas.Add(new Area(surface, bounds));
        lastWidth = bounds.Size.X;
    }

    // The knob and × of every placed symbol on the live surfaces, each surface in a window of its own above its
    // page's content (so they, not the page's fields, take the mouse). Draw after the surfaces.
    internal void DrawHandles(UiContext context)
    {
        if (!Decorating || Carrying != null || SheetOpen || holder.Identity.Appearance?.Symbols is not { } layer) return;
        foreach (var area in areas)
        {
            ImGui.SetCursorScreenPos(area.Bounds.Position);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            try
            {
                if (!ImGui.BeginChild("Symbols " + area.Surface, area.Bounds.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings)) continue;
                // Top first, so the symbol drawn on top is the one a click reaches.
                for (var index = layer.Items.Length - 1; index >= 0; index--)
                    if (layer.Items[index].Surface == area.Surface) Symbol(index, layer.Items[index], area, context);
                if ((uint)colourTarget < (uint)layer.Items.Length && layer.Items[colourTarget].Surface == area.Surface) ColourPopup(layer.Items[colourTarget], area, context);
            }
            finally { ImGui.EndChild(); ImGui.PopStyleVar(); }
        }
    }

    private void Symbol(int index, PlacedSymbol placed, Area area, UiContext context)
    {
        var shown = adjusting is { } held && held.Index == index ? held.Symbol : placed;
        var (center, size) = SymbolArt.Place(shown, area.Bounds);
        var list = ImGui.GetWindowDrawList();
        var radius = MathF.Max(5f, 7f * context.Scale);
        var stroke = MathF.Max(1f, 1.5f * context.Scale);
        ImGui.PushID(index);
        try
        {
            // The knob, along the symbol's turn: drag it round to turn the symbol, in or out to resize it.
            var (sin, cos) = MathF.SinCos(shown.Rotation);
            var knob = Inside(center + new Vector2(sin, -cos) * MathF.Max(size * HandleReach, radius * 3), radius, area.Bounds);
            list.AddLine(center + new Vector2(sin, -cos) * size * SymbolArt.Reach, knob, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.5f }), MathF.Max(1f, context.Scale));
            list.AddCircleFilled(knob, radius, ImGui.GetColorU32(UiTheme.Paper));
            list.AddCircle(knob, radius, ImGui.GetColorU32(UiTheme.Ink), 24, stroke);
            ImGui.SetCursorScreenPos(knob - new Vector2(radius * 1.6f));
            ImGui.InvisibleButton("Turn and size", new Vector2(radius * 3.2f));
            if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemHovered() && !ImGui.IsItemActive()) ImGui.SetTooltip("Drag round to turn, in or out to resize");
            if (ImGui.IsItemActive())
            {
                var toward = ImGui.GetIO().MousePos - center;
                if (toward.LengthSquared() > 1f)
                    adjusting = (index, placed with { Rotation = MathF.Atan2(toward.X, -toward.Y), Size = Math.Clamp(toward.Length() / HandleReach / area.Bounds.Size.X, PlacedSymbol.MinimumSize, PlacedSymbol.MaximumSize) });
            }
            if (ImGui.IsItemDeactivated() && adjusting is { } done && done.Index == index)
            {
                holder.ReplaceSymbol(index, done.Symbol);
                adjusting = null;
            }

            // The ×, a quarter turn round from the knob: takes the symbol off.
            (sin, cos) = MathF.SinCos(shown.Rotation + MathF.PI / 2);
            var mark = Inside(center + new Vector2(sin, -cos) * MathF.Max(size * HandleReach, radius * 3), radius, area.Bounds);
            ImGui.SetCursorScreenPos(mark - new Vector2(radius * 1.6f));
            ImGui.InvisibleButton("Remove", new Vector2(radius * 3.2f));
            var hovered = ImGui.IsItemHovered();
            var ink = ImGui.GetColorU32(hovered ? UiTheme.Action : UiTheme.Ink);
            list.AddCircleFilled(mark, radius, ImGui.GetColorU32(UiTheme.Paper));
            list.AddCircle(mark, radius, ink, 24, stroke);
            var arm = radius * 0.45f;
            list.AddLine(mark - new Vector2(arm), mark + new Vector2(arm), ink, stroke);
            list.AddLine(mark + new Vector2(-arm, arm), mark + new Vector2(arm, -arm), ink, stroke);
            if (hovered) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip("Take this symbol off"); }
            var removed = ImGui.IsItemClicked();

            // The colour dot, a quarter turn the other way from the ×: opens the symbol's colours.
            (sin, cos) = MathF.SinCos(shown.Rotation - MathF.PI / 2);
            var dot = Inside(center + new Vector2(sin, -cos) * MathF.Max(size * HandleReach, radius * 3), radius, area.Bounds);
            ImGui.SetCursorScreenPos(dot - new Vector2(radius * 1.6f));
            ImGui.InvisibleButton("Colour", new Vector2(radius * 3.2f));
            var dotHovered = ImGui.IsItemHovered();
            list.AddCircleFilled(dot, radius, ImGui.GetColorU32(shown.Colour?.Vector ?? (area.Surface == SymbolSurface.Cover ? UiTheme.Foil : UiTheme.EditionInk)));
            list.AddCircle(dot, radius, ImGui.GetColorU32(dotHovered ? UiTheme.Action : UiTheme.Ink), 24, stroke);
            if (dotHovered) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip("This symbol's colour"); }
            var recolour = ImGui.IsItemClicked();

            // The symbol itself: a click picks it up, a drag carries it.
            var body = new Vector2(MathF.Max(size * 0.8f, radius * 2));
            ImGui.SetCursorScreenPos(center - body / 2);
            ImGui.InvisibleButton("Symbol", body);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                list.AddCircle(center, size * 0.5f, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.6f }), 48, MathF.Max(1f, context.Scale));
            }
            if (ImGui.IsItemActivated()) { startedOnSymbol = true; draggedOnSymbol = false; }
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) draggedOnSymbol = true;
            if (ImGui.IsItemActive() && draggedOnSymbol) PickUp(index, placed, true);
            else if (ImGui.IsItemDeactivated() && startedOnSymbol && Carrying == null)
            {
                startedOnSymbol = false;
                if (!draggedOnSymbol) PickUp(index, placed, false);
            }
            Handles[index] = (center, knob, mark, dot);
            if (removed) holder.RemoveSymbol(index);
            if (recolour)
            {
                colourTarget = index;
                var now = placed.Colour?.Vector ?? (area.Surface == SymbolSurface.Cover ? UiTheme.Foil : UiTheme.EditionInk);
                picked = new Vector3(now.X, now.Y, now.Z);
                // Opened from the surface's own window (outside this symbol's id), where ColourPopup looks for it.
                ImGui.PopID();
                ImGui.OpenPopup("Symbol colour");
                ImGui.PushID(index);
            }
        }
        finally { ImGui.PopID(); }
    }

    // A symbol's colours: the passport's (it then follows the foil or page ink), the inks, and any colour; a custom
    // colour previews on the symbol while dragged and is kept when the picker lets go.
    private void ColourPopup(PlacedSymbol placed, Area area, UiContext context)
    {
        if (!PaperColours.Begin("Symbol colour", context))
        {
            if (adjusting is { } open && open.Index == colourTarget) adjusting = null;
            colourTarget = -1;
            return;
        }
        try
        {
            ImGui.TextUnformatted("Symbol colour");
            var size = MathF.Round(18 * context.Scale);
            var gap = MathF.Round(6 * context.Scale);
            var start = ImGui.GetCursorScreenPos() + new Vector2(size / 2 + 3, size / 2 + 3);
            var passport = area.Surface == SymbolSurface.Cover ? UiTheme.Foil : UiTheme.EditionInk;
            var inks = BookShellDrawing.Palette.Where(entry => entry.Hex >= 0).ToArray();
            for (var index = 0; index <= inks.Length; index++)
            {
                var center = start + new Vector2(index * (size + gap), 0);
                Rgb? value = index == 0 ? null : Rgb.Hex(inks[index - 1].Hex);
                var name = index == 0 ? area.Surface == SymbolSurface.Cover ? "The passport's foil" : "The passport's page ink" : inks[index - 1].Name;
                if (PaperColours.Swatch("Symbol swatch " + index, center, size, value?.Vector ?? passport, placed.Colour == value, name, context)) Keep(value);
                ColourSwatches[index] = center;
            }
            ImGui.SetCursorScreenPos(start - new Vector2(size / 2 + 3, size / 2 + 3) + new Vector2(0, size + gap + 6));
            if (PaperColours.Picker(ref picked, context)) Keep(Rgb.Of(new Vector4(picked, 1)));
            else if (ImGui.IsItemActive()) adjusting = (colourTarget, placed with { Colour = Rgb.Of(new Vector4(picked, 1)) });
        }
        finally { PaperColours.End(); }

        void Keep(Rgb? colour)
        {
            adjusting = null;
            // Changing a symbol brings it to the top; the popup follows it there.
            if (holder.ReplaceSymbol(colourTarget, placed with { Colour = colour }) && holder.Identity.Appearance is { } appearance) colourTarget = appearance.Placed.Items.Length - 1;
        }
    }

    private void PickUp(int index, PlacedSymbol placed, bool held)
    {
        Carrying = new Carry(placed.Symbol, index, placed.Rotation, placed.Size, held);
        SheetOpen = false;
    }

    // A handle by the surface's edge is kept on it; past it, its button would make the page scroll.
    private static Vector2 Inside(Vector2 point, float radius, UiRect area)
    {
        var margin = new Vector2(radius * 1.6f + 1f);
        return Vector2.Clamp(point, area.Position + margin, Vector2.Max(area.Position + margin, area.Max - margin));
    }

    // The sheet of symbols, laid like a card over region; it slides up as it opens and away as it closes.
    internal void DrawSheet(UiRect region, UiContext context, float seconds, bool reducedMotion)
    {
        var target = Decorating && SheetOpen ? 1f : 0f;
        var step = UiMotion.Frame(seconds) / UiMotion.SheetSlide;
        sheetLift = reducedMotion ? target : sheetLift < target ? MathF.Min(target, sheetLift + step) : MathF.Max(target, sheetLift - step);
        if (sheetLift <= 0f) return;
        var eased = UiMotion.Ease(sheetLift);
        var padding = 16 * context.Scale;
        var width = region.Size.X;
        var cell = MathF.Floor((width - padding * 2) / PassportSymbol.Columns);
        var height = padding * 2 + Caption("SYMBOLS") + 6 * context.Scale + Caption(Hint) + 10 * context.Scale + cell * PassportSymbol.Rows + 10 * context.Scale + ImGui.GetTextLineHeight();
        height = MathF.Min(height, region.Size.Y);
        var card = new UiRect(region.Position + new Vector2(0, (1 - eased) * height * 0.6f), new Vector2(width, height));
        Sheet = card;
        ImGui.SetCursorScreenPos(card.Position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * eased);
        try
        {
            if (!ImGui.BeginChild("Symbol sheet", card.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)) return;
            var list = ImGui.GetWindowDrawList();
            PageSurface.Draw(list, card, context);
            var flow = new PageFlow(card.Position + new Vector2(padding), width - padding * 2, context, true);
            flow.Text("SYMBOLS", TextRole.Caption, true);
            flow.Gap(6);
            flow.Text(Hint, TextRole.Caption, true);
            flow.Gap(10);
            var grid = flow.Reserve(cell * PassportSymbol.Rows);
            var live = sheetLift >= 1f;
            foreach (var symbol in PassportSymbol.All)
            {
                var at = grid.Position + new Vector2(symbol.Cell % PassportSymbol.Columns, symbol.Cell / PassportSymbol.Columns) * cell;
                var center = at + new Vector2(cell / 2);
                ImGui.SetCursorScreenPos(at);
                ImGui.BeginDisabled(!live);
                var picked = ImGui.InvisibleButton("Pick " + symbol.Id, new Vector2(cell));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    list.AddRectFilled(at + new Vector2(2), at + new Vector2(cell - 2), ImGui.GetColorU32(UiTheme.PaperInset), 4 * context.Scale);
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(symbol.Name);
                }
                SymbolArt.Draw(list, assets, symbol, center, cell * 0.78f, 0f, UiTheme.EditionInk);
                SheetCells[symbol.Id] = center;
                if (picked) { Carrying = new Carry(symbol.Id, -1, 0f, PlacedSymbol.StartSize, false); SheetOpen = false; }
            }
            flow.Gap(10);
            ImGui.SetCursorScreenPos(card.Position + new Vector2(padding, padding + flow.Height));
            if (BookControl.Link("Close the sheet", context)) SheetOpen = false;
            // Clicks on the card's own paper stay on it and never turn the page beneath (submitted last, so the
            // symbols and link above keep theirs).
            ImGui.SetCursorScreenPos(card.Position);
            ImGui.InvisibleButton("Sheet paper", card.Size);
        }
        finally { ImGui.EndChild(); ImGui.PopStyleVar(2); }

        float Caption(string text) => UiTypography.Measure(text, TextRole.Caption, context, width - padding * 2, true).Y;
    }

    private const string Hint = "PICK ONE, THEN CLICK WHERE IT GOES. ON THE COVER IT IS PRESSED IN FOIL; ON THE PAGES, PRINTED IN PAGE INK BENEATH THE TEXT.";

    // After the surfaces are drawn: moves, turns, and puts down a carried symbol.
    internal void Update(UiRect window, UiContext context)
    {
        TookClick = false;
        if (Carrying is not { } carry) return;
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        if (io.MouseWheel != 0f && window.Contains(mouse)) Carrying = carry = carry with { Rotation = Features.Stamps.StampPlacement.Normalize(carry.Rotation - io.MouseWheel * WheelStep) };
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { Cancel(); TookClick = true; return; }
        var under = areas.Where(area => area.Bounds.Contains(mouse)).Select(area => (Area?)area).FirstOrDefault();
        var released = carry.Held ? !ImGui.IsMouseDown(ImGuiMouseButton.Left) : ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        // A full surface takes no more (one moved within it still fits).
        var full = under is { } over && !HasRoomOn(over.Surface) && (carry.From < 0 || holder.Identity.Appearance?.Placed.Items[carry.From].Surface != over.Surface);
        if (released)
        {
            TookClick = !carry.Held;
            if (under is { } area && !full) { Put(carry, area, mouse); return; }
            // A drag let go off every surface returns the symbol; a click there keeps it in hand.
            if (carry.Held) { Cancel(); return; }
        }

        // The symbol in hand, in a window of its own above the surfaces, with a soft shadow beneath it.
        var size = carry.Size * (under?.Bounds.Size.X ?? lastWidth);
        var colour = under?.Surface == SymbolSurface.Cover ? UiTheme.Foil : UiTheme.EditionInk;
        ImGui.SetCursorScreenPos(window.Position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        try
        {
            if (ImGui.BeginChild("Carried symbol", window.Size, false, ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings))
            {
                var list = ImGui.GetWindowDrawList();
                if (PassportSymbol.Find(carry.Symbol) is { } symbol)
                {
                    SymbolArt.Draw(list, assets, symbol, mouse + new Vector2(3, 5) * context.Scale, size, carry.Rotation, new Vector4(0.1f, 0.07f, 0.03f, 0.22f));
                    SymbolArt.Draw(list, assets, symbol, mouse, size, carry.Rotation, colour with { W = 0.92f });
                }
            }
        }
        finally { ImGui.EndChild(); ImGui.PopStyleVar(); }
        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        if (full) ImGui.SetTooltip($"This holds {SymbolLayer.MaximumPerSurface} symbols already");
    }

    private void Put(Carry carry, Area area, Vector2 mouse)
    {
        var at = SymbolArt.At(mouse, carry.Size, area.Bounds);
        var placed = new PlacedSymbol(carry.Symbol, area.Surface, at.X, at.Y, carry.Rotation, carry.Size);
        if (carry.From >= 0) holder.ReplaceSymbol(carry.From, placed);
        // A new one goes on top, and the sheet comes back for the next.
        else { holder.AddSymbol(placed); SheetOpen = true; }
        Carrying = null;
        startedOnSymbol = false;
    }

    // Whether a surface can take another symbol (the sheet says so when one is full).
    internal bool HasRoomOn(string surface) => holder.Identity.Appearance?.Placed.HasRoomOn(surface) ?? true;
}
