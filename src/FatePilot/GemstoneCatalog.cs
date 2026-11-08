using FatePilot.Core;
using Lumina.Excel.Sheets;

namespace FatePilot;

// Coarse shelves for the picker's filter chips, from the item's FilterGroup.
public enum GemstoneShelf { Materials, Housing, Orchestrion, Gear, Other }

public sealed record GemstoneItem(uint ItemId, string Name, uint Icon, int Cost, GemstoneShelf Shelf, string Category, uint CategoryIcon, int StackSize, bool Unique);

// Items a gemstone trader sells for bicolor gemstones, read from the client's FateShop and
// SpecialShop sheets (in the client's language). Cached per trader, in shop order.
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
                    var category = item.ItemUICategory.ValueNullable;
                    list.Add(new(item.RowId, item.Name.ExtractText(), item.Icon, (int)cost.CurrencyCost, Shelf(item),
                        category?.Name.ExtractText() ?? "", (uint)(category?.Icon ?? 0), (int)Math.Max(1, item.StackSize), item.IsUnique));
                }
            }
        return cache[trader.NpcId] = list;
    }

    private static GemstoneShelf Shelf(Item item) => item.FilterGroup switch
    {
        12 => GemstoneShelf.Materials,
        14 => GemstoneShelf.Housing,
        32 => GemstoneShelf.Orchestrion,
        >= 1 and <= 4 => GemstoneShelf.Gear,
        _ => GemstoneShelf.Other,
    };
}
