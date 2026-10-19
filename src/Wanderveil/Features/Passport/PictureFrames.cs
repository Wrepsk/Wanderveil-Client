using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

// How the holder's own picture is set on the page, so it never sits on the paper as a bare pasted rectangle.
// Every style keeps within the frame's outer rectangle; Inner is where the picture itself shows (and where it
// is dragged). All of it is plain draw-list geometry, so it turns with a page leaf like the rest of the page.
internal static class PictureFrames
{
    // A slightly lighter paper for a printed mount, so a white-edged picture never meets the ink line directly.
    private static readonly Vector4 Mount = new(0.925f, 0.88f, 0.785f, 1f);
    // Black album corners, warm rather than pure black.
    private static readonly Vector4 CornerInk = new(0.13f, 0.11f, 0.10f, 1f);

    internal static UiRect Inner(UiRect outer, PictureFrame style, float scale) => style switch
    {
        PictureFrame.Printed => Inset(outer, 9 * scale),
        PictureFrame.Embossed => Inset(outer, 10 * scale),
        PictureFrame.Corners => Inset(outer, 3 * scale),
        _ => outer,
    };

    internal static void Draw(ImDrawListPtr list, UiRect outer, PictureFrame style, BookImage image, Vector2 uv0, Vector2 uv1, float scale)
    {
        var inner = Inner(outer, style, scale);
        switch (style)
        {
            case PictureFrame.Printed:
                Shadow(list, outer, scale);
                BookSurface.Rect(list, outer.Position, outer.Max, ImGui.GetColorU32(Mount));
                var line = MathF.Max(1, 1.25f * scale);
                var keyline = Inset(outer, 2 * scale);
                list.AddRect(keyline.Position, keyline.Max, ImGui.GetColorU32(UiTheme.Ink with { W = 0.85f }), 0, ImDrawFlags.None, line);
                BookSurface.Image(list, image.Texture, inner.Position, inner.Max, uv0, uv1);
                // A hairline where the picture meets its mount, as the edge of a print.
                list.AddRect(inner.Position, inner.Max, ImGui.GetColorU32(UiTheme.Ink with { W = 0.18f }), 0, ImDrawFlags.None, 1);
                break;
            case PictureFrame.Corners:
                Shadow(list, inner, scale);
                BookSurface.Image(list, image.Texture, inner.Position, inner.Max, uv0, uv1);
                Corners(list, inner, scale);
                break;
            case PictureFrame.Embossed:
                Emboss(list, outer, inner, scale);
                BookSurface.Image(list, image.Texture, inner.Position, inner.Max, uv0, uv1);
                break;
            case PictureFrame.Deckled:
                Deckle(list, outer, image, uv0, uv1, scale);
                break;
            default:
                BookSurface.Image(list, image.Texture, inner.Position, inner.Max, uv0, uv1);
                break;
        }
    }

    private static UiRect Inset(UiRect rect, float by) => new(rect.Position + new Vector2(by), Vector2.Max(Vector2.One, rect.Size - new Vector2(by * 2)));

    // A soft shadow below and right of a picture laid on the page: a few widening, fading layers.
    private static void Shadow(ImDrawListPtr list, UiRect rect, float scale)
    {
        for (var layer = 3; layer >= 1; layer--)
        {
            var grow = layer * 1.5f * scale;
            var offset = new Vector2(0.8f, 1.6f) * layer * scale;
            list.AddRectFilled(rect.Position + offset - new Vector2(grow * 0.5f), rect.Max + offset + new Vector2(grow * 0.5f), ImGui.GetColorU32(new Vector4(0.12f, 0.08f, 0.04f, 0.07f)), grow);
        }
    }

    // Four triangular pockets over the picture's corners, each reaching a little past the picture's edges.
    private static void Corners(ImDrawListPtr list, UiRect rect, float scale)
    {
        var leg = MathF.Min(rect.Size.X, rect.Size.Y) * 0.17f;
        var over = 2.5f * scale;
        var fill = ImGui.GetColorU32(CornerInk);
        var shade = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.18f));
        var edge = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.22f));
        foreach (var (corner, across, down) in new[]
        {
            (rect.Position, Vector2.UnitX, Vector2.UnitY),
            (new Vector2(rect.Max.X, rect.Position.Y), -Vector2.UnitX, Vector2.UnitY),
            (new Vector2(rect.Position.X, rect.Max.Y), Vector2.UnitX, -Vector2.UnitY),
            (rect.Max, -Vector2.UnitX, -Vector2.UnitY),
        })
        {
            var tip = corner - (across + down) * over;
            var a = tip + across * (leg + over);
            var b = tip + down * (leg + over);
            list.AddTriangleFilled(tip + new Vector2(0.6f, 1.2f) * scale, a + new Vector2(0.6f, 1.2f) * scale, b + new Vector2(0.6f, 1.2f) * scale, shade);
            list.AddTriangleFilled(tip, a, b, fill);
            // The pocket's open edge catches the light.
            list.AddLine(a, b, edge, MathF.Max(1, scale));
        }
    }

    // A mount pressed into the paper: a raised rim (light above and left, shadow below and right) around a
    // recess the picture sits in (shadowed above and left).
    private static void Emboss(ImDrawListPtr list, UiRect outer, UiRect inner, float scale)
    {
        var light = ImGui.GetColorU32(new Vector4(1f, 0.98f, 0.92f, 0.55f));
        var dark = ImGui.GetColorU32(new Vector4(0.25f, 0.17f, 0.08f, 0.32f));
        var soft = ImGui.GetColorU32(new Vector4(0.25f, 0.17f, 0.08f, 0.14f));
        var width = MathF.Max(1, 1.5f * scale);
        BookSurface.Rect(list, outer.Position, outer.Max, ImGui.GetColorU32(UiTheme.PaperInset with { W = 0.35f }));
        Bevel(list, outer, light, dark, width);
        Bevel(list, Inset(outer, 2.5f * scale), soft, soft, width * 0.7f);
        Bevel(list, Inset(inner, -1.5f * scale), dark, light, width);
    }

    // Top and left edges in one colour, bottom and right in another.
    private static void Bevel(ImDrawListPtr list, UiRect rect, uint topLeft, uint bottomRight, float width)
    {
        var topRight = new Vector2(rect.Max.X, rect.Position.Y);
        var bottomLeft = new Vector2(rect.Position.X, rect.Max.Y);
        list.AddLine(bottomLeft, rect.Position, topLeft, width);
        list.AddLine(rect.Position, topRight, topLeft, width);
        list.AddLine(topRight, rect.Max, bottomRight, width);
        list.AddLine(rect.Max, bottomLeft, bottomRight, width);
    }

    // The picture with a rough, torn edge: a band around it whose outer line wanders inward by a fibrous,
    // repeatable amount. The band is drawn as small textured quads so it bends with a turning leaf.
    private static void Deckle(ImDrawListPtr list, UiRect outer, BookImage image, Vector2 uv0, Vector2 uv1, float scale)
    {
        var depth = MathF.Max(3, 7 * scale);
        var core = Inset(outer, depth);
        var step = MathF.Max(2, 3 * scale);
        var perimeter = 2 * (outer.Size.X + outer.Size.Y);
        var count = Math.Clamp((int)(perimeter / step), 16, 1200);
        var inner = new Vector2[count];
        var edge = new Vector2[count];
        for (var index = 0; index < count; index++)
        {
            var t = index / (float)count;
            inner[index] = Along(core, t);
            var rim = Along(outer, t);
            // Torn paper: a slow wander with finer fibres on top, always leaving some of the band.
            var tear = 0.55f + 0.22f * MathF.Sin(t * 37f) + 0.13f * MathF.Sin(t * 113f + 1.3f) + 0.1f * Hash(index);
            edge[index] = Vector2.Lerp(rim, inner[index], Math.Clamp(1 - tear, 0.05f, 0.85f));
        }
        // A faint shadow of the torn shape, then the picture's middle, then the torn band around it.
        var shadow = ImGui.GetColorU32(new Vector4(0.12f, 0.08f, 0.04f, 0.16f));
        var drop = new Vector2(0.8f, 1.6f) * scale;
        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            list.AddQuadFilled(inner[index] + drop, edge[index] + drop, edge[next] + drop, inner[next] + drop, shadow);
        }
        BookSurface.Image(list, image.Texture, core.Position, core.Max, Uv(core.Position), Uv(core.Max));
        var white = 0xFFFFFFFFu;
        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            list.AddImageQuad(image.Texture, inner[index], edge[index], edge[next], inner[next], Uv(inner[index]), Uv(edge[index]), Uv(edge[next]), Uv(inner[next]), white);
        }
        // Torn fibres catch the light, and smooth the band's stepped outline.
        var fibre = ImGui.GetColorU32(new Vector4(1f, 0.97f, 0.9f, 0.45f));
        for (var index = 0; index < count; index++) list.AddLine(edge[index], edge[(index + 1) % count], fibre, MathF.Max(1, 0.9f * scale));

        Vector2 Uv(Vector2 point) => uv0 + (point - outer.Position) / outer.Size * (uv1 - uv0);
    }

    // A point t (0 to 1) of the way around a rectangle, clockwise from its top left corner.
    private static Vector2 Along(UiRect rect, float t)
    {
        var w = rect.Size.X;
        var h = rect.Size.Y;
        var d = t * 2 * (w + h);
        if (d < w) return rect.Position + new Vector2(d, 0);
        d -= w;
        if (d < h) return new Vector2(rect.Max.X, rect.Position.Y + d);
        d -= h;
        if (d < w) return new Vector2(rect.Max.X - d, rect.Max.Y);
        d -= w;
        return new Vector2(rect.Position.X, rect.Max.Y - d);
    }

    // A repeatable value from -1 to 1 for each point of the edge.
    private static float Hash(int index)
    {
        var hash = (uint)index * 2654435761u;
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return (hash % 2001) / 1000f - 1f;
    }
}
