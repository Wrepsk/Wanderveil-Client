using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;
using Wanderveil.UiChecks;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    checks++;
}

// Layout must react to actual font size as well as host scale; pages never share horizontal space accidentally.
foreach (var width in new[] { 280f, 400f, 680f, 820f, 1200f })
    foreach (var scale in new[] { 1f, 1.5f, 2f })
        foreach (var textScale in new[] { 1f, 1.5f, 2f })
        {
            var layout = PassportLayout.Create(width, new UiContext(scale, 13f * scale * textScale));
            var usedWidth = layout.IsSpread ? (layout.PageWidth * 2f) + layout.Gap : layout.PageWidth;
            Check(MathF.Abs(usedWidth - width) < 0.01f, "Pages exceed the available width.");
            Check(layout.PageWidth - (2f * layout.Padding) > 0f, "Page content has no usable width.");
        }
Check(PassportLayout.Create(820, new UiContext(1, 13)).IsSpread, "Default wide layout should be a spread.");
Check(!PassportLayout.Create(820, new UiContext(1, 30)).IsSpread, "Large text should switch to stacked pages.");
foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
{
    try
    {
        PassportLayout.Create(invalid, new UiContext(1, 13));
        throw new InvalidOperationException("Invalid geometry was accepted.");
    }
    catch (ArgumentOutOfRangeException)
    {
        checks++;
    }
}

Check(Contrast(UiTheme.Ink, UiTheme.Paper) >= 4.5, "Paper body text contrast is insufficient.");
Check(Contrast(UiTheme.MutedInk, UiTheme.Paper) >= 4.5, "Paper caption contrast is insufficient.");
foreach (var action in new[] { UiTheme.Action, UiTheme.ActionHovered, UiTheme.ActionPressed })
    Check(Contrast(UiTheme.Paper, action) >= 4.5, "Button label contrast is insufficient.");

var longProfile = new PassportPreview(
    "Ari of the Many Winding Roads and Forgotten Horizons",
    "An unusually long sample home-world label",
    "Bard / Botanist / Weaver / A very long favorite-job label",
    string.Concat(Enumerable.Repeat("A long story with quiet paths, old maps, and a view beyond the horizon. ", 10)),
    "Take the scenic route, even when the road asks for one more turn before the story can begin.");

var artifacts = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
if (artifacts != null)
    Directory.CreateDirectory(artifacts);
foreach (var scenario in new[]
{
    (Name: "passport-wide", Width: 820f, Height: 640f, Scale: 1f, Zoom: 1f, Data: PassportPreview.Sample),
    (Name: "passport-narrow", Width: 400f, Height: 760f, Scale: 1f, Zoom: 1f, Data: PassportPreview.Sample),
    (Name: "passport-large-text", Width: 820f, Height: 760f, Scale: 1f, Zoom: 1.75f, Data: PassportPreview.Sample),
    (Name: "passport-long-text", Width: 680f, Height: 760f, Scale: 1.25f, Zoom: 1.25f, Data: longProfile),
})
{
    using var host = new HeadlessUi(new Vector2(scenario.Width, scenario.Height), scenario.Scale, scenario.Zoom);
    var view = new PassportView(scenario.Data);
    for (var frame = 0; frame < 4; frame++)
    {
        host.Frame(() =>
        {
            var context = new UiContext(scenario.Scale, ImGui.GetFontSize());
            view.Draw(context);
            Check(ImGui.GetScrollMaxX() == 0f, scenario.Name + " has horizontal overflow.");
        });
    }
    Check(ImGui.GetDrawData().TotalVtxCount > 100, scenario.Name + " did not produce real draw data.");
    if (artifacts != null)
        SoftwarePreview.Save(Path.Combine(artifacts, scenario.Name + ".bmp"), host);
    Console.WriteLine("PASS " + scenario.Name);
}

using (var host = new HeadlessUi(new Vector2(420, 300)))
{
    var activations = 0;
    var center = Vector2.Zero;
    void DrawButton()
    {
        var originalColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
        if (UiButton.Draw("Explore discoveries", new UiContext(1, ImGui.GetFontSize())))
            activations++;
        center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
        Check(ImGui.GetStyle().Colors[(int)ImGuiCol.Button] == originalColor, "Button leaked its theme into the host.");
    }
    host.Frame(DrawButton);
    host.Frame(DrawButton);
    ImGui.GetIO().AddMousePosEvent(center.X, center.Y);
    ImGui.GetIO().AddMouseButtonEvent(0, true);
    host.Frame(DrawButton);
    Check(activations == 0, "Button activated on press instead of release.");
    ImGui.GetIO().AddMouseButtonEvent(0, false);
    host.Frame(DrawButton);
    host.Frame(DrawButton);
    Check(activations == 1, "Button did not activate exactly once on release.");

    host.Frame(() => { ImGui.SetKeyboardFocusHere(); DrawButton(); });
    ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true);
    host.Frame(DrawButton);
    ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false);
    host.Frame(DrawButton);
    Check(activations == 2, "Button did not support native keyboard activation.");

    host.Frame(() =>
    {
        var context = new UiContext(1, ImGui.GetFontSize());
        var shortText = UiTypography.Measure("Ari Vale", TextRole.Title, context, 220);
        var wrapped = UiTypography.Measure(longProfile.Name, TextRole.Title, context, 140);
        Check(wrapped.Y > shortText.Y, "Long titles did not wrap.");
        var start = ImGui.GetCursorScreenPos().Y;
        UiTypography.Text(longProfile.Name, TextRole.Title, context, UiTheme.Paper);
        var expected = UiTypography.Measure(longProfile.Name, TextRole.Title, context, UiLayout.ContentWidth);
        Check(ImGui.GetCursorScreenPos().Y - start >= expected.Y, "Typography did not reserve its measured height.");
    });
}
DiscoverChecks.Run(Check, artifacts);
BookChecks.Run(Check, artifacts);
BookShellChecks.Run(Check, artifacts);
GridaniaChecks.Run(Check, artifacts);
Console.WriteLine($"PASS {checks} checks using the real ImGui native context.");

static double Contrast(Vector4 first, Vector4 second)
{
    static double Channel(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    static double Luminance(Vector4 value) => (0.2126 * Channel(value.X)) + (0.7152 * Channel(value.Y)) + (0.0722 * Channel(value.Z));
    var a = Luminance(first);
    var b = Luminance(second);
    return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
}
