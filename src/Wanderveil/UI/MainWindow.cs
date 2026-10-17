using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Wanderveil.Features.Discover;
using Wanderveil.Features.Passport;
using Wanderveil.UI.Components;
using Wanderveil.UI.Primitives;

namespace Wanderveil.UI;

internal sealed class MainWindow : Window
{
    private readonly Action openSettings;
    private readonly PassportView passport = new(PassportPreview.Sample);
    private readonly DiscoverView discover = new(DiscoveryPreview.Samples);
    private string? requestedSection = "Passport";

    internal MainWindow(Action openSettings) : base("Wanderveil###Wanderveil.Main")
    {
        this.openSettings = openSettings;
        Size = new Vector2(820, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var context = new UiContext(ShellStyle.Scale, ImGui.GetFontSize());
        CompassMark.Draw();
        ImGui.SameLine();
        UiTypography.Text("Wanderveil", TextRole.Heading, context, UiTheme.Paper);
        UiTypography.Text("Every place has a story. Leave yours.", TextRole.Body, context, UiTheme.Paper);
        ImGui.Spacing();

        var request = requestedSection;
        requestedSection = null;
        if (ImGui.BeginTabBar("Wanderveil.Sections"))
        {
            try
            {
                if (ImGui.BeginTabItem("Discover", request == "Discover" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    try
                    {
                        discover.Draw(context);
                    }
                    finally
                    {
                        ImGui.EndTabItem();
                    }
                }
                if (ImGui.BeginTabItem("Passport", request == "Passport" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    try
                    {
                        if (passport.Draw(context))
                            requestedSection = "Discover";
                    }
                    finally
                    {
                        ImGui.EndTabItem();
                    }
                }
                DrawSection("Echoes", request, context, "Follow a story beyond the next horizon.",
                    "Creator-made adventures, clues, and chapters are planned for a later release.");
                DrawSection("Studio", request, context, "Leave a discovery for someone else.",
                    "Guided tools for postcards, stamp spots, and trails are coming later. Creation and publishing are not available yet.");
            }
            finally
            {
                ImGui.EndTabBar();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (UiButton.Draw("Settings", context))
            openSettings();
    }

    private static void DrawSection(string label, string? request, UiContext context, string heading, string description)
    {
        if (!ImGui.BeginTabItem(label, request == label ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            return;

        try
        {
            ImGui.Spacing();
            UiTypography.Text("Coming soon", TextRole.Caption, context, UiTheme.Veil);
            ImGui.Spacing();
            UiTypography.Text(heading, TextRole.Heading, context, UiTheme.Paper);
            ImGui.Spacing();
            UiTypography.Text(description, TextRole.Body, context, UiTheme.Paper);
            ImGui.Spacing();
        }
        finally
        {
            ImGui.EndTabItem();
        }
    }
}
