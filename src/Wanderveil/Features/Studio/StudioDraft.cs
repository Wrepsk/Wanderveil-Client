using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Wanderveil.Features.Studio;

// Draft, saved, and published are separate states. This preview only has the first: a draft lives in memory
// until the plugin unloads. Saving needs local storage and publishing needs an account and moderation, so
// both report themselves unavailable and never touch disk or the network.
internal sealed class StudioDraft
{
    internal const string SaveUnavailable = "Saving drafts arrives with local storage.";
    internal const string PublishUnavailable = "Publishing is unavailable in this preview. Nothing is sent.";

    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    internal StudioDraft(StudioTemplate template)
    {
        if (!template.Available || template.Fields.Count == 0)
            throw new ArgumentException("Only an available template with fields can start a draft.", nameof(template));
        Template = template;
        foreach (var field in template.Fields) values[field.Id] = "";
    }

    internal StudioTemplate Template { get; }
    // True once any field has been changed; a draft that was never touched has nothing to lose.
    internal bool Edited { get; private set; }
    internal static bool CanSave => false;
    internal static bool CanPublish => false;

    internal string this[string id] => values.GetValueOrDefault(id, "");

    internal IEnumerable<StudioField> Missing => Template.Fields.Where(entry => entry.Required && this[entry.Id].Trim().Length == 0);
    internal bool IsComplete => !Missing.Any();

    // Stores a field's text as typed: one line, no control characters, at most the field's length.
    // Returns whether the stored value changed.
    internal bool Set(string id, string? text)
    {
        var field = Template.Fields.FirstOrDefault(candidate => candidate.Id == id) ?? throw new ArgumentException("Unknown field " + id, nameof(id));
        var clean = Clean(text ?? "", field.MaxLength);
        if (clean == values[id]) return false;
        values[id] = clean;
        Edited = true;
        return true;
    }

    internal static int Length(string text) => new StringInfo(text).LengthInTextElements;

    private static string Clean(string text, int max)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
            builder.Append(char.IsControl(character) ? ' ' : character);
        var line = builder.ToString();
        var info = new StringInfo(line);
        return info.LengthInTextElements <= max ? line : info.SubstringByTextElements(0, max);
    }
}
