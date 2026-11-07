using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;
using AchievementRow = Lumina.Excel.Sheets.Achievement;
using NativeAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace FatePilot;

public enum AchievementGroup { Fate, Zone, Count }

// One FATE-related achievement, read from the client's own data in its language.
public sealed class FateAchievement(uint id, string name, string description, uint icon, AchievementGroup group, ushort[] fateIds, uint territory)
{
    public uint Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public uint Icon { get; } = icon;
    public AchievementGroup Group { get; } = group;
    public ushort[] FateIds { get; } = fateIds;   // FATEs named in the description (boss / specific FATEs)
    public uint Territory { get; } = territory;   // zone named in the description (shared FATE / zone counts)
    public bool Complete { get; set; }
    public uint Current { get; set; }
    public uint Max { get; set; }
}

// Builds the FATE achievement list from game data and keeps completion/progress current.
// Progress uses the game's own per-achievement request, one at a time and only while
// the Achievements page is open (the same request the in-game window makes on hover).
internal sealed unsafe class AchievementCatalog
{
    private static readonly Regex Quoted = new("[“\"«„「『‘](.+?)[”\"»“」』’]", RegexOptions.Compiled);
    private List<FateAchievement> rows = [];
    private string builtFor = "";
    private uint pending;
    private double pendingSince, lastRequest, lastRefresh;
    private readonly HashSet<uint> tried = [];
    private static double Now => Environment.TickCount64 / 1000d;

    public IReadOnlyList<FateAchievement> Rows => rows;
    public bool Loaded { get; private set; }

    // Every open-world FATE achievement lives in Battle → General (category 1); quests, the
    // Diadem and field operations use other categories. The word check is case-sensitive so
    // quest titles such as "Dragoon's Fate" never match.
    private const uint BattleGeneralCategory = 1;
    private static bool IsFateText(string text) =>
        text.Contains("FATE", StringComparison.Ordinal) || text.Contains("F.A.T.E", StringComparison.Ordinal)
        || text.Contains("ALÉA", StringComparison.Ordinal) || text.Contains("ALEA", StringComparison.Ordinal);

    public void EnsureBuilt()
    {
        var language = Services.Data.Language.ToString();
        if (builtFor == language && rows.Count > 0) return;
        builtFor = language;
        var fateIds = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        foreach (var fate in Services.Data.GetExcelSheet<Fate>())
        {
            var name = fate.Name.ExtractText().Trim();
            if (name.Length < 3 || fate.RowId > ushort.MaxValue) continue;
            if (!fateIds.TryGetValue(name, out var list)) fateIds[name] = list = [];
            list.Add((ushort)fate.RowId);
        }
        var zones = ZoneCatalog.Read().Where(z => z.Name.Length >= 3).OrderByDescending(z => z.Name.Length).ToList();
        var built = new List<FateAchievement>();
        foreach (var a in Services.Data.GetExcelSheet<AchievementRow>())
        {
            var name = a.Name.ExtractText();
            var description = a.Description.ExtractText();
            if (a.RowId == 0 || name.Length == 0 || !IsFateText(description)) continue;
            if (a.AchievementCategory.RowId != BattleGeneralCategory) continue;
            var named = Quoted.Matches(description).Select(m => m.Groups[1].Value.Trim().TrimEnd(',', '.', '、', '。'))
                .SelectMany(n => fateIds.TryGetValue(n, out var ids) ? ids : []).Distinct().ToArray();
            var zone = zones.FirstOrDefault(z => description.Contains(z.Name, StringComparison.OrdinalIgnoreCase))?.TerritoryId ?? 0;
            var group = named.Length > 0 ? AchievementGroup.Fate : zone != 0 ? AchievementGroup.Zone : AchievementGroup.Count;
            built.Add(new FateAchievement(a.RowId, name, description, a.Icon, group, named, zone));
        }
        rows = built;
        tried.Clear(); pending = 0;
    }

    // Called from the framework update while the Achievements page is visible.
    public void Tick()
    {
        EnsureBuilt();
        var native = NativeAchievement.Instance();
        Loaded = native != null && native->IsLoaded();
        if (!Loaded) return;
        var now = Now;
        if (now - lastRefresh > 2)
        {
            lastRefresh = now;
            foreach (var row in rows) row.Complete = native->IsComplete((int)row.Id);
        }
        if (pending != 0)
        {
            if (native->ProgressRequestState == NativeAchievement.AchievementState.Loaded && native->ProgressAchievementId == pending)
            {
                var row = rows.FirstOrDefault(r => r.Id == pending);
                if (row != null) { row.Current = native->ProgressCurrent; row.Max = native->ProgressMax; }
                pending = 0;
            }
            else if (now - pendingSince > 3) pending = 0;
            return;
        }
        if (now - lastRequest < 1) return;
        var next = rows.FirstOrDefault(r => !r.Complete && r.Max == 0 && r.Group != AchievementGroup.Fate && !tried.Contains(r.Id));
        if (next == null) return;
        tried.Add(next.Id);
        pending = next.Id; pendingSince = lastRequest = now;
        native->RequestAchievementProgress(next.Id);
    }
}
