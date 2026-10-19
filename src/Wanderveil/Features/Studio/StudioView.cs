using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Studio;

// Studio's two pages: a template chooser opposite a short introduction, then a draft's fields opposite a
// framed preview of what finders would see. Drafts stay in memory; saving and publishing are shown but
// unavailable, so draft, saved, and published never blur together.
internal sealed class StudioView(IReadOnlyList<StudioTemplate>? templates = null)
{
    private readonly IReadOnlyList<StudioTemplate> catalog = templates ?? StudioTemplate.Catalog;
    private readonly Dictionary<StudioTemplateKind, StudioDraft> drafts = [];
    private bool focusFirstField;

    internal StudioDraft? Draft { get; private set; }

    // Starts (or returns to) a draft from an available template; unavailable ones are refused.
    internal bool Choose(StudioTemplateKind kind)
    {
        var template = catalog.FirstOrDefault(candidate => candidate.Kind == kind);
        if (template is not { Available: true } || template.Fields.Count == 0) return false;
        if (!drafts.TryGetValue(kind, out var draft)) drafts[kind] = draft = new StudioDraft(template);
        Draft = draft;
        focusFirstField = true;
        return true;
    }

    // Back to the chooser; drafts are kept so a creator can return to them.
    internal void ShowTemplates()
    {
        Draft = null;
        focusFirstField = false;
    }

    // stacked: both sides share one page, so the preview follows the fields instead of facing them.
    internal void DrawLeft(UiContext context, bool stacked = false)
    {
        if (Draft is { } draft) DrawFields(draft, context, stacked);
        else DrawIntroduction(context);
    }

    internal void DrawRight(UiContext context)
    {
        if (Draft is { } draft) DrawPreview(draft, context);
        else DrawChooser(context);
    }

    private static void DrawIntroduction(UiContext context)
    {
        UiTypography.Text("WANDERVEIL / STUDIO", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(16, context);
        UiTypography.Text("Leave a discovery", TextRole.Title, context, UiTheme.Ink);
        Gap(12, context);
        UiTypography.Text("Start from a template, fill in a few lines, and see your discovery as a finder would.", TextRole.Body, context, UiTheme.Ink);
        Gap(20, context);
        UiTypography.Text("IN THIS PREVIEW", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(6, context);
        UiTypography.Text("Drafts stay in memory until the plugin unloads. Nothing is saved to disk, published, or sent anywhere.", TextRole.Body, context, UiTheme.MutedInk);
    }

    private void DrawChooser(UiContext context)
    {
        UiTypography.Text("CHOOSE A TEMPLATE", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(12, context);
        StudioTemplateKind? chosen = null;
        if (!catalog.Any(candidate => candidate.Available))
            UiTypography.Text("No template can be started in this preview. Nothing has been fetched.", TextRole.Body, context, UiTheme.MutedInk);
        if (!catalog.Any(candidate => candidate.Available)) Gap(14, context);
        foreach (var template in catalog.Where(candidate => candidate.Available))
        {
            UiTypography.Text(template.Name, TextRole.Heading, context, UiTheme.Ink);
            UiTypography.Text(template.Summary, TextRole.Body, context, UiTheme.MutedInk);
            Gap(4, context);
            var started = drafts.TryGetValue(template.Kind, out var draft) && draft.Edited;
            if (BookControl.Link(started ? "Continue your draft »" : template.Start + " »", context)) chosen = template.Kind;
            Gap(14, context);
        }
        var later = catalog.Where(candidate => !candidate.Available).ToArray();
        if (later.Length > 0)
        {
            Rule(context);
            UiTypography.Text("COMING LATER", TextRole.Caption, context, UiTheme.MutedInk);
            Gap(6, context);
            foreach (var template in later)
                UiTypography.Text(template.Name + " / " + template.Summary, TextRole.Body, context, UiTheme.MutedInk);
        }
        if (chosen is { } kind) Choose(kind);
    }

    private void DrawFields(StudioDraft draft, UiContext context, bool stacked)
    {
        var back = BookControl.Link("« All templates", context);
        Gap(10, context);
        UiTypography.Text("STUDIO / " + draft.Template.Name.ToUpperInvariant() + " DRAFT", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(8, context);
        UiTypography.Text("Your " + draft.Template.Name.ToLowerInvariant(), TextRole.Title, context, UiTheme.Ink);
        Gap(10, context);
        var first = focusFirstField;
        focusFirstField = false;
        foreach (var field in draft.Template.Fields)
        {
            var value = draft[field.Id];
            var left = field.MaxLength - StudioDraft.Length(value);
            var label = field.Label.ToUpperInvariant() + (field.Required ? "" : " (OPTIONAL)") + (left <= field.MaxLength / 5 ? " / " + left + " LEFT" : "");
            if (BookControl.Field(field.Id, label, field.Hint, ref value, field.MaxLength, context, first)) draft.Set(field.Id, value);
            first = false;
            Gap(8, context);
        }
        Gap(4, context);
        var missing = draft.Missing.Select(field => field.Label).ToArray();
        UiTypography.Text(missing.Length == 0 ? "Ready to preview. The card " + (stacked ? "below" : "opposite") + " is what finders would see." : "Still needed: " + string.Join(", ", missing) + ".", TextRole.Body, context, UiTheme.Ink);
        Gap(12, context);
        // Saved and published are their own states; neither exists yet, so their controls stay printed but inert.
        ImGui.BeginDisabled(!StudioDraft.CanSave);
        BookControl.Link("Save draft", context);
        ImGui.EndDisabled();
        ImGui.SameLine(0, 18 * context.Scale);
        ImGui.BeginDisabled(!StudioDraft.CanPublish || !draft.IsComplete);
        BookControl.Link("Publish", context);
        ImGui.EndDisabled();
        Gap(4, context);
        UiTypography.Text(StudioDraft.SaveUnavailable + " " + StudioDraft.PublishUnavailable, TextRole.Caption, context, UiTheme.MutedInk);
        if (back) ShowTemplates();
    }

    private static void DrawPreview(StudioDraft draft, UiContext context)
    {
        UiTypography.Text("PREVIEW", TextRole.Caption, context, UiTheme.MutedInk);
        Gap(10, context);
        var width = UiLayout.ContentWidth - 4 * context.Scale;
        var padding = 18 * context.Scale;
        var inner = MathF.Max(1, width - padding * 2);
        var measure = new PageFlow(Vector2.Zero, inner, context, false);
        Compose(ref measure, draft, inner, context);
        var bounds = new UiRect(ImGui.GetCursorScreenPos(), new Vector2(width, measure.Height + padding * 2));
        var list = ImGui.GetWindowDrawList();
        PageSurface.Draw(list, bounds, context);
        list.PushClipRect(bounds.Position, bounds.Max, true);
        try
        {
            var flow = new PageFlow(bounds.Position + new Vector2(padding), inner, context, true);
            Compose(ref flow, draft, inner, context);
        }
        finally { list.PopClipRect(); }
        ImGui.Dummy(bounds.Size + new Vector2(0, 4 * context.Scale));
        Gap(10, context);
        UiTypography.Text("DRAFT / NOT SAVED / NOT PUBLISHED", TextRole.Caption, context, UiTheme.MutedInk);
    }

    // The same composition measures the frame and paints it, so a long caption grows the card.
    private static void Compose(ref PageFlow flow, StudioDraft draft, float width, UiContext context)
    {
        var title = Shown(draft[StudioTemplate.Title]);
        var caption = Shown(draft[StudioTemplate.Caption]);
        var place = Shown(draft[StudioTemplate.Place]);
        var stamp = Shown(draft[StudioTemplate.Stamp]);
        flow.Text(draft.Template.Name.ToUpperInvariant(), TextRole.Caption, true);
        flow.Gap();
        if (draft.Template.Kind == StudioTemplateKind.Postcard)
        {
            var picture = flow.Reserve(width * 0.34f);
            if (flow.IsDrawing) Placeholder(picture, "No picture yet / uploads come later", context);
            flow.Gap();
            flow.Text(title ?? "Untitled postcard", TextRole.Title, title == null);
            flow.Rule();
            flow.Text(caption ?? "Your caption appears here.", TextRole.Body, caption == null);
            if (place != null) { flow.Gap(); flow.Text(place, TextRole.Heading); }
            flow.Rule();
            Stamp(ref flow, stamp, 3.6f, context);
        }
        else
        {
            flow.Text(title ?? "Unnamed spot", TextRole.Title, title == null);
            flow.Gap();
            flow.Text(place ?? "Where finders should look", TextRole.Heading, place == null);
            flow.Rule();
            Stamp(ref flow, stamp, 6f, context);
            if (caption != null) { flow.Rule(); flow.Text(caption); }
        }
        flow.Gap(UiMetrics.Medium);
        flow.Text("BY YOU / UNPUBLISHED DRAFT", TextRole.Caption, true);
    }

    private static void Stamp(ref PageFlow flow, string? name, float size, UiContext context)
    {
        var motif = flow.Reserve(context.FontSize * size);
        if (flow.IsDrawing) StampPreview.Draw(motif, context, UiTheme.EditionInk);
        flow.Gap();
        flow.Text(name ?? "Stamp name", TextRole.Body, name == null);
        flow.Text("A DESIGN PREVIEW, NOT AN ISSUED STAMP", TextRole.Caption, true);
    }

    private static string? Shown(string value) => value.Trim().Length == 0 ? null : value.Trim();

    private static void Placeholder(UiRect bounds, string label, UiContext context)
    {
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.PaperInset with { W = 0.6f }), 3 * context.Scale);
        list.AddRect(bounds.Position, bounds.Max, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.5f }), 3 * context.Scale, ImDrawFlags.None, MathF.Max(1, context.Scale));
        var measured = UiTypography.Measure(label, TextRole.Caption, context, bounds.Size.X - 12 * context.Scale);
        UiTypography.Draw(list, bounds.Position + (bounds.Size - measured) / 2, label, TextRole.Caption, context, bounds.Size.X - 12 * context.Scale, UiTheme.MutedInk);
    }

    private static void Gap(float units, UiContext context) => ImGui.Dummy(new Vector2(0, units * context.Scale));

    private static void Rule(UiContext context)
    {
        Gap(6, context);
        var start = ImGui.GetCursorScreenPos();
        BookSurface.Line(ImGui.GetWindowDrawList(), start, start + new Vector2(UiLayout.ContentWidth, 0), ImGui.GetColorU32(UiTheme.Gold), MathF.Max(1, context.Scale));
        Gap(10, context);
    }
}
