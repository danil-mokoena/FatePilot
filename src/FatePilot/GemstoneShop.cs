using Dalamud.Game.ClientState.Objects.Enums;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace FatePilot;

// 1.2 automatic gemstone purchase, inside the conversation FatePilot itself opened with the
// verified trader: pick a menu entry, find the queued item by name in the exchange shop, ask for
// the quantity and accept only a confirmation that names that item. Anything unexpected hands
// the purchase back to the player instead of guessing.
internal sealed unsafe class GemstoneShop
{
    private enum Stage { Idle, Opening, Shop, Closing }

    private Stage stage;
    private bool pending;
    private ulong traderId;
    private uint itemId;
    private double talkedAt, actedAt, firedAt, stageAt;
    private int talks, firedBaseline, failedFires;
    private readonly HashSet<int> triedMenu = [];
    private string? manual;
    private static double Now => Environment.TickCount64 / 1000d;
    public static bool IsOpen => Addon("ShopExchangeCurrency") != null;

    private static AtkUnitBase* Addon(string name)
    {
        var addon = (AtkUnitBase*)Services.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible ? addon : null;
    }

    public void Reset()
    {
        stage = Stage.Idle; traderId = 0; itemId = 0; talks = failedFires = 0; manual = null; pending = false;
        talkedAt = actedAt = firedAt = stageAt = 0; triedMenu.Clear();
    }

    // Called by TalkTo: this conversation is ours.
    public void Talked(ulong trader)
    {
        traderId = trader; talkedAt = Now; talks++;
        stage = Stage.Opening; stageAt = Now;
    }

    public static int Count(uint id)
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetInventoryItemCount(id, false, false, false) + inventory->GetInventoryItemCount(id, true, false, false);
    }

    private bool OurConversation()
    {
        if (traderId == 0 || Now - talkedAt > 600) return false;
        var f = EventFramework.Instance();
        if (f != null && (f->EventState1.ObjectId.Id == traderId || f->EventState2.ObjectId.Id == traderId || f->SceneGameObjectId.Id == traderId)) return true;
        return Services.Targets.Target?.GameObjectId == traderId;
    }

    private GemstoneBuyStep Manual(string reason)
    {
        manual = reason;
        Services.Log.Information($"Gemstone purchase handed to the player: {reason}");
        return new(reason, Manual: true);
    }

    private static string Clean(string text)
    {
        var chars = text.Where(c => !char.IsControl(c) && c is < '' or > '').ToArray();
        return new string(chars).Trim();
    }

    public GemstoneBuyStep Step(uint item, string name, int quantity)
    {
        if (manual != null) return new(manual, Manual: true);
        if (item != itemId) { itemId = item; failedFires = 0; }
        var now = Now;
        var yesno = Addon("SelectYesno");
        var shop = Addon("ShopExchangeCurrency");
        var menu = Addon("SelectIconString");
        if (menu == null) menu = Addon("SelectString");
        var talk = Addon("Talk");
        var anyOpen = yesno != null || shop != null || menu != null || talk != null;
        if (anyOpen && !OurConversation()) return Manual(L.T("uma janela que não é do trocador está aberta."));
        if (now - actedAt < .6) return new(Working(quantity));

        // Done (or out of gemstones): close everything we opened, nothing else.
        if (quantity <= 0)
        {
            if (!anyOpen) { Reset(); return new("", Closed: true); }
            stage = Stage.Closing; actedAt = now;
            if (yesno != null) return Manual(L.T("uma confirmação ficou aberta."));
            if (shop != null) shop->FireCallbackInt(-1);
            else if (menu != null) menu->Close(true);
            else if (talk != null) EventInteraction.ClickTalk();
            return new(L.T("Compra concluída. Fechando a loja."));
        }

        if (yesno != null)
        {
            if (!pending || now - firedAt > 10) return Manual(L.T("apareceu uma confirmação inesperada."));
            var prompt = ((AddonSelectYesno*)yesno)->PromptText;
            var text = prompt == null ? "" : Clean(Dalamud.Game.Text.SeStringHandling.SeString.Parse(prompt->NodeText.AsSpan()).TextValue);
            actedAt = now;
            if (!text.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                yesno->FireCallbackInt(1);
                return Manual(L.T("a confirmação da loja não cita {0}.", name));
            }
            yesno->FireCallbackInt(0);
            firedAt = now;
            return new(Working(quantity));
        }

        if (shop != null)
        {
            if (pending)
            {
                // A purchase was sent: wait for the bag before sending another one.
                if (Count(item) > firedBaseline) { pending = false; failedFires = 0; }
                else if (now - firedAt < 8) return new(Working(quantity));
                else
                {
                    pending = false;
                    if (++failedFires >= 2) return Manual(L.T("o jogo não aceitou a compra (bolsa cheia, rank de FATE compartilhado ou item já obtido)."));
                }
            }
            stage = Stage.Shop;
            var index = ShopIndex(shop, item, name);
            if (index < 0)
            {
                // Wrong menu entry: close this shop and try the next entry.
                actedAt = now; stage = Stage.Idle; stageAt = now;
                shop->FireCallbackInt(-1);
                return new(L.T("Procurando {0} nas lojas do trocador.", name));
            }
            var stack = GemstoneCatalog.ItemsFor(Services.Client.TerritoryType).FirstOrDefault(i => i.ItemId == item)?.StackSize ?? 1;
            var amount = Math.Clamp(quantity, 1, Math.Min(99, stack));
            var values = stackalloc AtkValue[3];
            values[0].SetInt(0); values[1].SetInt(index); values[2].SetInt(amount);
            firedBaseline = Count(item); firedAt = actedAt = now; pending = true;
            shop->FireCallback(3, values, true);
            Services.Log.Information($"Gemstone purchase sent: item {item} index {index} amount {amount}");
            return new(Working(quantity));
        }

        if (menu != null)
        {
            var count = ((AddonSelectIconString*)menu)->PopupMenu.PopupMenu.EntryCount;
            // The last entry closes the menu; every other one is tried once until the item is found.
            var options = Enumerable.Range(0, Math.Max(1, count - 1)).Where(i => !triedMenu.Contains(i)).ToList();
            if (count is < 1 or > 16 || options.Count == 0) return Manual(L.T("{0} não foi encontrado nas lojas do trocador.", name));
            actedAt = now; triedMenu.Add(options[0]); stage = Stage.Shop; stageAt = now;
            menu->FireCallbackInt(options[0]);
            return new(L.T("Abrindo a loja do trocador."));
        }

        if (talk != null) { actedAt = now; EventInteraction.ClickTalk(); return new(L.T("Abrindo a loja do trocador.")); }

        // Nothing open: the conversation ended (a shop was closed to try the next menu entry).
        if (now - stageAt < 3) return new(Working(quantity));
        if (stage == Stage.Opening && now - talkedAt < 10) return new(L.T("Abrindo a loja do trocador."));
        if (talks >= 4) return Manual(L.T("a loja do trocador não abriu."));
        if (Trader() is not { } trader) return Manual(L.T("o trocador de gemstones não está ao alcance."));
        actedAt = now;
        TalkTo(trader);
        return new(L.T("Abrindo a loja do trocador."));
    }

    private static string Working(int quantity) => L.T("Comprando no trocador ({0} restantes).", quantity);

    // The item's position in the shop list: the order in which the trader's items appear in the
    // shop's values, so items hidden by rank or quests never shift the index.
    private static int ShopIndex(AtkUnitBase* shop, uint item, string name)
    {
        var known = GemstoneCatalog.ItemsFor(Services.Client.TerritoryType).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new List<string>();
        for (var i = 0; i < Math.Min((int)shop->AtkValuesCount, 2000); i++)
        {
            var value = shop->AtkValues[i];
            if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString)) continue;
            string text;
            try { text = Clean(value.GetValueAsString()); } catch (Exception) { continue; }
            if (known.Contains(text) && !seen.Contains(text, StringComparer.OrdinalIgnoreCase)) seen.Add(text);
        }
        return seen.FindIndex(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static VendorActor? Trader()
    {
        if (GemstoneTraders.In(Services.Client.TerritoryType) is not { } trader) return null;
        var found = Services.Objects.Where(o => o.ObjectKind == ObjectKind.EventNpc && o.BaseId == trader.NpcId && o.IsTargetable).ToArray();
        var player = Services.Objects.LocalPlayer;
        if (found.Length != 1 || player == null || Vector3.Distance(player.Position, found[0].Position) > Math.Max(4, found[0].HitboxRadius + 3)) return null;
        return new(found[0].GameObjectId, found[0].Position, found[0].HitboxRadius);
    }

    // Starts the conversation with the verified trader next to the player.
    public void TalkTo(VendorActor trader)
    {
        var expected = GemstoneTraders.In(Services.Client.TerritoryType);
        var obj = Services.Objects.FirstOrDefault(o => o.GameObjectId == trader.Id);
        var player = Services.Objects.LocalPlayer;
        var target = TargetSystem.Instance();
        if (expected == null || obj == null || obj.BaseId != expected.NpcId || !obj.IsTargetable || player == null || target == null
            || Vector3.Distance(player.Position, obj.Position) > Math.Max(4, obj.HitboxRadius + 3))
            throw new InvalidOperationException(L.T("O trocador de gemstones não foi confirmado. Nenhuma interação foi feita."));
        Talked(trader.Id);
        target->InteractWithObject((NativeObject*)obj.Address, true);
    }
}
