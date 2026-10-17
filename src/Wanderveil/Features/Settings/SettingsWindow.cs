using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Wanderveil.Features.Settings;

internal sealed class SettingsWindow : Window
{
    private readonly Configuration configuration;
    private readonly Func<bool> saveConfiguration;
    private bool saveFailed;

    internal SettingsWindow(Configuration configuration, Func<bool> saveConfiguration)
        : base("Wanderveil Settings###Wanderveil.Settings")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        Size = new Vector2(420, 220);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 180),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Appearance");
        ImGui.Separator();
        ImGui.Spacing();

        var reducedMotion = configuration.ReducedMotion;
        if (ImGui.Checkbox("Reduce motion", ref reducedMotion))
        {
            configuration.ReducedMotion = reducedMotion;
            saveFailed = !saveConfiguration();
        }

        ImGui.TextWrapped("Prefer simple transitions for page turns and reveals.");

        if (saveFailed)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Your settings could not be saved. You can try again.");
            if (ImGui.Button("Retry save"))
                saveFailed = !saveConfiguration();
        }
    }
}
