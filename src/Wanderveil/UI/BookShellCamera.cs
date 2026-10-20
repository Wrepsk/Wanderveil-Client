using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

// Take a picture: while the book steps aside, the screen dims around a 3:4 frame guide, and a small paper panel under it
// offers Take, a countdown (off, 3, 5, or 10 seconds, kept for the session), and Cancel, with a tip about posing. Take
// (after the countdown) captures what is inside the guide; the capture is taken before any plugin window draws, so
// neither the guide nor the panel is in the picture. Escape cancels, also mid-countdown. The guide's edges move it and
// its corners resize it (staying 3:4), kept for the session; inside, the mouse passes through to the game's camera.
internal sealed class BookShellCamera(IPassportPortrait? picture)
{
    internal static readonly int[] Countdowns = [0, 3, 5, 10];

    // The countdown chosen (an index into Countdowns); 3 seconds at first.
    internal int Countdown { get; set; } = 1;
    // Seconds left before the picture is taken, while counting down.
    internal float? Remaining { get; private set; }
    internal UiRect Guide { get; private set; }
    // Where the guide sits: its centre (0 to 1 across and down the screen) and its height as a share of the screen's.
    private Vector2 centre = new(0.5f, 0.42f);
    private float share = 0.68f;
    // How thick the edges that move the guide are, and how large its corner handles, last frame.
    private float grip = 8f;
    // Where the panel lay last frame: the only part of the screen that takes the mouse.
    internal UiRect Panel { get; private set; }
    // For checks: where each control was drawn last frame, by name.
    internal Dictionary<string, Vector2> Controls { get; } = new(StringComparer.Ordinal);

    internal bool Available => picture?.CanTake == true;

    // Whether the guide's window takes the mouse at a point: on its panel, or on the guide's edges and corners. Inside
    // the guide and around it, the mouse passes to the game.
    internal bool TakesMouse(Vector2 point)
    {
        if (Panel.Contains(point)) return true;
        var outer = new UiRect(Guide.Position - new Vector2(grip), Guide.Size + new Vector2(grip * 2));
        var inner = new UiRect(Guide.Position + new Vector2(grip), Vector2.Max(Vector2.Zero, Guide.Size - new Vector2(grip * 2)));
        return outer.Contains(point) && !inner.Contains(point) || dragging;
    }

    private bool dragging;

    // The guide from its centre and share, kept within the screen and at least a fifth of its height.
    private UiRect Place(UiRect screen)
    {
        share = Math.Clamp(share, 0.2f, 0.95f);
        var height = MathF.Round(screen.Size.Y * share);
        var width = MathF.Round(height * PassportDocumentView.PortraitAspect);
        if (width > screen.Size.X * 0.95f) { width = MathF.Round(screen.Size.X * 0.95f); height = MathF.Round(width / PassportDocumentView.PortraitAspect); share = height / screen.Size.Y; }
        var middle = screen.Position + centre * screen.Size;
        var min = Vector2.Clamp(middle - new Vector2(width, height) / 2, screen.Position, Vector2.Max(screen.Position, screen.Max - new Vector2(width, height)));
        centre = (min + new Vector2(width, height) / 2 - screen.Position) / Vector2.Max(Vector2.One, screen.Size);
        return new UiRect(min, new Vector2(width, height));
    }

    // The guide's edges move it; its corners resize it from the opposite corner, staying 3:4. Returns the guide as it
    // now lies.
    private UiRect Handles(UiRect screen, UiRect guide, UiContext context)
    {
        if (Remaining != null) { dragging = false; return guide; }
        var io = ImGui.GetIO();
        var any = false;
        // Edges: four strips along the sides, between the corners.
        var edges = new[]
        {
            new UiRect(guide.Position + new Vector2(grip, -grip), new Vector2(guide.Size.X - grip * 2, grip * 2)),
            new UiRect(new Vector2(guide.Position.X + grip, guide.Max.Y - grip), new Vector2(guide.Size.X - grip * 2, grip * 2)),
            new UiRect(guide.Position + new Vector2(-grip, grip), new Vector2(grip * 2, guide.Size.Y - grip * 2)),
            new UiRect(new Vector2(guide.Max.X - grip, guide.Position.Y + grip), new Vector2(grip * 2, guide.Size.Y - grip * 2)),
        };
        for (var index = 0; index < edges.Length; index++)
        {
            ImGui.SetCursorScreenPos(edges[index].Position);
            ImGui.InvisibleButton("Guide edge " + index, Vector2.Max(Vector2.One, edges[index].Size));
            if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemActive())
            {
                any = true;
                centre += io.MouseDelta / Vector2.Max(Vector2.One, screen.Size);
            }
        }
        Controls["Guide edge"] = edges[0].Position + edges[0].Size / 2;
        // Corners: each resizes from the opposite one.
        var corners = new[] { (guide.Position, guide.Max), (new Vector2(guide.Max.X, guide.Position.Y), new Vector2(guide.Position.X, guide.Max.Y)), (new Vector2(guide.Position.X, guide.Max.Y), new Vector2(guide.Max.X, guide.Position.Y)), (guide.Max, guide.Position) };
        for (var index = 0; index < corners.Length; index++)
        {
            var (corner, anchor) = corners[index];
            ImGui.SetCursorScreenPos(corner - new Vector2(grip * 1.5f));
            ImGui.InvisibleButton("Guide corner " + index, new Vector2(grip * 3));
            if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(index is 0 or 3 ? ImGuiMouseCursor.ResizeNwse : ImGuiMouseCursor.ResizeNesw);
            if (ImGui.IsItemActive())
            {
                any = true;
                var reach = Vector2.Abs(io.MousePos - anchor);
                var height = MathF.Max(reach.Y, reach.X / PassportDocumentView.PortraitAspect);
                height = Math.Clamp(height, screen.Size.Y * 0.2f, screen.Size.Y * 0.95f);
                var size = new Vector2(height * PassportDocumentView.PortraitAspect, height);
                var toward = new Vector2(MathF.Sign(corner.X - anchor.X), MathF.Sign(corner.Y - anchor.Y));
                share = height / screen.Size.Y;
                centre = (anchor + toward * size / 2 - screen.Position) / Vector2.Max(Vector2.One, screen.Size);
            }
            Controls["Guide corner " + index] = corner;
        }
        dragging = any;
        return any ? Place(screen) : guide;
    }

    internal void Reset() => Remaining = null;

    // Draws the guide over screen. Returns true when the picture was taken, false when cancelled, and null meanwhile.
    internal bool? Draw(UiRect screen, UiContext context, float seconds, bool escape)
    {
        Controls.Clear();
        if (escape) { Remaining = null; return false; }
        var list = ImGui.GetWindowDrawList();
        grip = MathF.Max(8f, 10 * context.Scale);
        var guide = Place(screen);
        guide = Handles(screen, guide, context);
        Guide = guide;
        var height = guide.Size.Y;
        var width = guide.Size.X;

        // The rest of the screen dims; the guide is outlined, with brighter corners.
        var dim = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.45f));
        list.AddRectFilled(screen.Position, new Vector2(screen.Max.X, guide.Position.Y), dim);
        list.AddRectFilled(new Vector2(screen.Position.X, guide.Max.Y), screen.Max, dim);
        list.AddRectFilled(new Vector2(screen.Position.X, guide.Position.Y), new Vector2(guide.Position.X, guide.Max.Y), dim);
        list.AddRectFilled(new Vector2(guide.Max.X, guide.Position.Y), new Vector2(screen.Max.X, guide.Max.Y), dim);
        var line = MathF.Max(1f, context.Scale);
        list.AddRect(guide.Position, guide.Max, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.55f)), 0, ImDrawFlags.None, line);
        var arm = MathF.Min(width, height) * 0.08f;
        var white = ImGui.GetColorU32(Vector4.One);
        foreach (var (corner, across, down) in new[] { (guide.Position, 1f, 1f), (new Vector2(guide.Max.X, guide.Position.Y), -1f, 1f), (new Vector2(guide.Position.X, guide.Max.Y), 1f, -1f), (guide.Max, -1f, -1f) })
        {
            list.AddLine(corner, corner + new Vector2(arm * across, 0), white, line * 3);
            list.AddLine(corner, corner + new Vector2(0, arm * down), white, line * 3);
        }

        // Counting down: the seconds left, large in the guide; at zero, the picture is taken.
        if (Remaining is { } left)
        {
            left -= UiMotion.Frame(seconds);
            if (left <= 0f)
            {
                Remaining = null;
                picture?.Take(guide.Position, guide.Max);
                return true;
            }
            Remaining = left;
            var number = ((int)MathF.Ceiling(left)).ToString(CultureInfo.InvariantCulture);
            var big = context with { FontSize = MathF.Max(24f, height * 0.18f) / 1.65f, TitleFont = context.DisplayFont };
            var size = UiTypography.Measure(number, TextRole.Title, big, float.MaxValue, false);
            UiTypography.Draw(list, guide.Position + (guide.Size - size) / 2 + new Vector2(2, 3), number, TextRole.Title, big, float.MaxValue, new Vector4(0, 0, 0, 0.5f), false);
            UiTypography.Draw(list, guide.Position + (guide.Size - size) / 2, number, TextRole.Title, big, float.MaxValue, Vector4.One, false);
        }

        // The panel: Take, the countdown, and Cancel on one line, a tip beneath.
        var pad = 12 * context.Scale;
        var gap = 16 * context.Scale;
        var control = BookShellLayout.GetControlHeight(context);
        const string Tip = "Drag the frame's edges to move it and its corners to resize it. Pose anywhere: in group pose (/gpose) the game's HUD hides and you can choose poses and expressions; elsewhere its Hide UI key (Scroll Lock by default) hides it, but not this.";
        var labels = new List<string> { Remaining == null ? "Take" : "Taking" };
        foreach (var seconds0 in Countdowns) labels.Add(seconds0 == 0 ? "No countdown" : $"{seconds0} s");
        labels.Add("Cancel");
        var widths = labels.ConvertAll(label => UiTypography.Measure(label, TextRole.Caption, context, float.MaxValue, false).X + 16 * context.Scale);
        var row = 0f;
        foreach (var each in widths) row += each;
        row += gap * 2 + 4 * context.Scale * (Countdowns.Length - 1);
        var panelWidth = MathF.Min(screen.Size.X - pad * 2, MathF.Max(row, width) + pad * 2);
        var tipSize = UiTypography.Measure(Tip, TextRole.Caption, context, panelWidth - pad * 2, true);
        // Under the guide where it fits, else over it, else at the screen's foot; centred on the guide where it can be.
        var panelHeight = pad * 2 + control + 6 * context.Scale + tipSize.Y;
        var space = 14 * context.Scale;
        var top = guide.Max.Y + space + panelHeight <= screen.Max.Y - 8 ? guide.Max.Y + space
            : guide.Position.Y - space - panelHeight >= screen.Position.Y + 8 ? guide.Position.Y - space - panelHeight
            : screen.Max.Y - 8 - panelHeight;
        var panelX = Math.Clamp(guide.Position.X + (guide.Size.X - panelWidth) / 2, screen.Position.X + 8, MathF.Max(screen.Position.X + 8, screen.Max.X - 8 - panelWidth));
        var panel = new UiRect(new Vector2(panelX, top), new Vector2(panelWidth, panelHeight));
        Panel = panel;
        PageSurface.Draw(list, panel, context);
        var x = panel.Position.X + pad;
        var y = panel.Position.Y + pad;
        ImGui.BeginDisabled(Remaining != null || !Available);
        if (BookControl.Draw("Camera take", labels[0], new UiRect(new Vector2(x, y), new Vector2(widths[0], control)), context, BookControlStyle.Ink, hint: "Take the picture inside the frame")) Start();
        ImGui.EndDisabled();
        Controls["Take"] = new Vector2(x + widths[0] / 2, y + control / 2);
        x += widths[0] + gap;
        for (var index = 0; index < Countdowns.Length; index++)
        {
            var cell = new UiRect(new Vector2(x, y), new Vector2(widths[index + 1], control));
            ImGui.BeginDisabled(Remaining != null);
            if (BookControl.Draw("Camera countdown " + index, labels[index + 1], cell, context, BookControlStyle.Ink, Countdown == index, hint: Countdowns[index] == 0 ? "Take the picture at once" : $"Count down {Countdowns[index]} seconds first")) Countdown = index;
            ImGui.EndDisabled();
            if (Countdown == index) list.AddLine(new Vector2(cell.Position.X + 6 * context.Scale, cell.Max.Y - 2), new Vector2(cell.Max.X - 6 * context.Scale, cell.Max.Y - 2), ImGui.GetColorU32(UiTheme.Ink), MathF.Max(1f, 1.5f * context.Scale));
            Controls["Countdown " + index] = cell.Position + cell.Size / 2;
            x += cell.Size.X + 4 * context.Scale;
        }
        x += gap - 4 * context.Scale;
        var cancel = BookControl.Draw("Camera cancel", labels[^1], new UiRect(new Vector2(x, y), new Vector2(widths[^1], control)), context, BookControlStyle.Ink, hint: "Back to the passport (Escape)");
        Controls["Cancel"] = new Vector2(x + widths[^1] / 2, y + control / 2);
        UiTypography.Draw(list, new Vector2(panel.Position.X + pad, y + control + 6 * context.Scale), Tip, TextRole.Caption, context, panelWidth - pad * 2, UiTheme.MutedInk, true);
        if (cancel) { Remaining = null; return false; }
        return null;

        void Start()
        {
            if (Countdowns[Countdown] == 0) Remaining = 0.001f;
            else Remaining = Countdowns[Countdown];
        }
    }
}
