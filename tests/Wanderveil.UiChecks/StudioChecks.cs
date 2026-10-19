using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Studio;
using Wanderveil.UI;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class StudioChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        Domain(check);
        foreach (var scenario in new[] {
            (Name: "wide", Size: new Vector2(1200, 800), Text: 1f),
            (Name: "narrow", Size: new Vector2(452, 760), Text: 1f),
            (Name: "large-text", Size: new Vector2(1000, 900), Text: 1.75f) })
            Shell(check, artifacts, scenario.Name, scenario.Size, scenario.Text);
    }

    private static void Domain(Action<bool, string> check)
    {
        var catalog = StudioTemplate.Catalog;
        check(catalog[0].Kind == StudioTemplateKind.Postcard && catalog[1].Kind == StudioTemplateKind.StampSpot && catalog[0].Available && catalog[1].Available, "Postcard and Stamp Spot do not lead the Studio templates.");
        check(catalog.Skip(2).All(template => !template.Available && template.Fields.Count == 0), "Later templates look available.");
        check(catalog.Select(template => template.Kind).Distinct().Count() == catalog.Count, "Studio templates repeat.");
        foreach (var template in catalog.Where(template => template.Available))
        {
            check(template.Fields.Any(field => field.Required) && template.Fields.All(field => field.MaxLength > 0), template.Name + " has no required field or an empty limit.");
            check(template.Fields.Select(field => field.Id).Distinct().Count() == template.Fields.Count, template.Name + " repeats a field.");
        }
        try { _ = new StudioDraft(catalog[2]); check(false, "An unavailable template started a draft."); }
        catch (ArgumentException) { check(true, "Unavailable template refused."); }

        var draft = new StudioDraft(StudioTemplate.Postcard);
        check(!draft.Edited && !draft.IsComplete && draft.Missing.Count() == 2, "A new postcard draft is not untouched and incomplete.");
        check(!StudioDraft.CanSave && !StudioDraft.CanPublish, "The preview claims it can save or publish.");
        check(!draft.Set(StudioTemplate.Title, ""), "Setting an unchanged value reports a change.");
        check(draft.Set(StudioTemplate.Title, "Line one\nline two\t!") && draft[StudioTemplate.Title] == "Line one line two !", "Control characters survive in a one-line field.");
        check(draft.Edited, "Editing does not mark the draft edited.");
        draft.Set(StudioTemplate.Title, new string('a', 100));
        check(draft[StudioTemplate.Title].Length == 48, "A field exceeds its length.");
        // Lengths count what a reader sees as one character, so a limit never splits an emoji or accent.
        var flags = string.Concat(Enumerable.Repeat("🇯🇵", 60));
        draft.Set(StudioTemplate.Title, flags);
        check(StudioDraft.Length(draft[StudioTemplate.Title]) == 48 && draft[StudioTemplate.Title] == string.Concat(Enumerable.Repeat("🇯🇵", 48)), "A length limit splits a character.");
        draft.Set(StudioTemplate.Title, "   ");
        check(draft.Missing.Any(field => field.Id == StudioTemplate.Title), "A blank title counts as written.");
        draft.Set(StudioTemplate.Title, "The scenic route");
        draft.Set(StudioTemplate.Caption, "Worth the detour.");
        check(draft.IsComplete, "Optional fields block a complete postcard.");
        try { draft.Set("unknown", "x"); check(false, "An unknown field was accepted."); }
        catch (ArgumentException) { check(true, "Unknown field refused."); }

        var view = new StudioView();
        check(!view.Choose(StudioTemplateKind.Trail) && view.Draft == null, "An unavailable template opened a draft.");
        check(view.Choose(StudioTemplateKind.Postcard), "Postcard cannot be chosen.");
        view.Draft!.Set(StudioTemplate.Title, "Kept");
        view.ShowTemplates();
        check(view.Draft == null, "Returning to templates keeps the draft open.");
        view.Choose(StudioTemplateKind.StampSpot);
        check(view.Draft!.Template.Kind == StudioTemplateKind.StampSpot && view.Draft[StudioTemplate.Title] == "", "Templates share a draft.");
        view.Choose(StudioTemplateKind.Postcard);
        check(view.Draft![StudioTemplate.Title] == "Kept", "Returning to a template loses its draft.");
    }

    private static void Shell(Action<bool, string> check, string? artifacts, string name, Vector2 room, float text)
    {
        using var host = new HeadlessUi(room, 1f, text, materials: true);
        var shell = new BookShell(host.Textures!, PassportIdentity.Sample) { ReducedMotion = true };
        var context = new UiContext(1, 18 * text) { TitleFont = host.TitleFont, HeadingFont = host.HeadingFont, BodyFont = host.BodyFont, DisplayFont = host.DisplayFont, MonoFont = host.MonoFont, SignatureFont = host.SignatureFont };
        UiRect root = default;
        void Frame() => host.BookFrame(shell.FitSize(room - new Vector2(32), context), () =>
        {
            root = new UiRect(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            shell.Draw(root, context);
            check(ImGui.GetScrollMaxX() == 0 && ImGui.GetScrollMaxY() == 0, "Studio makes the book host scroll.");
        });
        void Frames(int count) { for (var index = 0; index < count; index++) Frame(); }
        void Click(Vector2 position)
        {
            ImGui.GetIO().AddMousePosEvent(position.X, position.Y); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, true); Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false); Frames(2);
        }
        void Key(ImGuiKey key) { ImGui.GetIO().AddKeyEvent(key, true); Frame(); ImGui.GetIO().AddKeyEvent(key, false); Frames(2); }
        void Save(string preview) { if (artifacts != null) { ImGui.GetIO().AddMousePosEvent(-100, -100); Frame(); SoftwarePreview.Save(Path.Combine(artifacts, preview), host); } }

        Frames(3);
        Click(BookShellLayout.OpenPoint(root));
        Frames(2);
        var layout = BookShellLayout.Create(root, context, 0);
        Click(layout.Bookmark(3).Position + layout.Bookmark(3).Size / 2);
        Frames(2);
        check(shell.Section == BookSection.Studio && !shell.IsRiffling, name + ": the Studio tab does not open Studio.");
        // A single page holds both sides and may scroll, as Settings does; a spread fits its paper.
        if (text <= 1f && layout.IsSpread) check(shell.PageOverflow == 0, name + ": the template pages overflow their paper.");
        Save("studio-" + name + "-templates.png");

        // The first available template's start link sits on the chooser page; probe down its left edge.
        var chooser = layout.IsSpread ? layout.RightPage : layout.LeftPage;
        // Large text can push it below the fold of a single page; scroll the page down between probes.
        for (var pass = 0; pass < 6 && shell.Studio.Draft == null; pass++)
        {
            for (var y = 20f; shell.Studio.Draft == null && y < chooser.Size.Y; y += 8)
                Click(chooser.Position + new Vector2(14, y));
            if (shell.Studio.Draft != null) break;
            var middle = chooser.Position + chooser.Size / 2;
            ImGui.GetIO().AddMousePosEvent(middle.X, middle.Y); Frame();
            ImGui.GetIO().AddMouseWheelEvent(0, -4); Frames(2);
        }
        check(shell.Studio.Draft?.Template.Kind == StudioTemplateKind.Postcard, name + ": the postcard template cannot be started from the page.");

        // Starting a draft puts the cursor in its first field; typing writes the title.
        Frames(2);
        ImGui.GetIO().AddInputCharacters("Moonlit pier");
        Frames(2);
        check(shell.Studio.Draft![StudioTemplate.Title] == "Moonlit pier", name + ": typing does not reach the first field.");
        // Tab moves from field to field, as in any form.
        Key(ImGuiKey.Tab);
        check(shell.Studio.Draft[StudioTemplate.Title] == "Moonlit pier", name + ": leaving the field loses the typed title.");
        ImGui.GetIO().AddInputCharacters("Lanterns on the water");
        Frames(2);
        check(shell.Studio.Draft[StudioTemplate.Caption] == "Lanterns on the water", name + ": Tab does not move to the caption field.");
        // Escape inside a field only leaves the field; with no field active it dismisses the passport.
        Key(ImGuiKey.Escape);
        check(shell.Phase == BookPhase.Open && shell.IsBookOpen, name + ": Escape while writing dismisses the passport.");
        check(shell.Studio.Draft[StudioTemplate.Caption] == "Lanterns on the water", name + ": Escape erases what was written in the field.");
        shell.Studio.Draft.Set(StudioTemplate.Place, "An imagined harbour at dusk");
        shell.Studio.Draft.Set(StudioTemplate.Stamp, "Moonlit pier");
        Frames(3);
        check(shell.Studio.Draft.IsComplete, name + ": a filled postcard is not complete.");
        if (text <= 1f && layout.IsSpread) check(shell.PageOverflow == 0, name + ": the postcard draft overflows its paper.");
        Save("studio-" + name + "-postcard.png");

        if (name == "wide")
        {
            shell.Studio.Choose(StudioTemplateKind.StampSpot);
            shell.Studio.Draft!.Set(StudioTemplate.Title, "Where the lanterns wait");
            shell.Studio.Draft.Set(StudioTemplate.Place, "A bend in the woodland path");
            Frames(3);
            check(shell.Studio.Draft.Missing.Select(field => field.Id).SequenceEqual([StudioTemplate.Stamp]), "The stamp spot does not report its missing stamp name.");
            Save("studio-wide-stamp-spot.png");
            check(shell.PageOverflow == 0, "The stamp spot draft overflows its paper.");
        }

        // Leaving Studio and coming back keeps the draft open where the creator left it.
        var kind = shell.Studio.Draft.Template.Kind;
        Click(layout.Bookmark(0).Position + layout.Bookmark(0).Size / 2);
        Click(layout.Bookmark(3).Position + layout.Bookmark(3).Size / 2);
        check(shell.Section == BookSection.Studio && shell.Studio.Draft?.Template.Kind == kind, name + ": visiting another section loses the open draft.");

        ImGui.GetIO().AddMousePosEvent(-100, -100);
        Frames(2);
        Key(ImGuiKey.Escape);
        check(shell.Phase != BookPhase.Open, name + ": Escape outside a field no longer dismisses the passport.");
        Console.WriteLine("PASS studio-" + name);
    }
}
