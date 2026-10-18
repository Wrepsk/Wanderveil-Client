using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Discover;

internal sealed class DiscoverView(IReadOnlyList<DiscoveryPreview> previews)
{
    internal DiscoveryPreview? SelectedDiscovery { get; private set; }
    private float browseScroll;
    private float? requestedScroll;
    private bool focusDetails;

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
            var height = DiscoveryCard.Measure(left, layout.ColumnWidth, layout.Padding, context);
            if (right != null)
                height = MathF.Max(height, DiscoveryCard.Measure(right, layout.ColumnWidth, layout.Padding, context));
            var origin = ImGui.GetCursorScreenPos();
            if (DiscoveryCard.Draw(left, layout.ColumnWidth, layout.Padding, height, context))
                opened = left;
            if (right != null)
            {
                ImGui.SetCursorScreenPos(origin + new Vector2(layout.ColumnWidth + layout.Gap, 0f));
                if (DiscoveryCard.Draw(right, layout.ColumnWidth, layout.Padding, height, context))
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
        var leftMeasure = new PageFlow(Vector2.Zero, contentWidth, context, false);
        var rightMeasure = new PageFlow(Vector2.Zero, contentWidth, context, false);
        ComposePostcard(ref leftMeasure, preview, contentWidth);
        ComposeReward(ref rightMeasure, preview, context);
        var leftHeight = leftMeasure.Height + (layout.Padding * 2f);
        var rightHeight = rightMeasure.Height + (layout.Padding * 2f);
        var height = MathF.Max(leftHeight, rightHeight);
        var origin = ImGui.GetCursorScreenPos();
        var left = new UiRect(origin, new Vector2(layout.ColumnWidth, layout.IsSplit ? height : leftHeight));
        var right = new UiRect(origin + (layout.IsSplit
            ? new Vector2(layout.ColumnWidth + layout.Gap, 0f)
            : new Vector2(0f, leftHeight + layout.Gap)), new Vector2(layout.ColumnWidth, layout.IsSplit ? height : rightHeight));
        DrawPage(left, layout.Padding, preview, context, true);
        DrawPage(right, layout.Padding, preview, context, false);
        ImGui.Dummy(new Vector2(width, (layout.IsSplit ? height : leftHeight + rightHeight + layout.Gap) + (4f * context.Scale)));
        ImGui.Dummy(new Vector2(0f, UiMetrics.Medium * context.Scale));
        ImGui.BeginDisabled();
        try
        {
            UiButton.Draw("Collect demo stamp", context);
        }
        finally
        {
            ImGui.EndDisabled();
        }
        UiTypography.Text("Collection is unavailable in this UI preview. No stamp is awarded or saved.", TextRole.Body, context, onPaper ? UiTheme.Ink : UiTheme.Paper);
    }

    private static void DrawPage(UiRect bounds, float padding, DiscoveryPreview preview, UiContext context, bool postcard)
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
                ComposeReward(ref flow, preview, context);
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

    private static void ComposeReward(ref PageFlow flow, DiscoveryPreview preview, UiContext context)
    {
        flow.Text(preview.Kind, TextRole.Caption, true);
        flow.Gap();
        flow.Text("A note on the map", TextRole.Title);
        flow.Gap();
        flow.Text(preview.Location, TextRole.Heading);
        flow.Gap();
        flow.Text("Imagined location only. No nearby detection or travel instructions.", TextRole.Body, true);
        flow.Rule();
        flow.Text("Stamp preview", TextRole.Heading);
        flow.Gap();
        var stamp = flow.Reserve(context.FontSize * 7f);
        if (flow.IsDrawing)
            StampPreview.Draw(stamp, context);
        flow.Gap();
        flow.Text(preview.StampName);
        flow.Gap();
        flow.Text("NOT COLLECTED / LOCAL DEMO DESIGN", TextRole.Caption, true);
        flow.Gap();
        flow.Text("A preview of a future collectible, not an official achievement or an issued stamp.", TextRole.Body, true);
    }
}
