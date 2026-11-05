using Dalamud.Configuration;
using FatePilot.Core;

namespace FatePilot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "auto";
    public RunOptions Options { get; set; } = new();
    public void Save() => Services.Plugin.SavePluginConfig(this);
}
