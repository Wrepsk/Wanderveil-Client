using System.Collections.Immutable;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features;
using Wanderveil.Features.Drawing;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.Infrastructure.Storage;
using Wanderveil.UI;

namespace Wanderveil.UiChecks;

// Drawing and writing on the stamp pages: the marks' model (rubbing out splits strokes, undo, limits, writing), their
// file, and Draw mode in the real shell (pencil, palette, eraser, undo, writing, and leaving it).
internal static class DrawingChecks
{
    private static readonly Rgb Graphite = Rgb.Hex(0x3A3D44);

    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Model(check);
        Storage(check);
        Shell(check, artifacts);
        Console.WriteLine("PASS drawing");
    }

    private static InkStroke Line(int sheet, Vector2 from, Vector2 to, int points = 50) =>
        new(sheet, Graphite, 0.006f, Enumerable.Range(0, points).Select(index => Vector2.Lerp(from, to, index / (float)(points - 1))).Select(point => new InkPoint(point.X, point.Y, 0.8f)).ToImmutableArray());

    private static void Model(Action<bool, string> check)
    {
        var marks = new PageMarks();
        var changes = 0;
        marks.Changed += () => changes++;
        check(!marks.AddStroke(new InkStroke(0, Graphite, 0.006f, [new InkPoint(0.5f, 0.5f, 1f)])) && !marks.AddStroke(Line(-1, Vector2.Zero, Vector2.One)), "A one-point stroke or one off the pages is kept.");
        check(marks.AddStroke(Line(0, new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.5f))) && changes == 1 && marks.Strokes.Count == 1, "Adding a stroke is not one change.");
        var wild = marks.AddStroke(new InkStroke(0, Graphite, 9f, [new InkPoint(-3f, 5f, 2f), new InkPoint(0.5f, float.NaN, 0.5f), new InkPoint(2f, -1f, -1f)]));
        check(wild && marks.Strokes[1].Points.Length == 2 && marks.Strokes[1].Width == 0.05f && marks.Strokes[1].Points.All(point => point.X >= -PageMarks.Margin && point.X <= 1 + PageMarks.Margin && point.Y >= -PageMarks.Margin && point.Y <= PageMarks.Depth && point.Pressure is >= 0 and <= 1),
            "A stroke is not kept on its page and margins, at a sensible width.");
        marks.Undo();
        // Rubbing out the middle of a line leaves two; missing it changes nothing.
        check(!marks.Erase(0, new Vector2(0.5f, 0.9f), 0.02f) && marks.Erase(0, new Vector2(0.5f, 0.5f), 0.05f), "The eraser changes a stroke it misses, or not one it crosses.");
        check(marks.StrokesOn(0).Count() == 2 && marks.StrokesOn(0).All(stroke => stroke.Points.All(point => MathF.Abs(point.X - 0.5f) > 0.05f)), "Rubbing out the middle of a stroke does not leave its two ends.");
        // A drag of the eraser is one step of undo.
        marks.BeginChange();
        marks.Erase(0, new Vector2(0.2f, 0.5f), 0.03f);
        marks.Erase(0, new Vector2(0.8f, 0.5f), 0.03f);
        marks.EndChange();
        var afterDrag = marks.Samples;
        check(marks.Undo() && marks.StrokesOn(0).Count() == 2 && marks.Samples > afterDrag && marks.Undo() && marks.StrokesOn(0).Count() == 1, "An eraser's drag is not one step of undo, or undo does not restore the stroke.");
        check(!marks.Erase(1, new Vector2(0.5f, 0.5f), 0.5f), "The eraser reaches another page.");

        // Writing: added, rewritten, taken away when emptied; cleaned and within its limits.
        check(marks.SetText(null, new PageText(0, "  A quiet\nmorning  ", 0.1f, 0.2f, 0.05f, Graphite)) && marks.Texts[0].Text == "A quiet morning", "Writing is not kept, tidied.");
        check(!marks.SetText(null, new PageText(0, "   ", 0.1f, 0.2f, 0.05f, Graphite)) && marks.SetText(0, marks.Texts[0] with { Text = "A bright morning" }) && marks.Texts[0].Text == "A bright morning", "Empty writing is kept, or rewriting fails.");
        check(marks.SetText(0, marks.Texts[0] with { Text = "" }) && marks.Texts.Count == 0, "Emptying a line does not take it away.");
        check(new PageMarks(texts: [new PageText(0, new string('a', 500), 0, 0, 0.05f, Graphite)]).Texts[0].Text.Length == PageMarks.TextLimit, "Writing is not cut to its limit.");
        var lines = new PageMarks();
        for (var index = 0; index < PageMarks.MaximumTextsPerPage; index++) lines.SetText(null, new PageText(2, "line", 0.1f, index * 0.04f, 0.04f, Graphite));
        check(!lines.SetText(null, new PageText(2, "one more", 0.1f, 0.1f, 0.04f, Graphite)) && lines.SetText(null, new PageText(3, "next page", 0.1f, 0.1f, 0.04f, Graphite)), "A page takes more lines than it may, or the limit spills onto another page.");
        // Erasing writing takes the whole line.
        check(lines.Erase(3, new Vector2(0.12f, 0.11f), 0.01f, text => (new Vector2(text.X, text.Y), new Vector2(text.X + 0.2f, text.Y + 0.05f))) && !lines.TextsOn(3).Any(), "The eraser does not take a line of writing it touches.");

        // A page holds at most its samples; the next page still has room.
        var busy = new PageMarks();
        while (busy.AddStroke(Line(5, new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f), PageMarks.MaximumStrokeSamples))) { }
        check(busy.SamplesOn(5) <= PageMarks.MaximumSamplesPerPage && busy.SamplesOn(5) > PageMarks.MaximumSamplesPerPage - PageMarks.MaximumStrokeSamples && busy.AddStroke(Line(6, Vector2.Zero, Vector2.One)), "A page holds more than its limit, or the limit spills onto the next page.");
        check(Line(0, Vector2.Zero, Vector2.One, 9000) is { } long_ && new PageMarks([long_]).Strokes[0].Points.Length == PageMarks.MaximumStrokeSamples, "A stroke is not cut to its limit.");
    }

    // Where a stroke's point lies on screen, on the page box it is measured from.
    private static Vector2 InkPointOnScreen(Wanderveil.UI.Primitives.UiRect page, InkPoint point) => page.Position + new Vector2(point.X, point.Y) * page.Size.X;

    private static void Storage(Action<bool, string> check)
    {
        var marks = new PageMarks([Line(0, new Vector2(0.123456f, 0.2f), new Vector2(0.8f, 1.3f)), Line(4, Vector2.Zero, new Vector2(1f, 0.5f), 3) with { Colour = Rgb.Hex(0xA3302A) }],
            [new PageText(1, "Ul'dah, at dusk", 0.2f, 0.3f, 0.055f, Rgb.Hex(0x2F4F8A))]);
        var text = MarksStore.Serialize(MarkBook.Of(marks));
        var read = MarksStore.Parse(text);
        check(read.Strokes.Count == 2 && read.Strokes[1].Colour == Rgb.Hex(0xA3302A) && read.Strokes[0].Points.Zip(marks.Strokes[0].Points).All(pair => MathF.Abs(pair.First.X - pair.Second.X) < 1e-4f && MathF.Abs(pair.First.Y - pair.Second.Y) < 1e-4f)
            && read.Texts.Single() == marks.Texts[0], "The drawing file does not round-trip strokes (to four decimals) and writing.");
        foreach (var bad in new[] { "{\"format\":\"wanderveil.passport-marks\",\"version\":1,\"strokes\":[{\"page\":0,\"colour\":\"#000000\",\"width\":0.01,\"points\":[0,0]}],\"texts\":[]}",
            "{\"format\":\"wanderveil.passport-marks\",\"version\":1,\"strokes\":[],\"texts\":[{\"page\":-1,\"text\":\"a\",\"x\":0,\"y\":0,\"size\":0.05,\"colour\":\"#000000\"}]}",
            "{\"format\":\"wanderveil.passport-marks\",\"version\":1,\"strokes\":[]}", "{\"format\":\"wanderveil.passport-stamps\",\"version\":1}" })
        {
            try { MarksStore.Parse(bad); check(false, $"A malformed drawing file is read: {bad}"); }
            catch (FormatException) { check(true, ""); }
        }
        try { MarksStore.Parse("{\"format\":\"wanderveil.passport-marks\",\"version\":2,\"strokes\":[],\"texts\":[]}"); check(false, "A later drawing file is read."); }
        catch (NotSupportedException) { check(true, ""); }

        // Saved to its own file and back, apart from the stamps.
        var directory = Path.Combine(Path.GetTempPath(), "wanderveil-marks-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var persistence = new MarksPersistence(new MarksStore(directory), "local"))
            {
                check(persistence.Loaded == LoadOutcome.New, "A first drawing file does not start new.");
                persistence.Marks.AddStroke(Line(2, new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.5f)));
                check(persistence.Flush(TimeSpan.FromSeconds(5)) && File.Exists(Path.Combine(directory, "marks-local.json")) && !File.Exists(Path.Combine(directory, "stamps-local.json")), "Drawing is not saved to its own file.");
            }
            using var again = new MarksPersistence(new MarksStore(directory), "local");
            check(again.Loaded == LoadOutcome.Loaded && again.Marks.StrokesOn(2).Count() == 1, "Saved drawing does not load back.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Shell(Action<bool, string> check, string? artifacts)
    {
        var marks = new PageMarks();
        using (var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), marks: marks))
        {
            book.Open();
            book.Shell.GoToPage(book.Shell.FixedPageCount + 1, true);
            for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
            var drawing = book.Shell.Drawing;
            // Draw sits beside the stamp control in the footer.
            var spot = book.StampControl();
            for (var x = spot.X; x > spot.X - 400 && !drawing.Drawing; x -= 8)
            {
                book.Move(new Vector2(x, spot.Y));
                if (ImGui.IsAnyItemHovered()) book.Click(new Vector2(x, spot.Y));
            }
            check(drawing.Drawing && drawing.Controls.ContainsKey("Pencil") && drawing.Controls.ContainsKey("Undo"), "The stamp pages do not offer Draw, opening the toolbar.");
            var page = book.Page(false);
            var visible = book.Shell.VisiblePage;
            book.Drag(page.Position + page.Size * new Vector2(0.15f, 0.2f), page.Position + page.Size * new Vector2(0.85f, 0.25f));
            check(marks.StrokesOn(0).Count() == 1 && marks.Strokes[0].Points.Length > 50 && book.Shell.VisiblePage == visible, $"Drawing on the page does not make one stroke, or turns the page ({marks.Strokes.Count} strokes).");
            // Red, bold, a second stroke.
            book.Click(drawing.Controls["Colour 3"]);
            book.Click(drawing.Controls["Size 2"]);
            book.Drag(page.Position + page.Size * new Vector2(0.2f, 0.4f), page.Position + page.Size * new Vector2(0.6f, 0.55f));
            check(marks.Strokes.Count == 2 && marks.Strokes[1].Colour == Rgb.Hex(0xA3302A) && marks.Strokes[1].Width > marks.Strokes[0].Width, "The palette and sizes do not set the next stroke.");
            // The eraser rubs out across the first stroke, splitting it; Undo puts it back.
            book.Click(drawing.Controls["Eraser"]);
            book.Click(drawing.Controls["Size 1"]);
            book.Drag(page.Position + page.Size * new Vector2(0.5f, 0.1f), page.Position + page.Size * new Vector2(0.5f, 0.32f));
            check(marks.Strokes.Count == 3, $"Rubbing across a stroke does not split it ({marks.Strokes.Count} strokes).");
            book.Click(drawing.Controls["Undo"]);
            check(marks.Strokes.Count == 2, "Undo does not put the rubbed-out stroke back.");
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl, true);
            book.Key(ImGuiKey.Z);
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl, false);
            book.Frame();
            check(marks.Strokes.Count == 1, "Ctrl+Z does not undo the last stroke.");
            // Writing on the right page, then rewriting it.
            book.Click(drawing.Controls["Text"]);
            var right = book.Page(true);
            book.Click(right.Position + right.Size * new Vector2(0.15f, 0.3f));
            check(drawing.Writing, "Clicking a page with Text does not start a line.");
            book.Type("Ul'dah, at dusk");
            book.Key(ImGuiKey.Enter);
            check(marks.TextsOn(1).SingleOrDefault().Text?.Text == "Ul'dah, at dusk" && !drawing.Writing, "A typed line is not kept on Enter.");
            book.Click(right.Position + right.Size * new Vector2(0.17f, 0.31f));
            check(drawing.Writing, "Clicking a line with Text does not rewrite it.");
            book.SelectAll();
            book.Type("Ul'dah, at dawn");
            book.Key(ImGuiKey.Enter);
            check(marks.TextsOn(1).Single().Text.Text == "Ul'dah, at dawn" && marks.Texts.Count == 1, "Rewriting a line does not replace it.");
            book.Click(drawing.Controls["Pencil"]);
            book.Drag(right.Position + right.Size * new Vector2(0.2f, 0.6f), right.Position + right.Size * new Vector2(0.7f, 0.75f));
            check(book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, "Drawing makes a page scroll.");
            // The paper's left, right, and top margins can be drawn on too (only the foot keeps its margin).
            var margin = new Vector2(page.Position.X - page.Size.X * 0.06f, page.Position.Y - page.Size.X * 0.04f);
            book.Drag(margin, margin + new Vector2(page.Size.X * 0.3f, 0));
            var reached = marks.Strokes[^1];
            check(reached.Sheet == 0 && reached.Points.Min(point => point.X) < 0 && reached.Points.Min(point => point.Y) < 0, "The pencil does not reach the paper's margins.");
            // A custom colour, then the eyedropper taking up the red line's colour again.
            book.Click(drawing.Controls["Custom colour"]);
            book.Frames(2);
            check(drawing.Colour == BookShellDrawing.Palette.Length, "The custom swatch does not choose the custom colour.");
            book.Key(ImGuiKey.Escape);
            book.Click(drawing.Controls["Eyedropper"]);
            var red = marks.Strokes.First(stroke => stroke.Colour == Rgb.Hex(0xA3302A));
            var onRed = InkPointOnScreen(red.Sheet == 0 ? page : right, red.Points[red.Points.Length / 2]);
            book.Click(onRed);
            check(drawing.Colour == 3 && drawing.Tool == DrawingTool.Pencil, $"The eyedropper does not take up a line's colour and hand back the pencil (colour {drawing.Colour}, {drawing.Tool}).");
            book.Click(drawing.Controls["Eyedropper"]);
            book.Click(InkPointOnScreen(page, reached.Points[reached.Points.Length / 2]));
            check(drawing.Colour == BookShellDrawing.Palette.Length && drawing.Custom == reached.Colour || drawing.Colour < BookShellDrawing.Palette.Length, "The eyedropper does not take up any line's colour.");
            book.Save(artifacts, "drawing-pages.bmp");
            // Escape ends Draw mode; the marks stay, drawn above the paper.
            book.Key(ImGuiKey.Escape);
            check(!drawing.Drawing && marks.Strokes.Count == 3, "Escape does not end drawing, or loses marks.");
            book.Save(artifacts, "drawing-done.bmp");
            // Turning back to the identity pages ends it too.
            drawing.Start();
            book.Key(ImGuiKey.PageUp);
            for (var frame = 0; frame < 60 && book.Shell.IsTurning; frame++) book.Frame();
            check(!drawing.Drawing, "Turning back to the identity pages does not end drawing.");

            // A busy page (near its limit) still draws quickly: measured against the same pages with nothing drawn.
            while (marks.Undo()) { }
            book.Shell.GoToPage(book.Shell.FixedPageCount + 1, true);
            for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
            // Timing on a shared machine is noisy: the best of three runs.
            var empty = Enumerable.Range(0, 3).Min(_ => book.Measure());
            var busy = Enumerable.Range(0, 60).Select(index => Line(0, new Vector2(0.05f, 0.05f + index * 0.02f), new Vector2(0.95f, 0.08f + index * 0.02f), 330)).ToArray();
            foreach (var stroke in busy) marks.AddStroke(stroke);
            var milliseconds = Enumerable.Range(0, 3).Min(_ => book.Measure());
            Console.WriteLine($"INFO drawing a page of {marks.SamplesOn(0)} samples: {milliseconds:0.00} ms per frame, {empty:0.00} with nothing drawn (headless CPU, whole book)");
            check(milliseconds < 50, "A busy drawn page is slow to draw.");
            book.Save(artifacts, "drawing-busy.bmp");
        }

        // On a single page the toolbar sits above the footer and still fits.
        using (var narrow = new CollectChecks.Book(new Vector2(520, 900), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), marks: new PageMarks([Line(0, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.4f))])))
        {
            narrow.Open();
            narrow.Shell.GoToPage(narrow.Shell.FixedPageCount + 1, false);
            for (var frame = 0; frame < 120 && narrow.Shell.IsRiffling; frame++) narrow.Frame();
            narrow.Shell.Drawing.Start();
            narrow.Frames(3);
            var controls = narrow.Shell.Drawing.Controls;
            var page = narrow.Page(false);
            narrow.Save(artifacts, "drawing-narrow.bmp");
            check(controls.Values.All(point => point.X >= page.Position.X - 40 && point.X <= page.Max.X + 40) && narrow.Shell.PageOverflowX == 0, $"The toolbar does not fit a single page ({string.Join(", ", controls.Select(entry => $"{entry.Key} {entry.Value.X:0}"))}; page {page.Position.X:0}-{page.Max.X:0}).");
        }

        // A turning leaf carries the drawing.
        using var turning = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), motion: true,
            marks: new PageMarks([Line(0, new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.6f))], [new PageText(1, "Turned with the page", 0.1f, 0.2f, 0.06f, Graphite)]));
        turning.Open();
        turning.Shell.GoToPage(turning.Shell.FixedPageCount + 1, true);
        for (var frame = 0; frame < 240 && (turning.Shell.IsRiffling || turning.Shell.IsTurning); frame++) turning.Frame();
        turning.Key(ImGuiKey.PageUp);
        for (var frame = 0; frame < 120 && turning.Shell.IsTurning; frame++)
        {
            if (frame == 10) turning.Save(artifacts, "drawing-turn.bmp", keepMouse: true);
            turning.Frame();
        }
        check(!turning.Shell.IsTurning, "A drawn page does not finish turning.");
    }
}
