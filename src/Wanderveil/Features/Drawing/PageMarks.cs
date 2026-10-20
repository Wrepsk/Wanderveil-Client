using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Wanderveil.Features.Passport;

namespace Wanderveil.Features.Drawing;

// A point of a pencil stroke on a stamp page, in page-width units (0 to 1 across; down the page as far as it goes, in
// the same units), with how hard it was pressed (0 to 1).
internal readonly record struct InkPoint(float X, float Y, float Pressure);

// A pencil stroke on one stamp page (Sheet 0 is the first stamp page): its colour, its full width in page-width units,
// and its points in drawing order.
internal sealed record InkStroke(int Sheet, Rgb Colour, float Width, ImmutableArray<InkPoint> Points);

// A line of typed writing on one stamp page: where it starts (its top left, in page-width units), its letters' height
// in page-width units, and its colour.
internal sealed record PageText(int Sheet, string Text, float X, float Y, float Size, Rgb Colour);

// The holder's drawing and writing on the stamp pages: a layer of its own, apart from the stamps, which it never
// changes. Every change can be undone (for the session), a drag of the eraser as one change. Each page and the whole
// passport have limits, so a busy page stays quick to draw and the file small.
internal sealed class PageMarks
{
    internal const int MaximumSamplesPerPage = 20_000;
    internal const int MaximumSamples = 200_000;
    internal const int MaximumStrokeSamples = 4_000;
    internal const int MaximumTextsPerPage = 30;
    internal const int TextLimit = 120;
    // How far down a page a mark may go, in page-width units (pages are taller than wide), and how far past the
    // page's box it may reach on the paper's left, right, and top margins.
    internal const float Depth = 2f;
    internal const float Margin = 0.25f;
    private const int UndoDepth = 64;

    private ImmutableList<InkStroke> strokes;
    private ImmutableList<PageText> texts;
    private readonly List<(ImmutableList<InkStroke>, ImmutableList<PageText>)> undo = [];
    private (ImmutableList<InkStroke> Strokes, ImmutableList<PageText> Texts)? group;

    internal PageMarks(IEnumerable<InkStroke>? strokes = null, IEnumerable<PageText>? texts = null)
    {
        this.strokes = ImmutableList<InkStroke>.Empty;
        this.texts = ImmutableList<PageText>.Empty;
        foreach (var stroke in strokes ?? []) if (Clean(stroke) is { } kept && HasRoomOn(kept.Sheet, kept.Points.Length)) this.strokes = this.strokes.Add(kept);
        foreach (var text in texts ?? []) if (Clean(text) is { } kept && TextsOn(kept.Sheet).Count() < MaximumTextsPerPage) this.texts = this.texts.Add(kept);
    }

    internal event Action? Changed;

    internal IReadOnlyList<InkStroke> Strokes => strokes;
    internal IReadOnlyList<PageText> Texts => texts;
    internal bool CanUndo => undo.Count > 0;
    internal int Samples => strokes.Sum(stroke => stroke.Points.Length);

    internal IEnumerable<InkStroke> StrokesOn(int sheet) => strokes.Where(stroke => stroke.Sheet == sheet);
    internal IEnumerable<(int Index, PageText Text)> TextsOn(int sheet) => texts.Select((text, index) => (index, text)).Where(entry => entry.text.Sheet == sheet);
    internal int SamplesOn(int sheet) => StrokesOn(sheet).Sum(stroke => stroke.Points.Length);

    // Whether a page (and the passport) can take this many more samples.
    internal bool HasRoomOn(int sheet, int samples) => SamplesOn(sheet) + samples <= MaximumSamplesPerPage && Samples + samples <= MaximumSamples;

    // Adds a stroke on top; false when it is empty or there is no room for it.
    internal bool AddStroke(InkStroke stroke)
    {
        if (Clean(stroke) is not { } kept || !HasRoomOn(kept.Sheet, kept.Points.Length)) return false;
        return Change(strokes.Add(kept), texts);
    }

    // Rubs out the strokes on a page within radius of centre (page-width units): only the parts it touches, so a
    // stroke crossed in the middle becomes two. Writing it touches (its box, given by the page) goes whole.
    internal bool Erase(int sheet, Vector2 centre, float radius, Func<PageText, (Vector2 Min, Vector2 Max)>? box = null)
    {
        var next = ImmutableList.CreateBuilder<InkStroke>();
        var touched = false;
        var reach = radius * radius;
        foreach (var stroke in strokes)
        {
            if (stroke.Sheet != sheet) { next.Add(stroke); continue; }
            var run = ImmutableArray.CreateBuilder<InkPoint>();
            var cut = false;
            foreach (var point in stroke.Points)
            {
                if (Vector2.DistanceSquared(new Vector2(point.X, point.Y), centre) <= reach + stroke.Width * stroke.Width / 4)
                {
                    cut = true;
                    if (run.Count >= 2) next.Add(stroke with { Points = run.ToImmutable() });
                    run.Clear();
                    continue;
                }
                run.Add(point);
            }
            if (!cut) { next.Add(stroke); continue; }
            touched = true;
            if (run.Count >= 2) next.Add(stroke with { Points = run.ToImmutable() });
        }
        var keptTexts = texts;
        if (box != null)
            foreach (var (index, text) in TextsOn(sheet).Reverse())
            {
                var (min, max) = box(text);
                var nearest = Vector2.Clamp(centre, min, max);
                if (Vector2.DistanceSquared(nearest, centre) <= reach) { keptTexts = keptTexts.RemoveAt(index); touched = true; }
            }
        return touched && Change(next.ToImmutable(), keptTexts);
    }

    // Writes a line (index null), rewrites one, or with empty text takes it away. False when nothing changed or the
    // page holds as many lines as it may.
    internal bool SetText(int? index, PageText text)
    {
        var kept = Clean(text);
        if (index is { } at)
        {
            if ((uint)at >= (uint)texts.Count) return false;
            return Change(strokes, kept == null ? texts.RemoveAt(at) : texts.SetItem(at, kept));
        }
        if (kept == null || TextsOn(kept.Sheet).Count() >= MaximumTextsPerPage) return false;
        return Change(strokes, texts.Add(kept));
    }

    // Takes every mark off one page (one step of undo).
    internal bool ClearSheet(int sheet) => Change(strokes.RemoveAll(stroke => stroke.Sheet == sheet), texts.RemoveAll(text => text.Sheet == sheet));

    // Becomes another copy of these marks (the one kept online, taken on this PC), checked as loaded ones are. Nothing to
    // undo afterwards: undoing past it would mix two copies.
    internal void Replace(IEnumerable<InkStroke> nextStrokes, IEnumerable<PageText> nextTexts)
    {
        var copy = new PageMarks(nextStrokes, nextTexts);
        group = null;
        undo.Clear();
        strokes = copy.strokes;
        texts = copy.texts;
        Changed?.Invoke();
    }

    // Takes every mark off every page, for good: nothing to undo afterwards.
    internal bool ClearAll()
    {
        var changed = Change(ImmutableList<InkStroke>.Empty, ImmutableList<PageText>.Empty);
        undo.Clear();
        return changed;
    }

    // Groups the changes until EndChange into one step of undo (an eraser's drag).
    internal void BeginChange() => group ??= (strokes, texts);

    internal void EndChange()
    {
        if (group is not { } before) return;
        group = null;
        if (before.Strokes != strokes || before.Texts != texts) Remember(before);
    }

    // Puts back the marks as they were before the last change.
    internal bool Undo()
    {
        if (group != null) EndChange();
        if (undo.Count == 0) return false;
        (strokes, texts) = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        Changed?.Invoke();
        return true;
    }

    private bool Change(ImmutableList<InkStroke> nextStrokes, ImmutableList<PageText> nextTexts)
    {
        if (nextStrokes == strokes && nextTexts == texts) return false;
        if (group == null) Remember((strokes, texts));
        strokes = nextStrokes;
        texts = nextTexts;
        Changed?.Invoke();
        return true;
    }

    private void Remember((ImmutableList<InkStroke>, ImmutableList<PageText>) state)
    {
        undo.Add(state);
        if (undo.Count > UndoDepth) undo.RemoveAt(0);
    }

    // Kept on a page that exists, within the page, at a sensible width, and no longer than a stroke may be.
    private static InkStroke? Clean(InkStroke stroke)
    {
        if (stroke.Sheet < 0 || stroke.Points.IsDefaultOrEmpty || !float.IsFinite(stroke.Width)) return null;
        var points = stroke.Points.Where(point => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Pressure))
            .Take(MaximumStrokeSamples)
            .Select(point => new InkPoint(Math.Clamp(point.X, -Margin, 1f + Margin), Math.Clamp(point.Y, -Margin, Depth), Math.Clamp(point.Pressure, 0f, 1f)))
            .ToImmutableArray();
        return points.Length < 2 ? null : stroke with { Width = Math.Clamp(stroke.Width, 0.001f, 0.05f), Points = points };
    }

    private static PageText? Clean(PageText text)
    {
        var clean = PassportIdentity.Clean(text.Text, TextLimit);
        if (text.Sheet < 0 || clean.Length == 0 || !float.IsFinite(text.X) || !float.IsFinite(text.Y) || !float.IsFinite(text.Size)) return null;
        return text with { Text = clean, X = Math.Clamp(text.X, -Margin, 1f), Y = Math.Clamp(text.Y, -Margin, Depth), Size = Math.Clamp(text.Size, 0.02f, 0.15f) };
    }
}
