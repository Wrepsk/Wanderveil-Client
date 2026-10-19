using System;
using System.Collections.Generic;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Timing for page turns, independent of ImGui and drawing. Several leaves can be in the air at once, so quick
// clicks are never lost or skipped: each one lifts another leaf right behind those already moving, like a
// short riffle.
// A leaf's Base is the earlier of the two reading positions it lies between: in a spread, the left page of
// the earlier spread (the leaf is pages Base+1 front / Base+2 back); on a single page, the page that swings
// away (Base+1 lies beneath). Progress 0 shows the earlier position, 1 the later one. Leaves in the air are
// always neighbours, lowest Base first. A forward request sends back the lowest leaf still heading back, or
// else lifts a new leaf after the last; a backward request does the same the other way. So turning back
// mid-turn reverses the newest leaf from where it is. Reduced motion turns instantly with the same result.
internal sealed class BookShellTurn
{
    // More leaves than this in the air and the oldest lands at once, so a held key cannot pile up pages.
    internal const int MaximumLeaves = 6;
    private readonly List<Leaf> leaves = [];
    private int target;

    internal sealed class Leaf(int @base, float progress, float goal)
    {
        internal int Base { get; } = @base;
        internal float Progress { get; set; } = progress;
        internal float Goal { get; set; } = goal;
        internal bool Forward => Goal > 0.5f;
        internal bool Landed => Progress == Goal;
    }

    internal bool Active => leaves.Count > 0;
    internal int Step { get; private set; } = 2;
    // The leaves in the air, lowest Base first.
    internal IReadOnlyList<Leaf> Leaves => leaves;
    // The lowest and highest Base in the air: the pages beneath them all are First (left) and Last's next.
    internal int First => leaves.Count > 0 ? leaves[0].Base : target;
    internal int Last => leaves.Count > 0 ? leaves[^1].Base : target;
    // The page the reader ends on when every turn completes (or now, when idle).
    internal int Target
    {
        get
        {
            if (leaves.Count == 0) return target;
            var forward = 0;
            foreach (var leaf in leaves) if (leaf.Forward) forward++;
            return First + forward * Step;
        }
    }

    // direction +1 forward, -1 back. Returns the page the reader will be on.
    internal int Request(int direction, int step, int visible, int pageCount, bool reduced)
    {
        if (Active && step != Step) Finish();
        if (!Active) target = visible;
        if (direction > 0)
        {
            // The lowest leaf heading back turns forward again; otherwise a new leaf lifts after the last.
            foreach (var leaf in leaves) if (!leaf.Forward) { leaf.Goal = 1f; return Settle(reduced); }
            var next = Active ? Last + step : visible;
            if (next + step >= pageCount) return Target;
            if (leaves.Count == MaximumLeaves) Land(leaves[0]);
            Step = step;
            leaves.Add(new Leaf(next, 0f, 1f));
        }
        else
        {
            for (var index = leaves.Count - 1; index >= 0; index--)
                if (leaves[index].Forward) { leaves[index].Goal = 0f; return Settle(reduced); }
            var next = (Active ? First : visible) - step;
            if (next < 0) return Target;
            if (leaves.Count == MaximumLeaves) Land(leaves[^1]);
            Step = step;
            leaves.Insert(0, new Leaf(next, 1f, 0f));
        }
        return Settle(reduced);
    }

    internal void Update(float seconds, bool reduced)
    {
        if (!Active) return;
        if (reduced) { Finish(); return; }
        var dt = UiMotion.Frame(seconds) / UiMotion.PageTurn;
        foreach (var leaf in leaves)
            leaf.Progress = leaf.Goal > leaf.Progress ? MathF.Min(leaf.Goal, leaf.Progress + dt) : MathF.Max(leaf.Goal, leaf.Progress - dt);
        Prune();
    }

    // Lands every turn at its goal immediately (also used when the layout changes mid-turn).
    internal void Finish()
    {
        if (!Active) return;
        target = Target;
        leaves.Clear();
    }

    internal void Reset(int visible)
    {
        leaves.Clear();
        target = visible;
    }

    private void Land(Leaf leaf)
    {
        leaf.Progress = leaf.Goal;
        Prune();
    }

    // Landed leaves leave the air from the ends: one landed forward at the low end becomes the page beneath on
    // the left, one landed back at the high end the page beneath on the right. A leaf landed between others
    // lies flat until its neighbours land.
    private void Prune()
    {
        var reading = Target;
        while (leaves.Count > 0 && leaves[0].Landed && leaves[0].Forward) leaves.RemoveAt(0);
        while (leaves.Count > 0 && leaves[^1].Landed && !leaves[^1].Forward) leaves.RemoveAt(leaves.Count - 1);
        if (leaves.Count == 0) target = reading;
    }

    private int Settle(bool reduced)
    {
        if (reduced) Finish();
        return Target;
    }
}
