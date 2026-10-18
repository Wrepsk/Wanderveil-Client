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
        Shell(check, artifacts);
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
        check(Math.Abs(frames - BookShellMotion.SlideSeconds * 60) <= 2, "Arrival does not take its slide duration.");
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

        check(BookShellMotion.Ease(0) == 0 && BookShellMotion.Ease(1) == 1 && Math.Abs(BookShellMotion.Ease(0.5f) - 0.5f) < 1e-5f, "Easing does not span 0 to 1 symmetrically.");
        var previous = 0f;
        for (var step = 1; step <= 100; step++)
        {
            var eased = BookShellMotion.Ease(step / 100f);
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
        var shell = new BookShell(host.Textures!, PassportPreview.Sample);
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

        shell.RequestDismiss();
        var closing = false;
        var hidden = false;
        for (var frame = 0; frame < 200 && !hidden; frame++)
        {
            Step();
            closing |= shell.Phase == BookPhase.Closing;
            if (artifacts != null && frame == 14) SoftwarePreview.Save(Path.Combine(artifacts, "passport-motion-closing.png"), host);
            check(!(shell.Lift < 1f && BookShellMotion.Ease(1) > 0 && shell.Phase == BookPhase.Closing), "The shell slides while closing.");
            hidden = intent.Close;
        }
        check(closing && hidden && shell.Phase == BookPhase.Hidden, "Dismissing the open shell does not close, slide away, and ask the host to hide.");
    }

    private static int Until(BookShellMotion motion, Func<bool> done, Action<bool, string> check)
    {
        var frames = 0;
        while (!done() && frames < 600) { motion.Update(Frame); frames++; }
        check(done(), "A motion never reaches its next phase.");
        return frames;
    }
}
