using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Stamps;

// How stamps appear on paper. A design not yet collected is a faint, square proof; an issued stamp is full
// ink, turned however its owner pressed it. Both use the design's impression art, or the procedural motif
// when that art is missing.
internal static class StampArt
{
    private const float ProofInk = 0.38f;

    internal static void Proof(UiRect bounds, StampDesign design, IBookAssets? assets, UiContext context) => Draw(bounds, design, assets, context, ProofInk, 0f);

    // tint, when given, replaces the stamp's colours (a shadow is the stamp in one dark tone).
    internal static void Issued(UiRect bounds, StampInstance stamp, IBookAssets? assets, UiContext context, float rotation = 0f, float ink = 1f, Vector4? tint = null) =>
        Draw(bounds, stamp.Design, assets, context, ink, rotation, tint);

    // A natural first turn for a stamp being placed, up to five degrees either way, the same every time for
    // that stamp (a stable hash, unlike string.GetHashCode). The owner can turn it however they like.
    internal static float Tilt(string id)
    {
        var hash = 2166136261u;
        foreach (var character in id) hash = (hash ^ character) * 16777619u;
        return ((hash % 1001) / 1000f * 2f - 1f) * (5f * MathF.PI / 180f);
    }

    // The issue date as printed on a stamp's info page, in the offset it was collected in.
    internal static string Date(DateTimeOffset at) => at.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static void Draw(UiRect bounds, StampDesign design, IBookAssets? assets, UiContext context, float ink, float angle, Vector4? tint = null)
    {
        if (assets != null && BookSurface.Stamp(bounds, design.Artwork, assets, ink, angle, tint)) return;
        StampPreview.Draw(bounds, context, tint ?? UiTheme.Action with { W = ink });
    }
}
