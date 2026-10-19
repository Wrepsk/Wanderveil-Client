using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Stamps;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Discover;

// Browsing and reading discoveries, and collecting a discovery's stamp into the passport's collection.
internal sealed class DiscoverView(IReadOnlyList<DiscoveryPreview> previews, StampCollection? stamps = null, IBookAssets? assets = null, IStampStorage? storage = null)
{
    internal DiscoveryPreview? SelectedDiscovery { get; private set; }
    internal StampCollection Stamps { get; } = stamps ?? new StampCollection();
    private float browseScroll;
    private float? requestedScroll;
    private bool focusDetails;
    private bool focusPassportLink;
    private StampInstance? passportRequested;

    // The stamp the reader asked to see in their passport, once; otherwise null.
    internal StampInstance? TakePassportRequest()
    {
        var requested = passportRequested;
        passportRequested = null;
        return requested;
    }

    internal void Draw(UiContext context, bool onPaper = false)
    {
        if (requestedScroll is { } scroll)
        {
            ImGui.SetScrollY(scroll);
            requestedScroll = null;
        }

        if (SelectedDiscovery is { } selected)
            DrawDetails(selected, context, onPaper);
        else
            DrawBrowse(context, onPaper);
    }

    private void DrawBrowse(UiContext context, bool onPaper)
    {
        var ink = onPaper ? UiTheme.Ink : UiTheme.Paper;
        UiTypography.Text("A little wonder awaits", TextRole.Title, context, ink);
        UiTypography.Text("DISCOVER / CURATED DEMO", TextRole.Caption, context, onPaper ? UiTheme.MutedInk : UiTheme.Veil);
        UiTypography.Text("A small journal of imagined places. This preview does not use your location.", TextRole.Body, context, ink);
        ImGui.Dummy(new Vector2(0f, UiMetrics.Medium * context.Scale));
        if (previews.Count == 0)
        {
            UiTypography.Text("No demo discoveries", TextRole.Heading, context, ink);
            UiTypography.Text("The sample journal is empty. Nothing has been fetched or published.", TextRole.Body, context, ink);
            return;
        }

        var width = MathF.Max(1f, UiLayout.ContentWidth - (4f * context.Scale));
        var layout = DiscoveryLayout.Create(width, context);
        DiscoveryPreview? opened = null;
        for (var index = 0; index < previews.Count; index += layout.IsSplit ? 2 : 1)
        {
            var left = previews[index];
            var right = layout.IsSplit && index + 1 < previews.Count ? previews[index + 1] : null;
            var height = DiscoveryCard.Measure(left, Stamps.For(left.Id), layout.ColumnWidth, layout.Padding, context);
            if (right != null)
                height = MathF.Max(height, DiscoveryCard.Measure(right, Stamps.For(right.Id), layout.ColumnWidth, layout.Padding, context));
            var origin = ImGui.GetCursorScreenPos();
            if (DiscoveryCard.Draw(left, Stamps.For(left.Id), assets, layout.ColumnWidth, layout.Padding, height, context))
                opened = left;
            if (right != null)
            {
                ImGui.SetCursorScreenPos(origin + new Vector2(layout.ColumnWidth + layout.Gap, 0f));
                if (DiscoveryCard.Draw(right, Stamps.For(right.Id), assets, layout.ColumnWidth, layout.Padding, height, context))
                    opened = right;
            }
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, height + layout.Gap));
        }
        if (opened != null)
        {
            browseScroll = ImGui.GetScrollY();
            SelectedDiscovery = opened;
            // Apply the top target before the detail frame establishes keyboard focus.
            ImGui.SetScrollY(0f);
            requestedScroll = 0f;
            focusDetails = true;
        }
    }

    private void DrawDetails(DiscoveryPreview preview, UiContext context, bool onPaper)
    {
        if (focusDetails)
        {
            ImGui.SetKeyboardFocusHere();
            focusDetails = false;
        }
        if (UiButton.Draw("Back", context))
        {
            SelectedDiscovery = null;
            requestedScroll = browseScroll;
        }
        UiTypography.Text(preview.Title, TextRole.Title, context, onPaper ? UiTheme.Ink : UiTheme.Paper);
        UiTypography.Text("DISCOVER / DEMO DETAILS", TextRole.Caption, context, onPaper ? UiTheme.MutedInk : UiTheme.Veil);
        ImGui.Dummy(new Vector2(0f, UiMetrics.Medium * context.Scale));
        var width = MathF.Max(1f, UiLayout.ContentWidth - (4f * context.Scale));
        var layout = DiscoveryLayout.Create(width, context);
        var contentWidth = layout.ColumnWidth - (layout.Padding * 2f);
        var held = Stamps.For(preview.Id);
        var leftMeasure = new PageFlow(Vector2.Zero, contentWidth, context, false);
        var rightMeasure = new PageFlow(Vector2.Zero, contentWidth, context, false);
        ComposePostcard(ref leftMeasure, preview, contentWidth);
        ComposeReward(ref rightMeasure, preview, held, null, context);
        var leftHeight = leftMeasure.Height + (layout.Padding * 2f);
        var rightHeight = rightMeasure.Height + (layout.Padding * 2f);
        var height = MathF.Max(leftHeight, rightHeight);
        var origin = ImGui.GetCursorScreenPos();
        var left = new UiRect(origin, new Vector2(layout.ColumnWidth, layout.IsSplit ? height : leftHeight));
        var right = new UiRect(origin + (layout.IsSplit
            ? new Vector2(layout.ColumnWidth + layout.Gap, 0f)
            : new Vector2(0f, leftHeight + layout.Gap)), new Vector2(layout.ColumnWidth, layout.IsSplit ? height : rightHeight));
        DrawPage(left, layout.Padding, preview, held, context, true);
        DrawPage(right, layout.Padding, preview, held, context, false);
        ImGui.Dummy(new Vector2(width, (layout.IsSplit ? height : leftHeight + rightHeight + layout.Gap) + (4f * context.Scale)));
        ImGui.Dummy(new Vector2(0f, UiMetrics.Medium * context.Scale));
        DrawCollect(preview, held, context, onPaper ? UiTheme.Ink : UiTheme.Paper);
    }

    // The collect action under a discovery: collect, already collected, or not collectible here.
    private void DrawCollect(DiscoveryPreview preview, StampInstance? held, UiContext context, Vector4 ink)
    {
        if (preview.Stamp == null) return;
        if (held != null)
        {
            UiTypography.Text($"Collected on {StampArt.Date(held.CollectedAt)}. {StampStorageText.Kept(storage)}", TextRole.Body, context, ink);
            ImGui.Dummy(new Vector2(0f, UiMetrics.Small * context.Scale));
            if (storage?.State == StampSaveState.Failed && BookControl.Link("Try saving again", context)) storage.Retry();
            if (focusPassportLink)
            {
                ImGui.SetKeyboardFocusHere();
                focusPassportLink = false;
            }
            if (BookControl.Link("See it in your passport »", context)) passportRequested = held;
            return;
        }
        if (!preview.CollectOnReading)
        {
            ImGui.BeginDisabled();
            try { UiButton.Draw("Collect stamp", context); }
            finally { ImGui.EndDisabled(); }
            UiTypography.Text("This stamp is collected at the place itself, which needs location awareness Wanderveil does not have yet.", TextRole.Body, context, ink);
            return;
        }
        // The action disappears once used, so keyboard focus moves on to the passport link.
        if (UiButton.Draw("Collect demo stamp", context) && Stamps.Collect(preview.Id, preview.Stamp).Outcome == CollectOutcome.Collected)
            focusPassportLink = true;
        UiTypography.Text("Adds one local demo stamp to your passport. It is not an official achievement.", TextRole.Body, context, ink);
    }

    private void DrawPage(UiRect bounds, float padding, DiscoveryPreview preview, StampInstance? held, UiContext context, bool postcard)
    {
        var drawList = ImGui.GetWindowDrawList();
        PageSurface.Draw(drawList, bounds, context);
        drawList.PushClipRect(bounds.Position, bounds.Max, true);
        try
        {
            var contentWidth = bounds.Size.X - (padding * 2f);
            var flow = new PageFlow(bounds.Position + new Vector2(padding), contentWidth, context, true);
            if (postcard)
                ComposePostcard(ref flow, preview, contentWidth);
            else
                ComposeReward(ref flow, preview, held, assets, context);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private static void ComposePostcard(ref PageFlow flow, DiscoveryPreview preview, float width)
    {
        flow.Text(preview.HasIllustration ? "ORIGINAL DEMO ILLUSTRATION" : "NO POSTCARD IMAGE", TextRole.Caption, true);
        flow.Gap();
        if (preview.HasIllustration)
        {
            var art = flow.Reserve(width * 0.6f);
            if (flow.IsDrawing)
                PostcardArtwork.Draw(art);
        }
        else
        {
            flow.Text("A place for a memory", TextRole.Title);
            flow.Gap();
            flow.Text("No image accompanies this sample. The story is still here.", TextRole.Body, true);
        }
        flow.Rule();
        flow.Text(preview.Caption);
        flow.Rule();
        flow.Text(preview.Creator, TextRole.Caption, true);
    }

    private static void ComposeReward(ref PageFlow flow, DiscoveryPreview preview, StampInstance? held, IBookAssets? assets, UiContext context)
    {
        flow.Text(preview.Kind, TextRole.Caption, true);
        flow.Gap();
        flow.Text("A note on the map", TextRole.Title);
        flow.Gap();
        flow.Text(preview.Location, TextRole.Heading);
        flow.Gap();
        flow.Text("Imagined location only. No nearby detection or travel instructions.", TextRole.Body, true);
        flow.Rule();
        if (preview.Stamp is not { } design)
        {
            flow.Text("No stamp", TextRole.Heading);
            flow.Gap();
            flow.Text("This discovery is for reading; it has no stamp to collect.", TextRole.Body, true);
            return;
        }
        flow.Text(held != null ? "Your stamp" : "Stamp preview", TextRole.Heading);
        flow.Gap();
        var stamp = flow.Reserve(context.FontSize * 7f);
        if (flow.IsDrawing)
        {
            if (held != null) StampArt.Issued(stamp, held, assets, context);
            else StampArt.Proof(stamp, design, assets, context);
        }
        flow.Gap();
        flow.Text(design.Name);
        flow.Gap();
        flow.Text(DiscoveryCard.Status(preview, held), TextRole.Caption, true);
        flow.Gap();
        flow.Text(held != null ? "Issued to this passport from a local demo design. Not an official achievement."
            : "A faint proof of the design. Not an official achievement or an issued stamp.", TextRole.Body, true);
    }
}
