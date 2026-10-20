using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Components;

// Symbols: the holder's symbol set, white masks in a grid (PassportSymbol), tinted where they are drawn.
internal enum BookAsset { Open, Cover, Portrait, Stamps, EditionCover, NationBanner, NationMap, EditionCrest, Symbols }

internal readonly record struct BookImage(ImTextureID Texture, int Width, int Height);

internal static class BookImageLevels
{
    // Size copies are ordered largest first. The smallest copy still at least as wide as drawn keeps
    // sampling within 2x (textures have no mipmaps), so edges stay smooth; too-large draws use the full copy.
    internal static int Pick(System.ReadOnlySpan<int> widths, float drawWidth)
    {
        for (var index = widths.Length - 1; index >= 0; index--)
            if (widths[index] >= drawWidth) return index;
        return 0;
    }
}

// Presentation-only port. Host textures and the CPU preview use the same painter.
internal interface IBookAssets
{
    // drawWidth is the on-screen width the whole image will span; 0 accepts any loaded size.
    bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f);

    // An edition's own plate (open book, cover, banner, map, crest), for drawing editions other than the one
    // the book is in, as the edition chooser does. Shared plates ignore the edition. Without per-edition plates
    // every edition draws the same.
    bool TryGet(BookAsset asset, string edition, out BookImage image, float drawWidth = 0f) => TryGet(asset, out image, drawWidth);

    // The same plate recoloured (EditionRecolour). Until a recoloured copy is ready, or without one, the printed
    // plate is drawn.
    bool TryGet(BookAsset asset, string edition, PlateTint tint, out BookImage image, float drawWidth = 0f) => TryGet(asset, edition, out image, drawWidth);

    internal static bool IsEditionPlate(BookAsset asset) => asset is BookAsset.Open or BookAsset.EditionCover or BookAsset.NationBanner or BookAsset.NationMap or BookAsset.EditionCrest;
}

// The book's view of its plates: the current edition's own, chosen anew each draw.
internal sealed class EditionAssets(IBookAssets inner, System.Func<string> edition) : IBookAssets
{
    // The colours the plates are drawn in this frame; the book sets them with the theme, so plates and text agree.
    internal PlateTint? Tint { get; set; }

    // The current edition's plates, recoloured when the holder chose other colours.
    public bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f)
    {
        if (Tint is { Changes: true } colours && IBookAssets.IsEditionPlate(asset)) return inner.TryGet(asset, edition(), colours, out image, drawWidth);
        return inner.TryGet(asset, edition(), out image, drawWidth);
    }

    public bool TryGet(BookAsset asset, string id, out BookImage image, float drawWidth = 0f) => inner.TryGet(asset, id, out image, drawWidth);
}
