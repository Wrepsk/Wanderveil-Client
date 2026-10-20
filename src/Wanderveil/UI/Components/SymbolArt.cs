using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// How the holder's symbols are drawn: one cell of the symbol plate, tinted a single colour, turned about its
// centre, and with a leaf, carried on the turning cover or page.
internal static class SymbolArt
{
    // How far a symbol's art reaches from its centre, as a share of its width (the plate's cells leave a margin).
    internal const float Reach = 0.47f;

    // Where a placed symbol sits on its surface: its centre and its width. The centre is kept far enough from
    // every edge that the whole symbol stays on its surface, as a stamp does on its page.
    internal static (Vector2 Center, float Size) Place(PlacedSymbol symbol, UiRect surface)
    {
        var size = symbol.Size * surface.Size.X;
        var reach = new Vector2(MathF.Min(size * Reach, surface.Size.X / 2), MathF.Min(size * Reach, surface.Size.Y / 2));
        return (surface.Position + reach + new Vector2(symbol.X, symbol.Y) * (surface.Size - reach * 2), size);
    }

    // The placement (0 to 1) that puts a symbol of this size's centre at a screen point, kept on the surface.
    internal static Vector2 At(Vector2 point, float size, UiRect surface)
    {
        var width = size * surface.Size.X;
        var reach = new Vector2(MathF.Min(width * Reach, surface.Size.X / 2), MathF.Min(width * Reach, surface.Size.Y / 2));
        return Vector2.Clamp((point - surface.Position - reach) / Vector2.Max(Vector2.One, surface.Size - reach * 2), Vector2.Zero, Vector2.One);
    }

    // Without the plate, a plain ring stands in for the symbol so it can still be placed and moved.
    internal static void Draw(ImDrawListPtr list, IBookAssets assets, PassportSymbol symbol, Vector2 center, float size, float rotation, Vector4 colour, LeafProjection? leaf = null)
    {
        var tint = leaf is { } lit ? LeafProjection.Tint(colour, lit.Light) : ImGui.GetColorU32(colour);
        if (!assets.TryGet(BookAsset.Symbols, out var image, size * PassportSymbol.Columns))
        {
            if (leaf == null) list.AddCircle(center, size * 0.4f, tint, 32, MathF.Max(1f, size * 0.05f));
            return;
        }
        var column = symbol.Cell % PassportSymbol.Columns;
        var row = symbol.Cell / PassportSymbol.Columns;
        var uv0 = new Vector2(column / (float)PassportSymbol.Columns, row / (float)PassportSymbol.Rows);
        var uv1 = new Vector2((column + 1) / (float)PassportSymbol.Columns, (row + 1) / (float)PassportSymbol.Rows);
        var half = size / 2;
        var (sin, cos) = MathF.SinCos(rotation);
        Vector2 Corner(float x, float y)
        {
            var point = center + new Vector2(x * half * cos - y * half * sin, x * half * sin + y * half * cos);
            return leaf is { } turning ? turning.Map(point) : point;
        }
        list.AddImageQuad(image.Texture, Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1),
            uv0, new Vector2(uv1.X, uv0.Y), uv1, new Vector2(uv0.X, uv1.Y), tint);
    }

    // The symbols placed on one surface, in order, skipping the one at except (carried in hand), and drawing the one in
    // shown as it is being changed (turned, resized, or recoloured) before the change is kept. colour is the passport's,
    // for symbols without their own.
    internal static void DrawSurface(ImDrawListPtr list, IBookAssets assets, SymbolLayer layer, string surface, UiRect area, Vector4 colour, LeafProjection? leaf = null, int except = -1, Vector2? shadow = null, Vector4 shadowColour = default, (int Index, PlacedSymbol Symbol)? shown = null)
    {
        for (var index = 0; index < layer.Items.Length; index++)
        {
            var placed = shown is { } changing && changing.Index == index ? changing.Symbol : layer.Items[index];
            if (index == except || placed.Surface != surface || PassportSymbol.Find(placed.Symbol) is not { } symbol) continue;
            var (center, size) = Place(placed, area);
            if (shadow is { } offset) Draw(list, assets, symbol, center + offset, size, placed.Rotation, shadowColour, leaf);
            Draw(list, assets, symbol, center, size, placed.Rotation, placed.Colour?.Vector ?? colour, leaf);
        }
    }
}
