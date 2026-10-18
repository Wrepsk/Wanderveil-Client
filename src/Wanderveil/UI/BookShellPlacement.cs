using System;
using System.Numerics;

namespace Wanderveil.UI;

// Where the passport window rests and where it is drawn while sliding, in screen pixels.
internal static class BookShellPlacement
{
    // previous is null only on the session's first summon, which centres the book in the work area.
    // Afterwards the rest stays where it was dragged or dismissed; a size change between closed and
    // open keeps the bottom centre fixed, and the book is kept inside the work area.
    internal static Vector2 Rest(Vector2? previous, Vector2 lastSize, Vector2 size, Vector2 workPos, Vector2 workSize)
    {
        var lowest = workPos + Vector2.Max(Vector2.Zero, workSize - size);
        if (previous is not { } position) return workPos + Vector2.Max(Vector2.Zero, (workSize - size) / 2);
        position += new Vector2((lastSize.X - size.X) / 2, lastSize.Y - size.Y);
        return Vector2.Clamp(position, workPos, lowest);
    }

    // lift 1 is at rest; lift 0 is just below the work area, so the book slides up from (and down to) off-screen.
    internal static Vector2 Slide(Vector2 rest, float lift, Vector2 workPos, Vector2 workSize, float margin)
    {
        var travel = workPos.Y + workSize.Y + margin - rest.Y;
        return rest + new Vector2(0, (1 - Math.Clamp(lift, 0f, 1f)) * travel);
    }
}
