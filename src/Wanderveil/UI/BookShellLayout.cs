using System;
using System.Numerics;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Fixed material silhouette; overflowing live content scrolls inside the paper.
// Index tabs sit above the board; printed controls and folios sit in the paper's lower margin.
internal readonly record struct BookShellLayout(UiRect Book, bool IsSpread, float ControlHeight, UiRect LeftPage, UiRect RightPage, UiRect Footer, float NavigationHeight, bool StackControls)
{
    // Opaque top of the open-book plate (tools/passport-art/svg/book-open.svg: board at y 44 of 1024).
    private const float BoardTop = 0.043f;

    internal static Vector2 Fit(float preferredWidth, Vector2 room, UiContext context, bool closed = false)
    {
        if (!float.IsFinite(preferredWidth) || preferredWidth < 1 || !float.IsFinite(room.X) || !float.IsFinite(room.Y) || room.X < 1 || room.Y < 1)
            throw new ArgumentOutOfRangeException(nameof(room));
        if (!float.IsFinite(context.Scale) || context.Scale <= 0 || !float.IsFinite(context.FontSize) || context.FontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        var control = Control(context);
        if (closed)
        {
            // The closed cover is exactly the leaf that swings open, so opening starts where the cover rests.
            var open = Fit(preferredWidth, room, context);
            var size = ClosedSize(LeafWidth(Create(new UiRect(Vector2.Zero, open), context, 0)));
            size *= MathF.Min(1f, MathF.Min(room.X / size.X, room.Y / size.Y));
            return Pixels(size.X, size.Y);
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
        var rows = TabWidth(window.Size.X, 4) >= TabLabel(context) ? 1 : 2;
        var navHeight = control * rows;
        var natural = BookSurface.NaturalHeight(window.Size.X, spread);
        var book = new UiRect(window.Position + new Vector2(0, navHeight - natural * BoardTop), new Vector2(window.Size.X, natural));
        var footerWidth = book.Size.X * (spread ? 0.79f : 0.70f);
        var stackControls = footerWidth < SettingsWidth(context) + Small(context) * 4 + Gap(context) * 3;
        var footerHeight = stackControls ? control * 2 : control;
        var footer = new UiRect(book.Position + new Vector2(book.Size.X * (spread ? 0.11f : page % 2 == 1 ? 0.10f : 0.22f), natural * 0.925f - footerHeight), new Vector2(footerWidth, footerHeight));
        var left = BookSurface.Content(book, spread ? BookFace.Spread : page % 2 == 1 ? BookFace.Right : BookFace.Left);
        var right = spread ? BookSurface.RightContent(book) : left;
        left = left with { Size = new Vector2(left.Size.X, MathF.Max(1, footer.Position.Y - left.Position.Y - 6 * context.Scale)) };
        right = right with { Size = new Vector2(right.Size.X, left.Size.Y) };
        return new BookShellLayout(book, spread, control, left, right, footer, navHeight, stackControls);
    }

    internal UiRect Bookmark(int index)
    {
        var columns = NavigationHeight > ControlHeight * 1.5f ? 2 : 4;
        var width = TabWidth(Book.Size.X, columns);
        var top = Book.Position.Y + Book.Size.Y * BoardTop - NavigationHeight;
        return new UiRect(new Vector2(Book.Position.X + Book.Size.X * 0.08f + index % columns * (width + 4), top + index / columns * ControlHeight), new Vector2(width, ControlHeight));
    }

    // Printed controls: Settings at the lower-left margin, then smaller / larger / close at the lower right.
    internal UiRect Settings(UiContext context) => new(Footer.Position, new Vector2(SettingsWidth(context), ControlHeight));

    internal UiRect Tool(int index, UiContext context)
    {
        var small = Small(context);
        var gap = Gap(context);
        var y = Footer.Max.Y - ControlHeight;
        return new UiRect(new Vector2(Footer.Max.X - (3 - index) * small - (2 - index) * gap, y), new Vector2(small, ControlHeight));
    }

    // Centre of each visible page's folio in the lower margin.
    internal Vector2 Folio(bool right) => new((right ? RightPage : LeftPage).Position.X + (right ? RightPage : LeftPage).Size.X / 2, Footer.Position.Y + ControlHeight / 2);

    // A quiet point on the outer paper margin of the given side, clear of page content.
    internal Vector2 TurnPoint(bool forward, int page)
    {
        var x = IsSpread ? forward ? 0.935f : 0.065f : page % 2 == 1 ? forward ? 0.86f : 0.05f : forward ? 0.955f : 0.15f;
        return Book.Position + new Vector2(Book.Size.X * x, Book.Size.Y * 0.5f);
    }

    // The cover leaf spans the right page's board in a spread (spine to outer edge), or a single page's board.
    internal static float LeafWidth(BookShellLayout layout) => layout.Book.Size.X * (layout.IsSpread ? 728f / 1536f : 728f / 768f);

    // The closed cover plate whose board (940 of its 1024 width) is a leaf of this width.
    internal static Vector2 ClosedSize(float leafWidth)
    {
        var width = leafWidth * 1024f / 940f;
        return new Vector2(width, width * 1.5f);
    }

    // The flat cover plate placement whose board is exactly board.
    internal static UiRect CoverPlate(UiRect board)
    {
        var size = ClosedSize(board.Size.X);
        return new UiRect(board.Position - new Vector2(size.X * 42f / 1024f, size.Y * 141f / 1536f), size);
    }

    internal static Vector2 OpenPoint(UiRect cover) => cover.Position + new Vector2(cover.Size.X * 0.8f, cover.Size.Y * 0.45f);

    internal static float GetControlHeight(UiContext context) => Control(context);
    internal static float Small(UiContext context) => MathF.Round(MathF.Max(22 * context.Scale, UiTypography.Size(TextRole.Caption, context) + 8 * context.Scale));
    internal static float Gap(UiContext context) => 4 * context.Scale;
    internal static float SettingsWidth(UiContext context) => MathF.Round(MathF.Max(64 * context.Scale, UiTypography.Size(TextRole.Caption, context) * 5f));

    private static float TabWidth(float width, int columns) => (width * 0.84f - 4 * (columns - 1)) / columns;
    // Widest label ("Passport"/"Discover") estimated without a live font, plus side padding.
    private static float TabLabel(UiContext context) => UiTypography.Size(TextRole.Caption, context) * 4.8f + 16 * context.Scale;
    private static bool Spread(float width, UiContext context) => width * 0.35f >= 230f * MathF.Max(context.Scale, context.FontSize / 16f);
    private static Vector2 Pixels(float width, float height) => new(MathF.Max(1, MathF.Floor(width)), MathF.Max(1, MathF.Floor(height)));
    private static float Control(UiContext context) => MathF.Max(30 * context.Scale, UiTypography.Size(TextRole.Caption, context) + 14 * context.Scale);
}
