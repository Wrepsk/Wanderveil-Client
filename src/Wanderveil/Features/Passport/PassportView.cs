using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

internal sealed class PassportView
{
    private readonly PassportPreview preview;

    internal PassportView(PassportPreview preview) => this.preview = preview;

    internal bool Draw(UiContext context)
    {
        UiTypography.Text("Adventurer's passport", TextRole.Title, context, UiTheme.Paper);
        UiTypography.Text("SAMPLE PASSPORT / OFFLINE PREVIEW", TextRole.Caption, context, UiTheme.Veil);
        ImGui.Dummy(new Vector2(0f, UiMetrics.Medium * context.Scale));

        // Leave room for the page shadow and the parent window's native scrollbar.
        var width = MathF.Max(1f, UiLayout.ContentWidth - (4f * context.Scale));
        var layout = PassportLayout.Create(width, context);
        var contentWidth = layout.PageWidth - (2f * layout.Padding);
        var measureIdentity = new PageFlow(Vector2.Zero, contentWidth, context, false);
        var measureJournal = new PageFlow(Vector2.Zero, contentWidth, context, false);
        ComposeIdentity(ref measureIdentity, context);
        ComposeJournal(ref measureJournal);
        var identityHeight = measureIdentity.Height + (2f * layout.Padding);
        var journalHeight = measureJournal.Height + (2f * layout.Padding);
        var origin = ImGui.GetCursorScreenPos();
        var pageHeight = MathF.Max(identityHeight, journalHeight);
        var identity = new UiRect(origin, new Vector2(layout.PageWidth, layout.IsSpread ? pageHeight : identityHeight));
        var journalOrigin = layout.IsSpread
            ? origin + new Vector2(layout.PageWidth + layout.Gap, 0f)
            : origin + new Vector2(0f, identityHeight + layout.Gap);
        var journal = new UiRect(journalOrigin, new Vector2(layout.PageWidth, layout.IsSpread ? pageHeight : journalHeight));

        DrawPage(identity, layout.Padding, context, true);
        DrawPage(journal, layout.Padding, context, false);
        var totalHeight = layout.IsSpread ? pageHeight : identityHeight + journalHeight + layout.Gap;
        ImGui.Dummy(new Vector2(width, totalHeight + (4f * context.Scale)));
        ImGui.Dummy(new Vector2(0f, UiMetrics.Medium * context.Scale));
        return UiButton.Draw("Explore discoveries", context);
    }

    private void DrawPage(UiRect bounds, float padding, UiContext context, bool identity)
    {
        var drawList = ImGui.GetWindowDrawList();
        PageSurface.Draw(drawList, bounds, context);
        drawList.PushClipRect(bounds.Position, bounds.Max, true);
        try
        {
            var flow = new PageFlow(bounds.Position + new Vector2(padding), bounds.Size.X - (2f * padding), context, true);
            if (identity)
                ComposeIdentity(ref flow, context);
            else
                ComposeJournal(ref flow);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private void ComposeIdentity(ref PageFlow flow, UiContext context)
    {
        flow.Text("WANDERVEIL / IDENTITY", TextRole.Caption, true);
        flow.Gap(UiMetrics.Medium);
        var portrait = flow.Reserve(MathF.Max(84f * context.Scale, context.FontSize * 5f));
        if (flow.IsDrawing)
            DrawPortrait(portrait, context);
        flow.Gap();
        flow.Text("Portrait placeholder", TextRole.Caption, true);
        flow.Rule();
        flow.Text(preview.Name, TextRole.Title);
        flow.Gap(UiMetrics.Medium);
        flow.Text("HOME WORLD", TextRole.Caption, true);
        flow.Gap();
        flow.Text(preview.HomeWorld);
        flow.Gap(UiMetrics.Medium);
        flow.Text("FAVORITE JOBS", TextRole.Caption, true);
        flow.Gap();
        flow.Text(preview.FavoriteJobs);
        flow.Rule();
        flow.Text("WV-DEMO-0001 / Sample identity", TextRole.Caption, true);
    }

    private void ComposeJournal(ref PageFlow flow)
    {
        flow.Text("NOTES FROM THE ROAD", TextRole.Caption, true);
        flow.Gap(UiMetrics.Medium);
        flow.Text("A little about me", TextRole.Title);
        flow.Gap();
        flow.Text(preview.Bio);
        flow.Rule();
        flow.Text("A WISH FOR THE ROAD", TextRole.Caption, true);
        flow.Gap();
        flow.Text(preview.Motto, TextRole.Heading);
        flow.Rule();
        flow.Text("Stamps & memories", TextRole.Heading);
        flow.Gap();
        flow.Text("No stamps yet.");
        flow.Gap();
        flow.Text("Your future discoveries will find a home here.", TextRole.Body, true);
    }

    private static void DrawPortrait(UiRect bounds, UiContext context)
    {
        var drawList = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(UiTheme.MutedInk);
        var center = bounds.Position + (bounds.Size / 2f);
        var radius = MathF.Min(bounds.Size.Y * 0.16f, bounds.Size.X * 0.12f);
        drawList.AddRectFilled(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.PaperInset), UiMetrics.CornerRadius * context.Scale);
        drawList.AddCircleFilled(center - new Vector2(0f, radius), radius, ink);
        drawList.AddRectFilled(center + new Vector2(-radius * 1.8f, radius * 0.4f),
            center + new Vector2(radius * 1.8f, radius * 2.2f), ink, radius);
    }
}
