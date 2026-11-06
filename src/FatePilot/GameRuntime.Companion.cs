using Dalamud.Game.ClientState.Conditions;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FatePilot;

internal sealed partial class GameRuntime
{
    private static unsafe float? ReadCompanionSeconds()
    {
        var ui = UIState.Instance();
        if (ui == null) return null;
        var seconds = ui->Buddy.CompanionInfo.TimeLeft;
        return float.IsFinite(seconds) && seconds >= 0 ? seconds : null;
    }

    private static unsafe bool CanUseGysahlGreens()
    {
        var player = Services.Objects.LocalPlayer;
        var c = Services.Conditions;
        if (!Services.Client.IsLoggedIn || player == null || player.IsDead || player.IsCasting || Services.Client.IsPvP ||
            c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51] || c[ConditionFlag.InCombat] ||
            c[ConditionFlag.Mounted] || c[ConditionFlag.InFlight] || c[ConditionFlag.MountOrOrnamentTransition] ||
            c[ConditionFlag.BoundByDuty] || c[ConditionFlag.BoundByDuty56] || c[ConditionFlag.BoundByDuty95] ||
            c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInEvent] || c[ConditionFlag.OccupiedInQuestEvent] ||
            c[ConditionFlag.WatchingCutscene] || c[ConditionFlag.OccupiedInCutSceneEvent] || c[ConditionFlag.OccupiedSummoningBell] ||
            EventInteraction.DialogueOpen || GreensShop.IsOpen || ReadCompanionSeconds() is not 0)
            return false;
        var inventory = GreensShop.Inventory();
        var actions = ActionManager.Instance();
        return inventory.Ready && inventory.Count > 0 && actions != null && actions->AnimationLock <= 0 && !actions->ActionQueued &&
            actions->GetActionStatus(ActionType.Item, GreensRestock.ItemId) == 0;
    }

    public unsafe bool UseGysahlGreens()
    {
        // Recheck the live client immediately before requesting this one specific item.
        if (!session || !companionEnabled || !CanUseGysahlGreens()) return false;
        var actions = ActionManager.Instance();
        return actions != null && actions->UseAction(ActionType.Item, GreensRestock.ItemId, extraParam: 0xFFFF);
    }
}
