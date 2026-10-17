using System;
using Dalamud.Configuration;

namespace Wanderveil;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ReducedMotion { get; set; }
}
