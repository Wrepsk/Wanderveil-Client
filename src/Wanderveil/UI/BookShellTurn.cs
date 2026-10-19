using System;

namespace Wanderveil.UI;

// Timing for one page turn, independent of ImGui and drawing.
// Base is the earlier of the two reading positions: in a spread, the left page of the earlier spread
// (the leaf is pages Base+1 front / Base+2 back); on a single page, the page that swings away
// (Base+1 lies beneath). Progress 0 shows the earlier position, 1 the later one.
// A request the same way while turning finishes the current turn and starts the next; the other way
// reverses the current turn from where it is. Reduced motion turns instantly with the same result.
internal sealed class BookShellTurn
{
    internal const float TurnSeconds = 0.5f;

    private float goal;

    internal bool Active { get; private set; }
    internal int Base { get; private set; }
    internal int Step { get; private set; } = 2;
    internal float Progress { get; private set; }
    // True while heading to the later reading position.
    internal bool Forward => goal > 0.5f;
    // The page the reader ends on when this turn completes (or now, when idle).
    internal int Target => Active ? Base + (goal > 0.5f ? Step : 0) : target;
    private int target;

    // direction +1 forward, -1 back. Returns the page the reader will be on.
    internal int Request(int direction, int step, int visible, int pageCount, bool reduced)
    {
        if (Active && step != Step) Finish();
        if (Active)
        {
            var forward = goal > 0.5f;
            if (direction > 0 == forward)
            {
                // Same way again: land this turn, then start the next from where it lands.
                Finish();
                visible = target;
            }
            else
            {
                goal = forward ? 0f : 1f;
                return Settle(reduced);
            }
        }
        var next = visible + (direction > 0 ? step : -step);
        if (next < 0 || next >= pageCount) { target = visible; return visible; }
        Step = step;
        Base = Math.Min(visible, next);
        Progress = direction > 0 ? 0f : 1f;
        goal = direction > 0 ? 1f : 0f;
        Active = true;
        return Settle(reduced);
    }

    internal void Update(float seconds, bool reduced)
    {
        if (!Active) return;
        if (reduced) { Finish(); return; }
        var dt = Math.Clamp(float.IsFinite(seconds) ? seconds : 0f, 0f, 0.1f) / TurnSeconds;
        Progress = goal > Progress ? MathF.Min(goal, Progress + dt) : MathF.Max(goal, Progress - dt);
        if (Progress == goal) Finish();
    }

    // Lands the turn at its goal immediately (also used when the layout changes mid-turn).
    internal void Finish()
    {
        if (!Active) return;
        target = Base + (goal > 0.5f ? Step : 0);
        Progress = goal;
        Active = false;
    }

    internal void Reset(int visible)
    {
        Active = false;
        Progress = 0f;
        target = visible;
    }

    private int Settle(bool reduced)
    {
        if (reduced) Finish();
        return Target;
    }
}
