using System;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

internal readonly record struct PassportLayout(bool IsSpread, float PageWidth, float Padding, float Gap)
{
    internal static PassportLayout Create(float availableWidth, UiContext context)
    {
        if (!float.IsFinite(availableWidth) || availableWidth <= 0f ||
            !float.IsFinite(context.Scale) || context.Scale <= 0f ||
            !float.IsFinite(context.FontSize) || context.FontSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(availableWidth), "Layout dimensions and scale must be positive and finite.");

        var gap = UiMetrics.PageGap * context.Scale;
        var minimumPageWidth = 250f * MathF.Max(context.Scale, context.FontSize / 16f);
        var isSpread = availableWidth >= (2f * minimumPageWidth) + gap;
        var pageWidth = isSpread ? (availableWidth - gap) / 2f : availableWidth;
        var padding = MathF.Min(UiMetrics.PagePadding * context.Scale, pageWidth / 8f);
        return new PassportLayout(isSpread, pageWidth, padding, gap);
    }
}
