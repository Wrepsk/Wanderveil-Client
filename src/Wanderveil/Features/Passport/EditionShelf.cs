using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

// The editions side by side as small closed covers, each named beneath with its region, for choosing one.
// Each cover is the edition's real cover, drawn as the closed book draws it.
internal static class EditionShelf
{
    // The cover plate's board within its 1024 x 1536 image (the rest is its soft shadow margin).
    private const float BoardTop = 141f / 1536f, BoardHeight = 1253f / 1536f;

    // labels: name and region beneath each cover; without them (a narrow row) hovering names a cover, and the
    // chosen one is marked by a bar beneath it.
    internal static float Height(float width, int columns, UiContext context, bool labels = true)
    {
        var rows = (PassportEdition.All.Count + columns - 1) / columns;
        return rows * Row(width, columns, context, labels) - Gap(context);
    }

    // Draws the shelf at origin; returns the edition clicked, if any. current is the passport's edition, marked
    // as chosen; pending is one picked and waiting for confirmation, marked as picked. Only a live shelf takes input.
    internal static PassportEdition? Draw(PassportDocumentView view, Vector2 origin, float width, int columns, string? current, string? pending, bool live, UiContext context, bool labels = true)
    {
        var list = ImGui.GetWindowDrawList();
        var gap = Gap(context);
        var cell = (width - gap * (columns - 1)) / columns;
        var plate = new Vector2(cell, cell * 1.5f);
        var row = Row(width, columns, context, labels);
        PassportEdition? clicked = null;
        for (var index = 0; index < PassportEdition.All.Count; index++)
        {
            var edition = PassportEdition.All[index];
            var at = origin + new Vector2(index % columns * (cell + gap), index / columns * row);
            var window = new UiRect(at - new Vector2(0, plate.Y * BoardTop), plate);
            var board = new UiRect(at + new Vector2(cell * 42f / 1024f, 0), new Vector2(cell * 940f / 1024f, plate.Y * BoardHeight));
            view.DrawCover(window, context, edition: edition);
            var hovered = false;
            if (live)
            {
                ImGui.SetCursorScreenPos(board.Position);
                ImGui.PushID("Edition " + edition.Id);
                if (ImGui.InvisibleButton("##Edition", board.Size)) clicked = edition;
                hovered = ImGui.IsItemHovered();
                ImGui.PopID();
                if (hovered) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip($"{edition.Name} / {edition.Region}"); }
            }
            var chosen = edition.Id == current;
            var picked = edition.Id == pending;
            if (hovered || picked) list.AddRect(board.Position - new Vector2(3 * context.Scale), board.Max + new Vector2(3 * context.Scale), ImGui.GetColorU32(UiTheme.Gold), 4 * context.Scale, ImDrawFlags.None, MathF.Max(1, 2 * context.Scale));
            if (!labels)
            {
                if (chosen) BookSurface.Line(list, new Vector2(board.Position.X, board.Max.Y + 5 * context.Scale), new Vector2(board.Max.X, board.Max.Y + 5 * context.Scale), ImGui.GetColorU32(UiTheme.Ink), MathF.Max(1, 2 * context.Scale));
                continue;
            }
            var label = new Vector2(at.X, board.Max.Y + 6 * context.Scale);
            var name = UiTypography.Measure(edition.Name, TextRole.Body, context, cell, false);
            UiTypography.Draw(list, label + new Vector2((cell - name.X) / 2, 0), edition.Name, TextRole.Body, context, cell, chosen || hovered || picked ? UiTheme.Ink : UiTheme.MutedInk, false);
            if (chosen)
            {
                var y = label.Y + name.Y + 1 * context.Scale;
                BookSurface.Line(list, new Vector2(label.X + (cell - name.X) / 2, y), new Vector2(label.X + (cell + name.X) / 2, y), ImGui.GetColorU32(UiTheme.Ink), MathF.Max(1, 1.5f * context.Scale));
            }
            var region = chosen ? "YOUR EDITION" : edition.Region;
            var small = UiTypography.Measure(region, TextRole.Caption, context, cell, false);
            UiTypography.Draw(list, label + new Vector2(MathF.Max(0, (cell - small.X) / 2), name.Y + 3 * context.Scale), region, TextRole.Caption, context, cell, UiTheme.MutedInk, false);
        }
        return clicked;
    }

    private static float Gap(UiContext context) => 14 * context.Scale;

    // One row: the board, then the name and region beneath, then the gap to the next row.
    private static float Row(float width, int columns, UiContext context, bool labels)
    {
        var cell = (width - Gap(context) * (columns - 1)) / columns;
        var under = labels ? 9 * context.Scale + UiTypography.Size(TextRole.Body, context) + UiTypography.Size(TextRole.Caption, context) : 8 * context.Scale;
        return cell * 1.5f * BoardHeight + under + Gap(context);
    }
}
