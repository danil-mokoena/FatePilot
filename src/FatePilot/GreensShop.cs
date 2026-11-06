using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Numerics;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace FatePilot;

// Only the verified NPC, gil shop and NQ item. Never sells, discards or accepts a generic confirmation.
internal sealed unsafe class GreensShop
{
    private ulong actorId;
    private double openedAt, advancedAt;
    private bool observedShop, closing;
    private static double Now => Environment.TickCount64 / 1000d;
    public string? Error { get; private set; }
    public static bool IsOpen => Addon("Shop") != null;

    private static AtkUnitBase* Addon(string name)
    {
        var addon = (AtkUnitBase*)Services.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible ? addon : null;
    }

    public static GreensInventory Inventory()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return new(false, 0, 0, 0);
        var count = 0; var capacity = 0;
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded || bag->Items == null || bag->Size is < 1 or > 50) return new(false, 0, 0, 0);
            for (var i = 0; i < bag->Size; i++)
            {
                var item = bag->GetInventorySlot(i);
                if (item == null || item->IsSymbolic) return new(false, 0, 0, 0);
                if (item->ItemId == 0) capacity += GreensRestock.StackSize;
                else if (item->ItemId == GreensRestock.ItemId && item->Flags == InventoryItem.ItemFlags.None)
                {
                    if (item->Quantity is < 0 or > GreensRestock.StackSize) return new(false, 0, 0, 0);
                    count += item->Quantity; capacity += GreensRestock.StackSize - item->Quantity;
                }
            }
        }
        return new(true, count, capacity, manager->GetGil());
    }

    public static VendorActor? Vendor()
    {
        if (Services.Client.TerritoryType != GreensRestock.VendorCity.TerritoryId) return null;
        var candidates = Services.Objects.Where(o => o.ObjectKind == ObjectKind.EventNpc && o.BaseId == GreensRestock.VendorId && o.IsTargetable).ToArray();
        return candidates.Length == 1 ? new(candidates[0].GameObjectId, candidates[0].Position, candidates[0].HitboxRadius) : null;
    }

    public static void ValidateData()
    {
        var item = Services.Data.GetExcelSheet<Item>().GetRowOrDefault(GreensRestock.ItemId);
        var npc = Services.Data.GetExcelSheet<ENpcBase>().GetRowOrDefault(GreensRestock.VendorId);
        var level = Services.Data.GetExcelSheet<Level>().GetRowOrDefault(4143334);
        var shop = Services.Data.GetSubrowExcelSheet<GilShopItem>().GetRow(GreensRestock.ShopId);
        if (item is not { } i || i.PriceMid != GreensRestock.Price || i.StackSize != GreensRestock.StackSize ||
            npc is not { } n || !n.ENpcData.Any(e => e.RowId == GreensRestock.ShopId) ||
            level is not { } l || l.Object.RowId != GreensRestock.VendorId || l.Territory.RowId != GreensRestock.VendorCity.TerritoryId ||
            Vector3.Distance(new(l.X, l.Y, l.Z), GreensRestock.VendorPosition) > 1 ||
            !shop.Any(s => s.Item.RowId == GreensRestock.ItemId && !s.IsHQ))
            throw new InvalidOperationException(L.T("Os dados do vendedor de Gysahl Greens mudaram. Reposição indisponível."));
    }

    public void Open(VendorActor requested)
    {
        if (actorId != 0 && Read(false) == GreensShopState.Closed) Reset();
        var vendor = Vendor(); var player = Services.Objects.LocalPlayer;
        if (actorId != 0 || IsOpen || EventInteraction.DialogueOpen || vendor == null || vendor.Id != requested.Id || player == null ||
            player.IsCasting || player.IsDead || Services.Conditions[ConditionFlag.InCombat] ||
            Vector3.Distance(player.Position, vendor.Position) > Math.Max(4, vendor.Hitbox + 2))
            throw new InvalidOperationException(L.T("Não foi possível iniciar a interação com o vendedor confirmado."));
        var obj = Services.Objects.First(o => o.GameObjectId == vendor.Id);
        var target = TargetSystem.Instance();
        if (target == null) throw new InvalidOperationException(L.T("Não foi possível iniciar a interação com o vendedor confirmado."));
        actorId = vendor.Id; openedAt = Now; advancedAt = -100; observedShop = closing = false; Error = null;
        target->InteractWithObject((NativeObject*)obj.Address, true);
    }

    private int SelectorIndex()
    {
        var selector = EventHandlerSelector.Instance();
        if (actorId == 0 || selector == null || selector->Target == null || selector->Target->GetGameObjectId().Id != actorId || selector->OptionsCount is < 1 or > 32) return -1;
        var found = -1;
        for (var i = 0; i < selector->OptionsCount; i++)
        {
            var handler = selector->Options[i].Handler;
            if (handler == null || handler->Info.EventId.Id != GreensRestock.ShopId) continue;
            if (found >= 0) return -1;
            found = i;
        }
        return found;
    }

    private bool MatchingContext()
    {
        var f = EventFramework.Instance();
        if (actorId == 0 || f == null) return false;
        bool Match(uint eventId, ulong objectId) => (eventId == GreensRestock.ShopId || eventId == 0x000E0000u)
            && (objectId == actorId || f->SceneGameObjectId.Id == actorId);
        return Match(f->EventState1.EventId.Id, f->EventState1.ObjectId.Id) || Match(f->EventState2.EventId.Id, f->EventState2.ObjectId.Id);
    }

    private ShopEventHandler* OwnedHandler()
    {
        var shop = Addon("Shop"); var proxy = ShopEventHandler.AgentProxy.Instance(); var agent = AgentShop.Instance();
        if (actorId == 0 || shop == null || proxy == null || agent == null || proxy->Handler == null || proxy->AddonId != shop->Id ||
            (nint)agent->EventReceiver != (nint)proxy) return null;
        var h = proxy->Handler;
        if (h->EventHandler.Info.EventId.Id != GreensRestock.ShopId) return null;
        var actor = h->EventHandler.EventGameObject;
        var scene = h->EventHandler.SceneGameObject;
        if ((actor == null || actor->GetGameObjectId().Id != actorId) && (scene == null || scene->GetGameObjectId().Id != actorId)) return null;
        return h;
    }

    public GreensShopState Read(bool occupied)
    {
        if (actorId == 0) return GreensShopState.None;
        if (Error != null) return GreensShopState.Failed;
        if (Addon("SelectYesno") != null) return Reject();
        var handler = OwnedHandler();
        if (handler != null)
        {
            observedShop = true;
            if (EventInteraction.DialogueOpen)
                return Addon("Talk") != null && MatchingContext() ? GreensShopState.Busy : Reject();
            if (handler->CurrentMode != 1 || handler->BuybackTabActive || handler->IsTradingWithRetainer || handler->StartingSell || handler->WaitingForSellConfirm)
                return Reject();
            return handler->StartingBuy || handler->WaitingForTransactionToFinish || closing ? GreensShopState.Busy : GreensShopState.Ready;
        }
        if (IsOpen) return Reject();
        if (observedShop)
        {
            if (EventInteraction.DialogueOpen) return Reject();
            return GreensShopState.Closed;
        }
        if (Now - openedAt > 20)
        { Error = L.T("A loja de Gysahl Greens não abriu a tempo."); return GreensShopState.Failed; }
        if (SelectorIndex() >= 0 || MatchingContext() || (!occupied && !EventInteraction.DialogueOpen) || Now - openedAt < 3)
            return GreensShopState.Opening;
        return Reject();
    }

    private GreensShopState Reject()
    { Error = L.T("A loja não corresponde à reposição solicitada."); return GreensShopState.Foreign; }

    public void Advance()
    {
        if (actorId == 0 || closing || Error != null || Now - advancedAt < .6) return;
        var index = SelectorIndex();
        if (index >= 0)
        {
            advancedAt = Now;
            var framework = EventFramework.Instance();
            if (framework != null) framework->InteractWithHandlerFromSelector(index);
        }
        else if (MatchingContext() && Addon("Talk") != null)
        { advancedAt = Now; EventInteraction.ClickTalk(); }
    }

    public void Buy(GreensPurchase purchase)
    {
        var h = OwnedHandler(); var inventory = Inventory(); var vendor = Vendor(); var player = Services.Objects.LocalPlayer;
        if (h == null || Read(true) != GreensShopState.Ready || h->ItemsCount is < 1 or > 60 || h->VisibleItemsCount is < 1 or > 60 ||
            vendor?.Id != actorId || player == null || Vector3.Distance(player.Position, vendor.Position) > Math.Max(4, vendor.Hitbox + 2) ||
            purchase.Quantity is < 1 or > GreensRestock.MaxBatch || !inventory.Ready || inventory.Count != purchase.BeforeCount ||
            inventory.Gil != purchase.BeforeGil || inventory.Gil < purchase.Cost || inventory.Capacity < purchase.Quantity)
            throw new InvalidOperationException(L.T("A loja ou o inventário mudou antes da compra. Nenhuma compra foi enviada."));
        var index = -1;
        for (var v = 0; v < h->VisibleItemsCount; v++)
        {
            var i = h->VisibleItems[v];
            if (i < 0 || i >= h->ItemsCount) throw new InvalidOperationException(L.T("Lista da loja inválida."));
            var item = h->Items[i];
            if (item.ItemId != GreensRestock.ItemId || item.IsHQ) continue;
            if (index >= 0 || item.PriceBuy != GreensRestock.Price || item.StackSize != GreensRestock.StackSize)
                throw new InvalidOperationException(L.T("Preço ou item inesperado na loja. Compra cancelada."));
            index = i;
        }
        if (index < 0) throw new InvalidOperationException(L.T("Gysahl Greens não está disponível na loja confirmada."));
        h->BuyItemIndex = index;
        h->ExecuteBuy(purchase.Quantity);
    }

    public void Close()
    {
        var h = OwnedHandler();
        if (h == null || h->StartingBuy || h->WaitingForTransactionToFinish || Error != null || EventInteraction.DialogueOpen) return;
        closing = true;
        var shop = Addon("Shop");
        if (shop != null) shop->Close(true);
    }

    public void Reset() { actorId = 0; observedShop = closing = false; Error = null; }
}
