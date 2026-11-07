namespace FatePilot.Core;

// A zone with no aetheryte of its own, reached by teleporting to a neighbouring
// city and taking that city's aethernet shard into the zone.
public sealed record Gateway(uint Territory, uint ViaTerritory, uint ViaAetheryteId, uint ShardId);

public static class Gateways
{
    // Game data (Aetheryte sheet): Idyllshire aetheryte 75 in territory 478;
    // shard 91 "Prologue Gate (Western Hinterlands)" lands in territory 399.
    public static readonly IReadOnlyList<Gateway> All = [new(399, 478, 75, 91)];

    public static Gateway? ForZone(uint territory) => All.FirstOrDefault(g => g.Territory == territory);

    public static Gateway? For(Destination? destination) => destination == null ? null
        : All.FirstOrDefault(g => g.Territory == destination.TerritoryId && g.ViaAetheryteId == destination.AetheryteId);

    // Where the teleport itself lands: the city for gateway zones, otherwise the zone.
    public static uint ArrivalTerritory(Destination destination) => For(destination)?.ViaTerritory ?? destination.TerritoryId;
}
