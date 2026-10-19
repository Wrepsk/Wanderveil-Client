using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// The gallery and the installed client share the same cover/page composition.
internal static class GridaniaTargets
{
    internal readonly record struct Reading(float LeftHeight, float RightHeight, float PageHeight, float Scroll);

    internal static Reading Draw(string target, UiRect window, PreviewTextures assets, UiContext context, ImFontPtr signatureFont,
        bool single = false, bool information = false, bool longText = false)
    {
        var profile = longText ? PassportIdentity.Sample with { Name = "Ari of the Many Winding Roads and Forgotten Horizons", Bio = string.Concat(Enumerable.Repeat("A keeper of quiet paths and small stories. ", 12)) } : PassportIdentity.Sample;
        var document = new PassportDocumentView(target == "cover-plum" ? new VariantAssets(assets) : assets, profile);
        context = context with { SignatureFont = signatureFont };
        if (target.StartsWith("cover", StringComparison.Ordinal)) { document.DrawCover(window, context); return default; }
        var natural = BookSurface.NaturalHeight(window.Size.X, !single);
        var book = new UiRect(window.Position - new Vector2(0, natural * 0.075f), new Vector2(window.Size.X, natural));
        var face = single ? information ? BookFace.Right : BookFace.Left : BookFace.Spread;
        BookSurface.Draw(book, face, assets, context);
        var left = BookSurface.Content(book, face);
        var start = target == "nation" ? 0 : target == "identity" ? 2 : 4;
        var scroll = 0f;
        var leftHeight = Page(left, start + (information ? 1 : 0));
        var rightHeight = single ? 0 : Page(BookSurface.RightContent(book), start + 1);
        return new Reading(leftHeight, rightHeight, left.Size.Y, scroll);

        float Page(UiRect bounds, int index)
        {
            ImGui.SetCursorScreenPos(bounds.Position);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 5 * context.Scale);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, UiTheme.Gold);
            var visible = ImGui.BeginChild(PassportDocumentView.SamplePages[index].Id, bounds.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings);
            try
            {
                var height = visible ? document.Compose(PassportDocumentView.SamplePages[index], context) : 0;
                scroll = MathF.Max(scroll, ImGui.GetScrollMaxY());
                return height;
            }
            finally { ImGui.EndChild(); ImGui.PopStyleColor(2); ImGui.PopStyleVar(); }
        }
    }

    private sealed class VariantAssets(PreviewTextures assets) : IBookAssets
    {
        public bool TryGet(BookAsset asset, out BookImage image, float drawWidth = 0f)
        {
            if (asset == BookAsset.EditionCover) { image = assets.Study("cover-plum-cutout"); return true; }
            return assets.TryGet(asset, out image);
        }
    }
}
