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

    // window is the cover plate's rectangle. With a leaf, the cover is drawn turning on its spine:
    // window is then the flat placement whose board the leaf's Source describes.
    internal void DrawCover(UiRect window, UiContext context, LeafProjection? leaf = null)
    {
        var list = ImGui.GetWindowDrawList();
        var light = leaf?.Light ?? 1f;
        var hasCover = assets.TryGet(BookAsset.EditionCover, out var cover, window.Size.X) || assets.TryGet(BookAsset.Cover, out cover, window.Size.X);
        if (leaf is { } turning)
        {
            // Only the board turns; the plate's soft shadow margin stays behind.
            if (hasCover) turning.Leaf(list, cover.Texture, new Vector2(42f / 1024f, 141f / 1536f), new Vector2(982f / 1024f, 1394f / 1536f), Vector4.One);
            else list.AddQuadFilled(turning.Project(0, 0), turning.Project(1, 0), turning.Project(1, 1), turning.Project(0, 1), LeafProjection.Tint(UiTheme.Edition, light));
        }
        else if (hasCover) list.AddImage(cover.Texture, window.Position, window.Max);
        else BookSurface.Draw(window, BookFace.Cover, assets, context);
        // Sizes, tops, and letter-spacing come from the Gridania board in the Eorzea Passport design (420 x 560 units).
        var (board, unit) = BookSurface.CoverBoard(window);
        var centerX = board.X + 210 * unit;
        CoverText("EORZEA PASSPORT", 117, 13, 6);
        if (assets.TryGet(BookAsset.EditionCrest, out var crest, 150 * unit))
        {
            var placement = new UiRect(board + new Vector2(135, 153) * unit, new Vector2(150 * unit));
            if (leaf is { } crestLeaf) crestLeaf.Image(list, crest.Texture, BookSurface.Fit(placement, crest), LeafProjection.Tint(Vector4.One, light));
            else BookSurface.Illustration(placement, crest);
        }
        CoverText("GRIDANIA", 321, 44, 8);
        CoverText("THE BLACK SHROUD", 396, 14, 6);
        CoverText("WANDERVEIL", 492, 10, 5, 0.7f);

        void CoverText(string text, float top, float size, float tracking, float alpha = 1)
        {
            // Title role at a font size that yields the design pixel size; the display face replaces it.
            var type = context with { FontSize = MathF.Max(9, size * unit) / 1.65f, TitleFont = context.DisplayFont };
            var spacing = MathF.Round(tracking * unit);
            var width = UiTypography.MeasureTracked(text, TextRole.Title, type, spacing);
            var position = new Vector2(centerX - width / 2, board.Y + top * unit);
            var foil = UiTheme.Foil;
            var shadow = new Vector4(0.02f, 0.05f, 0.03f, 0.55f * alpha);
            if (leaf is { } textLeaf)
            {
                var font = UiTypography.ResolveFont(text, TextRole.Title, type);
                var pixels = UiTypography.Size(TextRole.Title, type);
                textLeaf.Text(list, font, pixels, position + new Vector2(0, MathF.Max(1, unit)), text, spacing, ImGui.GetColorU32(shadow));
                textLeaf.Text(list, font, pixels, position, text, spacing, LeafProjection.Tint(foil with { W = alpha }, light));
                return;
            }
            UiTypography.DrawTracked(list, position + new Vector2(0, MathF.Max(1, unit)), text, TextRole.Title, type, spacing, shadow);
            UiTypography.DrawTracked(list, position, text, TextRole.Title, type, spacing, foil with { W = alpha });
        }
    }

    internal float Compose(PassportPage page, UiContext context)
    {
        var width = UiLayout.ContentWidth;
        var flow = new PageFlow(ImGui.GetCursorScreenPos(), width, context, true);
        switch (page.Kind)
        {
            case PassportPageKind.NationBanner: break; // Drawn on the paper by DrawNationBanner.
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

    // A pennant hung from the top edge of the page down to the lower margin, with foil lettering.
    internal void DrawNationBanner(UiRect paper, float bottom, UiContext context)
    {
        var list = ImGui.GetWindowDrawList();
        var height = MathF.Max(1, bottom - paper.Position.Y);
        if (!assets.TryGet(BookAsset.NationBanner, out var image, MathF.Min(paper.Size.X * 0.62f, height * 0.435f)))
        {
            UiTypography.Draw(list, paper.Position + new Vector2(paper.Size.X * 0.12f, paper.Size.Y * 0.1f), "Gridania", TextRole.Title, context, paper.Size.X * 0.76f, UiTheme.Ink, false);
            return;
        }
        var width = MathF.Min(paper.Size.X * 0.62f, height * image.Width / image.Height);
        height = width * image.Height / image.Width;
        var banner = new UiRect(new Vector2(paper.Position.X + (paper.Size.X - width) / 2, paper.Position.Y), new Vector2(width, height));
        BookSurface.Image(list, image.Texture, banner.Position, banner.Max);
        // Proportions are in the banner's 200 x 460 design units.
        var unit = width / 200f;
        Lettering("01 / THE FOREST NATION", 34, 8, 2.5f, 0.85f);
        if (assets.TryGet(BookAsset.EditionCrest, out var crest, 110 * unit)) BookSurface.Illustration(new UiRect(banner.Position + new Vector2(45, 62) * unit, new Vector2(110 * unit)), crest);
        Lettering("GRIDANIA", 198, 22, 4, 1);
        Lettering("REALM OF EORZEA", 240, 8, 3, 0.85f);

        void Lettering(string text, float top, float size, float tracking, float alpha)
        {
            var type = context with { FontSize = MathF.Max(8, size * unit) / 1.65f, TitleFont = context.DisplayFont };
            var spacing = MathF.Round(tracking * unit);
            var measured = UiTypography.MeasureTracked(text, TextRole.Title, type, spacing);
            var position = new Vector2(banner.Position.X + (width - measured) / 2, banner.Position.Y + top * unit);
            UiTypography.DrawTracked(list, position + new Vector2(0, MathF.Max(1, unit * 0.6f)), text, TextRole.Title, type, spacing, new Vector4(0.02f, 0.05f, 0.03f, 0.5f * alpha));
            UiTypography.DrawTracked(list, position, text, TextRole.Title, type, spacing, UiTheme.Foil with { W = alpha });
        }
    }

    private void Nation(ref PageFlow flow, bool isLeft, float width, UiContext context)
    {
        flow.Text(isLeft ? "01 / THE FOREST NATION" : "THE TWELVESWOOD", TextRole.Caption, true);
        flow.Gap(16);
        flow.Text(isLeft ? "Gridania" : "An atlas of quiet paths", TextRole.Title);
        flow.Gap(16);
        var art = flow.Reserve(380 * context.Scale);
        if (!assets.TryGet(isLeft ? BookAsset.NationBanner : BookAsset.NationMap, out var image, MathF.Min(width, art.Size.Y * 0.83f)))
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
            // A long name signs smaller, as a hand would to fit the line; past half size it wraps instead of clipping.
            var type = context with { HeadingFont = context.SignatureFont, FontSize = context.FontSize * 1.5f };
            var natural = UiTypography.Measure(profile.Name, TextRole.Heading, type, float.MaxValue, false).X;
            if (natural > width) type = type with { FontSize = type.FontSize * MathF.Max(0.5f, width / natural) };
            var wraps = UiTypography.Measure(profile.Name, TextRole.Heading, type, float.MaxValue, false).X > width;
            var measured = UiTypography.Measure(profile.Name, TextRole.Heading, type, width, wraps);
            var signature = flow.Reserve(MathF.Max(44 * context.Scale, measured.Y));
            if (flow.IsDrawing)
                UiTypography.Draw(ImGui.GetWindowDrawList(), signature.Position + new Vector2(wraps ? 0 : (width - measured.X) / 2, MathF.Max(0, 44 * context.Scale - measured.Y) / 2), profile.Name, TextRole.Heading, type, width, UiTheme.Ink, wraps);
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
        Pair(ref flow, "DATE OF ISSUE", "18 OCT 2026 / DEMO", "PASSPORT NO.", "WV-GR-0001", width, context, true);
        flow.Rule();
        flow.Text("A WISH FOR THE ROAD", TextRole.Caption, true);
        flow.Gap(8);
        flow.Text(profile.Motto, TextRole.Heading);
        flow.Gap(20);
        flow.Text("ABOUT THE ADVENTURER", TextRole.Caption, true);
        flow.Gap(8);
        flow.Text(profile.Bio);
        flow.Rule();
        var mono = new PageFlow(flow.Reserve(0).Position, width, context with { BodyFont = context.MonoFont }, flow.IsDrawing);
        mono.Text("P<WV<GRIDANIA<SAMPLE<IDENTITY<<0001", TextRole.Caption, true);
        flow.Reserve(mono.Height);
    }

    private void Pair(ref PageFlow flow, string label1, string value1, string label2, string value2, float width, UiContext context, bool monoSecond = false)
    {
        var second = monoSecond ? context with { BodyFont = context.MonoFont } : context;
        var gap = 16 * context.Scale;
        var column = (width - gap) / 2;
        var a = new PageFlow(Vector2.Zero, column, context, false);
        var b = new PageFlow(Vector2.Zero, column, second, false);
        Field(ref a, label1, value1, context); Field(ref b, label2, value2, second);
        var row = flow.Reserve(MathF.Max(a.Height, b.Height));
        a = new PageFlow(row.Position, column, context, true);
        b = new PageFlow(row.Position + new Vector2(column + gap, 0), column, second, true);
        Field(ref a, label1, value1, context); Field(ref b, label2, value2, second);
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
