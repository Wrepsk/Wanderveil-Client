using System;
using System.Numerics;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Fixed material silhouette; overflowing live content scrolls inside the paper.
internal readonly record struct BookShellLayout(UiRect Book, bool IsSpread, float ControlHeight, UiRect LeftPage, UiRect RightPage, UiRect Footer, float NavigationHeight, bool StackControls)
{
    internal static Vector2 Fit(float preferredWidth, Vector2 room, UiContext context, bool closed = false)
    {
        if (!float.IsFinite(preferredWidth) || preferredWidth < 1 || !float.IsFinite(room.X) || !float.IsFinite(room.Y) || room.X < 1 || room.Y < 1)
            throw new ArgumentOutOfRangeException(nameof(room));
        if (!float.IsFinite(context.Scale) || context.Scale <= 0 || !float.IsFinite(context.FontSize) || context.FontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        var control = Control(context);
        if (closed)
        {
            var wanted = MathF.Max(preferredWidth * context.Scale * 0.54f, 300 * MathF.Max(context.Scale, context.FontSize / 18));
            var coverWidth = MathF.Min(wanted, MathF.Min(room.X, room.Y / 1.5f));
            return Pixels(coverWidth, MathF.Floor(coverWidth) * 1.5f);
        }
        var spreadWidth = MathF.Max(1, MathF.Floor(MathF.Min(preferredWidth * context.Scale, MathF.Min(room.X, MathF.Max(1, room.Y - control) / (0.925f * 2f / 3f)))));
        if (Spread(spreadWidth, context)) return Pixels(spreadWidth, control + 0.925f * BookSurface.NaturalHeight(spreadWidth, true));
        var width = MathF.Max(1, MathF.Floor(MathF.Min(preferredWidth * context.Scale * 0.6f, MathF.Min(room.X, MathF.Max(1, room.Y - control * 2) / (0.925f * 4f / 3f)))));
        return Pixels(width, control * 2 + 0.925f * BookSurface.NaturalHeight(width, false));
    }

    internal static BookShellLayout Create(UiRect window, UiContext context, int page)
    {
        var spread = Spread(window.Size.X, context);
        var control = Control(context);
        var navHeight = control * (spread ? 1 : 2);
        var natural = BookSurface.NaturalHeight(window.Size.X, spread);
        var book = new UiRect(window.Position + new Vector2(0, navHeight - natural * 0.075f), new Vector2(window.Size.X, natural));
        var footerWidth = book.Size.X * (spread ? 0.79f : 0.74f);
        var small = MathF.Max(28 * context.Scale, context.FontSize + 10 * context.Scale);
        var stackControls = footerWidth < 80 * MathF.Max(context.Scale, context.FontSize / 18) + 120 * MathF.Max(context.Scale, context.FontSize / 18) + small * 3 + 20 * context.Scale;
        var footerHeight = stackControls ? control * 2 + 5 * context.Scale : control;
        var footer = new UiRect(book.Position + new Vector2(book.Size.X * (spread ? 0.11f : 0.16f), natural * 0.90f - footerHeight), new Vector2(footerWidth, footerHeight));
        var left = BookSurface.Content(book, spread ? BookFace.Spread : page % 2 == 1 ? BookFace.Right : BookFace.Left);
        var right = spread ? BookSurface.RightContent(book) : left;
        left = left with { Size = new Vector2(left.Size.X, MathF.Max(1, footer.Position.Y - left.Position.Y - 12 * context.Scale)) };
        right = right with { Size = new Vector2(right.Size.X, left.Size.Y) };
        return new BookShellLayout(book, spread, control, left, right, footer, navHeight, stackControls);
    }

    internal UiRect Bookmark(int index)
    {
        var columns = IsSpread ? 4 : 2;
        var width = Book.Size.X * 0.84f / columns;
        return new UiRect(new Vector2(Book.Position.X + Book.Size.X * 0.08f + index % columns * width, Book.Position.Y + Book.Size.Y * 0.075f - NavigationHeight + index / columns * ControlHeight), new Vector2(width - 4, ControlHeight));
    }

    internal static float GetControlHeight(UiContext context) => Control(context);
    internal static UiRect OpenButton(UiRect cover, UiContext context) => new(cover.Position + new Vector2(cover.Size.X * 0.30f, cover.Size.Y * 0.78f), new Vector2(cover.Size.X * 0.48f, Control(context)));

    private static bool Spread(float width, UiContext context) => width * 0.35f >= 230f * MathF.Max(context.Scale, context.FontSize / 16f);
    private static Vector2 Pixels(float width, float height) => new(MathF.Max(1, MathF.Floor(width)), MathF.Max(1, MathF.Floor(height)));
    private static float Control(UiContext context) => MathF.Max(30 * context.Scale, UiTypography.Size(TextRole.Caption, context) + 14 * context.Scale);
}
