using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Drawing;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// How the holder's drawing and writing look on a stamp page. A pencil stroke is a band of quads as wide as it was
// pressed, joined by averaged normals and tapering at its ends, sampling the paper's grain by its place on the page so
// the paper shows through the same way under every stroke (a fainter, wider band softens its edge). Writing is set in
// the handwriting face. All of it is geometry drawn flat in the page, so a turning leaf bends it with the page.
internal static class InkArt
{
    private const float Core = 0.88f;
    private const float Halo = 0.22f;
    private const int Taper = 6;

    // A page-width unit point on the page.
    internal static Vector2 OnPage(UiRect page, float x, float y) => page.Position + new Vector2(x, y) * page.Size.X;

    // The page-width unit point under a screen point.
    internal static Vector2 FromPage(UiRect page, Vector2 point) => (point - page.Position) / MathF.Max(1f, page.Size.X);

    internal static void Stroke(ImDrawListPtr list, IBookAssets assets, UiRect page, InkStroke stroke, float alpha = 1f) =>
        Stroke(list, assets, page, stroke.Points, stroke.Width, stroke.Colour.Vector, alpha);

    internal static void Stroke(ImDrawListPtr list, IBookAssets assets, UiRect page, IReadOnlyList<InkPoint> points, float width, Vector4 colour, float alpha = 1f)
    {
        if (points.Count < 2) return;
        var half = width * page.Size.X / 2;
        var core = ImGui.GetColorU32(colour with { W = Core * alpha });
        var halo = ImGui.GetColorU32(colour with { W = Halo * alpha });
        if (!assets.TryGet(BookAsset.Grain, out var grain))
        {
            // Without the grain, a plain line of the same weight.
            for (var index = 1; index < points.Count; index++)
                list.AddLine(OnPage(page, points[index - 1].X, points[index - 1].Y), OnPage(page, points[index].X, points[index].Y), core, MathF.Max(1f, half * 2 * points[index].Pressure));
            return;
        }
        // The grain spans the page's width and the same distance down (texture coordinates stay within 0 to 1 on the
        // page, so nothing relies on how the game wraps them).
        var depth = grain.Height / (float)grain.Width;
        Vector2 Uv(Vector2 unit) => new(Math.Clamp(unit.X, 0f, 1f), Math.Clamp(unit.Y / depth, 0f, 1f));
        foreach (var (scale, tint) in new[] { (1.7f, halo), (1f, core) })
        {
            var previous = Edge(0, scale);
            for (var index = 1; index < points.Count; index++)
            {
                var next = Edge(index, scale);
                list.AddImageQuad(grain.Texture, OnPage(page, previous.Left.X, previous.Left.Y), OnPage(page, next.Left.X, next.Left.Y), OnPage(page, next.Right.X, next.Right.Y), OnPage(page, previous.Right.X, previous.Right.Y),
                    Uv(previous.Left), Uv(next.Left), Uv(next.Right), Uv(previous.Right), tint);
                previous = next;
            }
        }

        // The stroke's two edges at a point, in page-width units: pressure sets the width, and the first and last few
        // points taper to a fine tip as a pencil lifts.
        (Vector2 Left, Vector2 Right) Edge(int index, float scale)
        {
            var point = points[index];
            var before = points[Math.Max(0, index - 1)];
            var after = points[Math.Min(points.Count - 1, index + 1)];
            var direction = new Vector2(after.X - before.X, after.Y - before.Y);
            var normal = direction.LengthSquared() < 1e-12f ? Vector2.UnitY : Vector2.Normalize(new Vector2(-direction.Y, direction.X));
            var taper = MathF.Min(1f, MathF.Min(index + 1, points.Count - index) / (float)Taper);
            var reach = width / 2 * (0.35f + 0.65f * point.Pressure) * (0.4f + 0.6f * taper) * scale;
            // At least half a pixel each side, so a fine line never vanishes.
            reach = MathF.Max(reach, 0.5f / MathF.Max(1f, page.Size.X));
            var centre = new Vector2(point.X, point.Y);
            return (centre + normal * reach, centre - normal * reach);
        }
    }

    // A line of writing: its letters' height is Size page-widths, in the handwriting face.
    internal static void Text(ImDrawListPtr list, UiRect page, PageText text, UiContext context, float alpha = 1f)
    {
        var type = Type(page, text.Size, context);
        UiTypography.Draw(list, OnPage(page, text.X, text.Y), text.Text, TextRole.Heading, type, page.Size.X * (1f - text.X), text.Colour.Vector with { W = alpha }, false);
    }

    // Where a line of writing lies on its page, in page-width units.
    internal static (Vector2 Min, Vector2 Max) Box(UiRect page, PageText text, UiContext context)
    {
        var size = UiTypography.Measure(text.Text, TextRole.Heading, Type(page, text.Size, context), float.MaxValue, false) / MathF.Max(1f, page.Size.X);
        return (new Vector2(text.X, text.Y), new Vector2(text.X, text.Y) + size);
    }

    // The handwriting face at a height of size page-widths (the heading role is 1.15 times the font size).
    internal static UiContext Type(UiRect page, float size, UiContext context) =>
        context with { HeadingFont = context.SignatureFont, FontSize = MathF.Max(6f, size * page.Size.X / 1.15f) };
}
