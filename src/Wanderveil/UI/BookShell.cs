using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal enum BookSection { Passport, Discover, Echoes, Studio }
internal readonly record struct BookShellIntent(bool Close, bool Settings, Vector2 Drag);

internal sealed class BookShell(IBookAssets assets, PassportPreview profile)
{
    internal const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
    private static readonly string[] Labels = ["Passport", "Discover", "Echoes", "Studio"];
    private readonly PassportView passport = new(profile, assets);
    private readonly DiscoverView discover = new(DiscoveryPreview.Samples);
    internal BookSection Section { get; private set; }
    internal int VisiblePage { get; private set; }
    internal float PreferredWidth { get; private set; } = 1040;
    internal DiscoveryPreview? SelectedDiscovery => discover.SelectedDiscovery;

    internal BookShellIntent Draw(UiRect bounds, UiContext context)
    {
        var layout = BookShellLayout.Create(bounds, context, Section == BookSection.Passport ? VisiblePage : 0);
        BookSurface.Draw(layout.Book, layout.IsSpread ? BookFace.Spread : Section == BookSection.Passport && VisiblePage == 1 ? BookFace.Right : BookFace.Left, assets, context);
        var drag = Grip(layout, !layout.IsSpread && Section == BookSection.Passport && VisiblePage == 1);
        var next = Section;
        for (var index = 0; index < Labels.Length; index++)
        {
            if (ImGui.IsWindowAppearing() && (int)Section == index) ImGui.SetKeyboardFocusHere();
            if (BookControl.Draw(Labels[index], Labels[index], layout.Bookmark(index), context, (int)Section == index, true, assets: assets)) next = (BookSection)index;
        }

        var explore = false;
        if (Section == BookSection.Passport)
        {
            Page(layout.IsSpread || VisiblePage == 0 ? "Identity" : "Notes", layout.LeftPage, () => explore |= passport.DrawPageContent(layout.IsSpread ? 0 : VisiblePage, context));
            if (layout.IsSpread) Page("Notes", layout.RightPage, () => explore |= passport.DrawPageContent(1, context));
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
        var settingsWidth = MathF.Min(80 * MathF.Max(context.Scale, context.FontSize / 18), MathF.Max(1, (firstRowEnd - footer.Position.X) * (layout.IsSpread ? 1 : 0.6f)));
        var settings = BookControl.Draw("Settings", "Settings", new UiRect(footer.Position, new Vector2(settingsWidth, layout.ControlHeight)), context, assets: assets);
        if (!layout.IsSpread && Section == BookSection.Passport)
        {
            var pager = new UiRect(footer.Position + new Vector2(settingsWidth + gap, 0), new Vector2(MathF.Max(1, firstRowEnd - footer.Position.X - settingsWidth - gap), layout.ControlHeight));
            if (BookControl.Draw("Page", VisiblePage == 0 ? "2 >" : "< 1", pager, context, hint: VisiblePage == 0 ? "Read notes" : "Read identity", assets: assets)) VisiblePage = 1 - VisiblePage;
        }
        if (BookControl.Draw("Smaller", "-", new UiRect(new Vector2(controlsX, controlsY), new Vector2(small, layout.ControlHeight)), context, hint: "Smaller passport", assets: assets)) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        if (BookControl.Draw("Larger", "+", new UiRect(new Vector2(controlsX + small + gap, controlsY), new Vector2(small, layout.ControlHeight)), context, hint: "Larger passport", assets: assets)) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        var close = BookControl.Draw("Close", "x", new UiRect(new Vector2(controlsX + 2 * (small + gap), controlsY), new Vector2(small, layout.ControlHeight)), context, hint: "Close passport (Escape)", assets: assets);
        Section = next;
        return new BookShellIntent(close, settings, drag);

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
