using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

internal enum PassportPageKind { NationBanner, NationMap, Portrait, Information, Blank, Stamps }
// Sheet: on a Stamps page, which stamp page it is (0 is the first after the fixed pages).
internal sealed record PassportPage(string Id, PassportPageKind Kind, int Sheet = 0)
{
    internal static PassportPage StampPage(int sheet) => new($"stamps-{sheet}", PassportPageKind.Stamps, sheet);
}

// Presentation data and composition; no ownership, persistence, or animation logic. Stamp pages are blank
// paper here: the stamps on them, placed by the owner, are drawn by the shell's stamp layer. The identity
// pages print the holder's identity; given an editor, they are written on in place instead.
internal sealed class PassportDocumentView(IBookAssets assets, PassportProfile holder, IPassportPortrait? picture = null)
{
    // The portrait frame's width over its height, as on the printed design.
    internal const float PortraitAspect = 0.75f;

    internal PassportDocumentView(IBookAssets assets, PassportIdentity identity) : this(assets, new PassportProfile(identity)) { }

    internal static readonly IReadOnlyList<PassportPage> SamplePages = Array.AsReadOnly(new[] {
        new PassportPage("nation-banner", PassportPageKind.NationBanner), new PassportPage("nation-map", PassportPageKind.NationMap),
        new PassportPage("portrait", PassportPageKind.Portrait), new PassportPage("information", PassportPageKind.Information) });

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
        // The holder's own line, if any, embossed in the same foil between the nation and the maker's mark.
        if (holder.Identity.CoverLine is { Length: > 0 } line) CoverText(line, 440, 15, 4, fit: 280);
        CoverText("WANDERVEIL", 492, 10, 5, 0.7f);

        void CoverText(string text, float top, float size, float tracking, float alpha = 1, float fit = 360)
        {
            // Title role at a font size that yields the design pixel size; the display face replaces it.
            var type = context with { FontSize = MathF.Max(9, size * unit) / 1.65f, TitleFont = context.DisplayFont };
            var spacing = MathF.Round(tracking * unit);
            var width = UiTypography.MeasureTracked(text, TextRole.Title, type, spacing);
            // A long line is set smaller to stay within fit design units (inside the vine border).
            if (width > fit * unit)
            {
                var shrink = MathF.Max(0.5f, fit * unit / width);
                type = type with { FontSize = MathF.Max(9 / 1.65f, type.FontSize * shrink) };
                spacing = MathF.Round(spacing * shrink);
                width = UiTypography.MeasureTracked(text, TextRole.Title, type, spacing);
            }
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

    // The passport number engraved into the lower right corner of a stamp page, as on the printed design:
    // pale typewriter letters pressed into the paper, with a dark edge along their upper left.
    // Drawn inside the page's window, before its stamps, so a stamp pressed over it covers it.
    internal void Engrave(UiContext context)
    {
        var number = holder.Identity.Number;
        var type = context with { BodyFont = context.MonoFont };
        var spacing = MathF.Round(3 * context.Scale);
        var width = UiTypography.MeasureTracked(number, TextRole.Caption, type, spacing);
        var height = UiTypography.Size(TextRole.Caption, type);
        var page = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        var at = page.Max - new Vector2(width + 8 * context.Scale, height + 6 * context.Scale);
        var list = ImGui.GetWindowDrawList();
        var light = MathF.Max(1, MathF.Round(context.Scale));
        UiTypography.DrawTracked(list, at - new Vector2(light), number, TextRole.Caption, type, spacing, UiTheme.MutedInk with { W = 0.5f });
        UiTypography.DrawTracked(list, at, number, TextRole.Caption, type, spacing, new Vector4(0.93f, 0.9f, 0.84f, 0.9f));
    }

    // editor: write on the identity pages in place (only on resting pages, never on a turning leaf).
    internal float Compose(PassportPage page, UiContext context, IdentityEditor? editor = null)
    {
        var width = UiLayout.ContentWidth;
        var origin = ImGui.GetCursorScreenPos();
        var flow = new PageFlow(origin, width, context, true);
        switch (page.Kind)
        {
            case PassportPageKind.NationBanner: break; // Drawn on the paper by DrawNationBanner.
            case PassportPageKind.NationMap: Nation(ref flow, false, width, context); break;
            case PassportPageKind.Portrait: Portrait(ref flow, width, context, editor); break;
            case PassportPageKind.Information: Information(ref flow, width, context, editor); break;
            case PassportPageKind.Stamps: break; // Blank paper; the owner's stamps are drawn over it.
            default: flow.Text("A place for future memories", TextRole.Title); break;
        }
        // Fields and links place themselves; the page's extent is the flow's, from its top.
        ImGui.SetCursorScreenPos(origin);
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

    private void Portrait(ref PageFlow flow, float width, UiContext context, IdentityEditor? editor)
    {
        var identity = holder.Identity;
        flow.Text("02 / PORTRAIT & SIGNATURE", TextRole.Caption, true);
        flow.Gap(16);
        var area = flow.Reserve(330 * context.Scale);
        var own = flow.IsDrawing ? PortraitFrame(area, context, editor) : identity.Portrait != null && picture?.TryGet(out _) == true;
        var hasPlaceholder = assets.TryGet(BookAsset.Portrait, out _);
        flow.Gap(12);
        flow.Text(own ? editor != null ? "DRAG TO MOVE / SCROLL TO ZOOM" : "HOLDER'S PORTRAIT" : hasPlaceholder ? "Demo portrait placeholder" : "Offline portrait preview", TextRole.Caption, true);
        editor?.PictureLinks(ref flow);
        if (own) editor?.FrameChoice(ref flow);
        flow.Gap(24);
        // A long name signs smaller, as a hand would to fit the line; past half size it wraps instead of clipping.
        var type = context with { HeadingFont = context.SignatureFont, FontSize = context.FontSize * 1.5f };
        if (editor != null) editor.Field(ref flow, IdentityField.Signature, TextRole.Heading, identity.Name.Length > 0 ? identity.Name : "Your signature", type);
        else
        {
            var signed = identity.SignedName;
            var natural = UiTypography.Measure(signed, TextRole.Heading, type, float.MaxValue, false).X;
            if (natural > width) type = type with { FontSize = type.FontSize * MathF.Max(0.5f, width / natural) };
            var wraps = UiTypography.Measure(signed, TextRole.Heading, type, float.MaxValue, false).X > width;
            var measured = UiTypography.Measure(signed, TextRole.Heading, type, width, wraps);
            var signature = flow.Reserve(MathF.Max(44 * context.Scale, measured.Y));
            if (flow.IsDrawing && signed.Length > 0)
                UiTypography.Draw(ImGui.GetWindowDrawList(), signature.Position + new Vector2(wraps ? 0 : (width - measured.X) / 2, MathF.Max(0, 44 * context.Scale - measured.Y) / 2), signed, TextRole.Heading, type, width, UiTheme.Ink, wraps);
        }
        flow.Rule();
        flow.Text(editor != null ? "HOLDER'S SIGNATURE / LEFT EMPTY, YOUR NAME SIGNS" : "HOLDER'S SIGNATURE", TextRole.Caption, true);
        if (editor == null) return;
        flow.Gap(20);
        flow.Text("ON THE COVER", TextRole.Caption, true);
        flow.Gap(5);
        editor.Field(ref flow, IdentityField.CoverLine, TextRole.Body, "Nothing added (as printed)");
        editor.CoverLinks(ref flow);
    }

    // The holder's own picture in its frame, placed as they placed it; otherwise the edition's placeholder.
    // Returns whether the holder's picture was drawn.
    private bool PortraitFrame(UiRect area, UiContext context, IdentityEditor? editor)
    {
        if (holder.Identity.Portrait is { } crop && picture != null && picture.TryGet(out var image))
        {
            var width = MathF.Min(area.Size.X, area.Size.Y * PortraitAspect);
            var size = new Vector2(width, width / PortraitAspect);
            var outer = new UiRect(area.Position + (area.Size - size) / 2, size);
            var style = holder.Identity.Frame;
            // The picture spans the frame's opening; a deckled picture runs to the frame's edge, torn there.
            var shows = style == PictureFrame.Deckled ? outer : PictureFrames.Inner(outer, style, context.Scale);
            var (uv0, uv1) = crop.Window(image.Width / (float)Math.Max(1, image.Height), shows.Size.X / shows.Size.Y);
            PictureFrames.Draw(ImGui.GetWindowDrawList(), outer, style, image, uv0, uv1, context.Scale);
            editor?.Crop(shows, image);
            return true;
        }
        if (assets.TryGet(BookAsset.Portrait, out _)) BookSurface.Portrait(area, assets, context);
        else UiTypography.Draw(ImGui.GetWindowDrawList(), area.Position, "Portrait unavailable", TextRole.Body, context, area.Size.X, UiTheme.MutedInk);
        return false;
    }

    private void Information(ref PageFlow flow, float width, UiContext context, IdentityEditor? editor)
    {
        var identity = holder.Identity;
        flow.Text("GRIDANIA / ADVENTURER IDENTITY", TextRole.Caption, true);
        editor?.CharacterLink(ref flow);
        flow.Gap(16);
        if (editor != null) editor.Field(ref flow, IdentityField.Name, TextRole.Title, "Your name");
        else if (identity.Name.Length > 0) flow.Text(identity.Name, TextRole.Title);
        else flow.Text("Not filled in yet. Choose Edit to write your details.", TextRole.Body, true);
        flow.Gap(20);
        Pair(ref flow, new("HOME WORLD", IdentityField.HomeWorld, "Home world"), new("DATA CENTER", IdentityField.DataCenter, "Data center"), width, context, editor);
        flow.Gap(16);
        Pair(ref flow, new("FAVORITE JOB", IdentityField.FavoriteJob, "Favorite job"), new("FREE COMPANY", IdentityField.FreeCompany, "Free company"), width, context, editor);
        flow.Gap(16);
        var issued = identity.Issued?.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant() ?? "";
        Pair(ref flow, new("DATE OF ISSUE", null, issued), new("PASSPORT NO.", null, identity.Number), width, context, null, true);
        flow.Rule();
        flow.Text("A WISH FOR THE ROAD", TextRole.Caption, true);
        flow.Gap(8);
        if (editor != null) editor.Field(ref flow, IdentityField.Motto, TextRole.Heading, "A wish for the road");
        else flow.Text(identity.Motto, TextRole.Heading);
        flow.Gap(20);
        flow.Text("ABOUT THE ADVENTURER", TextRole.Caption, true);
        flow.Gap(8);
        if (editor != null) editor.Field(ref flow, IdentityField.Bio, TextRole.Body, "A few words about you");
        else flow.Text(identity.Bio);
        flow.Rule();
        var mono = new PageFlow(flow.Reserve(0).Position, width, context with { BodyFont = context.MonoFont }, flow.IsDrawing);
        mono.Text(identity.MachineLine(), TextRole.Caption, true);
        flow.Reserve(mono.Height);
    }

    // One labelled box of the identity form: a field the holder writes (Field set; Text is its placeholder),
    // or fixed text (Field null).
    private readonly record struct Entry(string Label, IdentityField? Field, string Text);

    private void Pair(ref PageFlow flow, Entry first, Entry second, float width, UiContext context, IdentityEditor? editor, bool monoSecond = false)
    {
        var mono = monoSecond ? context with { BodyFont = context.MonoFont } : context;
        var gap = 16 * context.Scale;
        var column = (width - gap) / 2;
        var a = new PageFlow(Vector2.Zero, column, context, false);
        var b = new PageFlow(Vector2.Zero, column, mono, false);
        Field(ref a, first, editor); Field(ref b, second, editor);
        var row = flow.Reserve(MathF.Max(a.Height, b.Height));
        if (!flow.IsDrawing) return;
        a = new PageFlow(row.Position, column, context, true);
        b = new PageFlow(row.Position + new Vector2(column + gap, 0), column, mono, true);
        Field(ref a, first, editor); Field(ref b, second, editor);
    }

    private void Field(ref PageFlow flow, Entry entry, IdentityEditor? editor)
    {
        flow.Text(entry.Label, TextRole.Caption, true);
        flow.Gap(5);
        if (entry.Field is { } field && editor != null) { editor.Field(ref flow, field, TextRole.Body, entry.Text); return; }
        var value = entry.Field is { } shown ? holder.Identity[shown] : entry.Text;
        // An empty field prints blank, keeping its line, so the page still reads as a form to fill in.
        flow.Text(value);
    }
}
