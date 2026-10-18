using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Components;

internal enum BookAsset { Open, Cover, Portrait, Stamps, EditionCover, NationBanner, NationMap, EditionCrest }

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
}
