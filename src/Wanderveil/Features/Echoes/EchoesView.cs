using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Echoes;

// Echoes' two pages: what Echoes will be beside a sample dossier, opposite its chapters and objectives.
// Everything is printed matter; there are no controls, so nothing suggests the adventure can be played.
internal sealed class EchoesView(EchoPreview? preview)
{
    private readonly EchoPreview? dossier = preview == null ? null : EchoPreview.Validate(preview);

    internal void DrawLeft(UiContext context)
    {
        UiTypography.Text("WANDERVEIL / ECHOES", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(16, context);
        UiTypography.Text("Beyond the next horizon", TextRole.Title, context, UiTheme.Ink);
        Gap(12, context);
        UiTypography.Text("Echoes will link discoveries into longer stories, told in chapters with objectives to follow. This is how one might read; it cannot be played yet.", TextRole.Body, context, UiTheme.Ink);
        Gap(18, context);
        if (dossier == null)
        {
            UiTypography.Text("No sample adventure", TextRole.Heading, context, UiTheme.Ink);
            UiTypography.Text("The sample dossier is missing. Nothing has been fetched.", TextRole.Body, context, UiTheme.MutedInk);
            return;
        }
        var width = UiLayout.ContentWidth - 4 * context.Scale;
        var padding = 18 * context.Scale;
        var inner = MathF.Max(1, width - padding * 2);
        var measure = new PageFlow(Vector2.Zero, inner, context, false);
        Cover(ref measure, dossier);
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), new Vector2(width, measure.Height + padding * 2));
        var list = ImGui.GetWindowDrawList();
        PageSurface.Draw(list, bounds, context);
        list.PushClipRect(bounds.Position, bounds.Max, true);
        try
        {
            var flow = new PageFlow(bounds.Position + new Vector2(padding), inner, context, true);
            Cover(ref flow, dossier);
        }
        finally { list.PopClipRect(); }
        ImGui.Dummy(bounds.Size + new Vector2(0, 4 * context.Scale));
        Gap(10, context);
        UiTypography.Text("PREVIEW / NOT PLAYABLE / NOTHING IS TRACKED", TextRole.Caption, context, UiTheme.MutedInk);
    }

    internal void DrawRight(UiContext context)
    {
        UiTypography.Text("CHAPTERS", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(12, context);
        if (dossier == null)
        {
            UiTypography.Text("Chapters and objectives appear here once there is a dossier to read.", TextRole.Body, context, UiTheme.MutedInk);
            return;
        }
        var indent = MathF.Round(UiTypography.Size(TextRole.Heading, context) * 1.6f);
        foreach (var chapter in dossier.Chapters)
        {
            var origin = ImGui.GetCursorScreenPos();
            var line = UiTypography.Measure("I", TextRole.Heading, context, 100, false).Y;
            ImGui.Indent(indent);
            Marker(origin + new Vector2(indent * 0.38f, line * 0.55f), line * 0.42f, chapter, context);
            UiTypography.Text("Chapter " + chapter.Numeral + " / " + chapter.Title, TextRole.Heading, context, chapter.Sealed ? UiTheme.MutedInk : UiTheme.Ink);
            UiTypography.Text(chapter.Summary, TextRole.Body, context, UiTheme.MutedInk);
            if (chapter.Sealed)
                UiTypography.Text("SEALED", TextRole.Caption, context, UiTheme.MutedInk);
            foreach (var objective in chapter.Objectives)
            {
                Gap(6, context);
                var at = ImGui.GetCursorScreenPos();
                var body = UiTypography.Measure("A", TextRole.Body, context, 100, false).Y;
                Diamond(at + new Vector2(-indent * 0.62f, body * 0.55f), body * 0.2f, context);
                UiTypography.Text(objective.Text, TextRole.Body, context, UiTheme.Ink);
                if (objective.Hint is { } hint)
                    UiTypography.Text("HINT / " + hint, TextRole.Caption, context, UiTheme.MutedInk);
            }
            ImGui.Unindent(indent);
            Gap(16, context);
        }
        UiTypography.Text("Playable adventures, saved progress, and rewards arrive in a later phase.", TextRole.Body, context, UiTheme.MutedInk);
    }

    private static void Cover(ref PageFlow flow, EchoPreview dossier)
    {
        flow.Text("SAMPLE DOSSIER", TextRole.Caption, true);
        flow.Gap();
        flow.Text(dossier.Title, TextRole.Title);
        flow.Gap(4);
        flow.Text(dossier.Byline, TextRole.Caption, true);
        flow.Rule();
        flow.Text(dossier.Premise);
    }

    // An open chapter is an inked ring around its numeral's place; a sealed one carries a wax seal.
    private static void Marker(Vector2 center, float radius, EchoChapter chapter, UiContext context)
    {
        var list = ImGui.GetWindowDrawList();
        var stroke = MathF.Max(1, 1.4f * context.Scale);
        if (!chapter.Sealed)
        {
            list.AddCircle(center, radius, ImGui.GetColorU32(UiTheme.Edition), 32, stroke);
            list.AddCircleFilled(center, radius * 0.38f, ImGui.GetColorU32(UiTheme.Edition), 16);
            return;
        }
        list.AddCircleFilled(center + new Vector2(context.Scale), radius, ImGui.GetColorU32(UiTheme.Shadow), 32);
        list.AddCircleFilled(center, radius, ImGui.GetColorU32(UiTheme.Edition), 32);
        list.AddCircle(center, radius * 0.72f, ImGui.GetColorU32(UiTheme.Foil with { W = 0.7f }), 32, MathF.Max(1, context.Scale));
    }

    // A hollow printed diamond: an objective to follow, never a checkbox to tick.
    private static void Diamond(Vector2 center, float radius, UiContext context)
    {
        var ink = ImGui.GetColorU32(UiTheme.MutedInk);
        ImGui.GetWindowDrawList().AddQuad(center - new Vector2(0, radius), center + new Vector2(radius, 0), center + new Vector2(0, radius), center - new Vector2(radius, 0), ink, MathF.Max(1, 1.2f * context.Scale));
    }

    private static void Gap(float units, UiContext context) => ImGui.Dummy(new Vector2(0, units * context.Scale));
}
