using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// Gemstone purchase queue: the queue itself, then the trader's shop as a filterable table.
internal sealed partial class MainWindow
{
    private const uint GemstoneItemId = 26807;
    private int newOrderQuantity = 1;
    private string orderSearch = "";
    private GemstoneShelf? shelfFilter;
    private bool sortByPrice;

    private void DrawPurchaseQueue(RunOptions o)
    {
        var scale = ImGuiHelpers.GlobalScale;
        BeginCard("purchaseQueue");
        CardTitle(FontAwesomeIcon.ShoppingCart, L.T("Fila de compras com gemstones"));
        ImGui.TextWrapped(L.T("Quando o saldo cobre o próximo item, o FatePilot termina o FATE, vai ao trocador, compra e volta aos FATEs. Depois segue para o próximo item da fila."));
        var auto = o.AutoBuyGemstones;
        if (ImGui.Checkbox(L.T("Comprar automaticamente na loja do trocador"), ref auto)) { o.AutoBuyGemstones = auto; SaveSettings(); }
        Help(L.T("O FatePilot escolhe a loja no menu do trocador, acha o item pelo nome e só confirma uma troca que cite esse item. Se algo não bater, ele para e deixa a compra com você."));
        ImGui.Spacing();
        var city = o.GemstoneDestination ?? o.ReturnDestination;
        var catalog = city == null ? [] : GemstoneCatalog.ItemsFor(city.TerritoryId);
        if (city == null || GemstoneTraders.In(city.TerritoryId) == null)
        { ImGui.TextColored(Danger, L.T("Escolha acima a cidade para trocar gemstones.")); EndCard(); return; }
        if (catalog.Count == 0)
            ImGui.TextColored(Danger, L.T("A lista de itens deste trocador não está nos dados do jogo. Use Old Sharlayan, Radz-at-Han, Tuliyollal ou Solution Nine."));

        DrawQueueRows(o);
        if (catalog.Count > 0)
        {
            ImGui.Spacing(); ImGui.Spacing();
            DrawTraderShop(o, catalog, scale);
        }
        EndCard();
    }

    private void DrawQueueRows(RunOptions o)
    {
        if (o.PurchaseQueue.Count == 0) EmptyState(FontAwesomeIcon.ShoppingBasket, L.T("Fila vazia. Adicione itens na lista do trocador abaixo."));
        var balance = World.Gemstones;
        var nextIndex = o.PurchaseQueue.FindIndex(x => x.Remaining > 0);
        for (var i = 0; i < o.PurchaseQueue.Count; i++)
        {
            var order = o.PurchaseQueue[i];
            ImGui.PushID(i);
            GameIcon(order.Icon, 30); ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(i == nextIndex ? Gold : order.Remaining == 0 ? Success : ImGui.GetStyle().Colors[(int)ImGuiCol.Text], $"{i + 1}. {order.Name}");
            ImGui.TextDisabled(L.T("{0} de {1} comprados · {2} gemstones cada", order.Bought, order.Quantity, order.Cost));
            ImGui.EndGroup();
            if (order.Remaining == 0) { ImGui.SameLine(); Chip(L.T("Concluída"), Success); }
            else if (i == nextIndex) { ImGui.SameLine(); Chip(L.T("Próxima"), Gold); }
            var buttons = IconButtonWidth(FontAwesomeIcon.ChevronUp) * 4.6f;
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, CardWidth() - ImGui.GetCursorPosX() - buttons));
            ImGui.BeginDisabled(i == 0);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChevronUp)) { (o.PurchaseQueue[i], o.PurchaseQueue[i - 1]) = (o.PurchaseQueue[i - 1], order); SaveSettings(); }
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(i == o.PurchaseQueue.Count - 1);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChevronDown)) { (o.PurchaseQueue[i], o.PurchaseQueue[i + 1]) = (o.PurchaseQueue[i + 1], order); SaveSettings(); }
            ImGui.EndDisabled(); ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt)) { o.PurchaseQueue.RemoveAt(i); SaveSettings(); ImGui.PopID(); break; }
            if (i == nextIndex && order.Remaining > 0)
            {
                var need = order.Cost * order.Remaining;
                LevelBar(need == 0 ? 1 : (float)balance / need, L.T("Saldo para esta compra: {0} / {1}", balance.ToString("N0"), need.ToString("N0")), false);
            }
            ImGui.PopID();
            ImGui.Separator();
        }
        if (o.PurchaseQueue.Any(x => x.Remaining == 0) && ImGui.SmallButton(L.T("Limpar itens concluídos")))
        { o.PurchaseQueue.RemoveAll(x => x.Remaining == 0); SaveSettings(); }
    }

    private static string ShelfName(GemstoneShelf shelf) => shelf switch
    {
        GemstoneShelf.Materials => L.T("Materiais"),
        GemstoneShelf.Housing => L.T("Moradia"),
        GemstoneShelf.Orchestrion => L.T("Orchestrion"),
        GemstoneShelf.Gear => L.T("Equipamento"),
        _ => L.T("Outros"),
    };

    // The trader's items: filter pills with counts, search, then one table row per item.
    private void DrawTraderShop(RunOptions o, List<GemstoneItem> catalog, float scale)
    {
        var balance = World.Gemstones;
        ImGui.PushStyleColor(ImGuiCol.Text, Gold); DrawSymbol(FontAwesomeIcon.Store); ImGui.SameLine();
        ImGui.TextUnformatted(L.T("Itens do trocador")); ImGui.PopStyleColor();
        ImGui.SameLine(); ImGui.TextDisabled(L.T("Clique em Adicionar para pôr na fila"));
        // Balance pill on the right.
        var balanceText = balance.ToString("N0");
        var pillWidth = ImGui.CalcTextSize(balanceText).X + 34 * scale;
        ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, CardWidth() - ImGui.GetCursorPosX() - pillWidth));
        GameIcon(ItemIcon(GemstoneItemId), 18); ImGui.SameLine(); ImGui.TextColored(Gold, balanceText);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Seu saldo de bicolor gemstones."));
        ImGui.Spacing();

        // Filter pills.
        if (FilterPill("all", 0, L.T("Todos"), catalog.Count, shelfFilter == null)) shelfFilter = null;
        foreach (var group in catalog.GroupBy(i => i.Shelf).OrderBy(g => g.Key))
        {
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < 120 * scale) ImGui.NewLine();
            if (FilterPill(group.Key.ToString(), group.First().CategoryIcon, ShelfName(group.Key), group.Count(), shelfFilter == group.Key))
                shelfFilter = shelfFilter == group.Key ? null : group.Key;
        }
        ImGui.Spacing();

        // Search, quantity and sort.
        var qtyWidth = 110 * scale;
        ImGui.SetNextItemWidth(Math.Max(120 * scale, CardWidth() - qtyWidth - ImGui.CalcTextSize(L.T("Quantidade")).X - 190 * scale));
        ImGui.InputTextWithHint("###orderSearch", L.T("Buscar item do trocador"), ref orderSearch, 60);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(qtyWidth);
        if (ImGui.InputInt(L.T("Quantidade"), ref newOrderQuantity)) newOrderQuantity = Math.Clamp(newOrderQuantity, 1, 999);
        ImGui.SameLine();
        ImGui.Checkbox(L.T("Por preço"), ref sortByPrice);

        var shown = catalog.Where(x => (shelfFilter == null || x.Shelf == shelfFilter) &&
                (orderSearch.Length == 0 || x.Name.Contains(orderSearch, StringComparison.OrdinalIgnoreCase) || x.Category.Contains(orderSearch, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => sortByPrice ? x.Cost : 0).ThenBy(x => x.Name).ToList();
        if (shown.Count == 0) { EmptyState(FontAwesomeIcon.Search, L.T("Nenhum item com esse filtro.")); return; }

        var row = 34 * scale;
        var height = Math.Min(shown.Count, 9) * (row + ImGui.GetStyle().CellPadding.Y * 2) + ImGui.GetFrameHeightWithSpacing() + 4 * scale;
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("traderItems", 5, flags, new Vector2(CardWidth(), height))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(L.T("Item"), ImGuiTableColumnFlags.WidthStretch, 3.2f);
        ImGui.TableSetupColumn(L.T("Preço"), ImGuiTableColumnFlags.WidthFixed, 80 * scale);
        ImGui.TableSetupColumn(L.T("Na bolsa"), ImGuiTableColumnFlags.WidthFixed, 70 * scale);
        ImGui.TableSetupColumn(L.T("Saldo cobre"), ImGuiTableColumnFlags.WidthFixed, 110 * scale);
        ImGui.TableSetupColumn("##add", ImGuiTableColumnFlags.WidthFixed, 118 * scale);
        ImGui.TableHeadersRow();
        foreach (var item in shown)
        {
            ImGui.PushID((int)item.ItemId);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, row);
            var queued = o.PurchaseQueue.FirstOrDefault(q => q.ItemId == item.ItemId && q.Remaining > 0);
            var owned = GemstoneShop.Count(item.ItemId);

            ImGui.TableNextColumn();
            GameIcon(item.Icon, 30); ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted(item.Name);
            ImGui.PushStyleColor(ImGuiCol.Text, Muted); ImGui.TextUnformatted(item.Category); ImGui.PopStyleColor();
            ImGui.EndGroup();

            ImGui.TableNextColumn();
            CellCenter(row);
            GameIcon(ItemIcon(GemstoneItemId), 16); ImGui.SameLine(); ImGui.TextColored(Gold, item.Cost.ToString("N0"));

            ImGui.TableNextColumn();
            CellCenter(row);
            if (owned > 0) ImGui.TextUnformatted(owned.ToString("N0")); else ImGui.TextDisabled("—");

            ImGui.TableNextColumn();
            CellCenter(row);
            var canBuy = balance / Math.Max(1, item.Cost);
            if (canBuy > 0) ImGui.TextColored(Success, item.Unique ? L.T("Sim") : $"{Math.Min(canBuy, 999)}×");
            else ImGui.TextDisabled(L.T("faltam {0}", (item.Cost - balance).ToString("N0")));

            ImGui.TableNextColumn();
            CellCenter(row);
            if (item.Unique && owned > 0) Chip(L.T("Já tem"), Muted);
            else if (item.Unique && queued != null) Chip(L.T("Na fila"), Gold);
            else
            {
                if (queued != null) { Chip($"{queued.Remaining}×", Gold); ImGui.SameLine(); }
                var amount = item.Unique ? 1 : newOrderQuantity;
                if (queued != null ? ImGuiComponents.IconButton(FontAwesomeIcon.Plus) : ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, L.T("Adicionar")))
                {
                    if (queued != null) queued.Quantity = Math.Min(999, queued.Quantity + amount);
                    else o.PurchaseQueue.Add(new GemstoneOrder { ItemId = item.ItemId, Name = item.Name, Icon = item.Icon, Cost = item.Cost, Quantity = amount });
                    SaveSettings();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Adicionar {0}× à fila", amount));
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    // Vertically centers a single text line in a table cell of the given height.
    private static void CellCenter(float rowHeight)
    {
        var offset = (rowHeight - ImGui.GetFrameHeight()) / 2;
        if (offset > 0) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
        ImGui.AlignTextToFramePadding();
    }

    // Rounded toggle pill: game icon, label and count; filled when selected.
    private static bool FilterPill(string id, uint icon, string label, int count, bool selected)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var countText = count.ToString();
        var iconSize = icon == 0 ? 0 : 16 * scale;
        var pad = new Vector2(10, 4) * scale;
        var gap = 6 * scale;
        var labelSize = ImGui.CalcTextSize(label);
        var countSize = ImGui.CalcTextSize(countText);
        var size = new Vector2(pad.X * 2 + (iconSize > 0 ? iconSize + gap : 0) + labelSize.X + gap + countSize.X, labelSize.Y + pad.Y * 2);
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("###pill" + id, size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var accent = Gold;
        draw.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(accent with { W = selected ? .28f : hovered ? .12f : .05f }), size.Y / 2);
        draw.AddRect(origin, origin + size, ImGui.ColorConvertFloat4ToU32(selected ? accent : Muted with { W = .35f }), size.Y / 2);
        var x = origin.X + pad.X;
        if (iconSize > 0 && Services.Textures.TryGetFromGameIcon(new GameIconLookup(icon), out var shared) && shared != null && shared.TryGetWrap(out var texture, out _))
        {
            var top = origin.Y + (size.Y - iconSize) / 2;
            draw.AddImage(texture.Handle, new Vector2(x, top), new Vector2(x + iconSize, top + iconSize));
        }
        if (iconSize > 0) x += iconSize + gap;
        draw.AddText(new Vector2(x, origin.Y + pad.Y), ImGui.ColorConvertFloat4ToU32(selected ? accent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), label);
        x += labelSize.X + gap;
        draw.AddText(new Vector2(x, origin.Y + pad.Y), ImGui.ColorConvertFloat4ToU32(Muted), countText);
        return clicked;
    }
}
