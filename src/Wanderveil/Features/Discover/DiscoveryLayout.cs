using System;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Discover;

internal readonly record struct DiscoveryLayout(bool IsSplit, float ColumnWidth, float Padding, float Gap)
{
    internal static DiscoveryLayout Create(float width, UiContext context)
    {
        if (!float.IsFinite(width) || width <= 0f ||
            !float.IsFinite(context.Scale) || context.Scale <= 0f ||
            !float.IsFinite(context.FontSize) || context.FontSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width), "Layout dimensions and scale must be positive and finite.");

        var gap = UiMetrics.PageGap * context.Scale;
        var minimumWidth = 290f * MathF.Max(context.Scale, context.FontSize / 16f);
        var split = width >= (minimumWidth * 2f) + gap;
        var columnWidth = split ? (width - gap) / 2f : width;
        return new DiscoveryLayout(split, columnWidth, MathF.Min(UiMetrics.PagePadding * context.Scale, columnWidth / 8f), gap);
    }
}
