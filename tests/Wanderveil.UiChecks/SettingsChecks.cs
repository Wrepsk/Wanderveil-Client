using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.Features.Stamps;
using Wanderveil.UI;

namespace Wanderveil.UiChecks;

// The Settings categories: opening Settings swaps the section tabs for category tabs (the old sink behind the book,
// the new rise), each category fits its pages wide and narrow, a category tab riffles to its pages, and leaving
// Settings swaps the tabs back.
internal static class SettingsChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        foreach (var motion in new[] { false, true })
        {
            using var book = new CollectChecks.Book(new Vector2(1200, 820), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample), motion: motion);
            book.Open();
            check(book.Shell.TabSwap == 0f, "The section tabs are not shown on the passport.");
            book.Shell.ShowSettings();
            book.Frame();
            if (motion)
            {
                check(book.Shell.TabSwap is > 0f and < 1f, "The tab bar does not swap gradually with motion.");
                // Partway: the section tabs sinking or the category tabs rising, never both.
                for (var frame = 0; frame < 60 && book.Shell.TabSwap < 0.7f; frame++) book.Frame();
                book.Save(artifacts, "settings-tabs-rising.bmp");
            }
            for (var frame = 0; frame < 120 && (book.Shell.TabSwap < 1f || book.Shell.IsRiffling); frame++) book.Frame();
            check(book.Shell.TabSwap == 1f && book.Shell.Section == BookSection.Settings && book.Shell.Category == SettingsCategory.General, "Settings do not open on General with the category tabs.");
            // The third tab's place now holds Saved data, not Echoes.
            book.Click(book.Tab(2));
            for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
            check(book.Shell.Section == BookSection.Settings && book.Shell.Category == SettingsCategory.Saved, "The third category tab does not open Saved data, or reaches a section.");
            if (!motion)
                foreach (var (index, name) in new[] { (0, "general"), (1, "customization"), (2, "saved") })
                {
                    book.Click(book.Tab(index));
                    book.Frames(3);
                    check((int)book.Shell.Category == index && book.Shell.PageOverflow == 0 && book.Shell.PageOverflowX == 0, $"The {name} settings overflow their pages.");
                    book.Save(artifacts, $"settings-{name}.bmp");
                }
            // Back: the category tabs sink and the section tabs return, ready to take clicks again.
            book.Shell.GoBack();
            for (var frame = 0; frame < 120 && (book.Shell.TabSwap > 0f || book.Shell.IsRiffling); frame++) book.Frame();
            check(book.Shell.Section == BookSection.Passport && book.Shell.TabSwap == 0f, "Leaving Settings does not bring the section tabs back.");
            book.Click(book.Tab(2));
            for (var frame = 0; frame < 120 && book.Shell.IsRiffling; frame++) book.Frame();
            check(book.Shell.Section == BookSection.Echoes, "The section tabs do not work again after Settings.");
        }

        // On a single page every category still fits (its two sides stacked; Settings may scroll only with enlarged text).
        using (var narrow = new CollectChecks.Book(new Vector2(520, 900), new StampCollection(), identity: new PassportProfile(PassportIdentity.Sample)))
        {
            narrow.Open();
            narrow.Shell.ShowSettings();
            narrow.Frames(3);
            foreach (var index in new[] { 0, 1, 2 })
            {
                narrow.Click(narrow.Tab(index));
                narrow.Frames(3);
                check((int)narrow.Shell.Category == index && narrow.Shell.PageOverflowX == 0, $"Settings category {index} overflows sideways on a single page.");
            }
            narrow.Save(artifacts, "settings-narrow-saved.bmp");
        }
        Console.WriteLine("PASS settings-categories");
    }
}
