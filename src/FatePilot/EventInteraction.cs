using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace FatePilot;

// One short-lived interaction with an identified FATE object. No global dialogue skipper.
internal sealed unsafe class EventInteraction
{
    private ObjectiveActor? owned;
    private long expires, lastAdvance;

    public static int ItemCount(uint id)
    {
        var inventory = InventoryManager.Instance();
        if (id == 0 || inventory == null) return 0;
        return Math.Max(inventory->GetInventoryItemCount(id, false, false, false), inventory->GetItemCountInContainer(id, InventoryType.KeyItems));
    }

    private static bool Matches(NativeObject* obj, ushort fate) => obj != null &&
        (obj->FateId == fate || obj->EventId.Id == (0x801A0000u | fate) ||
         (obj->EventHandler != null && obj->EventHandler->Info.EventId.Id == (0x801A0000u | fate)));

    public static void SnapshotObjectives(FateContext* fate, List<ObjectiveActor> output)
    {
        foreach (var obj in Services.Objects)
        {
            if (obj.Address == 0 || obj.ObjectKind is not (ObjectKind.EventNpc or ObjectKind.EventObj or ObjectKind.BattleNpc)) continue;
            var native = (NativeObject*)obj.Address;
            if (obj is IBattleNpc enemy && (enemy.IsDead || enemy.StatusFlags.HasFlag(StatusFlags.Hostile))) continue;
            var knownStarter = fate->MotivationNpc is not (0 or 0xE0000000) && obj.EntityId == fate->MotivationNpc;
            var knownRecipient = fate->ObjectiveNpc is not (0 or 0xE0000000) && obj.EntityId == fate->ObjectiveNpc;
            var linked = Matches(native, fate->FateId);
            if (!knownStarter && !knownRecipient && !linked) continue;
            if (obj.IsTargetable && knownStarter && fate->State == FateState.Preparing)
                output.Add(new(obj.GameObjectId, fate->FateId, ObjectiveRole.Starter, obj.Position, obj.HitboxRadius));
            if (fate->Rule == 2 && obj.IsTargetable)
            {
                if (knownRecipient || (knownStarter && fate->ObjectiveNpc is 0 or 0xE0000000))
                    output.Add(new(obj.GameObjectId, fate->FateId, ObjectiveRole.Recipient, obj.Position, obj.HitboxRadius));
                else if (linked && obj.ObjectKind == ObjectKind.EventObj)
                    output.Add(new(obj.GameObjectId, fate->FateId, ObjectiveRole.Pickup, obj.Position, obj.HitboxRadius));
            }
            if (fate->Rule is 3 or 4 && obj.ObjectKind is ObjectKind.EventNpc or ObjectKind.BattleNpc)
                output.Add(new(obj.GameObjectId, fate->FateId, ObjectiveRole.Escort, obj.Position, obj.HitboxRadius));
        }
    }

    public void Begin(ObjectiveActor actor)
    {
        var fate = Services.Fates.FirstOrDefault(f => f.FateId == actor.FateId);
        var obj = Services.Objects.FirstOrDefault(o => o.GameObjectId == actor.Id);
        var player = Services.Objects.LocalPlayer;
        if (fate == null || fate.Address == 0 || obj == null || !obj.IsTargetable || player == null ||
            Vector3.Distance(player.Position, obj.Position) > Math.Max(4, obj.HitboxRadius + 2)) return;
        var valid = new List<ObjectiveActor>();
        SnapshotObjectives((FateContext*)fate.Address, valid);
        if (!valid.Any(a => a.Id == actor.Id && a.Role == actor.Role)) return;
        // A pre-existing dialogue is never adopted as ours.
        if (Visible("Talk") || Visible("SelectYesno") || Visible("SelectString")) return;
        var target = TargetSystem.Instance();
        if (target == null) return;
        owned = actor;
        expires = Environment.TickCount64 + 15000;
        lastAdvance = 0;
        target->InteractWithObject((NativeObject*)obj.Address, true);
    }

    public bool OwnsCurrentEvent
    {
        get
        {
            if (owned == null || Environment.TickCount64 >= expires) { Cancel(); return false; }
            var framework = EventFramework.Instance();
            if (framework == null || !Services.Fates.Any(f => f.FateId == owned.FateId)) return false;
            if (framework->SceneGameObjectId.Id != owned.Id) return false;
            var id = 0x801A0000u | owned.FateId;
            var first = framework->EventState1;
            var second = framework->EventState2;
            return (first.EventId.Id == id && first.ObjectId.Id == owned.Id) ||
                   (second.EventId.Id == id && second.ObjectId.Id == owned.Id);
        }
    }

    public void Advance()
    {
        if (!OwnsCurrentEvent || Environment.TickCount64 - lastAdvance < 600) return;
        lastAdvance = Environment.TickCount64;
        var talk = (AtkUnitBase*)Services.GameGui.GetAddonByName("Talk").Address;
        if (talk != null && talk->IsVisible)
        {
            var stage = AtkStage.Instance();
            if (stage == null) return;
            var evt = new AtkEvent
            {
                Target = &stage->AtkEventTarget, Listener = (AtkEventListener*)talk,
                State = new AtkEventState { StateFlags = AtkEventStateFlags.Pooled | AtkEventStateFlags.Unk3 },
            };
            var data = new AtkEventData();
            talk->ReceiveEvent(AtkEventType.MouseDown, 0, &evt, &data);
            talk->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
            talk->ReceiveEvent(AtkEventType.MouseUp, 0, &evt, &data);
            return;
        }
        var yesno = (AtkUnitBase*)Services.GameGui.GetAddonByName("SelectYesno").Address;
        if (yesno != null && yesno->IsVisible) yesno->FireCallbackInt(0);
    }

    private static bool Visible(string name)
    {
        var addon = (AtkUnitBase*)Services.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible;
    }

    public void Cancel() { owned = null; expires = 0; }
}
