using FatePilot.Core;
using Lumina.Excel.Sheets;

namespace FatePilot;

public sealed record LevelZone(Destination Destination, int Fates);

// FATE levels per zone, from the game data (FATE level + the zone resolved from layouts).
// Used to build a route of the zones with the most FATEs for a given job level.
internal sealed class FateLevelCatalog
{
    private Dictionary<uint, List<int>> levelsByZone = [];
    private Task? building;
    public bool Ready => building is { IsCompleted: true };

    public void EnsureBuilt() => building ??= Task.Run(() =>
    {
        var zones = AchievementCatalog.FateZones();
        var fieldZones = ZoneCatalog.Read().Select(z => z.TerritoryId).ToHashSet();
        var result = new Dictionary<uint, List<int>>();
        foreach (var fate in Services.Data.GetExcelSheet<Fate>())
        {
            if (fate.ClassJobLevel == 0 || !zones.TryGetValue(fate.RowId, out var zone) || !fieldZones.Contains(zone)) continue;
            if (!result.TryGetValue(zone, out var list)) result[zone] = list = [];
            list.Add(fate.ClassJobLevel);
        }
        levelsByZone = result;
    });

    // FATEs from a couple of levels below the job up to the allowed levels above it.
    public static (int Min, int Max) Band(int level, int above) => (Math.Max(1, level - 2), level + above);

    // Best unlocked zones for this level, most FATEs in the band first (at most `count`).
    public List<LevelZone> Best(int level, int above, IReadOnlyList<Destination> unlocked, int count = 3)
    {
        if (!Ready || level <= 0) return [];
        var (min, max) = Band(level, above);
        return levelsByZone
            .Select(z => (Zone: z.Key, Fates: z.Value.Count(l => l >= min && l <= max)))
            .Where(z => z.Fates > 0)
            .Select(z => (Destination: unlocked.FirstOrDefault(d => d.TerritoryId == z.Zone), z.Fates))
            .Where(z => z.Destination != null)
            .OrderByDescending(z => z.Fates)
            .Take(count)
            .Select(z => new LevelZone(z.Destination!, z.Fates))
            .ToList();
    }
}
