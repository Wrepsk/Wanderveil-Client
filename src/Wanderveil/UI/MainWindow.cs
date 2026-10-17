using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Wanderveil.UI;

internal sealed class MainWindow : Window
{
    private readonly Action openSettings;

    internal MainWindow(Action openSettings) : base("Wanderveil###Wanderveil.Main")
    {
        this.openSettings = openSettings;
        Size = new Vector2(680, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        CompassMark.Draw();
        ImGui.SameLine();
        ImGui.TextColored(ShellStyle.Parchment, "Wanderveil");
        ImGui.TextWrapped("Every place has a story. Leave yours.");
        ImGui.Spacing();

        if (ImGui.BeginTabBar("Wanderveil.Sections"))
        {
            try
            {
                DrawSection("Discover", "Every journey starts with a discovery.",
                    "Postcards, hidden stories, and scenic stops across Eorzea. Curated discoveries are coming soon.");
                DrawSection("Passport", "A place for the stories you collect.",
                    "Your adventurer's passport will hold travel stamps and memories. Your first pages are coming soon.");
                DrawSection("Echoes", "Follow a story beyond the next horizon.",
                    "Creator-made adventures, clues, and chapters are planned for a later release.");
                DrawSection("Studio", "Leave a discovery for someone else.",
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
        if (ImGui.Button("Settings"))
            openSettings();
    }

    private static void DrawSection(string label, string heading, string description)
    {
        if (!ImGui.BeginTabItem(label))
            return;

        try
        {
            ImGui.Spacing();
            ImGui.TextColored(ShellStyle.Veil, "Coming soon");
            ImGui.Spacing();
            ImGui.TextWrapped(heading);
            ImGui.Spacing();
            ImGui.TextWrapped(description);
            ImGui.Spacing();
        }
        finally
        {
            ImGui.EndTabItem();
        }
    }
}
