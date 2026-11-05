using FatePilot.Core;
using Lumina.Excel.Sheets;

namespace FatePilot;

internal sealed record ZoneCard(uint TerritoryId, uint ExpansionId, string Expansion, string Name, string ImagePath);

internal static class ZoneCatalog
{
    public static List<ZoneCard> Read() => Services.Data.GetExcelSheet<TerritoryType>()
        .Where(t => t.IsInUse && t.TerritoryIntendedUse.RowId == 1 && t.PlaceName.RowId != 0 && t.Map.RowId != 0 && t.ContentFinderCondition.RowId == 0)
        .Select(t => new ZoneCard(t.RowId, t.ExVersion.RowId, t.ExVersion.Value.Name.ExtractText(),
            t.PlaceName.Value.Name.ExtractText(), $"ui/loadingimage/{t.LoadingImage.Value.FileName.ExtractText()}_hr1.tex"))
        .OrderBy(t => t.ExpansionId).ThenBy(t => t.Name).ToList();
}
