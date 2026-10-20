using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class MotionChecks
{
    private const float Frame = 1f / 60f;

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Sequence(check);
        Interruption(check);
        Reduced(check);
        Turns(check);
        Riffles(check);
        Placement(check);
        Shell(check, artifacts);
        SingleTurn(check, artifacts);
        RiffleShell(check, artifacts);
        Console.WriteLine("PASS passport-motion");
    }

    // Arrival, opening, and dismissal run in order, and the book never slides while its cover is open.
    private static void Sequence(Action<bool, string> check)
    {
        var motion = new BookShellMotion();
        check(motion.Phase == BookPhase.Hidden && !motion.IsVisible, "A new motion is not hidden.");
        motion.Summon();
        check(motion.Phase == BookPhase.Entering && motion.Lift == 0f, "Summoning does not start below the screen.");
        var frames = Until(motion, () => motion.Phase == BookPhase.Closed, check);
        check(motion.Lift == 1f && motion.Cover == 0f, "Arrival does not rest closed.");
        check(Math.Abs(frames - UiMotion.Slide * 60) <= 2, "Arrival does not take its slide duration.");
        check(!motion.WantsOpen, "Arrival opens the cover by itself.");

        motion.Open();
        check(motion.WantsOpen && motion.Phase == BookPhase.Opening, "Opening is not requested at once.");
        Until(motion, () => motion.Phase == BookPhase.Open, check);
        check(motion.Reveal == 0f, "Pages appear before the cover has landed.");
        Until(motion, () => motion.Reveal >= 1f, check);

        motion.Dismiss();
        var sawClosing = false;
        var sawExiting = false;
        while (motion.Phase != BookPhase.Hidden)
        {
            sawClosing |= motion.Phase == BookPhase.Closing;
            sawExiting |= motion.Phase == BookPhase.Exiting;
            check(motion.Lift >= 1f || motion.Cover <= 0f, "The book slides away before its cover has closed.");
            check(!sawExiting || motion.Cover == 0f, "The cover reopens while sliding away.");
            check(motion.Reveal == 0f, "Pages stay visible during dismissal.");
            motion.Update(Frame);
        }
        check(sawClosing && sawExiting && !motion.IsVisible, "Dismissing an open book does not close to the cover, then slide away.");

        motion.Summon();
        check(motion.Lift == 0f && motion.Cover == 0f, "A new summon does not start closed from below the screen.");

        // Dismissing closes the cover faster than closing back to the cover does.
        int CloseFrames(bool dismiss)
        {
            var timed = new BookShellMotion { ReducedMotion = true };
            timed.Summon();
            timed.Open();
            timed.ReducedMotion = false;
            if (dismiss) timed.Dismiss(); else timed.CloseToCover();
            var frames = 0;
            while (timed.Cover > 0f && frames < 600) { timed.Update(Frame); frames++; }
            return frames;
        }
        var dismissFrames = CloseFrames(true);
        var coverFrames = CloseFrames(false);
        check(Math.Abs(dismissFrames - UiMotion.QuickCover * 60) <= 2 && Math.Abs(coverFrames - UiMotion.Cover * 60) <= 2 && dismissFrames < coverFrames,
            "Dismissing does not close the cover quickly, or closing to the cover is not unhurried.");

        check(UiMotion.Ease(0) == 0 && UiMotion.Ease(1) == 1 && Math.Abs(UiMotion.Ease(0.5f) - 0.5f) < 1e-5f, "Easing does not span 0 to 1 symmetrically.");
        var previous = 0f;
        for (var step = 1; step <= 100; step++)
        {
            var eased = UiMotion.Ease(step / 100f);
            check(eased >= previous, "Easing is not monotonic.");
            previous = eased;
        }
    }

    // Requests reverse a motion from where it is instead of jumping.
    private static void Interruption(Action<bool, string> check)
    {
        var motion = new BookShellMotion();
        motion.Summon();
        for (var frame = 0; frame < 10; frame++) motion.Update(Frame);
        var partway = motion.Lift;
        motion.Dismiss();
        motion.Update(Frame);
        check(motion.Lift < partway && motion.Lift > 0f, "Dismissing during arrival does not reverse from where the book is.");
        motion.Summon();
        var before = motion.Lift;
        motion.Update(Frame);
        check(motion.Lift > before && motion.Phase == BookPhase.Entering, "Summoning during a slide away does not reverse it.");

        Until(motion, () => motion.Phase == BookPhase.Closed, check);
        motion.Open();
        for (var frame = 0; frame < 12; frame++) motion.Update(Frame);
        var opened = motion.Cover;
        check(opened > 0f && opened < 1f, "The cover is not partway open.");
        motion.CloseToCover();
        motion.Update(Frame);
        check(motion.Cover < opened && motion.Phase == BookPhase.Closing, "Closing mid-swing does not reverse the cover.");
        motion.Open();
        motion.Open();
        motion.Update(Frame);
        check(motion.Phase == BookPhase.Opening, "Repeated open requests break the swing.");

        motion.Dismiss();
        motion.Open();
        check(!motion.WantsOpen, "A dismissed book can be reopened without summoning.");

        var hitch = new BookShellMotion();
        hitch.Summon();
        hitch.Update(5f);
        check(hitch.Lift > 0f && hitch.Lift < 0.5f, "A long frame skips most of the arrival.");
        hitch.Update(float.NaN);
        hitch.Update(-1f);
        check(float.IsFinite(hitch.Lift) && hitch.Lift > 0f, "Invalid frame times corrupt the motion.");
    }

    // Reduced motion reaches every result at once with the same order and explicit opening.
    private static void Reduced(Action<bool, string> check)
    {
        var motion = new BookShellMotion { ReducedMotion = true };
        motion.Summon();
        check(motion.Phase == BookPhase.Closed && motion.Lift == 1f, "Reduced motion does not arrive at once.");
        check(!motion.WantsOpen, "Reduced motion opens the cover by itself.");
        motion.Open();
        check(motion.Phase == BookPhase.Open && motion.Reveal == 1f, "Reduced motion does not open at once.");
        motion.Dismiss();
        check(motion.Phase == BookPhase.Hidden, "Reduced motion does not dismiss at once.");

        var switched = new BookShellMotion();
        switched.Summon();
        switched.Update(Frame);
        switched.ReducedMotion = true;
        check(switched.Phase == BookPhase.Closed, "Turning reduced motion on does not settle a motion in progress.");
    }

    // The real shell: no input while moving, the cover swing draws, and dismissal ends hidden.
    private static void Shell(Action<bool, string> check, string? artifacts)
    {
        using var host = new HeadlessUi(new Vector2(1200, 900), materials: true);
        var shell = new BookShell(host.Textures!, PassportIdentity.Sample);
        var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        var room = new Vector2(1168, 868);
        UiRect bounds = default;
        BookShellIntent intent = default;
        void Step()
        {
            shell.Advance(Frame);
            host.BookFrame(shell.FitSize(room, context), () =>
            {
                bounds = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                intent = shell.Draw(bounds, context);
                check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "The moving passport scrolls its host.");
            });
        }
        void Click(Vector2 point)
        {
            ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Step();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Step();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Step();
        }

        shell.BeginSession();
        Step();
        check(shell.Phase == BookPhase.Entering && shell.Lift < 1f, "The shell does not arrive from below.");
        var closedSize = bounds.Size;
        Click(BookShellLayout.OpenPoint(bounds));
        check(!shell.IsBookOpen, "Clicking the cover while it arrives opens it.");
        while (shell.Phase != BookPhase.Closed) Step();
        ImGui.GetIO().AddMousePosEvent(-100, -100); Step();

        Click(BookShellLayout.OpenPoint(bounds));
        check(shell.IsBookOpen && shell.Phase == BookPhase.Opening, "Clicking the arrived cover does not start opening.");
        // The request lands mid-frame; the next frame is sized open.
        Step();
        check(bounds.Size.X > closedSize.X, "The window does not take the open size while the cover swings.");
        var frames = 0;
        var saved = 0;
        while (shell.Phase == BookPhase.Opening && frames++ < 200)
        {
            var layout = BookShellLayout.Create(bounds, context, 0);
            Step();
            if (artifacts != null && saved < 3 && frames >= (saved + 1) * 9) SoftwarePreview.Save(Path.Combine(artifacts, $"passport-motion-opening-{++saved}.png"), host);
            if (frames == 15)
            {
                Click(layout.TurnPoint(true, 0));
                check(shell.VisiblePage == 0, "A click during the cover swing turns a page.");
            }
        }
        check(shell.Phase == BookPhase.Open, "The cover does not finish opening.");
        for (var frame = 0; frame < 30; frame++) Step();
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-open.png"), host);
        Click(BookShellLayout.Create(bounds, context, 0).TurnPoint(true, 0));
        check(shell.VisiblePage == 2, "Pages do not turn once the cover has landed.");

        // Turn back with motion, reversing mid-turn, and keep the page content on the turning leaf.
        Click(BookShellLayout.Create(bounds, context, 2).TurnPoint(false, 2));
        check(shell.IsTurning && shell.VisiblePage == 0, "Clicking the left side does not start turning back.");
        for (var frame = 0; frame < 8; frame++) Step();
        Click(BookShellLayout.Create(bounds, context, 0).TurnPoint(true, 0));
        check(shell.IsTurning && shell.VisiblePage == 2, "Clicking forward mid-turn does not reverse the turn.");
        while (shell.IsTurning) Step();
        check(shell.VisiblePage == 2 && shell.IsBookOpen, "A reversed turn does not land on the spread it returned to.");

        // Clicking quickly: every click turns its own leaf, following the ones in the air, and none is skipped.
        for (var click = 0; click < 4; click++) Click(BookShellLayout.Create(bounds, context, 2).TurnPoint(true, 2));
        check(shell.IsTurning && shell.VisiblePage == 10 && shell.LeavesInAir == 4, $"Four quick clicks put {shell.LeavesInAir} leaves in the air toward page {shell.VisiblePage + 1}.");
        frames = 0;
        for (; frames < 12; frames++) Step();
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-burst.png"), host);
        while (shell.IsTurning && frames++ < 240) Step();
        check(shell.VisiblePage == 10, "Quick clicks do not land where they lead.");
        for (var click = 0; click < 4; click++) Click(BookShellLayout.Create(bounds, context, 10).TurnPoint(false, 10));
        check(shell.IsTurning && shell.VisiblePage == 2 && shell.LeavesInAir == 4, "Quick clicks back are not turned leaf by leaf.");
        frames = 0;
        while (shell.IsTurning && frames++ < 240) Step();
        check(shell.VisiblePage == 2, "Quick clicks back do not land where they lead.");

        // Cost of a turn against a still spread: CPU time to build the frame and vertices submitted.
        // Headless and software-only; GPU cost in game is not measured here. The still spread is identity, the
        // page that later turns back into view (stamp pages after it are blank paper, far cheaper).
        (double Milliseconds, int Vertices) Measure(Action between)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var vertices = 0;
            for (var frame = 0; frame < 24; frame++) { between(); Step(); vertices = Math.Max(vertices, ImGui.GetDrawData().TotalVtxCount); }
            return (timer.Elapsed.TotalMilliseconds / 24, vertices);
        }
        var still = Measure(() => { });
        Click(BookShellLayout.Create(bounds, context, 2).TurnPoint(true, 2));
        var turning = 0;
        while (shell.IsTurning && turning < 120)
        {
            Step();
            turning++;
            if (artifacts != null && turning is 8 or 15 or 22) SoftwarePreview.Save(Path.Combine(artifacts, $"passport-motion-turn-{turning}.png"), host);
        }
        check(shell.VisiblePage == 4 && !shell.IsTurning, "A forward turn does not land on the first stamp spread.");

        Click(BookShellLayout.Create(bounds, context, 4).TurnPoint(false, 4));
        var moving = Measure(() => { });
        Console.WriteLine($"INFO passport-turn still {still.Milliseconds:0.00} ms/{still.Vertices} vertices, turning {moving.Milliseconds:0.00} ms/{moving.Vertices} vertices per frame (headless CPU)");
        check(moving.Vertices < still.Vertices * 3 && moving.Milliseconds < Math.Max(4, still.Milliseconds * 4), "A page turn costs far more than a still spread.");
        while (shell.IsTurning) Step();

        shell.RequestDismiss();
        var closing = false;
        var hidden = false;
        for (var frame = 0; frame < 200 && !hidden; frame++)
        {
            Step();
            closing |= shell.Phase == BookPhase.Closing;
            if (artifacts != null && frame == 14) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-closing.png"), host);
            check(!(shell.Lift < 1f && UiMotion.Ease(1) > 0 && shell.Phase == BookPhase.Closing), "The shell slides while closing.");
            hidden = intent.Close;
        }
        check(closing && hidden && shell.Phase == BookPhase.Hidden, "Dismissing the open shell does not close, slide away, and ask the host to hide.");
    }

    // The first summon centres the book; later summons return where it was dismissed or dragged.
    private static void Placement(Action<bool, string> check)
    {
        var workPos = new Vector2(0, 20);
        var workSize = new Vector2(1920, 1060);
        var closed = new Vector2(540, 810);
        var open = new Vector2(1040, 700);
        var first = BookShellPlacement.Rest(null, Vector2.Zero, closed, workPos, workSize);
        check(first == workPos + (workSize - closed) / 2, "The first summon is not centred on the screen.");

        var dismissed = new Vector2(600, 150);
        var recalled = BookShellPlacement.Rest(dismissed, closed, closed, workPos, workSize);
        check(recalled == dismissed, "A recalled passport does not return to where it was dismissed.");

        var opened = BookShellPlacement.Rest(dismissed, closed, open, workPos, workSize);
        check(opened.X + open.X / 2 == dismissed.X + closed.X / 2 && opened.Y + open.Y == dismissed.Y + closed.Y, "Opening moves the bottom centre of the book.");
        var reclosed = BookShellPlacement.Rest(opened, open, closed, workPos, workSize);
        check(reclosed == dismissed, "Opening then closing does not return the cover to the same place.");

        var offscreen = BookShellPlacement.Rest(new Vector2(-400, 5000), closed, closed, workPos, workSize);
        check(offscreen.X >= workPos.X && offscreen.Y + closed.Y <= workPos.Y + workSize.Y, "A remembered position outside the screen is not kept inside it.");

        var below = BookShellPlacement.Slide(dismissed, 0f, workPos, workSize, 24);
        check(below.X == dismissed.X && below.Y >= workPos.Y + workSize.Y, "The book does not slide from below the screen toward its remembered place.");
        check(BookShellPlacement.Slide(dismissed, 1f, workPos, workSize, 24) == dismissed, "A book at rest is not at its remembered place.");
    }

    // Page turns: quick same-way requests put more leaves in the air instead of skipping the one turning, and
    // an opposite request reverses the newest leaf from where it is.
    private static void Turns(Action<bool, string> check)
    {
        var turn = new BookShellTurn();
        var page = turn.Request(1, 2, 0, 6, false);
        check(page == 2 && turn.Active && turn.Leaves is [{ Base: 0, Progress: 0f }], "A forward turn does not start from the current spread.");
        var frames = 0;
        while (turn.Active && frames < 120) { turn.Update(Frame, false); frames++; }
        check(!turn.Active && turn.Target == 2 && Math.Abs(frames - UiMotion.PageTurn * 60) <= 2, "A turn does not land on the next spread in its duration.");

        page = turn.Request(-1, 2, 2, 6, false);
        check(page == 0 && turn.Leaves is [{ Base: 0, Progress: 1f }], "A backward turn does not start from the later spread.");
        for (var frame = 0; frame < 10; frame++) turn.Update(Frame, false);
        var partway = turn.Leaves[0].Progress;
        page = turn.Request(1, 2, 0, 6, false);
        check(page == 2 && turn.Leaves.Count == 1 && turn.Leaves[0].Progress == partway, "Reversing mid-turn jumps instead of turning back from where the leaf is.");
        turn.Update(Frame, false);
        check(turn.Leaves[0].Progress > partway, "A reversed turn does not move the other way.");

        // A second request the same way lifts the next leaf and leaves the first one turning.
        var first = turn.Leaves[0].Progress;
        page = turn.Request(1, 2, 2, 6, false);
        check(page == 4 && turn.Leaves.Count == 2 && turn.Leaves[0].Progress == first && turn.Leaves[1] is { Base: 2, Progress: 0f }, "A second forward request skips the turn in progress.");
        while (turn.Active) turn.Update(Frame, false);
        check(turn.Target == 4, "Two leaves in the air do not land two spreads on.");
        page = turn.Request(1, 2, 4, 6, false);
        check(page == 4 && !turn.Active, "Turning past the last spread starts a turn.");
        page = turn.Request(-1, 2, 0, 6, false);
        check(page == 0 && !turn.Active, "Turning before the first spread starts a turn.");

        // A burst of five clicks two frames apart: five leaves in the air, each behind the last, and a click
        // back sends only the newest one back.
        turn.Reset(0);
        for (var click = 0; click < 5; click++) { turn.Request(1, 2, turn.Target, 40, false); turn.Update(Frame, false); turn.Update(Frame, false); }
        check(turn.Target == 10 && turn.Leaves.Count == 5 && turn.Leaves.Select(leaf => leaf.Base).SequenceEqual([0, 2, 4, 6, 8]), "A burst of clicks does not put a leaf in the air for each.");
        check(turn.Leaves.Zip(turn.Leaves.Skip(1)).All(pair => pair.First.Progress > pair.Second.Progress), "Leaves in a burst do not follow one another.");
        page = turn.Request(-1, 2, turn.Target, 40, false);
        check(page == 8 && !turn.Leaves[^1].Forward && turn.Leaves.Take(4).All(leaf => leaf.Forward), "Turning back in a burst does not reverse just the newest leaf.");
        frames = 0;
        while (turn.Active && frames++ < 240) turn.Update(Frame, false);
        check(!turn.Active && turn.Target == 8, "A burst does not land where its clicks lead.");
        // A held key cannot pile up pages: past the limit the oldest leaf lands at once.
        for (var click = 0; click < 10; click++) turn.Request(1, 2, turn.Target, 60, false);
        check(turn.Leaves.Count == BookShellTurn.MaximumLeaves && turn.Target == 28, $"Ten clicks in one frame leave {turn.Leaves.Count} leaves and target page {turn.Target + 1}.");
        // The same backwards.
        turn.Reset(20);
        turn.Request(-1, 2, 20, 60, false);
        turn.Update(Frame, false);
        turn.Request(-1, 2, turn.Target, 60, false);
        check(turn.Target == 16 && turn.Leaves.Select(leaf => leaf.Base).SequenceEqual([16, 18]) && turn.Leaves[0].Progress == 1f, "A burst back does not lift the earlier leaves in order.");

        turn.Reset(3);
        page = turn.Request(1, 1, 3, 6, true);
        check(page == 4 && !turn.Active, "Reduced motion does not turn instantly.");
        turn.Request(1, 2, 0, 6, false);
        turn.Request(1, 1, 2, 6, false);
        check(turn.Step == 1 && turn.Leaves is [{ Base: 2 }], "Changing between spread and single pages mid-turn does not land the old turn first.");
        turn.Update(5f, false);
        check(turn.Leaves[0].Progress > 0f && turn.Leaves[0].Progress <= 0.21f, "A long frame skips most of a page turn.");
    }

    // On a single page, the page swings away to the left and the next one is revealed beneath.
    private static void SingleTurn(Action<bool, string> check, string? artifacts)
    {
        using var host = new HeadlessUi(new Vector2(452, 760), materials: true);
        var shell = new BookShell(host.Textures!, PassportIdentity.Sample);
        var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect bounds = default;
        void Step()
        {
            shell.Advance(Frame);
            host.BookFrame(shell.FitSize(new Vector2(420, 728), context), () => { bounds = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize()); shell.Draw(bounds, context); });
        }
        void Press(ImGuiKey key) { ImGui.GetIO().AddKeyEvent(key, true); Step(); ImGui.GetIO().AddKeyEvent(key, false); Step(); }
        shell.BeginSession();
        for (var frame = 0; frame < 40; frame++) Step();
        Press(ImGuiKey.Space);
        for (var frame = 0; frame < 70; frame++) Step();
        check(shell.Phase == BookPhase.Open, "The single-page passport does not open.");
        Press(ImGuiKey.PageDown);
        check(shell.IsTurning && shell.VisiblePage == 1, "Page Down does not turn a single page.");
        var frames = 0;
        while (shell.IsTurning && frames++ < 120)
        {
            Step();
            if (artifacts != null && frames == 9) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-turn-single.png"), host);
        }
        check(!shell.IsTurning && shell.VisiblePage == 1, "A single-page turn does not land on the next page.");
    }

    // A riffle staggers several leaves; reduced motion skips it.
    private static void Riffles(Action<bool, string> check)
    {
        check(BookShellRiffle.LeavesFor(1) == 5 && BookShellRiffle.LeavesFor(-2) == 7 && BookShellRiffle.LeavesFor(4) == 9, "Longer jumps do not riffle more pages.");
        var riffle = new BookShellRiffle();
        riffle.Start(-1, 5, BookSection.Studio, 2, false);
        check(riffle.Active && riffle.Direction == -1 && riffle.FromSection == BookSection.Studio && riffle.FromPage == 2, "A riffle does not remember where it started.");
        riffle.Update(UiMotion.RiffleStagger * 1.5f, false);
        check(riffle.Progress(0) > 0f && riffle.Progress(1) > 0f && riffle.Progress(2) == 0f, "Riffle leaves do not start one after another.");
        var frames = 0;
        while (riffle.Active && frames < 200) { riffle.Update(Frame, false); frames++; }
        check(Math.Abs(frames - (riffle.Seconds - UiMotion.LongestFrame) * 60) <= 2 && riffle.Progress(4) == 1f, "A riffle does not finish in its duration with every leaf landed.");
        riffle.Start(1, 7, BookSection.Passport, 0, true);
        check(!riffle.Active, "Reduced motion riffles.");
    }

    // The real shell: tabs riffle in book order, Settings are pages with Back, and the toggle saves.
    private static void RiffleShell(Action<bool, string> check, string? artifacts)
    {
        using var host = new HeadlessUi(new Vector2(1200, 900), materials: true);
        var settings = new FakeSettings();
        var shell = new BookShell(host.Textures!, PassportIdentity.Sample, settings: settings);
        var context = new UiContext(1, 18) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect bounds = default;
        void Step()
        {
            shell.Advance(Frame);
            host.BookFrame(shell.FitSize(new Vector2(1168, 868), context), () =>
            {
                bounds = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                shell.Draw(bounds, context);
                check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "A riffling passport scrolls its host.");
            });
        }
        void Click(Vector2 point)
        {
            ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Step();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Step();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Step();
        }
        BookShellLayout Layout() => BookShellLayout.Create(bounds, context, 0);
        shell.BeginSession();
        for (var frame = 0; frame < 40; frame++) Step();
        Click(BookShellLayout.OpenPoint(bounds));
        for (var frame = 0; frame < 70; frame++) Step();
        check(shell.Phase == BookPhase.Open, "The passport does not open for riffling.");

        // Passport -> Echoes: a later section, so pages flip right-to-left.
        var tab = Layout().Bookmark(2);
        Click(tab.Position + tab.Size / 2);
        check(shell.Section == BookSection.Echoes && shell.IsRiffling, "Choosing a later tab does not riffle to it.");
        var discover = Layout().Bookmark(1);
        Click(discover.Position + discover.Size / 2);
        check(shell.Section == BookSection.Echoes, "A tab chosen mid-riffle interrupts it.");
        var frames = 0;
        while (shell.IsRiffling && frames++ < 200)
        {
            Step();
            if (artifacts != null && frames is 4 or 12 or 20) SoftwarePreview.Save(Path.Combine(artifacts, $"passport-motion-riffle-{frames}.png"), host);
        }
        check(!shell.IsRiffling && shell.Section == BookSection.Echoes, "A riffle does not land on the chosen section.");

        // Back to an earlier section flips the other way; Settings and Back keep the reader's place.
        tab = Layout().Bookmark(0);
        Click(tab.Position + tab.Size / 2);
        check(shell.IsRiffling && shell.Section == BookSection.Passport, "Choosing an earlier tab does not riffle back.");
        for (var frame = 0; frame < 8; frame++) Step();
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-riffle-back.png"), host);
        while (shell.IsRiffling) Step();
        Click(Layout().TurnPoint(true, 0));
        while (shell.IsTurning) Step();
        var settingsMark = Layout().Settings(context);
        Click(settingsMark.Position + settingsMark.Size / 2);
        check(shell.Section == BookSection.Settings && shell.IsRiffling && shell.ReturnSection == BookSection.Passport, "The Settings mark does not riffle to the Settings pages.");
        while (shell.IsRiffling) Step();
        if (artifacts != null) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-settings.png"), host);

        // The printed checkbox saves the setting; it heads General's right page, the first control down it.
        check(shell.Category == SettingsCategory.General, "Settings do not open on General.");
        var options = Layout().RightPage;
        for (var y = 20f; !settings.ReducedMotion && shell.Section == BookSection.Settings && y < options.Size.Y; y += 12)
            Click(options.Position + new Vector2(30, y));
        check(settings.ReducedMotion && settings.Saves == 1, "The Reduce motion checkbox does not change and save the setting.");

        shell.GoBack();
        check(shell.Section == BookSection.Passport && shell.IsRiffling, "Back does not riffle to the page the reader left.");
        while (shell.IsRiffling) Step();
        check(shell.VisiblePage == 2, "Back does not return to the same passport spread.");

        // The host's settings button on a closed passport opens straight onto the Settings pages.
        var closed = new BookShell(host.Textures!, PassportIdentity.Sample, settings: settings) { ReducedMotion = true };
        closed.BeginSession();
        closed.ShowSettings();
        check(closed.IsBookOpen && closed.Section == BookSection.Settings && closed.ReturnSection == BookSection.Passport && !closed.IsRiffling, "Opening settings from a closed passport does not open onto them.");
    }

    private sealed class FakeSettings : IBookShellSettings
    {
        public bool ReducedMotion { get; set; }
        public int Saves { get; private set; }
        public bool Save() { Saves++; return true; }
    }

    private static int Until(BookShellMotion motion, Func<bool> done, Action<bool, string> check)
    {
        var frames = 0;
        while (!done() && frames < 600) { motion.Update(Frame); frames++; }
        check(done(), "A motion never reaches its next phase.");
        return frames;
    }
}
