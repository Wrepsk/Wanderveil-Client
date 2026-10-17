using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Discover;

internal static class DiscoveryCard
{
    internal static float Measure(DiscoveryPreview preview, float width, float padding, UiContext context)
    {
        var flow = new PageFlow(Vector2.Zero, width - (padding * 2f), context, false);
        Compose(ref flow, preview, context);
        return flow.Height + (padding * 2f);
    }

    internal static bool Draw(DiscoveryPreview preview, float width, float padding, float height, UiContext context)
    {
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), new Vector2(width, height));
        var opened = UiCard.Draw(preview.Id, bounds.Size, context);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(bounds.Position, bounds.Max, true);
        try
        {
            var flow = new PageFlow(bounds.Position + new Vector2(padding), width - (padding * 2f), context, true);
            Compose(ref flow, preview, context);
        }
        finally
        {
            drawList.PopClipRect();
        }
        return opened;
    }

    private static void Compose(ref PageFlow flow, DiscoveryPreview preview, UiContext context)
    {
        flow.Text(preview.Kind, TextRole.Caption, true);
        flow.Gap();
        flow.Text(preview.Title, TextRole.Title);
        flow.Gap();
        flow.Text(preview.Creator, TextRole.Caption, true);
        flow.Rule();
        flow.Text(preview.Location, TextRole.Heading);
        flow.Gap();
        flow.Text(preview.Teaser);
        flow.Rule();
        var stamp = flow.Reserve(context.FontSize * 4f);
        if (flow.IsDrawing)
            StampPreview.Draw(stamp, context);
        flow.Gap();
        flow.Text(preview.StampName, TextRole.Caption, true);
        flow.Gap();
        flow.Text("NOT COLLECTED / DEMO PREVIEW", TextRole.Caption, true);
        flow.Rule();
        flow.Text("Open discovery >", TextRole.Heading);
    }
}
