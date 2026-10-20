using System;

namespace Wanderveil.UI.Primitives;

// Motion tokens: every duration and the one easing curve the book uses, in seconds. Keeping them together
// keeps movement consistent, and Reduced motion means the same thing everywhere: each of these becomes instant.
internal static class UiMotion
{
    // The passport sliding up from below the screen, or back down.
    internal const float Slide = 0.42f;
    // The cover swinging open on its spine, or closed to read the cover again.
    internal const float Cover = 0.6f;
    // The cover closing on dismissal: putting the passport away should never feel like waiting.
    internal const float QuickCover = 0.25f;
    // Page content fading in once the cover has landed.
    internal const float Reveal = 0.18f;
    internal const float PageTurn = 0.5f;
    // One leaf of a riffle between sections, and the delay before the next leaf lifts.
    internal const float RiffleLeaf = 0.34f;
    internal const float RiffleStagger = 0.07f;
    // The selected tab's ribbon unrolling after the pages appear, and rolling up before a tab change.
    internal const float RibbonUnroll = 0.22f;
    internal const float RibbonRoll = 0.12f;
    // The tab bar swapping between section tabs and Settings category tabs: the old tabs sink, the new ones rise.
    internal const float TabSwap = 0.36f;
    // The most one frame may advance motion: a long hitch must not skip a sequence's ordering.
    internal const float LongestFrame = 0.1f;

    // A frame's elapsed time made safe for motion: finite, not negative, and at most LongestFrame.
    internal static float Frame(float seconds) => Math.Clamp(float.IsFinite(seconds) ? seconds : 0f, 0f, LongestFrame);

    // Smooth acceleration and deceleration (cubic in-out) for slides, swings, turns, and the ribbon.
    internal static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
