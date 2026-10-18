using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

// Review compositions, not runtime page routing or a collection model. Everything printed is live text.
internal static class PassportTargets
{
    internal static void Draw(string target, IBookAssets assets, UiContext context)
    {
        UiTypography.Text("WANDERVEIL  /  PASSPORT MATERIAL STUDY", TextRole.Caption, context, UiTheme.Paper);
        var origin = ImGui.GetCursorScreenPos();
        var width = UiLayout.ContentWidth;
        if (target == "cover")
        {
            var coverWidth = MathF.Min(480f * context.Scale, width);
            var cover = new UiRect(origin + new Vector2((width - coverWidth) / 2f, 12f), new Vector2(coverWidth, coverWidth * 1.5f));
            BookSurface.Draw(cover, BookFace.Cover, assets, context);
            var centerX = cover.Position.X + (cover.Size.X * 0.54f);
            Center("EORZEA", cover.Position.Y + (cover.Size.Y * 0.29f), TextRole.Title, 1.5f);
            Center("ADVENTURER'S PASSPORT", cover.Position.Y + (cover.Size.Y * 0.37f), TextRole.Caption, 1f);
            StampPreview.Draw(new UiRect(new Vector2(centerX - 72f, cover.Position.Y + (cover.Size.Y * 0.45f)), new Vector2(144f)), context, UiTheme.Gold);
            Center("WANDERVEIL", cover.Position.Y + (cover.Size.Y * 0.73f), TextRole.Heading, 1f);
            Center("Every place has a story.", cover.Position.Y + (cover.Size.Y * 0.80f), TextRole.Caption, 1f);
            ImGui.Dummy(new Vector2(width, cover.Size.Y + 24f));

            void Center(string text, float y, TextRole role, float size)
            {
                var type = context with { FontSize = context.FontSize * size };
                var measured = UiTypography.Measure(text, role, type, coverWidth * 0.7f);
                var pos = new Vector2(centerX - (measured.X / 2f), y);
                UiTypography.Draw(ImGui.GetWindowDrawList(), pos + new Vector2(0.8f, 1.2f), text, role, type, coverWidth * 0.7f, UiTheme.Ink);
                UiTypography.Draw(ImGui.GetWindowDrawList(), pos, text, role, type, coverWidth * 0.7f, UiTheme.Gold);
            }
        }
        else
        {
            var contentWidth = BookSurface.ContentWidth(width, true);
            var leftMeasure = new PageFlow(Vector2.Zero, contentWidth, context, false);
            var rightMeasure = new PageFlow(Vector2.Zero, contentWidth, context, false);
            Compose(ref leftMeasure, true, contentWidth);
            Compose(ref rightMeasure, false, contentWidth);
            var book = new UiRect(origin, new Vector2(width, BookSurface.Height(width, true, MathF.Max(leftMeasure.Height, rightMeasure.Height))));
            BookSurface.Draw(book, BookFace.Spread, assets, context);
            Page(BookSurface.Content(book, BookFace.Spread), true);
            Page(BookSurface.RightContent(book), false);
            ImGui.Dummy(book.Size);
        }
        UiTypography.Text("STATIC VISUAL TARGET / SYNTHETIC IDENTITY & DEMO SPECIMENS / NOT AWARDED", TextRole.Caption, context, UiTheme.Paper);

        void Page(UiRect bounds, bool left)
        {
            var list = ImGui.GetWindowDrawList();
            list.PushClipRect(bounds.Position, bounds.Max, true);
            try
            {
                var flow = new PageFlow(bounds.Position, bounds.Size.X, context, true);
                Compose(ref flow, left, bounds.Size.X);
            }
            finally { list.PopClipRect(); }
        }

        void Compose(ref PageFlow flow, bool left, float contentWidth)
        {
            if (target == "identity")
            {
                if (left)
                {
                    flow.Text("EORZEA / WAYFARER'S RECORD", TextRole.Caption, true);
                    flow.Gap(20);
                    var portrait = flow.Reserve(232f * context.Scale);
                    if (flow.IsDrawing) BookSurface.Portrait(portrait, assets, context);
                    flow.Gap(20);
                    flow.Text("Ari Vale", TextRole.Title);
                    flow.Gap();
                    flow.Text("Bard. Botanist. Keeper of small stories.", TextRole.Body, true);
                    flow.Rule();
                    flow.Text("HOME WORLD", TextRole.Caption, true);
                    flow.Gap(4);
                    flow.Text("Demo world", TextRole.Heading);
                    flow.Gap(16);
                    flow.Text("WV / 0001     /     SAMPLE IDENTITY", TextRole.Caption, true);
                }
                else
                {
                    flow.Text("NOTES FROM THE ROAD", TextRole.Caption, true);
                    flow.Gap(20);
                    flow.Text("The world is still\nfull of wonder.", TextRole.Title);
                    flow.Gap(20);
                    flow.Text("A keeper of quiet paths, old maps, and views worth taking the long way around.");
                    flow.Gap(24);
                    flow.Text("Take the scenic route. There is always another story waiting.", TextRole.Heading);
                    flow.Rule();
                    flow.Text("A PLACE FOR YOUR MEMORIES", TextRole.Caption, true);
                    flow.Gap(12);
                    flow.Text("No stamps collected yet. Your future journeys will leave their mark here.");
                    flow.Gap(36);
                    flow.Text("01   /   THE JOURNEY BEGINS", TextRole.Caption, true);
                }
            }
            else
            {
                flow.Text(left ? "IMPRESSIONS / SAMPLE COLLECTION" : "PLACES WORTH REMEMBERING", TextRole.Caption, true);
                flow.Gap(20);
                flow.Text(left ? "Along the way" : "Beyond the veil", TextRole.Title);
                flow.Gap(20);
                if (left)
                {
                    Stamp(ref flow, 0, "Salt & starlight", "Coastal journeys / Demo specimen", contentWidth * 0.46f);
                    flow.Gap(24);
                    Stamp(ref flow, 1, "Woodland wanderer", "Quiet paths / Demo specimen", contentWidth * 0.46f);
                }
                else
                {
                    Stamp(ref flow, 2, "A sky full of possibilities", "Celestial journeys / Demo specimen", contentWidth * 0.66f);
                    flow.Rule();
                    flow.Text("Every impression holds a small story. A place, a moment, a reason to return.");
                    flow.Gap(24);
                    flow.Text("These are visual specimens, not collected or issued stamps.", TextRole.Caption, true);
                    flow.Gap(24);
                    flow.Text("02-03   /   DEMO COLLECTION", TextRole.Caption, true);
                }
            }
        }

        void Stamp(ref PageFlow flow, int index, string title, string caption, float size)
        {
            var row = flow.Reserve(size);
            if (flow.IsDrawing)
                BookSurface.Stamp(new UiRect(row.Position + new Vector2((row.Size.X - size) / 2f, 0), new Vector2(size)), index, assets);
            flow.Gap(8);
            flow.Text(title, TextRole.Heading);
            flow.Gap(4);
            flow.Text(caption, TextRole.Caption, true);
        }
    }
}
