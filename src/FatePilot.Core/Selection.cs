using System.Numerics;

namespace FatePilot.Core;

// Why a FATE is not eligible (None = eligible). The order is the order of the checks.
public enum FateSkip { None, OtherZone, NotStarted, KindOff, GroupBoss, TooHigh, AlmostDone, TooLittleTime, Ignored, RecentlySkipped }

public static class Selection
{
    public static FateKey Key(FateInfo f, World w) => new(w.Territory, w.Instance, f.Id, f.StartedAt);

    public static FateSkip Check(FateInfo f, World w, RunOptions o, IReadOnlyDictionary<FateKey, double> excludedUntil, double now)
    {
        if (f.TerritoryId != w.Territory) return FateSkip.OtherZone;
        if (!(f.State == EventState.Running || (f.State == EventState.Preparing && f.CanStart))) return FateSkip.NotStarted;
        if (!FateRules.Enabled(f, o)) return FateSkip.KindOff;
        if (o.ExcludeGroupBosses && FateRules.IsGroupBoss(f.Id)) return FateSkip.GroupBoss;
        if (f.Level > w.Level + o.LevelAbovePlayer) return FateSkip.TooHigh;
        if (f.Progress >= o.MaximumProgress) return FateSkip.AlmostDone;
        if (f.State != EventState.Preparing && f.SecondsLeft < o.MinimumSecondsLeft) return FateSkip.TooLittleTime;
        if (o.ExcludedFates.Contains(f.Id)) return FateSkip.Ignored;
        if (excludedUntil.TryGetValue(Key(f, w), out var until) && until > now) return FateSkip.RecentlySkipped;
        return FateSkip.None;
    }

    public static string Reason(FateSkip skip) => skip switch
    {
        FateSkip.OtherZone => L.T("Outra zona"),
        FateSkip.NotStarted => L.T("Ainda não pode começar"),
        FateSkip.KindOff => L.T("Tipo desativado"),
        FateSkip.GroupBoss => L.T("Chefe de grupo"),
        FateSkip.TooHigh => L.T("Nível alto demais"),
        FateSkip.AlmostDone => L.T("Quase concluído"),
        FateSkip.TooLittleTime => L.T("Pouco tempo restante"),
        FateSkip.Ignored => L.T("Ignorado"),
        FateSkip.RecentlySkipped => L.T("Pulado há pouco"),
        _ => L.T("Elegível"),
    };

    public static FateInfo? Choose(World w, RunOptions o, IReadOnlyDictionary<FateKey, double> excludedUntil, double now) => w.Fates
        .Where(f => Check(f, w, o, excludedUntil, now) == FateSkip.None)
        .OrderByDescending(f => o.PriorityFates.Contains(f.Id))
        .ThenBy(f => Score(f, w, o))
        .ThenByDescending(f => o.PreferBonus && f.Bonus)
        .ThenBy(f => f.Id).FirstOrDefault();

    // Lower is better: travel cost, minus what a bonus is worth in yalms when rewards are preferred.
    public static float Score(FateInfo f, World w, RunOptions o) =>
        TravelCost(f.Position, w, o) - (o.PreferRewards && f.Bonus ? o.BonusWorthYalms : 0);

    // 1.5: a teleport is worth this many yalms of flying (loading screen, cast, mounting again).
    public const float TeleportPenalty = 200;

    // Flying straight there, or teleporting to the zone's aetheryte closest to it, whichever is cheaper.
    public static float TravelCost(Vector3 target, World w, RunOptions o) =>
        CloserAetheryte(target, w, o) is { } via ? Vector3.Distance(via.Position, target) + TeleportPenalty : Vector3.Distance(w.Position, target);

    // The zone's aetheryte to teleport to first, when that beats flying straight to the target.
    public static AetherytePoint? CloserAetheryte(Vector3 target, World w, RunOptions o)
    {
        if (!o.TeleportToCloserAetheryte) return null;
        var direct = Vector3.Distance(w.Position, target);
        var best = w.ZoneAetherytes.Where(a => a.Destination.TerritoryId == w.Territory)
            .MinBy(a => Vector3.Distance(a.Position, target));
        return best != null && Vector3.Distance(best.Position, target) + TeleportPenalty < direct ? best : null;
    }

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
