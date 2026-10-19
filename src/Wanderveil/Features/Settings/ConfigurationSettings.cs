using System;
using Wanderveil.UI;

namespace Wanderveil.Features.Settings;

// Backs the passport's Settings pages with the plugin's saved configuration.
internal sealed class ConfigurationSettings(Configuration configuration, Func<bool> save) : IBookShellSettings
{
    public bool ReducedMotion
    {
        get => configuration.ReducedMotion;
        set => configuration.ReducedMotion = value;
    }

    public bool Save() => save();
}
