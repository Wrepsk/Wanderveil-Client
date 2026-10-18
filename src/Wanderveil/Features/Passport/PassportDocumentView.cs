using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

internal enum PassportPageKind { NationBanner, NationMap, Portrait, Information, CollectionLeft, CollectionRight, Blank }
internal sealed record PassportPage(string Id, PassportPageKind Kind);

// Presentation data and composition; no ownership, collection, persistence, or animation logic.
internal sealed class PassportDocumentView(IBookAssets assets, PassportPreview profile)
{
    internal static readonly IReadOnlyList<PassportPage> SamplePages = Array.AsReadOnly(new[] {
        new PassportPage("nation-banner", PassportPageKind.NationBanner), new PassportPage("nation-map", PassportPageKind.NationMap),
        new PassportPage("portrait", PassportPageKind.Portrait), new PassportPage("information", PassportPageKind.Information),
        new PassportPage("collection-left", PassportPageKind.CollectionLeft), new PassportPage("collection-right", PassportPageKind.CollectionRight) });

    internal void DrawCover(UiRect window, UiContext context)
    {
        context = context with { FontSize = MathF.Min(context.FontSize, MathF.Max(12 * context.Scale, window.Size.X * 18 / 560)) };
        var hasCover = assets.TryGet(BookAsset.EditionCover, out var cover) || assets.TryGet(BookAsset.Cover, out cover);
        if (hasCover)
            ImGui.GetWindowDrawList().AddImage(cover.Texture, window.Position, window.Max);
        else BookSurface.Draw(window, BookFace.Cover, assets, context);
        // Positions follow the generated cover's board (tools/passport-art): centered, crest above the nation name.
        var centerX = window.Position.X + window.Size.X * 0.5f;
        CoverText("EORZEA PASSPORT", 0.255f, TextRole.Caption, 1);
        if (assets.TryGet(BookAsset.EditionCrest, out var crest)) BookSurface.Illustration(new UiRect(new Vector2(centerX - window.Size.X * 0.165f, window.Position.Y + window.Size.Y * 0.31f), new Vector2(window.Size.X * 0.33f, window.Size.X * 0.33f)), crest);
        CoverText("GRIDANIA", 0.555f, TextRole.Title, 1.35f);
        CoverText("THE BLACK SHROUD", 0.635f, TextRole.Caption, 1);
        CoverText("WANDERVEIL", 0.865f, TextRole.Caption, 0.85f);


        void CoverText(string text, float y, TextRole role, float size)
        {
            var type = context with { FontSize = context.FontSize * size };
            var width = window.Size.X * 0.58f;
            var measured = UiTypography.Measure(text, role, type, width, false);
            var position = new Vector2(centerX - measured.X / 2, window.Position.Y + window.Size.Y * y);
            var list = ImGui.GetWindowDrawList();
            UiTypography.Draw(list, position + new Vector2(0.7f, 1), text, role, type, width, new Vector4(0.04f, 0.06f, 0.03f, 0.9f), false);
            UiTypography.Draw(list, position, text, role, type, width, hasCover ? new Vector4(0.66f, 0.76f, 0.54f, 1) : UiTheme.Ink, false);
        }

    }

    internal float Compose(PassportPage page, UiContext context)
    {
        var width = UiLayout.ContentWidth;
        var flow = new PageFlow(ImGui.GetCursorScreenPos(), width, context, true);
        switch (page.Kind)
        {
            case PassportPageKind.NationBanner: Nation(ref flow, true, width, context); break;
            case PassportPageKind.NationMap: Nation(ref flow, false, width, context); break;
            case PassportPageKind.Portrait: Identity(ref flow, true, width, context); break;
            case PassportPageKind.Information: Identity(ref flow, false, width, context); break;
            case PassportPageKind.CollectionLeft: Collection(ref flow, true, width, context); break;
            case PassportPageKind.CollectionRight: Collection(ref flow, false, width, context); break;
            default: flow.Text("A place for future memories", TextRole.Title); break;
        }
        ImGui.Dummy(new Vector2(width, flow.Height));
        return flow.Height;
    }

    private void Nation(ref PageFlow flow, bool isLeft, float width, UiContext context)
    {
        flow.Text(isLeft ? "01 / THE FOREST NATION" : "THE TWELVESWOOD", TextRole.Caption, true);
        flow.Gap(16);
        flow.Text(isLeft ? "Gridania" : "An atlas of quiet paths", TextRole.Title);
        flow.Gap(16);
        var art = flow.Reserve(380 * context.Scale);
        if (!assets.TryGet(isLeft ? BookAsset.NationBanner : BookAsset.NationMap, out var image))
        { flow.Text("Artwork is loading or unavailable.", TextRole.Caption, true); return; }
        BookSurface.Illustration(art, image);
        flow.Gap(16);
        flow.Text(isLeft ? "Wreath & sprout / Banner study" : "Illustrative map study / Geography not validated", TextRole.Caption, true);
    }

    private void Identity(ref PageFlow flow, bool isLeft, float width, UiContext context)
    {
        if (isLeft)
        {
            flow.Text("02 / PORTRAIT & SIGNATURE", TextRole.Caption, true);
            flow.Gap(16);
            var portrait = flow.Reserve(330 * context.Scale);
            var hasPortrait = assets.TryGet(BookAsset.Portrait, out _);
            if (hasPortrait) BookSurface.Portrait(portrait, assets, context);
            else UiTypography.Draw(ImGui.GetWindowDrawList(), portrait.Position, "Portrait unavailable", TextRole.Body, context, width, UiTheme.MutedInk);
            flow.Gap(12);
            flow.Text(hasPortrait ? "Demo portrait placeholder" : "Offline portrait preview", TextRole.Caption, true);
            flow.Gap(24);
            var signature = flow.Reserve(44 * context.Scale);
            var type = context with { HeadingFont = context.SignatureFont, FontSize = context.FontSize * 1.5f };
            var measured = UiTypography.Measure(profile.Name, TextRole.Heading, type, width, false);
            UiTypography.Draw(ImGui.GetWindowDrawList(), signature.Position + new Vector2((width - measured.X) / 2, 0), profile.Name, TextRole.Heading, type, width, UiTheme.Ink, false);
            flow.Rule();
            flow.Text("HOLDER'S SIGNATURE / DEMO", TextRole.Caption, true);
            return;
        }
        flow.Text("GRIDANIA / ADVENTURER IDENTITY", TextRole.Caption, true);
        flow.Gap(16);
        flow.Text(profile.Name, TextRole.Title);
        flow.Gap(20);
        Pair(ref flow, "HOME WORLD", profile.HomeWorld, "DATA CENTER", "Demo DC", width, context);
        flow.Gap(16);
        Pair(ref flow, "FAVORITE JOB", profile.FavoriteJobs, "FREE COMPANY", "Unassigned", width, context);
        flow.Gap(16);
        Pair(ref flow, "DATE OF ISSUE", "18 OCT 2026 / DEMO", "PASSPORT NO.", "WV-GR-0001", width, context);
        flow.Rule();
        flow.Text("A WISH FOR THE ROAD", TextRole.Caption, true);
        flow.Gap(8);
        flow.Text(profile.Motto, TextRole.Heading);
        flow.Gap(20);
        flow.Text("ABOUT THE ADVENTURER", TextRole.Caption, true);
        flow.Gap(8);
        flow.Text(profile.Bio);
        flow.Rule();
        flow.Text("WV / GRIDANIA / SAMPLE IDENTITY / 0001", TextRole.Caption, true);
    }

    private void Pair(ref PageFlow flow, string label1, string value1, string label2, string value2, float width, UiContext context)
    {
        var gap = 16 * context.Scale;
        var column = (width - gap) / 2;
        var a = new PageFlow(Vector2.Zero, column, context, false);
        var b = new PageFlow(Vector2.Zero, column, context, false);
        Field(ref a, label1, value1, context); Field(ref b, label2, value2, context);
        var row = flow.Reserve(MathF.Max(a.Height, b.Height));
        a = new PageFlow(row.Position, column, context, true);
        b = new PageFlow(row.Position + new Vector2(column + gap, 0), column, context, true);
        Field(ref a, label1, value1, context); Field(ref b, label2, value2, context);
    }

    private void Field(ref PageFlow flow, string label, string value, UiContext context)
    {
        flow.Text(label, TextRole.Caption, true);
        flow.Gap(5);
        flow.Text(value);
    }

    private void Collection(ref PageFlow flow, bool isLeft, float width, UiContext context)
    {
        flow.Text(isLeft ? "03 / IMPRESSIONS" : "04 / PLACES WORTH REMEMBERING", TextRole.Caption, true);
        flow.Gap(16);
        flow.Text(isLeft ? "By root & river" : "Under another sky", TextRole.Title);
        flow.Gap(20);
        if (isLeft)
        {
            BookSurface.Stamp(flow.Reserve(140 * context.Scale), 1, assets);
            flow.Gap(8);
            flow.Text("Woodland wanderer / Demo specimen", TextRole.Caption, true);
            flow.Gap(24);
            BookSurface.Stamp(flow.Reserve(140 * context.Scale), 0, assets);
            flow.Gap(8);
            flow.Text("Salt & starlight / Demo specimen", TextRole.Caption, true);
        }
        else
        {
            BookSurface.Stamp(flow.Reserve(240 * context.Scale), 2, assets);
            flow.Gap(12);
            flow.Text("A sky full of possibilities", TextRole.Heading);
            flow.Rule();
            flow.Text("A place, a moment, a reason to return. Every impression holds a small story.");
            flow.Gap(24);
            flow.Text("DEMO COLLECTION / NOT COLLECTED OR ISSUED", TextRole.Caption, true);
        }
    }

}
