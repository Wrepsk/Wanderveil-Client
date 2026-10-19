using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.Features.Passport;

// Writing on the identity pages: each field is typed in place, in the page's own type, over a faint inset.
// Text wraps at word breaks as it does in print. ImGui's text input cannot wrap, so the editor wraps it
// itself: line breaks in the buffer are soft breaks it inserted, re-flowed after every edit, and removed
// again before the text is kept. The holder cannot type line breaks of their own (Enter finishes a field).
// Every change is kept at once; the identity store saves it in the background.
internal sealed class IdentityEditor
{
    private readonly PassportProfile profile;
    private readonly IPassportPortrait? picture;
    private readonly ICharacterSource? character;
    private readonly ImGui.ImGuiInputTextCallbackDelegate rewrap;
    // Reading the logged-in character, and what came of the last read.
    private Task<CharacterDetails?>? reading;
    private string? note;
    // The field being typed in and its text as typed (kept text is trimmed, so it can lag the buffer).
    private IdentityField? active;
    private string typed = "";
    // Measures for the field whose input is drawing now, read by the rewrap callback.
    private float wrapWidth;
    private int limit;

    internal IdentityEditor(PassportProfile profile, IPassportPortrait? picture = null, ICharacterSource? character = null)
    {
        this.profile = profile;
        this.picture = picture;
        this.character = character;
        rewrap = Rewrap;
    }

    internal bool Typing => active != null;

    // Called when editing starts or stops: forgets any message from the last session.
    internal void Reset()
    {
        active = null;
        note = null;
    }

    // Offers to copy the logged-in character's name, home world, and data center into the passport.
    internal void CharacterLink(ref PageFlow flow)
    {
        if (character == null) return;
        if (reading is { IsCompleted: true } done)
        {
            var details = done.IsCompletedSuccessfully ? done.Result : null;
            note = !done.IsCompletedSuccessfully ? "Your character could not be read, so nothing was filled in."
                : details == null ? "No character is logged in, so nothing was filled in."
                : "Filled in from your character. Change anything you like; none of it is verified.";
            if (details != null) profile.Update(profile.Identity with { Name = details.Name, HomeWorld = details.HomeWorld, DataCenter = details.DataCenter });
            reading = null;
        }
        flow.Gap(10);
        if (Link(ref flow, reading != null ? "Reading your character" : "Fill in from my character", "Copy your logged-in character's name, home world, and data center") && reading == null)
        {
            note = null;
            reading = character.Read();
        }
        if (note == null) return;
        flow.Gap(4);
        flow.Text(note, TextRole.Caption, true);
    }

    // Choosing, replacing, or removing the holder's own picture, and what came of the last choice.
    internal void PictureLinks(ref PageFlow flow)
    {
        if (picture == null) return;
        flow.Gap(8);
        var own = profile.Identity.Portrait != null;
        var choose = own ? "Choose another picture" : "Choose a picture";
        var context = flow.Context;
        var first = Measure(choose, context);
        var second = own ? Measure("Remove picture", context) : Vector2.Zero;
        var gap = 18 * context.Scale;
        var row = flow.Reserve(MathF.Max(first.Y, second.Y));
        if (flow.IsDrawing)
        {
            ImGui.SetCursorScreenPos(row.Position);
            if (BookControl.Link(choose, context)) picture.Choose();
            if (own && row.Size.X >= first.X + gap + second.X)
            {
                ImGui.SetCursorScreenPos(row.Position + new Vector2(first.X + gap, 0));
                if (BookControl.Link("Remove picture", context)) picture.Remove();
            }
        }
        if (own && row.Size.X < first.X + gap + second.X && Link(ref flow, "Remove picture")) picture.Remove();
        var status = picture.State switch
        {
            PortraitState.Loading => "Reading your picture",
            PortraitState.Failed => picture.Problem ?? "That picture could not be used.",
            _ => null,
        };
        if (status == null) return;
        flow.Gap(4);
        flow.Text(status, TextRole.Caption, true);
    }

    internal void CoverLinks(ref PageFlow flow)
    {
        var identity = profile.Identity;
        if (identity.Name.Length == 0 || identity.CoverLine == identity.Name) return;
        flow.Gap(4);
        if (Link(ref flow, "Use my name", "Emboss your name on the cover")) profile.Update(identity with { CoverLine = identity.Name });
    }

    // Over the holder's picture: drag to move it in its frame, scroll to zoom. It always covers the frame.
    internal void Crop(UiRect frame, BookImage image)
    {
        var crop = profile.Identity.Portrait ?? PortraitCrop.Centered;
        var aspect = image.Width / (float)Math.Max(1, image.Height);
        ImGui.SetCursorScreenPos(frame.Position);
        ImGui.InvisibleButton("Portrait placement", frame.Size);
        var next = crop;
        var (min, max) = crop.Window(aspect, PassportDocumentView.PortraitAspect);
        var span = max - min;
        var center = (min + max) / 2;
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
        {
            var moved = ImGui.GetIO().MouseDelta / frame.Size * span;
            next = crop with { X = center.X - moved.X, Y = center.Y - moved.Y };
        }
        var wheel = ImGui.GetIO().MouseWheel;
        if (ImGui.IsItemHovered() && wheel != 0f) next = next with { Zoom = Math.Clamp(next.Zoom * MathF.Pow(1.1f, wheel), 1f, PortraitCrop.MaximumZoom) };
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            var list = ImGui.GetWindowDrawList();
            list.AddRect(frame.Position, frame.Max, ImGui.GetColorU32(UiTheme.Gold), 0, ImDrawFlags.None, MathF.Max(1, frame.Size.X / 160));
        }
        if (next != crop) profile.Update(profile.Identity with { Portrait = next.Clamped(aspect, PassportDocumentView.PortraitAspect) });
    }

    // A printed link at the flow's cursor, taking its height in the flow.
    private static bool Link(ref PageFlow flow, string label, string? hint = null)
    {
        var bounds = flow.Reserve(Measure(label, flow.Context).Y);
        if (!flow.IsDrawing) return false;
        ImGui.SetCursorScreenPos(bounds.Position);
        var pressed = BookControl.Link(label, flow.Context);
        if (hint != null && ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
        return pressed;
    }

    private static Vector2 Measure(string label, UiContext context) => UiTypography.Measure(label, TextRole.Body, context, float.MaxValue, false) + new Vector2(4, 6) * context.Scale;

    // One field at the flow's cursor: as tall as its wrapped text, at least one line.
    internal void Field(ref PageFlow flow, IdentityField field, TextRole role, string placeholder, UiContext? type = null)
    {
        var context = type ?? flow.Context;
        var value = active == field ? typed : profile.Identity[field];
        var font = UiTypography.ResolveFont(value.Length > 0 ? value : placeholder, role, context);
        var size = UiTypography.Size(role, context);
        var pad = new Vector2(4, 2) * context.Scale;
        ImGui.PushFont(font);
        ImGui.SetWindowFontScale(size / ImGui.GetFontSize());
        try
        {
            // Room for the caret at a line's end, so wrapping never makes the input scroll sideways.
            wrapWidth = MathF.Max(1, flow.Width - pad.X * 2 - size * 0.4f);
            limit = PassportIdentity.Limit(field);
            var wrapped = Wrap(value, wrapWidth);
            var lines = 1;
            foreach (var character in wrapped) if (character == '\n') lines++;
            var bounds = flow.Reserve(lines * ImGui.GetTextLineHeight() + pad.Y * 2 + 1);
            if (!flow.IsDrawing) return;
            Input(field, bounds, wrapped, placeholder, font, size, pad, context);
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
        }
    }

    private void Input(IdentityField field, UiRect bounds, string wrapped, string placeholder, ImFontPtr font, float size, Vector2 pad, UiContext context)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, pad);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * context.Scale);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiTheme.PaperInset with { W = 0.35f });
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiTheme.PaperInset with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, UiTheme.PaperInset with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Ink);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, UiTheme.Gold with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, UiTheme.Ink);
        try
        {
            ImGui.SetCursorScreenPos(bounds.Position);
            var text = wrapped;
            // Bytes: up to four per character, plus the soft breaks.
            var changed = ImGui.InputTextMultiline("##" + field, ref text, limit * 4 + 256, bounds.Size,
                ImGuiInputTextFlags.NoHorizontalScroll | ImGuiInputTextFlags.CtrlEnterForNewLine | ImGuiInputTextFlags.CallbackEdit, rewrap);
            var raw = text.Replace("\n", "", StringComparison.Ordinal);
            if (ImGui.IsItemActive()) { active = field; if (changed) typed = raw; else if (ImGui.IsItemActivated()) typed = profile.Identity[field]; }
            else if (active == field) active = null;
            if (changed) profile.Update(profile.Identity.With(field, raw));
            if (!ImGui.IsItemActive() && raw.Length == 0)
                ImGui.GetWindowDrawList().AddText(font, size, ImGui.GetItemRectMin() + pad, ImGui.GetColorU32(UiTheme.MutedInk with { W = 0.75f }), placeholder);
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
        }
        finally
        {
            ImGui.PopStyleColor(7);
            ImGui.PopStyleVar(3);
        }
    }

    // After every edit: drop the soft breaks, keep the text within its limit, wrap it again, and put the
    // caret back after the same character.
    private int Rewrap(ref ImGuiInputTextCallbackData data)
    {
        var text = Encoding.UTF8.GetString(data.BufTextSpan);
        var caret = Encoding.UTF8.GetCharCount(data.BufTextSpan[..Math.Clamp(data.CursorPos, 0, data.BufTextLen)]);
        var before = 0;
        for (var index = 0; index < caret && index < text.Length; index++) if (text[index] != '\n') before++;
        var raw = text.Replace("\n", "", StringComparison.Ordinal);
        var info = new System.Globalization.StringInfo(raw);
        if (info.LengthInTextElements > limit) raw = info.SubstringByTextElements(0, limit);
        var wrapped = Wrap(raw, wrapWidth);
        if (wrapped == text) return 0;
        var at = 0;
        for (var seen = 0; at < wrapped.Length && seen < Math.Min(before, raw.Length); at++) if (wrapped[at] != '\n') seen++;
        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, wrapped);
        data.CursorPos = data.SelectionStart = data.SelectionEnd = Encoding.UTF8.GetByteCount(wrapped.AsSpan(0, at));
        return 0;
    }

    // Greedy word wrap in the current font and scale: a break goes after the space that ends a line, so
    // removing the breaks gives back the text exactly. A word wider than the line breaks between characters.
    internal static string Wrap(string text, float width)
    {
        if (text.Length == 0) return text;
        var result = new StringBuilder(text.Length + 8);
        var line = new StringBuilder();
        var start = 0;
        while (start < text.Length)
        {
            var end = start;
            while (end < text.Length && text[end] != ' ') end++;
            while (end < text.Length && text[end] == ' ') end++;
            var word = text[start..end];
            start = end;
            if (Fits(line + word.TrimEnd())) { line.Append(word); continue; }
            if (line.Length > 0) { result.Append(line).Append('\n'); line.Clear(); }
            if (Fits(word.TrimEnd())) { line.Append(word); continue; }
            // A word too long for any line: as many characters as fit, then the rest on the next lines.
            foreach (var character in word)
            {
                if (line.Length > 0 && character != ' ' && !char.IsLowSurrogate(character) && !Fits(line.ToString() + character)) { result.Append(line).Append('\n'); line.Clear(); }
                line.Append(character);
            }
        }
        return result.Append(line).ToString();

        bool Fits(string candidate) => ImGui.CalcTextSize(candidate).X <= width;
    }
}
