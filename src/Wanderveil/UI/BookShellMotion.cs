using System;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal enum BookPhase { Hidden, Entering, Closed, Opening, Open, Closing, Exiting }

// Timing for the passport's presentation sequence, independent of ImGui and drawing:
// Hidden -> Entering (slides up, closed) -> Closed -> Opening -> Open -> Closing -> Exiting -> Hidden.
// Two progress values carry it: Lift (0 hidden below the screen, 1 at rest) and Cover (0 closed, 1 open).
// Requests set a goal; Update moves toward it, always closing before sliding away and arriving before
// opening, so any request can interrupt and reverse a motion from where it is. Reduced motion jumps
// straight to the goal with the same results. Stepping aside (for the camera) is the one exception: the open book
// slides down without closing, and Return brings it back up open, as it was.
internal sealed class BookShellMotion
{
    private enum Goal { Hidden, Closed, Open, Aside }
    private Goal goal = Goal.Hidden;

    internal float Lift { get; private set; }
    internal float Cover { get; private set; }
    // Fades page content in after the cover settles open; 0 whenever the book is not fully open.
    internal float Reveal { get; private set; }
    // Turning reduced motion on settles any motion in progress at once.
    internal bool ReducedMotion
    {
        get => reduced;
        set { reduced = value; if (value) Finish(); }
    }
    private bool reduced;

    internal bool WantsOpen => goal is Goal.Open or Goal.Aside;
    internal bool SteppedAside => goal == Goal.Aside;
    internal bool IsVisible => goal != Goal.Hidden || Lift > 0f;
    // True while anything moves; the book takes no page or drag input then.
    internal bool IsMoving => Lift != Target(goal).Lift || Cover != Target(goal).Cover;

    internal BookPhase Phase
    {
        get
        {
            if (goal == Goal.Hidden && Lift <= 0f) return BookPhase.Hidden;
            if (goal == Goal.Aside) return Lift <= 0f ? BookPhase.Hidden : BookPhase.Open;
            if (goal == Goal.Open) return Cover >= 1f ? BookPhase.Open : Lift < 1f ? BookPhase.Entering : BookPhase.Opening;
            if (Cover > 0f) return BookPhase.Closing;
            if (goal == Goal.Hidden) return BookPhase.Exiting;
            return Lift < 1f ? BookPhase.Entering : BookPhase.Closed;
        }
    }

    // Summoning always starts from below the screen, closed.
    internal void Summon()
    {
        if (goal == Goal.Hidden && Lift <= 0f) { Cover = 0f; Reveal = 0f; }
        goal = Goal.Closed;
        Settle();
    }

    internal void Open() { if (goal != Goal.Hidden) goal = Goal.Open; Settle(); }
    internal void CloseToCover() { if (goal != Goal.Hidden) goal = Goal.Closed; Settle(); }
    internal void Dismiss() { goal = Goal.Hidden; Settle(); }
    // An open book slides down, staying open; Return brings it back up.
    internal void StepAside() { if (goal == Goal.Open) goal = Goal.Aside; Settle(); }
    internal void Return() { if (goal == Goal.Aside) goal = Goal.Open; Settle(); }

    // Ends every motion at once, for teardown or an immediate hide.
    internal void Finish() { var target = Target(goal); Lift = target.Lift; Cover = target.Cover; Reveal = goal is Goal.Open or Goal.Aside ? 1f : 0f; }

    internal void Update(float seconds)
    {
        var dt = UiMotion.Frame(seconds);
        if (ReducedMotion) { Finish(); return; }
        var target = Target(goal);
        var landed = Cover >= 1f;
        if (Cover > target.Cover) Cover = MathF.Max(target.Cover, Cover - dt / (goal == Goal.Hidden ? UiMotion.QuickCover : UiMotion.Cover));
        else if (Lift != target.Lift) Lift = Step(Lift, target.Lift, dt / UiMotion.Slide);
        else if (Cover < target.Cover) Cover = MathF.Min(target.Cover, Cover + dt / UiMotion.Cover);
        // Pages start to appear on the frame after the cover lands.
        Reveal = landed && Cover >= 1f && goal is Goal.Open or Goal.Aside ? MathF.Min(1f, Reveal + dt / UiMotion.Reveal) : 0f;
    }

    // Page content hides the moment the book starts to close; it reappears only after a landing.
    private void Settle()
    {
        if (goal is not (Goal.Open or Goal.Aside)) Reveal = 0f;
        if (ReducedMotion) Finish();
    }

    private static float Step(float value, float target, float amount) =>
        value < target ? MathF.Min(target, value + amount) : MathF.Max(target, value - amount);

    private static (float Lift, float Cover) Target(Goal goal) => goal switch
    {
        Goal.Hidden => (0f, 0f),
        Goal.Closed => (1f, 0f),
        Goal.Aside => (0f, 1f),
        _ => (1f, 1f),
    };
}
