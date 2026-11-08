using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// 1.1 purchase queue: items from the chosen trader city's shop, bought one goal at a time.
internal sealed partial class MainWindow
{
    private int newOrderQuantity = 1;
    private string orderSearch = "";

    private void DrawPurchaseQueue(RunOptions o)
    {
        var scale = ImGuiHelpers.GlobalScale;
        BeginCard("purchaseQueue");
        CardTitle(FontAwesomeIcon.ShoppingCart, L.T("Fila de compras com gemstones"));
        ImGui.TextWrapped(L.T("Quando o saldo cobre o próximo item, o FatePilot termina o FATE, vai ao trocador, abre a conversa e espera você comprar. A compra aparece na bolsa, ele marca na fila e volta aos FATEs. Depois segue para o próximo item."));
        ImGui.Spacing();
        var city = o.GemstoneDestination ?? o.ReturnDestination;
        var catalog = city == null ? [] : GemstoneCatalog.ItemsFor(city.TerritoryId);
        if (city == null || GemstoneTraders.In(city.TerritoryId) == null)
        { ImGui.TextColored(Danger, L.T("Escolha acima a cidade para trocar gemstones.")); EndCard(); return; }
        if (catalog.Count == 0)
            ImGui.TextColored(Danger, L.T("A lista de itens deste trocador não está nos dados do jogo. Use Old Sharlayan, Radz-at-Han, Tuliyollal ou Solution Nine."));

        // Queue.
        if (o.PurchaseQueue.Count == 0) EmptyState(FontAwesomeIcon.ShoppingBasket, L.T("Fila vazia. Adicione itens abaixo."));
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

        // Add from the trader's shop.
        if (catalog.Count > 0)
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(Math.Min(CardWidth() * .55f, 340 * scale));
            ImGui.InputTextWithHint("###orderSearch", L.T("Buscar item do trocador"), ref orderSearch, 60);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110 * scale);
            if (ImGui.InputInt(L.T("Quantidade"), ref newOrderQuantity)) newOrderQuantity = Math.Clamp(newOrderQuantity, 1, 999);
            var shown = catalog.Where(x => orderSearch.Length == 0 || x.Name.Contains(orderSearch, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
            if (ImGui.BeginChild("orderCatalog", new Vector2(CardWidth(), Math.Min(shown.Count, 6) * 30 * scale + 6 * scale), true))
            {
                foreach (var item in shown)
                {
                    ImGui.PushID((int)item.ItemId);
                    var pos = ImGui.GetCursorPos();
                    if (ImGui.Selectable("###add", false, ImGuiSelectableFlags.None, new Vector2(0, 26 * scale)))
                    {
                        o.PurchaseQueue.Add(new GemstoneOrder { ItemId = item.ItemId, Name = item.Name, Icon = item.Icon, Cost = item.Cost, Quantity = newOrderQuantity });
                        SaveSettings();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Adicionar {0}× à fila", newOrderQuantity));
                    ImGui.SetCursorPos(pos);
                    GameIcon(item.Icon, 24); ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(item.Name);
                    ImGui.SameLine(); GameIcon(ItemIcon(26807), 16); ImGui.SameLine(); ImGui.TextDisabled(item.Cost.ToString("N0"));
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
        }
        Help(L.T("Por enquanto o clique de comprar é seu: o FatePilot ainda não clica dentro da loja. Use Copiar dados da janela de troca com a loja aberta e mande o texto, para a compra ficar automática."));
        EndCard();
    }
}
