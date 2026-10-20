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
// neither the guide nor the panel is in the picture. Escape cancels, also mid-countdown.
internal sealed class BookShellCamera(IPassportPortrait? picture)
{
    internal static readonly int[] Countdowns = [0, 3, 5, 10];

    // The countdown chosen (an index into Countdowns); 3 seconds at first.
    internal int Countdown { get; set; } = 1;
    // Seconds left before the picture is taken, while counting down.
    internal float? Remaining { get; private set; }
    internal UiRect Guide { get; private set; }
    // Where the panel lay last frame: the only part of the screen that takes the mouse.
    internal UiRect Panel { get; private set; }
    // For checks: where each control was drawn last frame, by name.
    internal Dictionary<string, Vector2> Controls { get; } = new(StringComparer.Ordinal);

    internal bool Available => picture?.CanTake == true;

    internal void Reset() => Remaining = null;

    // Draws the guide over screen. Returns true when the picture was taken, false when cancelled, and null meanwhile.
    internal bool? Draw(UiRect screen, UiContext context, float seconds, bool escape)
    {
        Controls.Clear();
        if (escape) { Remaining = null; return false; }
        var list = ImGui.GetWindowDrawList();
        var height = MathF.Round(screen.Size.Y * 0.68f);
        var width = MathF.Round(height * PassportDocumentView.PortraitAspect);
        var guide = new UiRect(screen.Position + new Vector2((screen.Size.X - width) / 2, screen.Size.Y * 0.08f), new Vector2(width, height));
        Guide = guide;

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
        const string Tip = "Pose anywhere. In group pose (/gpose) the game's HUD hides and you can choose poses and expressions; elsewhere its Hide UI key (Scroll Lock by default) hides it.";
        var labels = new List<string> { Remaining == null ? "Take" : "Taking" };
        foreach (var seconds0 in Countdowns) labels.Add(seconds0 == 0 ? "No countdown" : $"{seconds0} s");
        labels.Add("Cancel");
        var widths = labels.ConvertAll(label => UiTypography.Measure(label, TextRole.Caption, context, float.MaxValue, false).X + 16 * context.Scale);
        var row = 0f;
        foreach (var each in widths) row += each;
        row += gap * 2 + 4 * context.Scale * (Countdowns.Length - 1);
        var panelWidth = MathF.Min(screen.Size.X - pad * 2, MathF.Max(row, width) + pad * 2);
        var tipSize = UiTypography.Measure(Tip, TextRole.Caption, context, panelWidth - pad * 2, true);
        var panel = new UiRect(new Vector2(screen.Position.X + (screen.Size.X - panelWidth) / 2, guide.Max.Y + 14 * context.Scale), new Vector2(panelWidth, pad * 2 + control + 6 * context.Scale + tipSize.Y));
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
