using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Components;

internal enum BookAsset { Open, Cover, Portrait, Stamps }

internal readonly record struct BookImage(ImTextureID Texture, int Width, int Height);

// Presentation-only port. Host textures and the CPU preview use the same painter.
internal interface IBookAssets
{
    bool TryGet(BookAsset asset, out BookImage image);
}
