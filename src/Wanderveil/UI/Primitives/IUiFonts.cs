using Dalamud.Bindings.ImGui;

namespace Wanderveil.UI.Primitives;

// Resolves a role font to a copy rasterized at the drawn pixel size, so text is never stretched.
internal interface IUiFonts
{
    ImFontPtr Sized(ImFontPtr font, float pixels);
}
