using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Stamps;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Discover;

internal static class DiscoveryCard
{
    internal static float Measure(DiscoveryPreview preview, StampInstance? held, float width, float padding, UiContext context)
    {
        var flow = new PageFlow(Vector2.Zero, width - (padding * 2f), context, false);
        Compose(ref flow, preview, held, null, context);
        return flow.Height + (padding * 2f);
    }

    internal static bool Draw(DiscoveryPreview preview, StampInstance? held, IBookAssets? assets, float width, float padding, float height, UiContext context)
    {
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), new Vector2(width, height));
        var opened = UiCard.Draw(preview.Id, bounds.Size, context);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(bounds.Position, bounds.Max, true);
        try
        {
            var flow = new PageFlow(bounds.Position + new Vector2(padding), width - (padding * 2f), context, true);
            Compose(ref flow, preview, held, assets, context);
        }
        finally
        {
            drawList.PopClipRect();
        }
        return opened;
    }

    // The stamp line a card and a detail page print: whether it is held, can be collected, or neither.
    internal static string Status(DiscoveryPreview preview, StampInstance? held) =>
        held != null ? $"COLLECTED {StampArt.Date(held.CollectedAt)} / LOCAL DEMO"
        : preview.Stamp == null ? "NO STAMP WITH THIS DISCOVERY"
        : preview.CollectOnReading ? "NOT COLLECTED / LOCAL DEMO"
        : "COLLECT ON SITE / NOT AVAILABLE YET";

    private static void Compose(ref PageFlow flow, DiscoveryPreview preview, StampInstance? held, IBookAssets? assets, UiContext context)
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
        if (preview.Stamp is { } design)
        {
            var stamp = flow.Reserve(context.FontSize * 4f);
            if (flow.IsDrawing)
            {
                if (held != null) StampArt.Issued(stamp, held, assets, context);
                else StampArt.Proof(stamp, design, assets, context);
            }
            flow.Gap();
            flow.Text(design.Name, TextRole.Caption, true);
            flow.Gap();
        }
        flow.Text(Status(preview, held), TextRole.Caption, true);
        flow.Rule();
        flow.Text("Open discovery >", TextRole.Heading);
    }
}
