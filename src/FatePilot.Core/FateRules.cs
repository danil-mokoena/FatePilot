namespace FatePilot.Core;

public static class FateRules
{
    // Curated large-group overworld encounters, including their alternate locations.
    // Independent of the client's language; update when new special encounters are added.
    private static readonly HashSet<ushort> GroupBosses =
    [196,197,198,199,200,201,202,203,204,205,206,207,505,506,
     877,878,879,902,903,904,905,906,907,1103,1104,1105,1106,1107,1108,
     1432,1464,1763,1855,1871,1922];
    public static bool IsGroupBoss(ushort id) => GroupBosses.Contains(id);
    // Values from the client's Fate sheet; collection is 2, not a combat category.
    public static FateKind FromRule(byte rule) => rule switch
    {
        1 => FateKind.Combat,
        2 => FateKind.Collection,
        3 => FateKind.Escort,
        4 => FateKind.Defense,
        _ => FateKind.Unknown,
    };

    public static bool Enabled(FateInfo fate, RunOptions options) => fate.EffectiveKind switch
    {
        FateKind.Combat or FateKind.Defense => true,
        FateKind.Collection => options.IncludeCollection,
        FateKind.Escort => options.IncludeEscort,
        _ => false,
    };
}
