using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// Static review gallery. Production routing, customization, signature input, and motion are unchanged.
internal static class GridaniaTargets
{
    internal readonly record struct Reading(float LeftHeight, float RightHeight, float PageHeight, float Scroll);

    internal static Reading Draw(string target, UiRect window, PreviewTextures assets, UiContext context, ImFontPtr signatureFont,
        bool single = false, bool information = false, bool longText = false)
    {
        if (target.StartsWith("cover", StringComparison.Ordinal))
        {
            ImGui.GetWindowDrawList().AddImage(assets.Study(target == "cover-plum" ? "cover-plum-cutout" : "cover").Texture, window.Position, window.Max);
            var centerX = window.Position.X + window.Size.X * 0.54f;
            CoverText("EORZEA PASSPORT", 0.25f, TextRole.Caption, 1);
            BookSurface.Illustration(new UiRect(new Vector2(centerX - window.Size.X * 0.19f, window.Position.Y + window.Size.Y * 0.36f), new Vector2(window.Size.X * 0.38f, window.Size.Y * 0.26f)), assets.Study("crest"));
            CoverText("GRIDANIA", 0.67f, TextRole.Title, 1.35f);
            CoverText("THE BLACK SHROUD", 0.73f, TextRole.Caption, 1);
            CoverText("WANDERVEIL", 0.82f, TextRole.Caption, 0.85f);
            return default;

            void CoverText(string text, float y, TextRole role, float size)
            {
                var type = context with { FontSize = context.FontSize * size };
                var width = window.Size.X * 0.58f;
                var measured = UiTypography.Measure(text, role, type, width, false);
                var position = new Vector2(centerX - measured.X / 2, window.Position.Y + window.Size.Y * y);
                var list = ImGui.GetWindowDrawList();
                UiTypography.Draw(list, position + new Vector2(0.7f, 1), text, role, type, width, new Vector4(0.04f, 0.06f, 0.03f, 0.9f), false);
                UiTypography.Draw(list, position, text, role, type, width, new Vector4(0.83f, 0.72f, 0.45f, 1), false);
            }
        }

        var natural = BookSurface.NaturalHeight(window.Size.X, !single);
        var book = new UiRect(window.Position - new Vector2(0, natural * 0.075f), new Vector2(window.Size.X, natural));
        var face = single ? information ? BookFace.Right : BookFace.Left : BookFace.Spread;
        BookSurface.Draw(book, face, assets, context);
        var left = BookSurface.Content(book, face);
        var right = single ? left : BookSurface.RightContent(book);
        var leftHeight = 0f;
        var rightHeight = 0f;
        var scroll = 0f;
        Page("Left study", left, !information, ref leftHeight);
        if (!single) Page("Right study", right, false, ref rightHeight);
        return new Reading(leftHeight, rightHeight, left.Size.Y, scroll);

        void Page(string id, UiRect bounds, bool isLeft, ref float height)
        {
            ImGui.SetCursorScreenPos(bounds.Position);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 5 * context.Scale);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, UiTheme.Gold);
            var visible = ImGui.BeginChild(id, bounds.Size, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings);
            try
            {
                if (visible)
                {
                    var width = UiLayout.ContentWidth;
                    var flow = new PageFlow(ImGui.GetCursorScreenPos(), width, context, true);
                    if (target == "nation") Nation(ref flow, isLeft, width);
                    else if (target == "identity") Identity(ref flow, isLeft, width);
                    else Collection(ref flow, isLeft, width);
                    height = flow.Height;
                    ImGui.Dummy(new Vector2(width, height));
                    scroll = MathF.Max(scroll, ImGui.GetScrollMaxY());
                }
            }
            finally { ImGui.EndChild(); ImGui.PopStyleColor(2); ImGui.PopStyleVar(); }
        }

        void Nation(ref PageFlow flow, bool isLeft, float width)
        {
            flow.Text(isLeft ? "01 / THE FOREST NATION" : "THE TWELVESWOOD", TextRole.Caption, true);
            flow.Gap(16);
            flow.Text(isLeft ? "Gridania" : "An atlas of quiet paths", TextRole.Title);
            flow.Gap(16);
            var art = flow.Reserve(380 * context.Scale);
            var fitted = BookSurface.Illustration(art, assets.Study(isLeft ? "banner" : "map-overhead"));
            if (!isLeft)
            {
                var label = "WV";
                var measured = UiTypography.Measure(label, TextRole.Caption, context, fitted.Size.X * 0.22f, false);
                UiTypography.Draw(ImGui.GetWindowDrawList(), new Vector2(fitted.Position.X + fitted.Size.X / 2 - measured.X / 2, fitted.Position.Y + fitted.Size.Y * 0.035f), label, TextRole.Caption, context, fitted.Size.X * 0.22f, UiTheme.Ink, false);
            }
            flow.Gap(16);
            flow.Text(isLeft ? "Lily & entwined serpents / Banner study" : "Illustrative map study / Geography not validated", TextRole.Caption, true);
        }

        void Identity(ref PageFlow flow, bool isLeft, float width)
        {
            if (isLeft)
            {
                flow.Text("02 / PORTRAIT & SIGNATURE", TextRole.Caption, true);
                flow.Gap(16);
                BookSurface.Portrait(flow.Reserve(330 * context.Scale), assets, context);
                flow.Gap(12);
                flow.Text("Illustrated demo portrait", TextRole.Caption, true);
                flow.Gap(24);
                var signature = flow.Reserve(44 * context.Scale);
                var type = context with { HeadingFont = signatureFont, FontSize = context.FontSize * 1.5f };
                var measured = UiTypography.Measure("Ari Vale", TextRole.Heading, type, width, false);
                UiTypography.Draw(ImGui.GetWindowDrawList(), signature.Position + new Vector2((width - measured.X) / 2, 0), "Ari Vale", TextRole.Heading, type, width, UiTheme.Ink, false);
                flow.Rule();
                flow.Text("HOLDER'S SIGNATURE / DEMO", TextRole.Caption, true);
                return;
            }
            flow.Text("GRIDANIA / ADVENTURER IDENTITY", TextRole.Caption, true);
            flow.Gap(16);
            flow.Text(longText ? "Ari of the Many Winding Roads and Forgotten Horizons" : "Ari Vale", TextRole.Title);
            flow.Gap(20);
            Pair(ref flow, "HOME WORLD", "Demo world", "DATA CENTER", "Demo DC", width);
            flow.Gap(16);
            Pair(ref flow, "FAVORITE JOB", "Bard / Botanist", "FREE COMPANY", "Unassigned", width);
            flow.Gap(16);
            Pair(ref flow, "DATE OF ISSUE", "18 OCT 2026 / DEMO", "PASSPORT NO.", "WV-GR-0001", width);
            flow.Rule();
            flow.Text("A WISH FOR THE ROAD", TextRole.Caption, true);
            flow.Gap(8);
            flow.Text("Take the scenic route. There is always another story waiting.", TextRole.Heading);
            flow.Gap(20);
            flow.Text("ABOUT THE ADVENTURER", TextRole.Caption, true);
            flow.Gap(8);
            flow.Text(longText ? string.Concat(Enumerable.Repeat("A keeper of quiet paths and small stories. ", 12)) : "A keeper of small stories, quiet paths, and views worth taking the long way around.");
            flow.Rule();
            flow.Text("WV / GRIDANIA / SAMPLE IDENTITY / 0001", TextRole.Caption, true);
        }

        void Pair(ref PageFlow flow, string label1, string value1, string label2, string value2, float width)
        {
            var gap = 16 * context.Scale;
            var column = (width - gap) / 2;
            var a = new PageFlow(Vector2.Zero, column, context, false);
            var b = new PageFlow(Vector2.Zero, column, context, false);
            Field(ref a, label1, value1); Field(ref b, label2, value2);
            var row = flow.Reserve(MathF.Max(a.Height, b.Height));
            a = new PageFlow(row.Position, column, context, true);
            b = new PageFlow(row.Position + new Vector2(column + gap, 0), column, context, true);
            Field(ref a, label1, value1); Field(ref b, label2, value2);
        }

        void Field(ref PageFlow flow, string label, string value)
        {
            flow.Text(label, TextRole.Caption, true);
            flow.Gap(5);
            flow.Text(value);
        }

        void Collection(ref PageFlow flow, bool isLeft, float width)
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
}
