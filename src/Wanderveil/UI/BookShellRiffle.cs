using System;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Timing for a riffle: several leaves flipping in quick succession when the book jumps to another
// section, like an old cartoon flipbook settling on a page. Each leaf turns for UiMotion.RiffleLeaf,
// starting UiMotion.RiffleStagger after the previous one. Direction +1 flips right-to-left (to a later section), -1 the reverse.
// The book is already logically on its destination; this only describes the leaves in between.
internal sealed class BookShellRiffle
{
    private float elapsed;

    internal bool Active { get; private set; }
    internal int Direction { get; private set; } = 1;
    internal int Leaves { get; private set; }
    internal BookSection FromSection { get; private set; }
    internal int FromPage { get; private set; }
    internal float Seconds => UiMotion.RiffleLeaf + UiMotion.RiffleStagger * (Leaves - 1);

    // More leaves for a longer jump: five for a neighbouring section, up to nine.
    internal static int LeavesFor(int distance) => Math.Clamp(3 + 2 * Math.Abs(distance), 5, 9);

    internal void Start(int direction, int leaves, BookSection fromSection, int fromPage, bool reduced)
    {
        Direction = direction < 0 ? -1 : 1;
        Leaves = Math.Max(1, leaves);
        FromSection = fromSection;
        FromPage = fromPage;
        elapsed = 0f;
        Active = !reduced;
    }

    internal void Update(float seconds, bool reduced)
    {
        if (!Active) return;
        if (reduced) { Active = false; return; }
        elapsed += UiMotion.Frame(seconds);
        if (elapsed >= Seconds) Active = false;
    }

    internal void Reset() { Active = false; elapsed = 0f; }

    // 0 before leaf index starts to turn, 1 once it has landed.
    internal float Progress(int index) => !Active ? 1f : Math.Clamp((elapsed - index * UiMotion.RiffleStagger) / UiMotion.RiffleLeaf, 0f, 1f);
}
