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
    // FATE id → zone, learned whenever a FATE is seen (the game data has no direct link).
    public Dictionary<ushort, uint> FateZones { get; set; } = [];
    // Local per-zone farming history (0.7).
    public ZoneHistory History { get; set; } = new();
    // 0.9: interface theme, map zoom, map in the compact panel and keyboard shortcuts.
    public string Theme { get; set; } = "chocobo";
    public float MapRadius { get; set; } = 250f;
    public bool CompactMap { get; set; }
    public List<HotkeyBinding> Hotkeys { get; set; } = [];
    // 1.0: in-game validation checklist (0 = not checked, 1 = works, 2 = problem).
    public Dictionary<string, int> Checklist { get; set; } = [];
    public void Save() => Services.Plugin.SavePluginConfig(this);
}
