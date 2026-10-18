using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal enum BookSection { Passport, Discover, Echoes, Studio }
internal readonly record struct BookShellIntent(bool Close, bool Settings, Vector2 Drag);

// The book itself is the control surface: press-and-drag anywhere moves it, a click on its right side
// opens it or turns forward, and a click on its left side turns back (from the first page, to the cover).
// Real items (tabs, printed controls, page content) keep their own clicks and keyboard navigation.
internal sealed class BookShell(IBookAssets assets, PassportPreview profile, IReadOnlyList<PassportPage>? pages = null)
{
    internal const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
    private static readonly string[] Labels = ["Passport", "Discover", "Echoes", "Studio"];
    private readonly PassportDocumentView passport = new(assets, profile);
    private readonly IReadOnlyList<PassportPage> documentPages = ValidatePages(pages ?? PassportDocumentView.SamplePages);
    private readonly DiscoverView discover = new(DiscoveryPreview.Samples);
    private bool pressing;
    private bool dragging;
    internal BookSection Section { get; private set; }
    internal int VisiblePage { get; private set; }
    internal bool IsBookOpen { get; private set; }
    private bool focusSection;
    internal int PageCount => documentPages.Count;
    internal float PreferredWidth { get; private set; } = 1040;
    internal DiscoveryPreview? SelectedDiscovery => discover.SelectedDiscovery;

    internal BookShellIntent Draw(UiRect bounds, UiContext context)
    {
        if (!IsBookOpen) return DrawClosed(bounds, context);
        var layout = BookShellLayout.Create(bounds, context, Section == BookSection.Passport ? VisiblePage : 0);
        if (layout.IsSpread) VisiblePage -= VisiblePage % 2;
        var step = layout.IsSpread ? 2 : 1;

        // Tabs first: the book surface drawn next covers their feet, so they read as tucked behind the pages.
        var next = Section;
        for (var index = 0; index < Labels.Length; index++)
        {
            if ((ImGui.IsWindowAppearing() || focusSection) && (int)Section == index) ImGui.SetKeyboardFocusHere();
            if (BookControl.Draw(Labels[index], Labels[index], layout.Bookmark(index), context, BookControlStyle.Tab, (int)Section == index)) next = (BookSection)index;
        }
        focusSection = false;
        BookSurface.Draw(layout.Book, layout.IsSpread ? BookFace.Spread : Section == BookSection.Passport && VisiblePage % 2 == 1 ? BookFace.Right : BookFace.Left, assets, context);

        // Page text scales with the paper, never below the host text size.
        var pageWidth = layout.Book.Size.X * (layout.IsSpread ? 0.5f : 1f);
        var page = context with { FontSize = MathF.Max(context.FontSize, MathF.Round(pageWidth * 16f / 420f)) };
        var explore = false;
        if (Section == BookSection.Passport)
        {
            DocumentPage(VisiblePage, layout.LeftPage);
            if (layout.IsSpread && VisiblePage + 1 < documentPages.Count) DocumentPage(VisiblePage + 1, layout.RightPage);
            Folio(VisiblePage, false);
            if (layout.IsSpread && VisiblePage + 1 < documentPages.Count) Folio(VisiblePage + 1, true);
        }
        else
        {
            ImGui.PushID((int)Section);
            try
            {
                if (layout.IsSpread) Page("Section heading", layout.LeftPage, () => Introduction(page));
                Page("Section content", layout.IsSpread ? layout.RightPage : layout.LeftPage, () =>
                {
                    if (Section == BookSection.Discover) discover.Draw(page, onPaper: true);
                    else Introduction(page);
                });
            }
            finally { ImGui.PopID(); }
        }
        if (explore) next = BookSection.Discover;

        var settings = BookControl.Draw("Settings", "Settings", layout.Settings(context), page, BookControlStyle.Ink, hint: "Wanderveil settings");
        if (BookControl.Draw("Smaller", "-", layout.Tool(0, context), page, BookControlStyle.Ink, hint: "Smaller passport")) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        if (BookControl.Draw("Larger", "+", layout.Tool(1, context), page, BookControlStyle.Ink, hint: "Larger passport")) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        var close = BookControl.Draw("Close", "×", layout.Tool(2, context), page, BookControlStyle.Ink, hint: "Close passport (Escape)");
        Section = next;

        var turns = Section == BookSection.Passport;
        var drag = Gesture(layout.Book, turns, out var click);
        if (turns)
        {
            if (click is { } side) Turn(side, step);
            if (Keys(ImGuiKey.PageDown)) Turn(1, step);
            if (Keys(ImGuiKey.PageUp)) Turn(-1, step);
            if (Keys(ImGuiKey.Home)) CloseBook();
        }
        return new BookShellIntent(close, settings, drag);

        void DocumentPage(int index, UiRect region)
        {
            var entry = documentPages[index];
            Page(entry.Id, region, () =>
            {
                passport.Compose(entry, page);
                if (entry.Kind == PassportPageKind.Information)
                {
                    ImGui.Dummy(new Vector2(0, 10 * context.Scale));
                    explore |= BookControl.Link("Explore discoveries »", page);
                }
            });
        }

        void Folio(int index, bool right)
        {
            var label = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var measured = UiTypography.Measure(label, TextRole.Caption, page, 80, false);
            UiTypography.Draw(ImGui.GetWindowDrawList(), layout.Folio(right) - measured / 2, label, TextRole.Caption, page, 80, UiTheme.MutedInk, false);
        }

        void Page(string id, UiRect region, Action content)
        {
            ImGui.SetCursorScreenPos(region.Position);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 5 * context.Scale);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, UiTheme.Gold);
            try
            {
                var visible = ImGui.BeginChild(id, region.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings);
                try { if (visible) content(); }
                finally { ImGui.EndChild(); }
            }
            finally { ImGui.PopStyleColor(2); ImGui.PopStyleVar(2); }
        }
    }

    internal void BeginSession()
    {
        IsBookOpen = false;
        Section = BookSection.Passport;
        VisiblePage = 0;
        pressing = dragging = false;
    }

    internal Vector2 FitSize(Vector2 room, UiContext context) => BookShellLayout.Fit(PreferredWidth, room, context, !IsBookOpen);

    private void Turn(int direction, int step)
    {
        if (direction < 0)
        {
            if (VisiblePage == 0) CloseBook();
            else VisiblePage = Math.Max(0, VisiblePage - step);
        }
        else if (VisiblePage + step < PageCount) VisiblePage += step;
    }

    private void CloseBook()
    {
        IsBookOpen = false;
        pressing = dragging = false;
    }

    private void OpenBook()
    {
        IsBookOpen = true;
        VisiblePage = 0;
        focusSection = true;
        pressing = dragging = false;
    }

    private BookShellIntent DrawClosed(UiRect bounds, UiContext context)
    {
        passport.DrawCover(bounds, context);
        // Settings and window controls are stamped into the cover's lower band, beneath its arc.
        var (board, unit) = BookSurface.CoverBoard(bounds);
        var stamp = context with { FontSize = MathF.Max(context.FontSize, MathF.Round(15 * unit)) };
        var height = MathF.Max(BookShellLayout.Small(context), 22 * unit);
        var top = board.Y + 531 * unit + (22 * unit - height) / 2;
        var settingsWidth = BookShellLayout.SettingsWidth(context);
        var settings = BookControl.Draw("Settings", "Settings", new UiRect(new Vector2(board.X + 58 * unit, top), new Vector2(settingsWidth, height)), stamp, BookControlStyle.Foil, hint: "Wanderveil settings");
        var small = BookShellLayout.Small(context);
        var x = board.X + 362 * unit - 3 * small - 2 * BookShellLayout.Gap(context);
        if (BookControl.Draw("Smaller", "-", new UiRect(new Vector2(x, top), new Vector2(small, height)), stamp, BookControlStyle.Foil, hint: "Smaller passport")) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        x += small + BookShellLayout.Gap(context);
        if (BookControl.Draw("Larger", "+", new UiRect(new Vector2(x, top), new Vector2(small, height)), stamp, BookControlStyle.Foil, hint: "Larger passport")) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        x += small + BookShellLayout.Gap(context);
        var close = BookControl.Draw("Close", "×", new UiRect(new Vector2(x, top), new Vector2(small, height)), stamp, BookControlStyle.Foil, hint: "Close passport (Escape)");

        var cover = new UiRect(board, new Vector2(420, 560) * unit);
        var drag = Gesture(cover, true, out var click, opensOnly: true);
        if (click > 0) OpenBook();
        else if (!ImGui.IsAnyItemFocused() && (Keys(ImGuiKey.Space) || Keys(ImGuiKey.Enter) || Keys(ImGuiKey.KeypadEnter) || Keys(ImGuiKey.PageDown))) OpenBook();
        return new BookShellIntent(close, settings, drag);
    }

    // Press anywhere on the book that is not an item. Moving past the drag threshold moves the book;
    // releasing without moving is a click on the left (-1) or right (+1) half.
    private Vector2 Gesture(UiRect surface, bool clickable, out int? click, bool opensOnly = false)
    {
        click = null;
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var free = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && !ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive() && surface.Contains(mouse);
        if (free && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { pressing = true; dragging = false; }
        var side = mouse.X >= surface.Position.X + surface.Size.X / 2 ? 1 : -1;
        if (!pressing)
        {
            if (free && clickable && (!opensOnly || side > 0)) Hint(surface, side);
            return Vector2.Zero;
        }
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left)) dragging = true;
        if (dragging) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            pressing = false;
            if (!dragging && clickable && surface.Contains(mouse)) click = side;
            dragging = false;
            return Vector2.Zero;
        }
        return dragging ? io.MouseDelta : Vector2.Zero;
    }

    // A soft shadow along the outer edge shows which way a click will go.
    private static void Hint(UiRect surface, int side)
    {
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var width = surface.Size.X * 0.06f;
        var top = surface.Position.Y + surface.Size.Y * 0.08f;
        var bottom = surface.Position.Y + surface.Size.Y * 0.90f;
        var dark = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.10f));
        var clear = ImGui.GetColorU32(Vector4.Zero);
        var list = ImGui.GetWindowDrawList();
        if (side > 0) list.AddRectFilledMultiColor(new Vector2(surface.Max.X - width, top), new Vector2(surface.Max.X - surface.Size.X * 0.025f, bottom), clear, dark, dark, clear);
        else list.AddRectFilledMultiColor(new Vector2(surface.Position.X + surface.Size.X * 0.025f, top), new Vector2(surface.Position.X + width, bottom), dark, clear, clear, dark);
    }

    private static bool Keys(ImGuiKey key) => ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.IsAnyItemActive() && ImGui.IsKeyPressed(key, false);

    private static IReadOnlyList<PassportPage> ValidatePages(IReadOnlyList<PassportPage> source)
    {
        var copy = source.ToArray();
        if (copy.Length == 0 || copy.Any(page => page == null || string.IsNullOrWhiteSpace(page.Id)) || copy.Select(page => page.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("A passport needs pages with unique stable identifiers.", nameof(source));
        return Array.AsReadOnly(copy);
    }

    private void Introduction(UiContext context)
    {
        UiTypography.Text("WANDERVEIL / " + Labels[(int)Section].ToUpperInvariant(), TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 16 * context.Scale));
        UiTypography.Text(Section switch { BookSection.Discover => "Every place has a story.", BookSection.Echoes => "Beyond the next horizon", _ => "Leave a discovery" }, TextRole.Title, context, UiTheme.Ink);
        ImGui.Dummy(new Vector2(0, 12 * context.Scale));
        UiTypography.Text(Section == BookSection.Discover ? "A journal of imagined places. Curated demo discoveries; no location is read." : "Coming later. Creation, publishing, and creator-made adventures are unavailable in this preview.", TextRole.Body, context, UiTheme.Ink);
    }

    internal static void PrepareWindow(Vector2 size)
    {
        // Explicit content extents prevent the previous larger frame from making the host scroll on zoom-out.
        ImGui.SetNextWindowContentSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
    }
    internal static void PopWindowStyle() => ImGui.PopStyleVar(3);
}
