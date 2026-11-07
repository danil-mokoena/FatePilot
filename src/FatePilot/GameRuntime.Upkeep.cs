using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Action = System.Action;

namespace FatePilot;

public sealed record GearsetInfo(int Index, uint Job, string Name, int Level);
public sealed record FoodChoice(uint ItemId, bool Hq, string Name, uint Icon, int Count);

// 0.5 upkeep: pause/resume control, gearsets, food, bag/durability reading and self-repair.
internal sealed partial class GameRuntime
{
    private const uint WellFedStatus = 48;
    private const uint MealCategory = 46;
    private static readonly uint[] DarkMatter = [5594, 5595, 5596, 5597, 5598, 10386, 17837, 33916];
    private RunOptions upkeepOptions = new();

    // ----- Pause / job switch: release and retake game control, keeping travel counters. -----
    public void Suspend()
    {
        interaction.Cancel();
        List<string> errors = [];
        void Attempt(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex.Message); } }
        Attempt(greensShop.Close);
        Attempt(nav.Stop);
        Attempt(combat.End);
        session = false;
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
    }

    public void ResumeSession()
    {
        var missing = Dependencies().Where(x => !x.Available).Select(x => x.Name).ToList();
        if (missing.Count > 0) throw new InvalidOperationException(L.T("API indisponível: ") + string.Join(", ", missing));
        combat.Begin();
        session = true;
    }

    public unsafe bool EquipGearset(int index)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null || !module->IsValidGearset(index)) return false;
        module->EquipGearset(index);
        return true;
    }

    public static unsafe List<GearsetInfo> Gearsets()
    {
        var list = new List<GearsetInfo>();
        var module = RaptureGearsetModule.Instance();
        var state = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        if (module == null || state == null) return list;
        var jobs = Services.Data.GetExcelSheet<ClassJob>();
        for (var i = 0; i < 100; i++)
        {
            if (!module->IsValidGearset(i)) continue;
            var g = module->GetGearset(i);
            if (g == null) continue;
            var job = jobs.GetRowOrDefault(g->ClassJob);
            if (job == null || g->ClassJob is 0 or (>= 8 and <= 18)) continue;
            var exp = job.Value.ExpArrayIndex;
            var level = exp >= 0 && exp < state->ClassJobLevels.Length ? state->ClassJobLevels[exp] : 0;
            list.Add(new(i, g->ClassJob, g->NameString, level));
        }
        return list;
    }

    // ----- Food -----
    public static unsafe List<FoodChoice> FoodChoices()
    {
        var inventory = InventoryManager.Instance();
        var items = Services.Data.GetExcelSheet<Item>();
        var found = new Dictionary<(uint, bool), int>();
        if (inventory == null) return [];
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;
                var row = items.GetRowOrDefault(slot->ItemId);
                if (row?.ItemUICategory.RowId != MealCategory) continue;
                var key = (slot->ItemId, slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality));
                found[key] = found.GetValueOrDefault(key) + (int)slot->Quantity;
            }
        }
        return found.Select(f => new FoodChoice(f.Key.Item1, f.Key.Item2, items.GetRow(f.Key.Item1).Name.ExtractText(), items.GetRow(f.Key.Item1).Icon, f.Value))
            .OrderBy(f => f.Name).ToList();
    }

    private static float? ReadFoodSeconds()
    {
        var player = Services.Objects.LocalPlayer;
        if (player == null) return null;
        var fed = player.StatusList.FirstOrDefault(s => s.StatusId == WellFedStatus);
        return fed == null ? 0 : Math.Max(0, fed.RemainingTime);
    }

    private static unsafe int FoodCount(uint id, bool hq)
    {
        var inventory = InventoryManager.Instance();
        return id == 0 || inventory == null ? 0 : inventory->GetInventoryItemCount(id, hq, false, false);
    }

    private static unsafe bool CanUseItem(uint actionId)
    {
        var actions = ActionManager.Instance();
        return actions != null && actions->AnimationLock <= 0 && !actions->ActionQueued && actions->GetActionStatus(ActionType.Item, actionId) == 0;
    }

    public unsafe bool UseFood(uint itemId, bool hq)
    {
        var id = hq ? itemId + 1_000_000 : itemId;
        if (!session || FoodCount(itemId, hq) <= 0 || !CanUseItem(id)) return false;
        var actions = ActionManager.Instance();
        return actions != null && actions->UseAction(ActionType.Item, id, extraParam: 0xFFFF);
    }

    // ----- Bag and durability -----
    private static unsafe int FreeBagSlots()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? -1 : (int)inventory->GetEmptySlotsInBag();
    }

    // Lowest condition among equipped items, in percent (game stores 0..30000).
    private static unsafe int MinDurability()
    {
        var inventory = InventoryManager.Instance();
        var container = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null) return -1;
        var min = int.MaxValue;
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;
            min = Math.Min(min, slot->Condition / 300);
        }
        return min == int.MaxValue ? -1 : min;
    }

    public static unsafe int DarkMatterCount()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0 : DarkMatter.Sum(id => inventory->GetInventoryItemCount(id, false, false, false));
    }

    // ----- Self-repair: open Repair, press Repair All, confirm only that prompt, close. -----
    private enum RepairStage { None, Opening, Confirming, Repairing, Closing }
    private RepairStage repairStage;
    private double repairStarted, repairStepAt;
    private static double Clock => Environment.TickCount64 / 1000d;
    private bool RepairBusy => repairStage != RepairStage.None;

    private static unsafe bool CanSelfRepair()
    {
        var actions = ActionManager.Instance();
        return DarkMatterCount() > 0 && actions != null && actions->GetActionStatus(ActionType.GeneralAction, 6) == 0;
    }

    public unsafe bool StartRepair()
    {
        if (!session || RepairBusy || !CanSelfRepair()) return false;
        var actions = ActionManager.Instance();
        if (actions == null || !actions->UseAction(ActionType.GeneralAction, 6)) return false;
        repairStage = RepairStage.Opening;
        repairStarted = repairStepAt = Clock;
        return true;
    }

    private unsafe void TickRepair()
    {
        if (repairStage == RepairStage.None) return;
        var now = Clock;
        if (now - repairStarted > 20) { CloseRepair(); repairStage = RepairStage.None; return; }
        var repair = (AddonRepair*)Services.GameGui.GetAddonByName("Repair").Address;
        var yesNo = (AtkUnitBase*)Services.GameGui.GetAddonByName("SelectYesno").Address;
        switch (repairStage)
        {
            case RepairStage.Opening:
                if (repair == null || !repair->AtkUnitBase.IsVisible || now - repairStepAt < 0.5) return;
                var button = repair->RepairAllButton;
                if (button == null || !button->IsEnabled) { repairStage = RepairStage.Closing; repairStepAt = now; return; }
                ClickButton(&repair->AtkUnitBase, button);
                repairStage = RepairStage.Confirming; repairStepAt = now;
                return;
            case RepairStage.Confirming:
                // Only the prompt that appears right after our own Repair All click.
                if (yesNo != null && yesNo->IsVisible && now - repairStepAt < 5)
                { yesNo->FireCallbackInt(0); repairStage = RepairStage.Repairing; repairStepAt = now; }
                else if (now - repairStepAt >= 5) { repairStage = RepairStage.Closing; repairStepAt = now; }
                return;
            case RepairStage.Repairing:
                if (now - repairStepAt < 2.5) return;
                repairStage = RepairStage.Closing; repairStepAt = now;
                return;
            case RepairStage.Closing:
                CloseRepair();
                repairStage = RepairStage.None;
                return;
        }
    }

    private static unsafe void CloseRepair()
    {
        var repair = (AtkUnitBase*)Services.GameGui.GetAddonByName("Repair").Address;
        if (repair != null && repair->IsVisible) repair->Close(true);
    }

    private static unsafe void ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        var node = button->AtkComponentBase.OwnerNode;
        if (node == null) return;
        var evt = node->AtkResNode.AtkEventManager.Event;
        if (evt == null) return;
        var data = new AtkEventData();
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt, &data);
    }
}
