using System.Numerics;
using Dalamud.Bindings.ImGui;
using Wanderveil.Features.Discover;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UiChecks;

internal static class DiscoverChecks
{
    internal static void Run(Action<bool, string> check, string? artifacts)
    {
        foreach (var width in new[] { 280f, 400f, 680f, 820f, 1200f })
            foreach (var scale in new[] { 1f, 1.5f, 2f })
                foreach (var textScale in new[] { 1f, 1.5f, 2f })
                {
                    var layout = DiscoveryLayout.Create(width, new UiContext(scale, 13f * scale * textScale));
                    var used = layout.IsSplit ? (layout.ColumnWidth * 2f) + layout.Gap : layout.ColumnWidth;
                    check(MathF.Abs(used - width) < 0.01f, "Discovery columns exceed the available width.");
                    check(layout.ColumnWidth - (layout.Padding * 2f) > 0f, "Discovery content has no usable width.");
                }
        check(DiscoveryLayout.Create(820, new UiContext(1, 13)).IsSplit, "Wide discoveries should use two columns.");
        check(!DiscoveryLayout.Create(820, new UiContext(1, 30)).IsSplit, "Large discovery text should stack columns.");
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            try
            {
                DiscoveryLayout.Create(invalid, new UiContext(1, 13));
                throw new InvalidOperationException("Invalid discovery geometry was accepted.");
            }
            catch (ArgumentOutOfRangeException)
            {
                check(true, "Invalid discovery geometry rejected.");
            }
        }

        var longPreview = DiscoveryPreview.Postcard with
        {
            Title = "The very long scenic route beyond the forgotten horizon and the last light of the evening",
            Creator = "An unusually long synthetic creator name / Demo curator",
            Location = "An imagined overlook along an unusually long and winding coastal path",
            Teaser = string.Concat(Enumerable.Repeat("A quiet path, an old map, and another distant horizon. ", 5)),
            Caption = string.Concat(Enumerable.Repeat("A long postcard about a small moment beside the sea. ", 20)),
            StampName = "A very long local demo stamp name to keep the memory of an unexpected journey",
        };
        foreach (var scenario in new[]
        {
            (Name: "discover-wide", Width: 820f, Height: 760f, Scale: 1f, Zoom: 1f, Data: DiscoveryPreview.Samples, Detail: -1),
            (Name: "discover-narrow", Width: 400f, Height: 760f, Scale: 1f, Zoom: 1f, Data: DiscoveryPreview.Samples, Detail: -1),
            (Name: "discover-large-text", Width: 820f, Height: 760f, Scale: 1f, Zoom: 1.75f, Data: DiscoveryPreview.Samples, Detail: -1),
            (Name: "discover-long-text", Width: 680f, Height: 760f, Scale: 1.25f, Zoom: 1.25f, Data: new[] { longPreview }, Detail: -1),
            (Name: "discovery-detail-wide", Width: 820f, Height: 760f, Scale: 1f, Zoom: 1f, Data: DiscoveryPreview.Samples, Detail: 0),
            (Name: "discovery-detail-narrow", Width: 400f, Height: 760f, Scale: 1f, Zoom: 1f, Data: DiscoveryPreview.Samples, Detail: 0),
            (Name: "discovery-detail-large-text", Width: 820f, Height: 760f, Scale: 1f, Zoom: 1.75f, Data: DiscoveryPreview.Samples, Detail: 0),
            (Name: "discovery-detail-long-text", Width: 680f, Height: 760f, Scale: 1.25f, Zoom: 1.25f, Data: new[] { longPreview }, Detail: 0),
            (Name: "discovery-missing-art", Width: 820f, Height: 760f, Scale: 1f, Zoom: 1f, Data: DiscoveryPreview.Samples, Detail: 1),
            (Name: "discover-empty", Width: 400f, Height: 300f, Scale: 1f, Zoom: 1f, Data: Array.Empty<DiscoveryPreview>(), Detail: -1),
        })
        {
            using var host = new HeadlessUi(new Vector2(scenario.Width, scenario.Height), scenario.Scale, scenario.Zoom);
            var view = new DiscoverView(scenario.Data);
            void Draw() => view.Draw(new UiContext(scenario.Scale, ImGui.GetFontSize()));
            host.Frame(Draw);
            host.Frame(Draw);
            if (scenario.Detail >= 0)
            {
                Activate(host, Draw, scenario.Detail);
                check(view.SelectedDiscovery == scenario.Data[scenario.Detail], scenario.Name + " did not open the keyboard-selected discovery.");
            }
            for (var frame = 0; frame < 4; frame++)
            {
                host.Frame(() =>
                {
                    Draw();
                    check(ImGui.GetScrollMaxX() == 0f, scenario.Name + " has horizontal overflow.");
                });
            }
            check(ImGui.GetDrawData().TotalVtxCount > 100, scenario.Name + " produced no real draw data.");
            if (artifacts != null)
                SoftwarePreview.Save(Path.Combine(artifacts, scenario.Name + ".bmp"), host);
            if (scenario.Detail >= 0)
            {
                if (scenario.Width <= 400f || scenario.Zoom > 1f)
                {
                    host.Frame(() => { Draw(); ImGui.SetScrollY(ImGui.GetScrollMaxY()); });
                    host.Frame(Draw);
                    host.Frame(() =>
                    {
                        Draw();
                        check(ImGui.GetScrollY() > 0f, scenario.Name + " did not provide vertical scrolling.");
                        check(ImGui.GetScrollMaxX() == 0f, scenario.Name + " overflows at the bottom.");
                    });
                    if (artifacts != null)
                        SoftwarePreview.Save(Path.Combine(artifacts, scenario.Name + "-end.bmp"), host);
                    // Return to the focused Back item without changing its keyboard focus.
                    host.Frame(() => { Draw(); ImGui.SetScrollY(0f); });
                    host.Frame(Draw);
                }
                // Opening transfers keyboard focus to Back; no mouse or test-side focus override is needed.
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true);
                host.Frame(Draw);
                ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false);
                host.Frame(Draw);
                host.Frame(Draw);
                check(view.SelectedDiscovery == null, scenario.Name + " Back did not return to browsing.");
            }
            Console.WriteLine("PASS " + scenario.Name);
        }

        using (var host = new HeadlessUi(new Vector2(420, 760)))
        {
            var center = Vector2.Zero;
            var activations = 0;
            void DrawCard()
            {
                var context = new UiContext(1, ImGui.GetFontSize());
                var width = UiLayout.ContentWidth - 4f;
                var height = DiscoveryCard.Measure(DiscoveryPreview.Postcard, width, 24f, context);
                var original = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
                if (DiscoveryCard.Draw(DiscoveryPreview.Postcard, width, 24f, height, context))
                    activations++;
                center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
                check(ImGui.GetStyle().Colors[(int)ImGuiCol.Button] == original, "Discovery card leaked its style.");
            }
            host.Frame(DrawCard);
            host.Frame(DrawCard);
            ImGui.GetIO().AddMousePosEvent(center.X, center.Y);
            ImGui.GetIO().AddMouseButtonEvent(0, true);
            host.Frame(DrawCard);
            check(activations == 0, "Discovery card opened on press.");
            ImGui.GetIO().AddMouseButtonEvent(0, false);
            host.Frame(DrawCard);
            host.Frame(DrawCard);
            check(activations == 1, "Discovery card did not open once on release.");

            ImGui.GetIO().AddMouseButtonEvent(0, true);
            host.Frame(DrawCard);
            ImGui.GetIO().AddMousePosEvent(-100, -100);
            ImGui.GetIO().AddMouseButtonEvent(0, false);
            host.Frame(DrawCard);
            check(activations == 1, "Releasing outside the card incorrectly opened it.");
        }

        using (var host = new HeadlessUi(new Vector2(280, 300), 1f, 2f))
        {
            var activations = 0;
            void DrawDisabled()
            {
                ImGui.BeginDisabled();
                try
                {
                    if (UiButton.Draw("Collect demo stamp with an unusually long label", new UiContext(1, ImGui.GetFontSize())))
                        activations++;
                    check(ImGui.GetItemRectSize().X <= UiLayout.ContentWidth, "Long button exceeds available width.");
                    check(ImGui.GetItemRectSize().Y > ImGui.GetFrameHeight(), "Long button did not grow for wrapped text.");
                }
                finally
                {
                    ImGui.EndDisabled();
                }
            }
            host.Frame(DrawDisabled);
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
            ImGui.GetIO().AddMousePosEvent(center.X, center.Y);
            ImGui.GetIO().AddMouseButtonEvent(0, true);
            host.Frame(DrawDisabled);
            ImGui.GetIO().AddMouseButtonEvent(0, false);
            host.Frame(DrawDisabled);
            check(activations == 0, "Disabled preview collection action activated.");
        }

        foreach (var zoom in new[] { 1f, 1.25f, 1.75f, 2f })
        {
            using var host = new HeadlessUi(new Vector2(400, 300), 1f, zoom);
            host.Frame(() =>
            {
                var context = new UiContext(1, ImGui.GetFontSize());
                UiButton.Draw("Back", context);
                var singleLineHeight = MathF.Max(MathF.Max(UiMetrics.ButtonHeight, ImGui.GetFrameHeight()), context.FontSize + UiMetrics.Small);
                check(MathF.Abs(ImGui.GetItemRectSize().Y - singleLineHeight) < 0.01f,
                    $"Short button wrapped: zoom {zoom}, font {context.FontSize}, label {ImGui.CalcTextSize("Back")}, item {ImGui.GetItemRectSize()}, expected height {singleLineHeight}.");
            });
        }

        using (var host = new HeadlessUi(new Vector2(400, 300)))
        {
            var samples = Enumerable.Range(0, 8).Select(index => DiscoveryPreview.Postcard with { Id = "scroll-demo-" + index }).ToArray();
            var view = new DiscoverView(samples);
            var scroll = 0f;
            void Draw()
            {
                view.Draw(new UiContext(1, ImGui.GetFontSize()));
                scroll = ImGui.GetScrollY();
            }
            host.Frame(Draw);
            host.Frame(Draw);
            host.Frame(() => { Draw(); ImGui.SetScrollY(1000f); });
            host.Frame(Draw);
            host.Frame(Draw);
            var browseScroll = scroll;
            check(browseScroll > 900f, "Browse did not scroll to the test position.");
            if (artifacts != null)
                SoftwarePreview.Save(Path.Combine(artifacts, "discover-scroll.bmp"), host);
            ImGui.GetIO().AddMousePosEvent(100, 160);
            ImGui.GetIO().AddMouseButtonEvent(0, true);
            host.Frame(Draw);
            ImGui.GetIO().AddMouseButtonEvent(0, false);
            host.Frame(Draw);
            host.Frame(Draw);
            host.Frame(Draw);
            check(view.SelectedDiscovery != null, "Mouse did not open a scrolled discovery.");
            check(scroll == 0f, "Detail did not start at the top.");
            Activate(host, Draw, 0);
            host.Frame(Draw);
            host.Frame(Draw);
            check(view.SelectedDiscovery == null, "Back did not leave the scrolled discovery.");
            check(MathF.Abs(scroll - browseScroll) < 1f, "Back did not restore the browse scroll position.");
        }
    }

    private static void Activate(HeadlessUi host, Action draw, int focusOffset)
    {
        host.Frame(() => { ImGui.SetKeyboardFocusHere(focusOffset); draw(); });
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, true);
        host.Frame(draw);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Space, false);
        host.Frame(draw);
        host.Frame(draw);
    }
}
