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

    internal static float NaturalHeight(float width, bool spread) => width * (spread ? 2f / 3f : 4f / 3f);

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
        if (assets == null || !assets.TryGet(asset, out var image))
        {
            PageSurface.Draw(ImGui.GetWindowDrawList(), bounds, context);
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

    internal static void Portrait(UiRect bounds, IBookAssets assets, UiContext context)
    {
        if (!assets.TryGet(BookAsset.Portrait, out var image))
            return;
        var aspect = image.Width / (float)image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        var min = bounds.Position + ((bounds.Size - size) / 2f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        var mount = new Vector2(4f * context.Scale);
        drawList.AddRectFilled(min - mount, max + mount, ImGui.GetColorU32(UiTheme.Paper));
        drawList.AddImage(image.Texture, min, max);
    }

    internal static UiRect Illustration(UiRect bounds, BookImage image)
    {
        var aspect = image.Width / (float)image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        var fitted = new UiRect(bounds.Position + (bounds.Size - size) / 2, size);
        ImGui.GetWindowDrawList().AddImage(image.Texture, fitted.Position, fitted.Max);
        return fitted;
    }

    internal static void Stamp(UiRect bounds, int index, IBookAssets assets)
    {
        if ((uint)index > 2 || !assets.TryGet(BookAsset.Stamps, out var image))
            return;
        // Authored atlas regions: trim the middle gutter to exclude a neighboring impression's stray ink.
        var region = index == 1 ? new Vector2(0.34f, 0.65f) : new Vector2(index / 3f, (index + 1) / 3f);
        var aspect = (region.Y - region.X) * image.Width / image.Height;
        var size = new Vector2(MathF.Min(bounds.Size.X, bounds.Size.Y * aspect), MathF.Min(bounds.Size.Y, bounds.Size.X / aspect));
        var min = bounds.Position + ((bounds.Size - size) / 2f);
        ImGui.GetWindowDrawList().AddImage(image.Texture, min, min + size,
            new Vector2(region.X, 0f), new Vector2(region.Y, 1f));
    }
}
