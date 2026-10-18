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

internal sealed class BookShell(IBookAssets assets, PassportPreview profile, IReadOnlyList<PassportPage>? pages = null)
{
    internal const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
    private static readonly string[] Labels = ["Passport", "Discover", "Echoes", "Studio"];
    private readonly PassportDocumentView passport = new(assets, profile);
    private readonly IReadOnlyList<PassportPage> documentPages = ValidatePages(pages ?? PassportDocumentView.SamplePages);
    private readonly DiscoverView discover = new(DiscoveryPreview.Samples);
    internal BookSection Section { get; private set; }
    internal int VisiblePage { get; private set; }
    internal bool IsBookOpen { get; private set; }
    private bool focusCover = true;
    private bool focusSection;
    internal int PageCount => documentPages.Count;
    internal float PreferredWidth { get; private set; } = 1040;
    internal DiscoveryPreview? SelectedDiscovery => discover.SelectedDiscovery;

    internal BookShellIntent Draw(UiRect bounds, UiContext context)
    {
        if (!IsBookOpen) return DrawClosed(bounds, context);
        var layout = BookShellLayout.Create(bounds, context, Section == BookSection.Passport ? VisiblePage : 0);
        if (layout.IsSpread) VisiblePage -= VisiblePage % 2;
        BookSurface.Draw(layout.Book, layout.IsSpread ? BookFace.Spread : Section == BookSection.Passport && VisiblePage % 2 == 1 ? BookFace.Right : BookFace.Left, assets, context);
        var drag = Grip(layout, !layout.IsSpread && Section == BookSection.Passport && VisiblePage % 2 == 1);
        var next = Section;
        for (var index = 0; index < Labels.Length; index++)
        {
            if ((ImGui.IsWindowAppearing() || focusSection) && (int)Section == index) ImGui.SetKeyboardFocusHere();
            if (BookControl.Draw(Labels[index], Labels[index], layout.Bookmark(index), context, (int)Section == index, true, assets: assets)) next = (BookSection)index;
        }
        focusSection = false;

        var explore = false;
        if (Section == BookSection.Passport)
        {
            DocumentPage(VisiblePage, layout.LeftPage);
            if (layout.IsSpread && VisiblePage + 1 < documentPages.Count) DocumentPage(VisiblePage + 1, layout.RightPage);
        }
        else
        {
            ImGui.PushID((int)Section);
            try
            {
                if (layout.IsSpread) Page("Section heading", layout.LeftPage, () => Introduction(context));
                Page("Section content", layout.IsSpread ? layout.RightPage : layout.LeftPage, () =>
                {
                    if (Section == BookSection.Discover) discover.Draw(context, onPaper: true);
                    else Introduction(context);
                });
            }
            finally { ImGui.PopID(); }
        }
        if (explore) next = BookSection.Discover;

        var footer = layout.Footer;
        var small = MathF.Max(28 * context.Scale, context.FontSize + 10 * context.Scale);
        var gap = 5 * context.Scale;
        var controlsWidth = small * 3 + gap * 2;
        var controlsX = footer.Max.X - controlsWidth;
        var controlsY = footer.Max.Y - layout.ControlHeight;
        var firstRowEnd = layout.StackControls ? footer.Max.X : controlsX - gap;
        var settingsWidth = MathF.Min(80 * MathF.Max(context.Scale, context.FontSize / 18), MathF.Max(1, (firstRowEnd - footer.Position.X) * 0.45f));
        var settings = BookControl.Draw("Settings", "Settings", new UiRect(footer.Position, new Vector2(settingsWidth, layout.ControlHeight)), context, assets: assets);
        if (Section == BookSection.Passport)
        {
            var pager = new UiRect(footer.Position + new Vector2(settingsWidth + gap, 0), new Vector2(MathF.Max(1, firstRowEnd - footer.Position.X - settingsWidth - gap), layout.ControlHeight));
            var arrowWidth = MathF.Min(small, pager.Size.X * 0.20f);
            var step = layout.IsSpread ? 2 : 1;
            ImGui.BeginDisabled(VisiblePage == 0);
            if (BookControl.Draw("Previous page", "<", pager with { Size = new Vector2(arrowWidth, pager.Size.Y) }, context, hint: "Previous page", assets: assets)) VisiblePage = Math.Max(0, VisiblePage - step);
            ImGui.EndDisabled();
            var middleWidth = MathF.Min(96 * MathF.Max(context.Scale, context.FontSize / 18), MathF.Max(1, pager.Size.X - 2 * (arrowWidth + gap)));
            var middle = new UiRect(pager.Position + new Vector2((pager.Size.X - middleWidth) / 2, 0), new Vector2(middleWidth, pager.Size.Y));
            if (BookControl.Draw("Return to cover", "Cover", middle, context, hint: $"Close the book / Page {VisiblePage + 1} of {PageCount}", assets: assets)) { IsBookOpen = false; focusCover = true; }
            ImGui.BeginDisabled(VisiblePage + step >= PageCount);
            if (BookControl.Draw("Next page", ">", new UiRect(new Vector2(pager.Max.X - arrowWidth, pager.Position.Y), new Vector2(arrowWidth, pager.Size.Y)), context, hint: "Next page", assets: assets)) VisiblePage = Math.Min(PageCount - 1, VisiblePage + step);
            ImGui.EndDisabled();
        }
        if (BookControl.Draw("Smaller", "-", new UiRect(new Vector2(controlsX, controlsY), new Vector2(small, layout.ControlHeight)), context, hint: "Smaller passport", assets: assets)) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        if (BookControl.Draw("Larger", "+", new UiRect(new Vector2(controlsX + small + gap, controlsY), new Vector2(small, layout.ControlHeight)), context, hint: "Larger passport", assets: assets)) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        var close = BookControl.Draw("Close", "x", new UiRect(new Vector2(controlsX + 2 * (small + gap), controlsY), new Vector2(small, layout.ControlHeight)), context, hint: "Close passport (Escape)", assets: assets);
        Section = next;
        return new BookShellIntent(close, settings, drag);

        void DocumentPage(int index, UiRect region)
        {
            var page = documentPages[index];
            Page(page.Id, region, () =>
            {
                passport.Compose(page, context);
                if (page.Kind == PassportPageKind.Information)
                {
                    ImGui.Dummy(new Vector2(0, 12 * context.Scale));
                    explore |= UiButton.Draw("Explore discoveries", context);
                }
            });
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
        focusCover = true;
    }

    internal Vector2 FitSize(Vector2 room, UiContext context) => BookShellLayout.Fit(PreferredWidth, room, context, !IsBookOpen);

    private BookShellIntent DrawClosed(UiRect bounds, UiContext context)
    {
        passport.DrawCover(bounds, context);
        ImGui.SetCursorScreenPos(bounds.Position + new Vector2(bounds.Size.X * 0.07f, bounds.Size.Y * 0.2f));
        ImGui.InvisibleButton("Move closed passport", new Vector2(bounds.Size.X * 0.07f, bounds.Size.Y * 0.55f));
        var drag = ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) ? ImGui.GetIO().MouseDelta : Vector2.Zero;
        if (focusCover || ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        focusCover = false;
        if (BookControl.Draw("Open passport", "Open", BookShellLayout.OpenButton(bounds, context), context, hint: "Open passport", assets: assets)) { IsBookOpen = true; VisiblePage = 0; focusSection = true; }
        var height = BookShellLayout.GetControlHeight(context);
        var footer = new UiRect(bounds.Position + new Vector2(bounds.Size.X * 0.1f, bounds.Size.Y * 0.88f), new Vector2(bounds.Size.X * 0.8f, height));
        var small = MathF.Max(28 * context.Scale, context.FontSize + 10 * context.Scale);
        var gap = 5 * context.Scale;
        var x = footer.Max.X - 3 * small - 2 * gap;
        var settingsWidth = MathF.Min(80 * MathF.Max(context.Scale, context.FontSize / 18), MathF.Max(1, x - footer.Position.X - gap));
        var settings = BookControl.Draw("Settings", "Settings", footer with { Size = new Vector2(settingsWidth, height) }, context, assets: assets);
        if (BookControl.Draw("Smaller", "-", new UiRect(new Vector2(x, footer.Position.Y), new Vector2(small, height)), context, assets: assets)) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        if (BookControl.Draw("Larger", "+", new UiRect(new Vector2(x + small + gap, footer.Position.Y), new Vector2(small, height)), context, assets: assets)) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        var close = BookControl.Draw("Close", "x", new UiRect(new Vector2(x + 2 * (small + gap), footer.Position.Y), new Vector2(small, height)), context, assets: assets);
        return new BookShellIntent(close, settings, drag);
    }

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

    private static Vector2 Grip(BookShellLayout layout, bool rightEdge)
    {
        ImGui.SetCursorScreenPos(layout.Book.Position + new Vector2(layout.Book.Size.X * (layout.IsSpread ? 0.045f : rightEdge ? 0.90f : 0.09f), layout.Book.Size.Y * 0.16f));
        ImGui.InvisibleButton("Move passport", new Vector2(layout.Book.Size.X * 0.04f, layout.Book.Size.Y * 0.65f));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag the leather edge to move the passport");
        return ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) ? ImGui.GetIO().MouseDelta : Vector2.Zero;
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
