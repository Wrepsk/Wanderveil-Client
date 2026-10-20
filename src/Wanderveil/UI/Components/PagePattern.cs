using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI.Components;

// The paper's background pattern, drawn live over the page plate in the rings' colour and as faintly as the printed
// rings (which the plate carries for Guilloche, and which are inked out for every other background). Drawn flat in
// the paper's own rectangle, so a turning leaf warps it with the page; long lines are drawn in short steps so they
// bend with the paper. Nothing is drawn past the paper's edge.
internal static class PagePattern
{
    // The printed rings' strength (tools/passport-art/svg/book-open.svg, stroke-opacity .05); dots and emblems, being
    // smaller, print a little stronger to show as much.
    private const float Strength = 0.05f;
    // Straight lines are drawn in this many pieces, so they bend with a curling leaf.
    private const int Steps = 12;

    // unit is the pattern's cell size; by default a twentieth of the paper's width, as on a page.
    internal static void Draw(ImDrawListPtr list, PageBackground background, UiRect paper, Vector4 ink, PassportEdition edition, IBookAssets? assets, float alpha = 1f, float? unit = null)
    {
        if (background is PageBackground.Guilloche or PageBackground.Plain || paper.Size.X < 4 || paper.Size.Y < 4) return;
        var u = unit ?? paper.Size.X / 20f;
        var thickness = MathF.Max(1f, paper.Size.X * 0.0022f);
        var colour = ImGui.GetColorU32(ink with { W = Strength * alpha });
        switch (background)
        {
            case PageBackground.Lined:
                for (var y = paper.Position.Y + u * 2; y < paper.Max.Y - u; y += u * 1.1f) Straight(new Vector2(paper.Position.X, y), new Vector2(paper.Max.X, y));
                break;
            case PageBackground.Grid:
                for (var y = paper.Position.Y + u / 2; y < paper.Max.Y; y += u) Straight(new Vector2(paper.Position.X, y), new Vector2(paper.Max.X, y));
                for (var x = paper.Position.X + u / 2; x < paper.Max.X; x += u) Straight(new Vector2(x, paper.Position.Y), new Vector2(x, paper.Max.Y));
                break;
            case PageBackground.Dots:
                var dot = ImGui.GetColorU32(ink with { W = Strength * 2.4f * alpha });
                for (var y = paper.Position.Y + u / 2; y < paper.Max.Y; y += u * 0.9f)
                    for (var x = paper.Position.X + u / 2; x < paper.Max.X; x += u * 0.9f)
                        list.AddCircleFilled(new Vector2(x, y), thickness * 1.2f, dot, 8);
                break;
            case PageBackground.Zigzag:
                for (var y = paper.Position.Y + u; y < paper.Max.Y - u * 0.4f; y += u * 1.4f)
                    // A triangle wave: peaks every half cell.
                    Along(y, x => (1f - 4f * MathF.Abs((x - paper.Position.X) / u % 1f - 0.5f)) * u * 0.35f, u / 2);
                break;
            case PageBackground.Waves:
                for (var y = paper.Position.Y + u; y < paper.Max.Y - u * 0.4f; y += u * 1.4f)
                    Along(y, x => MathF.Sin((x - paper.Position.X) / (u * 2) * MathF.Tau) * u * 0.3f, u / 4);
                break;
            case PageBackground.Edition:
                if (assets == null || PassportSymbol.Find("emblem-" + edition.Id) is not { } emblem) break;
                var tint = ink with { W = Strength * 1.8f * alpha };
                var size = u * 2.2f;
                var row = 0;
                for (var y = paper.Position.Y + u * 2f; y + size / 2 <= paper.Max.Y; y += u * 3.6f, row++)
                    for (var x = paper.Position.X + u * (row % 2 == 0 ? 2f : 3.8f); x + size / 2 <= paper.Max.X; x += u * 3.6f)
                        SymbolArt.Draw(list, assets, emblem, new Vector2(x, y), size, 0f, tint);
                break;
        }

        void Straight(Vector2 from, Vector2 to)
        {
            for (var step = 1; step <= Steps; step++) list.AddLine(Vector2.Lerp(from, to, (step - 1) / (float)Steps), Vector2.Lerp(from, to, step / (float)Steps), colour, thickness);
        }

        // A row across the paper, offset from y by shape(x), in steps of spacing (the last one ending at the edge).
        void Along(float y, Func<float, float> shape, float spacing)
        {
            Vector2 At(float x) => new(x, Math.Clamp(y + shape(x), paper.Position.Y, paper.Max.Y));
            var previous = At(paper.Position.X);
            for (var x = paper.Position.X + spacing; previous.X < paper.Max.X; x += spacing)
            {
                var next = At(MathF.Min(x, paper.Max.X));
                list.AddLine(previous, next, colour, thickness);
                previous = next;
            }
        }
    }

    // The printed rings, drawn for a background's small preview (the plate carries them on the pages themselves).
    internal static void Rings(ImDrawListPtr list, UiRect paper, Vector4 ink)
    {
        var colour = ImGui.GetColorU32(ink with { W = Strength * 2f });
        var centre = paper.Position + paper.Size * new Vector2(0.5f, 0.6f);
        var step = MathF.Max(3f, paper.Size.X / 12f);
        list.PushClipRect(paper.Position, paper.Max, true);
        for (var radius = step; radius < paper.Size.X; radius += step) list.AddCircle(centre, radius, colour, 32, 1f);
        list.PopClipRect();
    }
}
