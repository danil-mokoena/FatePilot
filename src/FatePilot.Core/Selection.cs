using System.Numerics;

namespace FatePilot.Core;

public static class Selection
{
    public static FateKey Key(FateInfo f, World w) => new(w.Territory, w.Instance, f.Id, f.StartedAt);

    public static FateInfo? Choose(World w, RunOptions o, IReadOnlyDictionary<FateKey, double> excludedUntil, double now) => w.Fates
        .Where(f => f.TerritoryId == w.Territory && (f.State == EventState.Running || (f.State == EventState.Preparing && f.CanStart)) && FateRules.Enabled(f, o)
            && (!o.ExcludeGroupBosses || !FateRules.IsGroupBoss(f.Id))
            && f.Level <= w.Level + o.LevelAbovePlayer && f.Progress < o.MaximumProgress
            && (f.State == EventState.Preparing || f.SecondsLeft >= o.MinimumSecondsLeft) && !o.ExcludedFates.Contains(f.Id)
            && (!excludedUntil.TryGetValue(Key(f, w), out var until) || until <= now))
        .OrderByDescending(f => o.PriorityFates.Contains(f.Id))
        .ThenBy(f => Score(f, w, o))
        .ThenByDescending(f => o.PreferBonus && f.Bonus)
        .ThenBy(f => f.Id).FirstOrDefault();

    // Lower is better: distance, minus what a bonus is worth in yalms when rewards are preferred.
    public static float Score(FateInfo f, World w, RunOptions o) =>
        Vector3.Distance(w.Position, f.Position) - (o.PreferRewards && f.Bonus ? o.BonusWorthYalms : 0);

    public static int? NextInstance(int current, int count, ISet<int> visited)
    {
        if (current <= 0 || count is < 2 or > 9) return null;
        for (var i = 1; i <= count; i++)
        {
            var next = (current - 1 + i) % count + 1;
            if (next != current && !visited.Contains(next)) return next;
        }
        return null;
    }

    public static Destination? NextZone(IReadOnlyList<Destination> route, uint territory)
    {
        if (route.Count == 0) return null;
        var index = -1;
        for (var i = 0; i < route.Count; i++) if (route[i].TerritoryId == territory) index = i;
        var next = route[(index + 1) % route.Count];
        return next.TerritoryId == territory ? null : next;
    }
}
