using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

internal enum BookFace { Spread, Left, Right, Cover }

// The artwork contains only materials. Content bounds, text, and input remain independent.
internal static class BookSurface
{
    internal static float ContentWidth(float width, bool spread) => width * (spread ? 0.35f : 0.70f);

    // While a page turns, large page art is drawn in this many vertical strips so it bends with the
    // curled leaf (text glyphs are already small). Set only around a turning leaf's content; UI thread.
    internal static int Strips { get; set; } = 1;

    internal static void Image(ImDrawListPtr list, ImTextureID texture, Vector2 min, Vector2 max, Vector2? uv0 = null, Vector2? uv1 = null, uint color = 0xFFFFFFFF)
    {
        var a = uv0 ?? Vector2.Zero;
        var b = uv1 ?? Vector2.One;
        if (Strips <= 1) { list.AddImage(texture, min, max, a, b, color); return; }
        for (var index = 0; index < Strips; index++)
        {
            var t0 = index / (float)Strips;
            var t1 = (index + 1) / (float)Strips;
            list.AddImage(texture, new Vector2(min.X + (max.X - min.X) * t0, min.Y), new Vector2(min.X + (max.X - min.X) * t1, max.Y),
                new Vector2(a.X + (b.X - a.X) * t0, a.Y), new Vector2(a.X + (b.X - a.X) * t1, b.Y), color);
        }
    }

    internal static void Rect(ImDrawListPtr list, Vector2 min, Vector2 max, uint color)
    {
        if (Strips <= 1) { list.AddRectFilled(min, max, color); return; }
        for (var index = 0; index < Strips; index++)
            list.AddRectFilled(new Vector2(min.X + (max.X - min.X) * index / Strips, min.Y), new Vector2(min.X + (max.X - min.X) * (index + 1) / Strips, max.Y), color);
    }

    internal static void Line(ImDrawListPtr list, Vector2 from, Vector2 to, uint color, float thickness = 1f)
    {
        var segments = Math.Max(1, Strips);
        for (var index = 0; index < segments; index++)
            list.AddLine(Vector2.Lerp(from, to, index / (float)segments), Vector2.Lerp(from, to, (index + 1) / (float)segments), color, thickness);
    }

    // The paper of one page in the open-book plate (pages at x 66-768 and 768-1470, y 66-958 of 1536 x 1024).
    internal static UiRect Paper(UiRect book, BookFace face, bool rightOfSpread = false)
    {
        var natural = NaturalHeight(book.Size.X, face == BookFace.Spread);
        var (x0, x1) = face switch
        {
            BookFace.Spread => rightOfSpread ? (768f / 1536f, 1470f / 1536f) : (66f / 1536f, 768f / 1536f),
            BookFace.Right => (0f, 702f / 768f),
            _ => (66f / 768f, 1f),
        };
        return new UiRect(book.Position + new Vector2(book.Size.X * x0, natural * 66f / 1024f), new Vector2(book.Size.X * (x1 - x0), natural * 892f / 1024f));
    }

    // The opaque board of the open-book plate (x 40-1496, y 44-980 of 1536 x 1024); around it is only shadow.
    // A single page shows one half of the plate; tall pages stretch between the same top and bottom margins.
    internal static UiRect Board(UiRect book, BookFace face)
    {
        var natural = NaturalHeight(book.Size.X, face == BookFace.Spread);
        var (x0, x1) = face switch { BookFace.Spread => (40f / 1536f, 1496f / 1536f), BookFace.Right => (0f, 728f / 768f), _ => (40f / 768f, 1f) };
        var top = natural * 44f / 1024f;
        return new UiRect(book.Position + new Vector2(book.Size.X * x0, top), new Vector2(book.Size.X * (x1 - x0), book.Size.Y - 2 * top));
    }

    internal static float NaturalHeight(float width, bool spread) => width * (spread ? 2f / 3f : 4f / 3f);

    // The closed cover plate's board sits at 42,141 (940 x 1253) of 1024 x 1536 and is authored in
    // 420 x 560 design units (tools/passport-art). Returns that board's origin and pixels per design unit.
    internal static (Vector2 Origin, float Unit) CoverBoard(UiRect cover) =>
        (cover.Position + new Vector2(cover.Size.X * 42f / 1024f, cover.Size.Y * 141f / 1536f), cover.Size.X * 940f / 1024f / 420f);

    internal static float Height(float width, bool spread, float contentHeight)
    {
        if (!float.IsFinite(width) || width < 1f || !float.IsFinite(contentHeight) || contentHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var natural = NaturalHeight(width, spread);
        return MathF.Max(natural, contentHeight + (natural * 0.29f));
    }

    internal static UiRect Content(UiRect book, BookFace face)
    {
        var spread = face == BookFace.Spread;
        var natural = NaturalHeight(book.Size.X, spread);
        var left = book.Position + new Vector2(book.Size.X * (spread ? 0.11f : face == BookFace.Right ? 0.10f : 0.22f), natural * 0.13f);
        return new UiRect(left, new Vector2(ContentWidth(book.Size.X, spread), book.Size.Y - (natural * 0.29f)));
    }

    internal static UiRect RightContent(UiRect book)
    {
        var left = Content(book, BookFace.Spread);
        return left with { Position = left.Position + new Vector2(book.Size.X * 0.44f, 0f) };
    }

    internal static bool Draw(UiRect bounds, BookFace face, IBookAssets? assets, UiContext context)
    {
        var asset = face == BookFace.Cover ? BookAsset.Cover : BookAsset.Open;
        // A single page shows half of the open-book plate, so the whole plate spans twice its width.
        var span = face is BookFace.Left or BookFace.Right ? bounds.Size.X * 2 : bounds.Size.X;
        if (assets == null || !assets.TryGet(asset, out var image, span))
        {
            Fallback(ImGui.GetWindowDrawList(), bounds, face, context);
            return false;
        }

        var drawList = ImGui.GetWindowDrawList();
        var u0 = face == BookFace.Right ? 0.5f : 0f;
        var u1 = face == BookFace.Left ? 0.5f : 1f;
        if (face == BookFace.Cover)
        {
            drawList.AddImage(image.Texture, bounds.Position, bounds.Max);
            return true;
        }

        // Preserve top/bottom construction; repeat only a quiet central paper/binding strip on tall pages.
        var natural = NaturalHeight(bounds.Size.X, face == BookFace.Spread);
        var cap = natural * 0.4f;
        var strip = natural * 0.2f;
        Image(0f, cap, 0f, 0.4f);
        var end = bounds.Size.Y - cap;
        for (var y = cap; y < end - 0.01f; y += strip)
        {
            var height = MathF.Min(strip, end - y);
            Image(y, height, 0.4f, 0.4f + (0.2f * height / strip));
        }
        Image(end, cap, 0.6f, 1f);
        return true;

        void Image(float y, float height, float v0, float v1) => drawList.AddImage(image.Texture,
            bounds.Position + new Vector2(0f, y), bounds.Position + new Vector2(bounds.Size.X, y + height),
            new Vector2(u0, v0), new Vector2(u1, v1));
    }

    // While plates load (or if one cannot), a drawn book in the same places: edition cloth, paper, and spine,
    // so the cover still reads as a cover and pages keep their paper. Geometry follows the plates above.
    // clothColour and foilColour draw a given edition's cover; otherwise the current edition's.
    internal static void Fallback(ImDrawListPtr list, UiRect bounds, BookFace face, UiContext context, Vector4? clothColour = null, Vector4? foilColour = null)
    {
        var cloth = ImGui.GetColorU32(clothColour ?? UiTheme.Edition);
        var foil = ImGui.GetColorU32((foilColour ?? UiTheme.Foil) with { W = 0.6f });
        var shadow = ImGui.GetColorU32(UiTheme.Shadow);
        var thin = MathF.Max(1, context.Scale);
        if (face == BookFace.Cover)
        {
            var (origin, unit) = CoverBoard(bounds);
            var max = origin + new Vector2(420, 560) * unit;
            list.AddRectFilled(origin + new Vector2(4, 6) * unit, max + new Vector2(4, 6) * unit, shadow, 10 * unit);
            list.AddRectFilled(origin, max, cloth, 10 * unit);
            list.AddRectFilled(origin, new Vector2(origin.X + 18 * unit, max.Y), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.18f)), 10 * unit, ImDrawFlags.RoundCornersLeft);
            list.AddRect(origin + new Vector2(30, 16) * unit, max - new Vector2(16) * unit, foil, 4 * unit, ImDrawFlags.None, thin);
            return;
        }
        var spread = face == BookFace.Spread;
        var natural = NaturalHeight(bounds.Size.X, spread);
        var width = spread ? 1536f : 768f;
        var board = Board(bounds, face);
        var boardMin = board.Position;
        var boardMax = board.Max;
        list.AddRectFilled(boardMin + new Vector2(3, 5) * context.Scale, boardMax + new Vector2(3, 5) * context.Scale, shadow, 8 * context.Scale);
        list.AddRectFilled(boardMin, boardMax, cloth, 8 * context.Scale);
        var paperTop = bounds.Position.Y + natural * 66f / 1024f;
        var paperBottom = bounds.Max.Y - natural * 66f / 1024f;
        var (paperLeft, paperRight) = face switch { BookFace.Spread => (66f, 1470f), BookFace.Right => (0f, 702f), _ => (66f, 768f) };
        var paperMin = new Vector2(bounds.Position.X + bounds.Size.X * paperLeft / width, paperTop);
        var paperMax = new Vector2(bounds.Position.X + bounds.Size.X * paperRight / width, paperBottom);
        list.AddRectFilled(paperMin, paperMax, ImGui.GetColorU32(UiTheme.Paper), 2 * context.Scale);
        // The gutter: a soft shadow on both sides of the spine, or along the bound edge of a single page.
        var dark = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.16f));
        var clear = ImGui.GetColorU32(Vector4.Zero);
        var reach = (paperMax.X - paperMin.X) * (spread ? 0.06f : 0.1f);
        var spine = face switch { BookFace.Spread => (paperMin.X + paperMax.X) / 2, BookFace.Right => paperMin.X, _ => paperMax.X };
        if (face != BookFace.Right) list.AddRectFilledMultiColor(new Vector2(spine - reach, paperMin.Y), new Vector2(spine, paperMax.Y), clear, dark, dark, clear);
        if (face != BookFace.Left) list.AddRectFilledMultiColor(new Vector2(spine, paperMin.Y), new Vector2(spine + reach, paperMax.Y), dark, clear, clear, dark);
        if (spread) list.AddLine(new Vector2(spine, paperMin.Y), new Vector2(spine, paperMax.Y), ImGui.GetColorU32(UiTheme.Ink with { W = 0.3f }), thin);
    }

    internal static void Portrait(UiRect bounds, IBookAssets assets, UiContext context)
    {
        if (!assets.TryGet(BookAsset.Portrait, out var image, MathF.Min(bounds.Size.X, bounds.Size.Y * 0.75f)))
            return;
        var aspect = image.Width / (float)image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        var min = bounds.Position + ((bounds.Size - size) / 2f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        var mount = new Vector2(4f * context.Scale);
        Rect(drawList, min - mount, max + mount, ImGui.GetColorU32(UiTheme.Paper));
        Image(drawList, image.Texture, min, max);
    }

    // The aspect-preserving placement Illustration draws, without drawing it.
    internal static UiRect Fit(UiRect bounds, BookImage image)
    {
        var aspect = image.Width / (float)image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        return new UiRect(bounds.Position + (bounds.Size - size) / 2, size);
    }

    internal static UiRect Illustration(UiRect bounds, BookImage image)
    {
        var aspect = image.Width / (float)image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        var fitted = new UiRect(bounds.Position + (bounds.Size - size) / 2, size);
        Image(ImGui.GetWindowDrawList(), image.Texture, fitted.Position, fitted.Max);
        return fitted;
    }

    // Draws one impression from the atlas, turned by angle (radians) about its centre; false when it has
    // no such impression or the art is missing.
    internal static bool Stamp(UiRect bounds, int index, IBookAssets assets, float alpha = 1f, float angle = 0f, Vector4? tint = null)
    {
        if ((uint)index > 2 || !assets.TryGet(BookAsset.Stamps, out var image, MathF.Min(bounds.Size.X, bounds.Size.Y) * 3.2f))
            return false;
        // Authored atlas regions: trim the middle gutter to exclude a neighboring impression's stray ink.
        var region = index == 1 ? new Vector2(0.34f, 0.65f) : new Vector2(index / 3f, (index + 1) / 3f);
        var aspect = (region.Y - region.X) * image.Width / image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        var color = ImGui.GetColorU32(tint ?? new Vector4(1, 1, 1, alpha));
        if (angle != 0f)
        {
            // A turned impression shrinks slightly so its corners stay inside the place.
            var center = bounds.Position + bounds.Size / 2f;
            var half = size * 0.47f;
            var (sin, cos) = MathF.SinCos(angle);
            Vector2 Corner(float x, float y) => center + new Vector2(x * half.X * cos - y * half.Y * sin, x * half.X * sin + y * half.Y * cos);
            ImGui.GetWindowDrawList().AddImageQuad(image.Texture, Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1),
                new Vector2(region.X, 0f), new Vector2(region.Y, 0f), new Vector2(region.Y, 1f), new Vector2(region.X, 1f), color);
            return true;
        }
        var min = bounds.Position + ((bounds.Size - size) / 2f);
        Image(ImGui.GetWindowDrawList(), image.Texture, min, min + size, new Vector2(region.X, 0f), new Vector2(region.Y, 1f), color);
        return true;
    }
}
