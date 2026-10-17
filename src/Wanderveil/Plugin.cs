using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Wanderveil.Features.Settings;
using Wanderveil.UI;

namespace Wanderveil;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private const string CommandName = "/wanderveil";

    private readonly WindowSystem windowSystem = new("Wanderveil");
    private readonly Configuration configuration;
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;
    private bool commandRegistered;
    private bool disposed;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        settingsWindow = new SettingsWindow(configuration, SaveConfiguration);
        mainWindow = new MainWindow(OpenSettings);

        try
        {
            windowSystem.AddWindow(mainWindow);
            windowSystem.AddWindow(settingsWindow);

            commandRegistered = CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open or close Wanderveil. Use /wanderveil settings for appearance settings.",
            });

            if (!commandRegistered)
                throw new InvalidOperationException("Could not register /wanderveil; the command is already in use.");

            PluginInterface.UiBuilder.Draw += windowSystem.Draw;
            PluginInterface.UiBuilder.OpenMainUi += OpenMain;
            PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
            Log.Information("Wanderveil loaded.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;

        if (commandRegistered)
        {
            CommandManager.RemoveHandler(CommandName);
            commandRegistered = false;
        }

        // These windows own no textures, hooks, subscriptions, or background tasks.
        windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string arguments)
    {
        if (string.Equals(arguments.Trim(), "settings", StringComparison.OrdinalIgnoreCase))
            OpenSettings();
        else
            mainWindow.Toggle();
    }

    private void OpenMain() => mainWindow.IsOpen = true;

    private void OpenSettings() => settingsWindow.IsOpen = true;

    private bool SaveConfiguration()
    {
        try
        {
            PluginInterface.SavePluginConfig(configuration);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not save Wanderveil settings.");
            return false;
        }
    }
}
