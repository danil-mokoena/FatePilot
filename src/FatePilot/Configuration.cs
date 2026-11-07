using Dalamud.Configuration;
using FatePilot.Core;

namespace FatePilot;

// A named snapshot of every session setting (route, filters, goals, costs, supplies, jobs).
[Serializable]
public sealed class RouteProfile
{
    public string Name { get; set; } = "";
    public RunOptions Options { get; set; } = new();
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "auto";
    public RunOptions Options { get; set; } = new();
    public List<RouteProfile> Profiles { get; set; } = [];
    public void Save() => Services.Plugin.SavePluginConfig(this);
}
