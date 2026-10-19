using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Echoes;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Features.Studio;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Tabbed sections, in book order, then the Settings pages reached from the printed Settings mark.
internal enum BookSection { Passport, Discover, Echoes, Studio, Settings }
internal readonly record struct BookShellIntent(bool Close, Vector2 Drag);

// The book itself is the control surface: press-and-drag anywhere moves it, a click on its right side
// opens it or turns forward, and a click on its left side turns back (from the first page, to the cover).
// Real items (tabs, printed controls, page content) keep their own clicks and keyboard navigation.
// BookShellMotion sequences arrival, the cover swing, and dismissal; the book takes no input while it moves.
// Moving between sections riffles several pages in book direction; Settings are pages of the book too.
// identity is the holder's stored identity (profile only seeds one when none is given); portrait, character,
// and identityStorage are the host's picture, logged-in character, and identity file, each optional.
internal sealed class BookShell(IBookAssets assets, PassportIdentity profile, IReadOnlyList<PassportPage>? pages = null, IBookShellSettings? settings = null, BookShellContent? content = null, StampCollection? stamps = null, ISavedState? storage = null,
    PassportProfile? identity = null, IPassportPortrait? portrait = null, ICharacterSource? character = null, ISavedState? identityStorage = null)
{
    internal const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
    private static readonly string[] Labels = ["Passport", "Discover", "Echoes", "Studio", "Settings"];
    private const int TabCount = 4;
    // The passport's stamps: Discover adds to them and the collection pages print them.
    private readonly StampCollection collection = stamps ??= new StampCollection();
    private readonly PassportProfile holder = identity ??= new PassportProfile(profile);
    private readonly PassportDocumentView passport = new(assets, identity!, portrait);
    private readonly IdentityEditor editor = new(identity!, portrait, character);
    // The fixed pages (nation, identity); unlimited blank stamp pages follow them.
    private readonly IReadOnlyList<PassportPage> fixedPages = ValidatePages(pages ?? PassportDocumentView.SamplePages);
    private readonly BookShellStamps desk = new(stamps!, assets);
    private readonly DiscoverView discover = new((content ?? BookShellContent.Sample).Discoveries, stamps, assets, storage);
    private readonly StudioView studio = new((content ?? BookShellContent.Sample).Templates);
    private readonly EchoesView echoes = new((content ?? BookShellContent.Sample).Dossier);
    private readonly BookShellMotion motion = Arrived();
    private readonly BookShellTurn turn = new();
    private readonly BookShellRiffle riffle = new();
    private bool pressing;
    private bool dragging;
    private BookSection ribbonSection;
    private float ribbonExtension;
    private bool saveFailed;
    // Where the book drew itself last frame (board, tabs, ribbon). The host window is a transparent
    // rectangle; only these parts take the mouse, so clicks on the margins around the book reach the game.
    private readonly List<UiRect> solid = [];
    // A press, drag, or edit that started on the book keeps the mouse until it ends, wherever it goes.
    private bool holding;
    // Per-page shrink-to-fit factor, converged from each page's measured content height.
    private readonly Dictionary<string, float> fits = new(StringComparer.Ordinal);
    internal BookSection Section { get; private set; }
    // The reading position in the Passport section; kept while visiting other sections.
    internal int VisiblePage { get; private set; }
    // Where Settings' Back link returns to.
    internal BookSection ReturnSection { get; private set; }
    // The logical state: true from the moment opening is requested, even while the cover still swings.
    internal bool IsBookOpen => motion.WantsOpen;
    internal BookPhase Phase => motion.Phase;
    // Eased 0 (below the screen) to 1 (at rest); the host window offsets itself by it.
    internal float Lift => UiMotion.Ease(motion.Lift);
    internal bool ReducedMotion { get => motion.ReducedMotion; set => motion.ReducedMotion = value; }
    private bool focusSection;
    // The passport has 100 pages for now: the fixed pages, then blank stamp pages up to the hundredth.
    internal const int TotalPages = 100;
    internal int PageCount => Math.Max(fixedPages.Count, TotalPages);
    internal int FixedPageCount => fixedPages.Count;
    internal BookShellStamps Desk => desk;
    internal PassportProfile Identity => holder;
    // Whether the identity pages are being written on (the footer's Edit / Done).
    internal bool EditingIdentity { get; private set; }
    // In game the plugin reads Escape from the game's own key state, so it can keep the game from also opening
    // its system menu, and hands it over with RequestEscape; then the book must not read it from ImGui as well.
    internal bool ReadsEscape { get; init; } = true;
    // Whether Escape would do something here: put back a carried stamp, close stamp info, stop editing, or close the book.
    // Read from the game's update thread, so only a plain flag.
    internal bool WantsEscape => wantsEscape;
    private volatile bool wantsEscape;
    private volatile bool escapeRequested;

    internal void RequestEscape() => escapeRequested = true;
    // The page number being typed over: null when none, otherwise whether it is the right page's.
    private bool? pageEntry;
    private string pageText = "";
    private bool focusPageEntry;
    // Whether a page number is being typed (the book's own keys then wait).
    internal bool EnteringPage => pageEntry != null;
    internal float PreferredWidth { get; private set; } = 1040;
    internal DiscoveryPreview? SelectedDiscovery => discover.SelectedDiscovery;
    internal StudioView Studio => studio;
    internal StampCollection Stamps => collection;
    // Largest scroll range among the pages drawn last frame; zero when every page fits its paper.
    internal float PageOverflow { get; private set; }
    // Largest sideways overflow among the pages drawn last frame; text must always wrap within its paper.
    internal float PageOverflowX { get; private set; }
    internal bool IsTurning => turn.Active;
    internal bool IsRiffling => riffle.Active;

    // Advances motion; the host calls it before sizing the window so size and drawing agree each frame.
    internal void Advance(float seconds)
    {
        motion.Update(seconds);
        turn.Update(seconds, ReducedMotion);
        riffle.Update(seconds, ReducedMotion);
    }

    // The host's window flags for this frame: the book's own, plus pass-through when the mouse is off the book.
    internal ImGuiWindowFlags HostFlags(Vector2 mouse) => WindowFlags | (OwnsMouse(mouse) ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMouseInputs);

    internal bool OwnsMouse(Vector2 mouse) => holding || solid.Any(part => part.Contains(mouse));

    internal BookShellIntent Draw(UiRect bounds, UiContext context)
    {
        solid.Clear();
        try { return DrawBook(bounds, context); }
        finally
        {
            holding = pressing || dragging || desk.Busy || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsAnyItemActive();
            // A field being written in keeps Escape for itself; otherwise the book wants it while it is shown and
            // either holds a stamp or info, or has focus.
            wantsEscape = motion.Phase != BookPhase.Hidden && !ImGui.IsAnyItemActive()
                && (desk.Busy || desk.Info != null || EditingIdentity || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows));
        }
    }

    private BookShellIntent DrawBook(UiRect bounds, UiContext context)
    {
        if (motion.Phase == BookPhase.Hidden) return new BookShellIntent(true, Vector2.Zero);
        // Escape plays the dismissal, except while a field is being written in: there it only leaves the field.
        // A carried stamp, an open stamp info page, or editing takes Escape first.
        var escape = escapeRequested;
        escapeRequested = false;
        if (ReadsEscape && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.IsAnyItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape, false)) escape = true;
        if (escape && !ImGui.IsAnyItemActive())
        {
            if (desk.Busy) desk.Cancel();
            else if (desk.Info != null) desk.CloseInfo();
            else if (EditingIdentity) StopEditing();
            else motion.Dismiss();
        }
        if (motion.Cover <= 0f) return DrawClosed(bounds, context);
        if (motion.Cover < 1f || !motion.WantsOpen) return DrawTurning(bounds, context);
        var turning = Section == BookSection.Passport && turn.Active && !riffle.Active;
        // On a single page the page beneath a turning leaf decides which half of the plate shows.
        var layout = BookShellLayout.Create(bounds, context, Section == BookSection.Passport ? turning && turn.Step == 1 ? turn.Base + 1 : VisiblePage : 0);
        if (turn.Active && turn.Step != (layout.IsSpread ? 2 : 1)) { turn.Finish(); VisiblePage = turn.Target; turning = false; }
        PageOverflow = PageOverflowX = 0;
        if (layout.IsSpread) VisiblePage -= VisiblePage % 2;
        var step = layout.IsSpread ? 2 : 1;
        // Editing lasts while the identity pages rest open; turning or leaving them ends it.
        var identityShown = Section == BookSection.Passport && !turning && !riffle.Active && (IsIdentity(VisiblePage) || layout.IsSpread && IsIdentity(VisiblePage + 1));
        if (EditingIdentity && !identityShown) StopEditing();

        // Pages, tabs, and controls fade in once the cover has landed.
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, MathF.Max(0.01f, motion.Reveal));
        // Tabs first: the book surface drawn next covers their feet, so they read as tucked behind the pages.
        BookSection? chosen = null;
        for (var index = 0; index < TabCount; index++)
        {
            if ((ImGui.IsWindowAppearing() || focusSection) && (int)Section == index) ImGui.SetKeyboardFocusHere();
            if (BookControl.Draw(Labels[index], Labels[index], layout.Bookmark(index), context, BookControlStyle.Tab, (int)Section == index)) chosen = (BookSection)index;
        }
        focusSection = false;
        solid.Add(BookSurface.Board(layout.Book, layout.IsSpread ? BookFace.Spread : Section == BookSection.Passport && VisiblePage % 2 == 1 ? BookFace.Right : BookFace.Left));
        for (var index = 0; index < TabCount; index++) solid.Add(layout.Bookmark(index).Union(BookControl.RibbonBounds(layout.Bookmark(index), context)));
        var surfacePage = turning && !layout.IsSpread ? turn.Base + 1 : VisiblePage;
        BookSurface.Draw(layout.Book, layout.IsSpread ? BookFace.Spread : Section == BookSection.Passport && surfacePage % 2 == 1 ? BookFace.Right : BookFace.Left, assets, context);
        Ribbon(layout, context);

        // Page contents scale with the paper like printed matter: a smaller book shows smaller text,
        // spacing, and artwork rather than scrolling. Enlarged host text still enlarges it (and may scroll).
        var pageWidth = layout.Book.Size.X * (layout.IsSpread ? 0.5f : 1f);
        var unit = pageWidth / 520f;
        var enlarged = MathF.Max(1f, context.FontSize / (18f * context.Scale));
        var page = context with { Scale = unit, FontSize = MathF.Max(10f, MathF.Round(pageWidth * 16f / 420f * enlarged)) };
        var explore = false;
        var back = false;
        desk.BeginFrame();
        if (riffle.Active) DrawRiffle();
        else if (turning) DrawTurn();
        else DrawSpread(Section, VisiblePage);

        var openSettings = Section != BookSection.Settings && BookControl.Draw("Settings", "Settings", layout.Settings(context), page, BookControlStyle.Ink, hint: "Wanderveil settings");
        if (BookControl.Draw("Smaller", "-", layout.Tool(0, context), page, BookControlStyle.Ink, hint: "Smaller passport")) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        if (BookControl.Draw("Larger", "+", layout.Tool(1, context), page, BookControlStyle.Ink, hint: "Larger passport")) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        var close = BookControl.Draw("Close", "×", layout.Tool(2, context), page, BookControlStyle.Ink, hint: "Close passport (Escape)");
        var resting = Section == BookSection.Passport && !riffle.Active && !turning;
        if (resting) StampControls(identityShown);
        ImGui.PopStyleVar();
        var edgeTurn = desk.Update(bounds, layout.Book, layout.LeftPage.Size.X, page, ImGui.GetIO().DeltaTime);

        var placeStamp = discover.TakePassportRequest();
        if (chosen is { } tab) Navigate(tab);
        else if (explore) Navigate(BookSection.Discover);
        else if (placeStamp != null && collection.PlacementOf(placeStamp) is { } placed) { Navigate(BookSection.Passport); ShowPage(fixedPages.Count + placed.Sheet, layout.IsSpread); }
        else if (placeStamp != null) PlaceStamp(placeStamp, layout.IsSpread);
        else if (openSettings) ShowSettings();
        else if (back) GoBack();
        if (close) motion.Dismiss();

        var turns = Section == BookSection.Passport && !riffle.Active;
        // While a stamp is carried or turned, the mouse belongs to it: the book neither moves nor turns.
        int? click = null;
        var drag = desk.Busy || desk.TookClick ? Vector2.Zero : Gesture(layout.Book, turns, out click);
        if (turns)
        {
            if (edgeTurn != 0 && !turn.Active) Turn(edgeTurn, step);
            // While writing, a click on the paper only leaves the field; it never turns the page.
            if (click is { } side && !EditingIdentity) Turn(side, step);
            if (Keys(ImGuiKey.PageDown)) Turn(1, step);
            if (Keys(ImGuiKey.PageUp)) Turn(-1, step);
            if (Keys(ImGuiKey.Home) && desk.Carrying == null) CloseBook();
            if (Keys(ImGuiKey.End)) GoToPage(fixedPages.Count + Math.Max(0, collection.LastSheet) + 1, step == 2);
        }
        return new BookShellIntent(motion.Phase == BookPhase.Hidden, drag);

        // The stamp control in the footer (Place a waiting stamp, Put back, or Arrange/Done), and an open stamp's info
        // page over the page opposite its stamp (over its own page when only one page shows).
        // On the identity pages the footer offers Edit (or Done) instead.
        void StampControls(bool identityPages)
        {
            if (desk.InfoSide() is { } right && desk.Info is { } shown && collection.PlacementOf(shown) is { } at)
                desk.DrawInfo(layout.IsSpread ? right ? layout.LeftPage : layout.RightPage : layout.LeftPage, fixedPages.Count + at.Sheet + 1, page);
            if (identityPages && desk.Carrying == null)
            {
                var edit = EditingIdentity ? "Done" : "Edit";
                if (BookControl.Draw("Identity", edit, Footer(edit), page, BookControlStyle.Ink, hint: EditingIdentity ? "Stop editing (Escape)" : "Write your details, signature, and picture"))
                {
                    if (EditingIdentity) StopEditing();
                    else { EditingIdentity = true; editor.Reset(); }
                }
                return;
            }
            var waiting = collection.Unplaced.FirstOrDefault();
            var count = collection.Unplaced.Count();
            var label = desk.Carrying != null ? "Put back" : waiting != null ? count == 1 ? "Place stamp" : $"Place stamp ({count})" : desk.Arrange ? "Done" : "Arrange";
            var hint = desk.Carrying != null ? "Put the stamp you are holding back (Escape)" : waiting != null ? "Pick up a stamp waiting to be placed" : desk.Arrange ? "Stop arranging stamps" : "Move and turn your stamps";
            if (!BookControl.Draw("Stamps", label, Footer(label), page, BookControlStyle.Ink, hint: hint)) return;
            if (desk.Carrying != null) desk.Cancel();
            else if (waiting != null) PlaceStamp(waiting, layout.IsSpread);
            else desk.Arrange = !desk.Arrange;
        }

        // A footer control's place: right-aligned before the size and close marks, clear of the page number on a single page.
        UiRect Footer(string label)
        {
            var tools = layout.Tool(0, context);
            var width = MathF.Max(BookShellLayout.SettingsWidth(context), UiTypography.Measure(label, TextRole.Caption, page, float.MaxValue, false).X + 20 * page.Scale);
            return new UiRect(new Vector2(tools.Position.X - BookShellLayout.Gap(context) - width, tools.Position.Y), new Vector2(width, tools.Size.Y));
        }

        // The resting spread of a section: passport pages, or a section's two pages (one when narrow).
        void DrawSpread(BookSection section, int at)
        {
            if (section == BookSection.Passport)
            {
                DocumentPage(at, layout.LeftPage, false, true);
                if (layout.IsSpread && at + 1 < PageCount) DocumentPage(at + 1, layout.RightPage, true, true);
                Folio(at, false, true);
                if (layout.IsSpread && at + 1 < PageCount) Folio(at + 1, true, true);
                return;
            }
            StaticSide(section, at, layout.IsSpread ? Side.Left : Side.Single, layout.LeftPage);
            if (layout.IsSpread) StaticSide(section, at, Side.Right, layout.RightPage);
        }

        void StaticSide(BookSection section, int at, Side side, UiRect region)
        {
            if (section == BookSection.Passport)
            {
                var index = at + (side == Side.Right ? 1 : 0);
                if (index < PageCount) DocumentPage(index, region, side == Side.Right, false);
                return;
            }
            ImGui.PushID((int)section);
            try { Page(side == Side.Left ? "Section heading" : "Section content", region, () => SectionSide(section, side, page)); }
            finally { ImGui.PopID(); }
        }

        // What one side of a section shows, drawn at the cursor of the current (page or leaf) window.
        void SectionSide(BookSection section, Side side, UiContext type)
        {
            switch (section)
            {
                case BookSection.Settings:
                    if (side != Side.Right) back |= SettingsIntroduction(type);
                    if (side == Side.Single) ImGui.Dummy(new Vector2(0, 24 * type.Scale));
                    if (side != Side.Left) SettingsOptions(type);
                    break;
                case BookSection.Discover when side != Side.Left:
                    discover.Draw(type, onPaper: true);
                    break;
                case BookSection.Echoes:
                    if (side != Side.Right) echoes.DrawLeft(type);
                    if (side == Side.Single) ImGui.Dummy(new Vector2(0, 24 * type.Scale));
                    if (side != Side.Left) echoes.DrawRight(type);
                    break;
                case BookSection.Studio:
                    if (side != Side.Right) studio.DrawLeft(type, side == Side.Single);
                    if (side == Side.Single) ImGui.Dummy(new Vector2(0, 24 * type.Scale));
                    if (side != Side.Left) studio.DrawRight(type);
                    break;
                default:
                    DiscoverIntroduction(type);
                    break;
            }
        }

        // The content a turning leaf carries for one side of a section, or null for a blank leaf.
        Action? LeafSide(BookSection section, int at, Side side)
        {
            if (section != BookSection.Passport) return () => SectionSide(section, side, page);
            var index = at + (side == Side.Right ? 1 : 0);
            if (index >= PageCount) return null;
            var entry = PageAt(index);
            return () =>
            {
                if (entry.Kind == PassportPageKind.NationBanner) Banner(index, side == Side.Right);
                Contents(entry, Printed(Fit(entry)), false, side == Side.Right);
            };
        }

        // live: the resting spread, whose stamps take input; pages beside a turn or riffle only show theirs.
        void DocumentPage(int index, UiRect region, bool rightSide, bool live)
        {
            var entry = PageAt(index);
            if (entry.Kind == PassportPageKind.NationBanner) Banner(index, rightSide);
            var fit = Fit(entry);
            var type = Printed(fit);
            Page(entry.Id, region, () =>
            {
                Contents(entry, type, live, rightSide);
                var used = ImGui.GetCursorPosY();
                if (entry.Kind != PassportPageKind.Stamps && enlarged <= 1f && used > 1f)
                {
                    var target = MathF.Min(1f, fit * region.Size.Y * 0.96f / used);
                    if (used > region.Size.Y || MathF.Abs(target - fit) > 0.015f || (target >= 1f && fit < 1f)) fits[entry.Id] = MathF.Max(0.4f, MathF.Min(target, used > region.Size.Y ? fit * 0.97f : 1f));
                }
            });
        }

        // Printed pages shrink to their paper; enlarged host text keeps its size and may scroll instead.
        float Fit(PassportPage entry) => enlarged > 1f ? 1f : fits.GetValueOrDefault(entry.Id, 1f);
        UiContext Printed(float fit) => page with { Scale = page.Scale * fit, FontSize = MathF.Max(8f, MathF.Round(page.FontSize * fit)) };

        void Banner(int index, bool rightSide)
        {
            var face = layout.IsSpread ? BookFace.Spread : index % 2 == 1 ? BookFace.Right : BookFace.Left;
            passport.DrawNationBanner(BookSurface.Paper(layout.Book, face, layout.IsSpread && rightSide), layout.Footer.Position.Y - 4 * context.Scale, page);
        }

        void Contents(PassportPage entry, UiContext type, bool live, bool rightSide)
        {
            if (entry.Kind == PassportPageKind.Stamps)
            {
                passport.Engrave(type);
                desk.DrawSheet(entry.Sheet, live, rightSide && layout.IsSpread, type);
                return;
            }
            passport.Compose(entry, type, live && EditingIdentity ? editor : null);
            if (entry.Kind == PassportPageKind.Information && !(live && EditingIdentity))
            {
                ImGui.Dummy(new Vector2(0, 10 * type.Scale));
                explore |= BookControl.Link("Explore discoveries »", type);
            }
        }

        // The geometry of a turning paper leaf: in a spread it hinges on the spine; on a single page it
        // hinges on the page's outer left edge and swings away to the left.
        (UiRect Face, Vector2 Hinge, float Width, float UvLeft, float UvRight) Leaf(bool back, int parityPage)
        {
            var book = layout.Book;
            // A leaf is the paper alone (plate y 66-958); the board stays where it is.
            var faceHeight = book.Size.Y * 892f / 1024f;
            var center = book.Position.Y + book.Size.Y / 2;
            if (layout.IsSpread)
            {
                var width = book.Size.X * 702f / 1536f;
                var hinge = new Vector2(book.Position.X + book.Size.X / 2, center);
                return (new UiRect(new Vector2(back ? hinge.X - width : hinge.X, center - faceHeight / 2), new Vector2(width, faceHeight)), hinge, width, back ? 66f / 1536f : 1470f / 1536f, 0.5f);
            }
            var even = parityPage % 2 == 0;
            var singleWidth = book.Size.X * 702f / 768f;
            var singleHinge = new Vector2(book.Position.X + (even ? book.Size.X * 66f / 768f : 0f), center);
            return (new UiRect(new Vector2(singleHinge.X, center - faceHeight / 2), new Vector2(singleWidth, faceHeight)), singleHinge, singleWidth, even ? 66f / 1536f : 0.5f, even ? 0.5f : 1470f / 1536f);
        }

        // Draws one turning leaf at angle (0 flat on the right or on the single page, pi flat on the left).
        // front/back are what its two faces carry; on a single page its back is never shown.
        void Sheet(string id, float angle, float bend, int parityPage, Action? front, Action? back)
        {
            var showsBack = angle > MathF.PI / 2;
            var (face, hinge, width, uvA, uvB) = Leaf(showsBack, parityPage);
            var leaf = new LeafProjection(face, hinge, width, face.Size.Y, angle, 1, bend, 6.5f);
            if (layout.IsSpread)
            {
                // uvA is the face's outer edge, uvB the spine.
                TurningLeaf(id, showsBack ? layout.LeftPage : layout.RightPage, face, leaf, showsBack, uvB, uvA, 1f, showsBack ? back : front);
                return;
            }
            var fade = !showsBack ? 1f : MathF.Max(0f, 1f - (angle - MathF.PI / 2) / (MathF.PI / 3));
            TurningLeaf(id, layout.LeftPage, face, leaf, false, uvA, uvB, fade, showsBack ? null : front);
        }

        // A page turn: the two pages that stay put draw normally, then the turning leaf draws its real
        // page content flat in its own child window and warps those vertices onto the leaf. Child windows
        // draw in order, so the leaf lands above the pages beneath it.
        void DrawTurn()
        {
            var first = turn.Base;
            var angle = MathF.PI * UiMotion.Ease(turn.Progress);
            // Paper curls as it turns: its outer edge trails the motion, most while it stands upright.
            var bend = -0.9f * MathF.Sin(angle) * (turn.Forward ? 1f : -1f);
            if (layout.IsSpread)
            {
                DocumentPage(first, layout.LeftPage, false, false);
                Folio(first, false);
                if (first + 3 < PageCount) { DocumentPage(first + 3, layout.RightPage, true, false); Folio(first + 3, true); }
                Sheet("Turning leaf", angle, bend, first, LeafSide(BookSection.Passport, first, Side.Right), LeafSide(BookSection.Passport, first + 2, Side.Left));
            }
            else
            {
                DocumentPage(first + 1, layout.LeftPage, false, false);
                Folio(first + 1, false);
                // A single page swings away to the left about its outer edge and fades, revealing the next.
                var region = BookShellLayout.Create(bounds, context, first).LeftPage;
                var (face, hinge, width, uvA, uvB) = Leaf(false, first);
                var leaf = new LeafProjection(face, hinge, width, face.Size.Y, angle, 1, bend, 6.5f);
                var fade = angle <= MathF.PI / 2 ? 1f : MathF.Max(0f, 1f - (angle - MathF.PI / 2) / (MathF.PI / 3));
                TurningLeaf("Turning leaf", region, face, leaf, false, uvA, uvB, fade, angle <= MathF.PI / 2 ? LeafSide(BookSection.Passport, first, Side.Left) : null);
            }
        }

        // A riffle between sections: a burst of leaves. The first leaf carries the old pages away, the
        // last brings the new ones, and the leaves between are blank. Pages not yet covered or already
        // revealed stay live underneath.
        void DrawRiffle()
        {
            var from = (Section: riffle.FromSection, Page: riffle.FromPage);
            var to = (Section, Page: VisiblePage);
            var forward = riffle.Direction > 0;
            var count = riffle.Leaves;
            var noneStarted = riffle.Progress(0) <= 0f;
            var lastStarted = riffle.Progress(count - 1) > 0f;
            var firstLanded = riffle.Progress(0) >= 1f;
            var allLanded = riffle.Progress(count - 1) >= 1f;
            if (layout.IsSpread)
            {
                // Leaves lift from the start side and land on the end side.
                var startSide = forward ? Side.Right : Side.Left;
                var endSide = forward ? Side.Left : Side.Right;
                Static(startSide, noneStarted ? from : lastStarted ? to : null);
                Static(endSide, !firstLanded ? from : allLanded ? to : null);
            }
            else if (forward) Static(Side.Single, noneStarted ? from : lastStarted ? to : null);
            else Static(Side.Single, !firstLanded ? from : allLanded ? to : null);

            // Leaves still on the right lie with the earliest on top; leaves already on the left pile up
            // with the latest on top. Draw from the bottom of each pile.
            var moving = Enumerable.Range(0, count).Where(index => riffle.Progress(index) is > 0f and < 1f).ToArray();
            var angles = moving.ToDictionary(index => index, index => MathF.PI * (forward ? UiMotion.Ease(riffle.Progress(index)) : 1f - UiMotion.Ease(riffle.Progress(index))));
            var left = moving.Where(index => angles[index] > MathF.PI / 2);
            var right = moving.Where(index => angles[index] <= MathF.PI / 2);
            var order = forward ? left.OrderBy(index => index).Concat(right.OrderByDescending(index => index)) : left.OrderByDescending(index => index).Concat(right.OrderBy(index => index));
            var single = layout.IsSpread ? (Side?)null : Side.Single;
            foreach (var index in order)
            {
                var angle = angles[index];
                var bend = -1.0f * MathF.Sin(angle) * (forward ? 1f : -1f);
                Action? front = null, behind = null;
                if (forward && index == 0) front = LeafSide(from.Section, from.Page, single ?? Side.Right);
                if (!forward && index == count - 1) front = LeafSide(to.Section, to.Page, single ?? Side.Right);
                if (forward && index == count - 1) behind = LeafSide(to.Section, to.Page, Side.Left);
                if (!forward && index == 0) behind = LeafSide(from.Section, from.Page, Side.Left);
                Sheet("Riffle leaf " + index, angle, bend, layout.IsSpread ? 0 : SinglePage(forward && index == 0 ? from : to), front, behind);
            }

            void Static(Side side, (BookSection Section, int Page)? state)
            {
                if (state is not { } shown) return;
                var region = side == Side.Right ? layout.RightPage : layout.LeftPage;
                ImGui.PushID("Riffle " + (int)shown.Section);
                try { StaticSide(shown.Section, shown.Page, side, region); }
                finally { ImGui.PopID(); }
            }

            int SinglePage((BookSection Section, int Page) state) => state.Section == BookSection.Passport ? state.Page : 0;
        }

        void TurningLeaf(string id, UiRect region, UiRect face, LeafProjection leaf, bool back, float uvHinge, float uvOuter, float alpha, Action? content)
        {
            ImGui.SetCursorScreenPos(region.Position);
            // The same padding and scrollbar reservation as a resting page, so content lays out identically.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 5 * context.Scale);
            try
            {
                var visible = ImGui.BeginChild(id, region.Size, false, ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings);
                try
                {
                    if (!visible) return;
                    var list = ImGui.GetWindowDrawList();
                    // The lifted leaf shades the paper beside the spine on whichever side it hangs over.
                    var dark = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.24f * MathF.Sin(leaf.Angle) * alpha));
                    var clear = ImGui.GetColorU32(Vector4.Zero);
                    var reach = face.Size.X * 0.35f;
                    if (leaf.Angle <= MathF.PI / 2) list.AddRectFilledMultiColor(new Vector2(leaf.Hinge.X, face.Position.Y), new Vector2(leaf.Hinge.X + reach, face.Max.Y), dark, clear, clear, dark);
                    else list.AddRectFilledMultiColor(new Vector2(leaf.Hinge.X - reach, face.Position.Y), new Vector2(leaf.Hinge.X, face.Max.Y), clear, dark, dark, clear);
                    if (assets.TryGet(BookAsset.Open, out var open, layout.Book.Size.X))
                        leaf.Leaf(list, open.Texture, new Vector2(uvHinge, 66f / 1024f), new Vector2(uvOuter, 958f / 1024f), Vector4.One with { W = alpha });
                    // Without the plate the leaf is plain paper, so its content never turns through the air.
                    else list.AddQuadFilled(leaf.Project(0, 0), leaf.Project(1, 0), leaf.Project(1, 1), leaf.Project(0, 1), LeafProjection.Tint(UiTheme.Paper with { W = alpha }, leaf.Light));
                    var start = list.VtxBuffer.Size;
                    var startIndex = list.IdxBuffer.Size;
                    if (content != null)
                    {
                        ImGui.SetCursorScreenPos(region.Position);
                        // Large art is drawn in strips so it bends with the curled paper.
                        BookSurface.Strips = 16;
                        try { content(); }
                        finally { BookSurface.Strips = 1; }
                    }
                    // Only what shows in the page's window turns with the leaf, never what is scrolled out of it.
                    LeafProjection.Crop(list, start, startIndex, region);
                    leaf.Warp(list, start, face, back, alpha);
                    var viewport = ImGui.GetMainViewport();
                    LeafProjection.Unclip(list, new Vector4(viewport.Pos, viewport.Pos.X + viewport.Size.X, viewport.Pos.Y + viewport.Size.Y));
                }
                finally { ImGui.EndChild(); }
            }
            finally { ImGui.PopStyleVar(2); }
        }

        // A page number. On the passport's resting pages it is also a way to any page: click it, type a page
        // (1 to 100), and press Enter. Escape or clicking away leaves it as it was.
        void Folio(int index, bool right, bool live = false)
        {
            var label = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var measured = UiTypography.Measure(label, TextRole.Caption, page, 80, false);
            var center = layout.Folio(right);
            var jumps = live && Section == BookSection.Passport;
            if (jumps && pageEntry == right) { PageEntry(center, measured); return; }
            UiTypography.Draw(ImGui.GetWindowDrawList(), center - measured / 2, label, TextRole.Caption, page, 80, UiTheme.MutedInk, false);
            if (!jumps) return;
            var pad = new Vector2(8, 3) * page.Scale;
            ImGui.SetCursorScreenPos(center - measured / 2 - pad);
            if (ImGui.InvisibleButton("Page number " + (right ? "right" : "left"), measured + pad * 2))
            {
                pageEntry = right;
                pageText = label;
                focusPageEntry = true;
            }
            if (!ImGui.IsItemHovered()) return;
            ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
            ImGui.SetTooltip($"Go to page ({FirstPageNumber} to {PageCount})");
            BookSurface.Line(ImGui.GetWindowDrawList(), center + new Vector2(-measured.X / 2, measured.Y / 2), center + measured / 2, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.6f }), MathF.Max(1, page.Scale));
        }

        void PageEntry(Vector2 center, Vector2 measured)
        {
            var width = MathF.Max(measured.X, UiTypography.Measure("000", TextRole.Caption, page, 80, false).X) + 12 * page.Scale;
            var list = ImGui.GetWindowDrawList();
            ImGui.PushFont(UiTypography.ResolveFont("0123456789", TextRole.Caption, page));
            ImGui.SetWindowFontScale(UiTypography.Size(TextRole.Caption, page) / ImGui.GetFontSize());
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 1) * page.Scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, UiTheme.PaperInset with { W = 0.55f });
            ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Ink);
            ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, UiTheme.Gold with { W = 0.45f });
            try
            {
                ImGui.SetCursorScreenPos(center - new Vector2(width / 2, ImGui.GetFrameHeight() / 2));
                ImGui.SetNextItemWidth(width);
                if (focusPageEntry) { ImGui.SetKeyboardFocusHere(); focusPageEntry = false; }
                var entered = ImGui.InputText("##Go to page", ref pageText, 4, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                BookSurface.Line(list, new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y), ImGui.GetItemRectMax(), ImGui.GetColorU32(UiTheme.Ink), MathF.Max(1, page.Scale));
                if (entered)
                {
                    pageEntry = null;
                    if (int.TryParse(pageText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
                        GoToPage(number, layout.IsSpread);
                }
                // Leaving it (Escape, Tab, or a click elsewhere) closes it; so does a click while it never took focus.
                else if (ImGui.IsItemDeactivated() || (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))) pageEntry = null;
            }
            finally
            {
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(2);
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
            }
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
                try { if (visible) { content(); PageOverflow = MathF.Max(PageOverflow, ImGui.GetScrollMaxY()); PageOverflowX = MathF.Max(PageOverflowX, ImGui.GetScrollMaxX()); } }
                finally { ImGui.EndChild(); }
            }
            finally { ImGui.PopStyleColor(2); ImGui.PopStyleVar(2); }
        }
    }

    private enum Side { Left, Right, Single }

    // The selected tab's ribbon unrolls after the pages appear and rerolls before moving to another tab;
    // on the Settings pages, which have no tab, it stays rolled up.
    private void Ribbon(BookShellLayout layout, UiContext context)
    {
        var dt = UiMotion.Frame(ImGui.GetIO().DeltaTime);
        var wanted = Section == BookSection.Settings ? (BookSection?)null : Section;
        if (wanted != ribbonSection || wanted == null)
        {
            ribbonExtension = ReducedMotion ? 0f : ribbonExtension - dt / UiMotion.RibbonRoll;
            if (ribbonExtension <= 0f) { ribbonExtension = 0f; if (wanted is { } next) ribbonSection = next; }
        }
        else ribbonExtension = ReducedMotion ? 1f : motion.Reveal < 0.5f || riffle.Active ? ribbonExtension : MathF.Min(1f, ribbonExtension + dt / UiMotion.RibbonUnroll);
        if (ribbonExtension > 0f) BookControl.Ribbon(layout.Bookmark((int)ribbonSection), context, UiMotion.Ease(ribbonExtension));
    }

    // Moves to another section by riffling pages in book order: a later section flips right-to-left.
    private void Navigate(BookSection to)
    {
        if (to == Section || riffle.Active) return;
        if (turn.Active) { turn.Finish(); VisiblePage = turn.Target; }
        StopEditing();
        desk.Cancel();
        desk.CloseInfo();
        pageEntry = null;
        var distance = (int)to - (int)Section;
        riffle.Start(Math.Sign(distance), BookShellRiffle.LeavesFor(distance), Section, VisiblePage, ReducedMotion);
        Section = to;
        pressing = dragging = false;
    }

    // Opens the passport on the first empty page after the last placed stamp, with a stamp in hand to place.
    private void PlaceStamp(StampInstance stamp, bool spread)
    {
        Navigate(BookSection.Passport);
        ShowPage(fixedPages.Count + collection.LastSheet + 1, spread);
        desk.PickUp(stamp);
    }

    // Opens a printed page number (1 to the last page; anything outside is clamped, and while a stamp is held,
    // to the stamp pages). A spread or more away, the pages riffle there, as between sections.
    internal void GoToPage(int number, bool spread)
    {
        if (Section != BookSection.Passport || riffle.Active) return;
        if (turn.Active) { turn.Finish(); VisiblePage = turn.Target; }
        var step = spread ? 2 : 1;
        var target = Math.Clamp(number, FirstPageNumber, PageCount) - 1;
        if (spread) target -= target % 2;
        if (target == VisiblePage) return;
        var spreads = Math.Abs(target - VisiblePage) / step;
        if (spreads > 1) riffle.Start(Math.Sign(target - VisiblePage), BookShellRiffle.LeavesFor(Math.Min(3, spreads - 1)), Section, VisiblePage, ReducedMotion);
        else { VisiblePage = turn.Request(Math.Sign(target - VisiblePage), step, VisiblePage, PageCount, ReducedMotion); desk.CloseInfo(); return; }
        ShowPage(target, spread);
    }

    // The lowest page a jump may open: the first stamp page while a stamp is held, otherwise page 1.
    internal int FirstPageNumber => desk.Carrying != null ? fixedPages.Count + 1 : 1;

    private int FirstStampPage(bool spread) => spread ? fixedPages.Count - fixedPages.Count % 2 : fixedPages.Count;

    // Shows a passport page at once (its spread, when the book shows two pages).
    private void ShowPage(int index, bool spread)
    {
        if (spread) index -= index % 2;
        VisiblePage = Math.Clamp(index, 0, PageCount - 1);
        turn.Reset(VisiblePage);
        desk.CloseInfo();
    }

    private PassportPage PageAt(int index) => index < fixedPages.Count ? fixedPages[index] : PassportPage.StampPage(index - fixedPages.Count);

    private bool IsIdentity(int index) => index < fixedPages.Count && fixedPages[index].Kind is PassportPageKind.Portrait or PassportPageKind.Information;

    private void StopEditing()
    {
        EditingIdentity = false;
        editor.Reset();
    }

    // Opens the Settings pages, from the printed Settings mark or the host's settings button. A closed
    // book opens straight onto them; an open book riffles forward to them.
    internal void ShowSettings()
    {
        if (Section == BookSection.Settings) return;
        if (!motion.WantsOpen)
        {
            ReturnSection = BookSection.Passport;
            Section = BookSection.Settings;
            OpenBook();
            return;
        }
        ReturnSection = Section;
        Navigate(BookSection.Settings);
    }

    // Settings' Back link: riffles back to the section (and passport page) the reader came from.
    internal void GoBack()
    {
        if (Section == BookSection.Settings) Navigate(ReturnSection);
    }

    private bool SettingsIntroduction(UiContext context)
    {
        UiTypography.Text("WANDERVEIL / SETTINGS", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 16 * context.Scale));
        UiTypography.Text("Settings", TextRole.Title, context, UiTheme.Ink);
        ImGui.Dummy(new Vector2(0, 12 * context.Scale));
        UiTypography.Text("How your passport moves. Changes are saved as you make them.", TextRole.Body, context, UiTheme.Ink);
        ImGui.Dummy(new Vector2(0, 20 * context.Scale));
        return BookControl.Link("« Back to " + Labels[(int)ReturnSection], context);
    }

    private void SettingsOptions(UiContext context)
    {
        UiTypography.Text("MOTION", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 10 * context.Scale));
        if (settings == null)
        {
            UiTypography.Text("Settings are not available here.", TextRole.Body, context, UiTheme.MutedInk);
            return;
        }
        if (BookControl.Toggle("Reduce motion", settings.ReducedMotion, context))
        {
            settings.ReducedMotion = !settings.ReducedMotion;
            saveFailed = !settings.Save();
        }
        ImGui.Dummy(new Vector2(0, 6 * context.Scale));
        UiTypography.Text("Skip the sliding, cover, and page-turn movement. Everything still opens, turns, and closes, just instantly.", TextRole.Body, context, UiTheme.MutedInk);
        if (saveFailed)
        {
            ImGui.Dummy(new Vector2(0, 14 * context.Scale));
            UiTypography.Text("Your settings could not be saved.", TextRole.Body, context, UiTheme.Ink);
            ImGui.Dummy(new Vector2(0, 6 * context.Scale));
            if (BookControl.Link("Try saving again", context)) saveFailed = !settings.Save();
        }
        StampOptions(context);
        IdentityOptions(context);
    }

    // Anything loading the passport's identity found, and a retry when saving it failed.
    private void IdentityOptions(UiContext context)
    {
        if (IdentityStorageText.Notice(identityStorage) is not { } notice) return;
        ImGui.Dummy(new Vector2(0, 24 * context.Scale));
        UiTypography.Text("PASSPORT", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 10 * context.Scale));
        UiTypography.Text(notice, TextRole.Body, context, UiTheme.Ink);
        if (identityStorage?.State != SaveState.Failed) return;
        ImGui.Dummy(new Vector2(0, 6 * context.Scale));
        if (BookControl.Link("Try saving your passport again", context)) identityStorage.Retry();
    }

    // How many stamps the passport holds, whether they are saved, and anything loading them found.
    private void StampOptions(UiContext context)
    {
        ImGui.Dummy(new Vector2(0, 24 * context.Scale));
        UiTypography.Text("STAMPS", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 10 * context.Scale));
        var waiting = collection.Unplaced.Count();
        UiTypography.Text(StampStorageText.Count(collection.Stamps.Count, storage) + (waiting > 0 ? $" / {waiting} WAITING TO BE PLACED" : ""), TextRole.Caption, context, UiTheme.Ink);
        if (StampStorageText.Notice(storage) is { } notice)
        {
            ImGui.Dummy(new Vector2(0, 6 * context.Scale));
            UiTypography.Text(notice, TextRole.Body, context, UiTheme.Ink);
        }
        if (storage?.State != SaveState.Failed) return;
        ImGui.Dummy(new Vector2(0, 6 * context.Scale));
        if (BookControl.Link("Try saving stamps again", context)) storage.Retry();
    }

    internal void BeginSession()
    {
        Section = ribbonSection = BookSection.Passport;
        VisiblePage = 0;
        turn.Reset(0);
        riffle.Reset();
        desk.Cancel();
        desk.CloseInfo();
        StopEditing();
        pageEntry = null;
        ribbonExtension = 0f;
        pressing = dragging = false;
        motion.Summon();
    }

    // Summons again (reversing a slide away in progress) or starts the full dismissal sequence.
    internal void Summon() => motion.Summon();
    internal void RequestDismiss() => motion.Dismiss();

    // The open size is used from the moment opening is requested until the cover has fully closed.
    internal Vector2 FitSize(Vector2 room, UiContext context) => BookShellLayout.Fit(PreferredWidth, room, context, !(motion.WantsOpen || motion.Cover > 0f));

    private static BookShellMotion Arrived()
    {
        var arrived = new BookShellMotion();
        arrived.Summon();
        return arrived;
    }

    private void Turn(int direction, int step)
    {
        // A held stamp stays on the stamp pages: no turning back past the first of them.
        if (direction < 0 && desk.Carrying != null && !turn.Active && VisiblePage - step < FirstStampPage(step == 2)) return;
        // From the first page, the left side closes the book; a turn in progress reverses first.
        if (direction < 0 && VisiblePage == 0 && !turn.Active) { CloseBook(); return; }
        VisiblePage = turn.Request(direction, step, VisiblePage, PageCount, ReducedMotion);
        desk.CloseInfo();
    }

    private void CloseBook()
    {
        motion.CloseToCover();
        pressing = dragging = false;
    }

    private void OpenBook()
    {
        motion.Open();
        VisiblePage = 0;
        turn.Reset(0);
        riffle.Reset();
        focusSection = true;
        pressing = dragging = false;
    }

    private BookShellIntent DrawClosed(UiRect bounds, UiContext context)
    {
        passport.DrawCover(bounds, context);
        var moving = motion.IsMoving;
        ImGui.BeginDisabled(moving);
        // Window controls are stamped into the cover's lower band, beneath its arc; Settings live inside the book.
        var (board, unit) = BookSurface.CoverBoard(bounds);
        solid.Add(new UiRect(board, new Vector2(420, 560) * unit));
        var stamp = context with { FontSize = MathF.Max(context.FontSize, MathF.Round(15 * unit)) };
        var height = MathF.Max(BookShellLayout.Small(context), 22 * unit);
        var top = board.Y + 531 * unit + (22 * unit - height) / 2;
        var small = BookShellLayout.Small(context);
        var spacing = MathF.Max(BookShellLayout.Gap(context), 10 * unit);
        var x = board.X + 356 * unit - 3 * small - 2 * spacing;
        if (BookControl.Draw("Smaller", "-", new UiRect(new Vector2(x, top), new Vector2(small, height)), stamp, BookControlStyle.Foil, hint: "Smaller passport")) PreferredWidth = MathF.Max(420, PreferredWidth / 1.15f);
        x += small + spacing;
        if (BookControl.Draw("Larger", "+", new UiRect(new Vector2(x, top), new Vector2(small, height)), stamp, BookControlStyle.Foil, hint: "Larger passport")) PreferredWidth = MathF.Min(1400, PreferredWidth * 1.15f);
        x += small + spacing;
        var close = BookControl.Draw("Close", "×", new UiRect(new Vector2(x, top), new Vector2(small, height)), stamp, BookControlStyle.Foil, hint: "Close passport (Escape)");
        ImGui.EndDisabled();
        if (close) motion.Dismiss();
        if (moving) { pressing = dragging = false; return new BookShellIntent(motion.Phase == BookPhase.Hidden, Vector2.Zero); }

        var cover = new UiRect(board, new Vector2(420, 560) * unit);
        var drag = Gesture(cover, true, out var click, opensOnly: true);
        if (click > 0) OpenBook();
        else if (!ImGui.IsAnyItemFocused() && (Keys(ImGuiKey.Space) || Keys(ImGuiKey.Enter) || Keys(ImGuiKey.KeypadEnter) || Keys(ImGuiKey.PageDown))) OpenBook();
        return new BookShellIntent(motion.Phase == BookPhase.Hidden, drag);
    }

    // The cover swinging open on its spine (or closed again), with no page content or input until it lands.
    // At the start it sits exactly where the closed cover was, then the book glides so the spread ends centred.
    private BookShellIntent DrawTurning(UiRect bounds, UiContext context)
    {
        // The swinging cover takes no input, but clicks during it must not fall through to the game either.
        solid.Add(bounds);
        pressing = dragging = false;
        var layout = BookShellLayout.Create(bounds, context, 0);
        var eased = UiMotion.Ease(motion.Cover);
        var angle = MathF.PI * eased;
        var book = layout.Book;
        var spread = layout.IsSpread;
        var hingeX = book.Position.X + book.Size.X * (spread ? 0.5f : 40f / 768f);
        var leafWidth = BookShellLayout.LeafWidth(layout);
        var centerY = book.Position.Y + book.Size.Y * 0.5f;
        var coverHeight = leafWidth * 4f / 3f;
        // The closed cover shares this window's bottom centre, as the host anchors the window.
        var closedSize = BookShellLayout.ClosedSize(leafWidth);
        var closed = new UiRect(new Vector2(bounds.Position.X + (bounds.Size.X - closedSize.X) / 2, bounds.Max.Y - closedSize.Y), closedSize);
        var shift = (BookSurface.CoverBoard(closed).Origin - new Vector2(hingeX, centerY - coverHeight / 2)) * (1 - eased);
        var list = ImGui.GetWindowDrawList();
        var viewport = ImGui.GetMainViewport();
        list.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, false);
        try
        {
            var moved = book with { Position = book.Position + shift };
            var hinge = new Vector2(hingeX, centerY) + shift;
            var hasOpen = assets.TryGet(BookAsset.Open, out var open, moved.Size.X * (spread ? 1 : 2));
            // Beneath the leaf: the right half of the spread, or the single first page.
            if (hasOpen)
            {
                if (spread) list.AddImage(open.Texture, moved.Position + new Vector2(moved.Size.X / 2, 0), moved.Max, new Vector2(0.5f, 0), Vector2.One);
                else list.AddImage(open.Texture, moved.Position, moved.Max, Vector2.Zero, new Vector2(0.5f, 1));
            }
            else if (spread) BookSurface.Fallback(list, new UiRect(moved.Position + new Vector2(moved.Size.X / 2, 0), new Vector2(moved.Size.X / 2, moved.Size.Y)), BookFace.Right, context);
            else BookSurface.Fallback(list, moved, BookFace.Left, context);
            // The lifted leaf shades the paper next to the spine.
            var dark = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.30f * MathF.Sin(angle)));
            var clear = ImGui.GetColorU32(Vector4.Zero);
            list.AddRectFilledMultiColor(new Vector2(hinge.X, moved.Position.Y + moved.Size.Y * 0.065f), new Vector2(hinge.X + leafWidth * 0.3f, moved.Position.Y + moved.Size.Y * 0.935f), dark, clear, clear, dark);
            if (angle <= MathF.PI / 2)
            {
                var source = new UiRect(new Vector2(hinge.X, hinge.Y - coverHeight / 2), new Vector2(leafWidth, coverHeight));
                passport.DrawCover(BookShellLayout.CoverPlate(source), context, new LeafProjection(source, hinge, leafWidth, coverHeight, angle, 1));
            }
            else if (spread && hasOpen)
            {
                // The cover's inside is the first left page; it lands exactly where the open spread draws it.
                var pageHeight = moved.Size.Y * 936f / 1024f;
                var leaf = new LeafProjection(new UiRect(new Vector2(hinge.X, hinge.Y - pageHeight / 2), new Vector2(leafWidth, pageHeight)), hinge, leafWidth, pageHeight, angle, 1);
                leaf.Leaf(list, open.Texture, new Vector2(768f / 1536f, 44f / 1024f), new Vector2(40f / 1536f, 980f / 1024f), Vector4.One);
            }
            else
            {
                // A single page has nowhere for the cover to land, so its inside fades as it swings away.
                var leaf = new LeafProjection(default, hinge, leafWidth, coverHeight, angle, 1);
                var fade = 1f - (angle - MathF.PI / 2) / (MathF.PI / 2);
                list.AddQuadFilled(leaf.Project(0, 0), leaf.Project(1, 0), leaf.Project(1, 1), leaf.Project(0, 1), LeafProjection.Tint(UiTheme.Edition with { W = fade }, leaf.Light));
            }
        }
        finally { list.PopClipRect(); }
        return new BookShellIntent(motion.Phase == BookPhase.Hidden, Vector2.Zero);
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

    private static void DiscoverIntroduction(UiContext context)
    {
        UiTypography.Text("WANDERVEIL / DISCOVER", TextRole.Caption, context, UiTheme.MutedInk);
        ImGui.Dummy(new Vector2(0, 16 * context.Scale));
        UiTypography.Text("Every place has a story.", TextRole.Title, context, UiTheme.Ink);
        ImGui.Dummy(new Vector2(0, 12 * context.Scale));
        UiTypography.Text("A journal of imagined places. Curated demo discoveries; no location is read.", TextRole.Body, context, UiTheme.Ink);
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
