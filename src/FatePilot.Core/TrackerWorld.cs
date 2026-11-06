namespace FatePilot.Core;

// Game world metadata only. No character identity or home-world fallback.
public sealed record TrackerWorld(uint Id, string Name, string DataCenter)
{
    public static TrackerWorld? FromCurrent(uint id, string name, string dataCenter, bool isPublic, bool loading)
        => loading || !isPublic || id == 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dataCenter)
            ? null : new(id, name, dataCenter);
}
