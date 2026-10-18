using System;

namespace Wanderveil.UI;

internal enum BookPhase { Hidden, Entering, Closed, Opening, Open, Closing, Exiting }

// Timing for the passport's presentation sequence, independent of ImGui and drawing:
// Hidden -> Entering (slides up, closed) -> Closed -> Opening -> Open -> Closing -> Exiting -> Hidden.
// Two progress values carry it: Lift (0 hidden below the screen, 1 at rest) and Cover (0 closed, 1 open).
// Requests set a goal; Update moves toward it, always closing before sliding away and arriving before
// opening, so any request can interrupt and reverse a motion from where it is. Reduced motion jumps
// straight to the goal with the same results.
internal sealed class BookShellMotion
{
    internal const float SlideSeconds = 0.42f;
    internal const float CoverSeconds = 0.6f;
    // Dismissing closes the cover quickly so putting the passport away never feels like waiting.
    internal const float DismissCoverSeconds = 0.25f;
    internal const float RevealSeconds = 0.18f;

    private enum Goal { Hidden, Closed, Open }
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

    internal bool WantsOpen => goal == Goal.Open;
    internal bool IsVisible => goal != Goal.Hidden || Lift > 0f;
    // True while anything moves; the book takes no page or drag input then.
    internal bool IsMoving => Lift != Target(goal).Lift || Cover != Target(goal).Cover;

    internal BookPhase Phase
    {
        get
        {
            if (goal == Goal.Hidden && Lift <= 0f) return BookPhase.Hidden;
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

    // Ends every motion at once, for teardown or an immediate hide.
    internal void Finish() { var target = Target(goal); Lift = target.Lift; Cover = target.Cover; Reveal = goal == Goal.Open ? 1f : 0f; }

    internal void Update(float seconds)
    {
        // A long hitch must not skip a sequence's ordering; at most one frame of a slow display moves.
        var dt = Math.Clamp(float.IsFinite(seconds) ? seconds : 0f, 0f, 0.1f);
        if (ReducedMotion) { Finish(); return; }
        var target = Target(goal);
        var landed = Cover >= 1f;
        if (Cover > target.Cover) Cover = MathF.Max(target.Cover, Cover - dt / (goal == Goal.Hidden ? DismissCoverSeconds : CoverSeconds));
        else if (Lift != target.Lift) Lift = Step(Lift, target.Lift, dt / SlideSeconds);
        else if (Cover < target.Cover) Cover = MathF.Min(target.Cover, Cover + dt / CoverSeconds);
        // Pages start to appear on the frame after the cover lands.
        Reveal = landed && Cover >= 1f && goal == Goal.Open ? MathF.Min(1f, Reveal + dt / RevealSeconds) : 0f;
    }

    // Smooth acceleration and deceleration for both slides and the cover swing.
    internal static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    // Page content hides the moment the book starts to close; it reappears only after a landing.
    private void Settle()
    {
        if (goal != Goal.Open) Reveal = 0f;
        if (ReducedMotion) Finish();
    }

    private static float Step(float value, float target, float amount) =>
        value < target ? MathF.Min(target, value + amount) : MathF.Max(target, value - amount);

    private static (float Lift, float Cover) Target(Goal goal) => goal switch
    {
        Goal.Hidden => (0f, 0f),
        Goal.Closed => (1f, 0f),
        _ => (1f, 1f),
    };
}
