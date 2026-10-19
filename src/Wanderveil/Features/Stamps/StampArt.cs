using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Stamps;

// How stamps appear on paper. A design not yet collected is a faint proof; an issued stamp is full ink.
// Both use the design's impression art, or the procedural motif when that art is missing.
internal static class StampArt
{
    private const float ProofInk = 0.38f;

    internal static void Proof(UiRect bounds, StampDesign design, IBookAssets? assets, UiContext context) => Draw(bounds, design, assets, context, ProofInk);

    internal static void Issued(UiRect bounds, StampInstance stamp, IBookAssets? assets, UiContext context) => Draw(bounds, stamp.Design, assets, context, 1f);

    // A place on the page still waiting for a stamp: a faint dashed ring.
    internal static void Empty(UiRect bounds, UiContext context)
    {
        var list = ImGui.GetWindowDrawList();
        var center = bounds.Position + bounds.Size / 2;
        var radius = MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.42f;
        var color = ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.45f });
        const int Dashes = 28;
        for (var index = 0; index < Dashes; index++)
        {
            var from = index * MathF.Tau / Dashes;
            list.PathArcTo(center, radius, from, from + MathF.Tau / Dashes * 0.55f, 4);
            list.PathStroke(color, ImDrawFlags.None, MathF.Max(1f, 1.5f * context.Scale));
        }
    }

    // The issue date as printed under a stamp, in the offset it was collected in.
    internal static string Date(DateTimeOffset at) => at.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static void Draw(UiRect bounds, StampDesign design, IBookAssets? assets, UiContext context, float ink)
    {
        if (assets != null && BookSurface.Stamp(bounds, design.Artwork, assets, ink)) return;
        StampPreview.Draw(bounds, context, UiTheme.Action with { W = ink });
    }
}
