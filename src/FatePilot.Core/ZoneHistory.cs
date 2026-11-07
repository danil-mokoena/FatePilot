using System.Globalization;
using System.Numerics;

namespace FatePilot.Core;

// Running sums for one map cell where FATEs appeared (average = sum / count).
public sealed class FateSpot
{
    public int Count { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public Vector3 Center => Count == 0 ? Vector3.Zero : new Vector3(X, Y, Z) / Count;
}

// Local, per-zone farming history across sessions. Nothing here leaves the machine.
public sealed class ZoneStat
{
    public double Seconds { get; set; }
    public int Fates { get; set; }
    public int Failed { get; set; }
    public int Gems { get; set; }
    public Dictionary<string, FateSpot> Spots { get; set; } = [];

    public double Hours => Seconds / 3600;
    public double GemsPerHour => Hours >= 1d / 60 ? Gems / Hours : 0;
    public double FatesPerHour => Hours >= 1d / 60 ? Fates / Hours : 0;
    public double FailureRate => Fates + Failed == 0 ? 0 : (double)Failed / (Fates + Failed);
}

public sealed class ZoneHistory
{
    public const float CellSize = 100f;   // yalms per spot cell
    public const int HotspotMinimum = 3;  // appearances before a cell is trusted
    public Dictionary<uint, ZoneStat> Zones { get; set; } = [];

    public ZoneStat For(uint territory)
    {
        if (!Zones.TryGetValue(territory, out var stat)) Zones[territory] = stat = new ZoneStat();
        return stat;
    }

    public void RecordSpot(uint territory, Vector3 position)
    {
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{MathF.Floor(position.X / CellSize)}:{MathF.Floor(position.Z / CellSize)}");
        var spots = For(territory).Spots;
        if (!spots.TryGetValue(key, out var spot)) spots[key] = spot = new FateSpot();
        spot.Count++; spot.X += position.X; spot.Y += position.Y; spot.Z += position.Z;
    }

    // The cell where FATEs appeared most often in this zone (only once it has enough samples).
    public Vector3? Hotspot(uint territory)
    {
        if (!Zones.TryGetValue(territory, out var stat)) return null;
        var best = stat.Spots.Values.Where(s => s.Count >= HotspotMinimum).MaxBy(s => s.Count);
        return best?.Center;
    }
}
