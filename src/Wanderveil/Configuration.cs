using System;
using Dalamud.Configuration;

namespace Wanderveil;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ReducedMotion { get; set; }

    // The Wanderveil server to sign in to; empty until the holder enters one (no public server exists yet).
    public string ServerUrl { get; set; } = "";
}
