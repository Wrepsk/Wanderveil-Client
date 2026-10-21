using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Wanderveil.UI;

// Page numbers as the book prints them, written as a list with ranges ("5, 7-9").
internal static class PageRanges
{
    // The pages a list names, in order and once each; null when it names none, or one outside first..last.
    internal static int[]? Parse(string text, int first, int last)
    {
        var pages = new SortedSet<int>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split(['-', '–'], 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)) return null;
            var to = from;
            if (ends.Length == 2 && !int.TryParse(ends[1], NumberStyles.None, CultureInfo.InvariantCulture, out to)) return null;
            if (from > to) (from, to) = (to, from);
            if (from < first || to > last) return null;
            for (var page = from; page <= to; page++) pages.Add(page);
        }
        return pages.Count == 0 ? null : [.. pages];
    }

    // A list of pages as text, runs of three or more as ranges (a plain hyphen: every page face has one).
    internal static string Format(IEnumerable<int> pages)
    {
        var sorted = pages.Distinct().Order().ToArray();
        var parts = new List<string>();
        for (var index = 0; index < sorted.Length;)
        {
            var end = index;
            while (end + 1 < sorted.Length && sorted[end + 1] == sorted[end] + 1) end++;
            if (end - index >= 2) parts.Add($"{sorted[index]}-{sorted[end]}");
            else for (var at = index; at <= end; at++) parts.Add(sorted[at].ToString(CultureInfo.InvariantCulture));
            index = end + 1;
        }
        return string.Join(", ", parts);
    }
}
