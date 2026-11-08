using FatePilot.Core;
using Lumina.Excel.Sheets;

namespace FatePilot;

public sealed record GemstoneItem(uint ItemId, string Name, uint Icon, int Cost);

// Items a gemstone trader sells for bicolor gemstones, read from the client's FateShop and
// SpecialShop sheets (in the client's language). Cached per trader.
internal static class GemstoneCatalog
{
    private const uint BicolorGemstone = 26807;
    private static readonly Dictionary<uint, List<GemstoneItem>> cache = [];

    public static List<GemstoneItem> ItemsFor(uint territory)
    {
        if (GemstoneTraders.In(territory) is not { } trader) return [];
        if (cache.TryGetValue(trader.NpcId, out var cached)) return cached;
        var list = new List<GemstoneItem>();
        var shop = Services.Data.GetExcelSheet<FateShop>().GetRowOrDefault(trader.NpcId);
        var shops = Services.Data.GetExcelSheet<SpecialShop>();
        var items = Services.Data.GetExcelSheet<Item>();
        if (shop != null)
            foreach (var shopRef in shop.Value.SpecialShop)
            {
                if (shopRef.RowId == 0 || shops.GetRowOrDefault(shopRef.RowId) is not { } special) continue;
                foreach (var entry in special.Item)
                {
                    var receive = entry.ReceiveItems.FirstOrDefault();
                    var cost = entry.ItemCosts.FirstOrDefault(c => c.ItemCost.RowId == BicolorGemstone);
                    if (receive.Item.RowId == 0 || cost.CurrencyCost == 0 || receive.ReceiveCount != 1) continue;
                    if (list.Any(i => i.ItemId == receive.Item.RowId) || items.GetRowOrDefault(receive.Item.RowId) is not { } item) continue;
                    list.Add(new(item.RowId, item.Name.ExtractText(), item.Icon, (int)cost.CurrencyCost));
                }
            }
        return cache[trader.NpcId] = list.OrderBy(i => i.Name).ToList();
    }
}
