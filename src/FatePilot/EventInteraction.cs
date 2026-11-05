using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
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
internal sealed unsafe class EventInteraction(Action<string> log)
{
    private ObjectiveActor? owned;
    private readonly InteractionLease lease = new();
    private long lastAdvance;
    private InteractionStatus lastStatus;
    private static double Now => Environment.TickCount64 / 1000d;

    public static int ItemCount(uint id)
    {
        var inventory = InventoryManager.Instance();
        if (id == 0 || inventory == null) return 0;
        return Math.Max(inventory->GetInventoryItemCount(id, false, false, false), inventory->GetItemCountInContainer(id, InventoryType.KeyItems));
    }

    private static bool Matches(NativeObject* obj, FateContext* fate)
    {
        if (obj == null) return false;
        var director = 0x801A0000u | fate->FateId;
        var array = fate->ArrayEventHandler.Id;
        var id = obj->EventId.Id;
        var handler = obj->EventHandler == null ? 0 : obj->EventHandler->Info.EventId.Id;
        return obj->FateId == fate->FateId || id == director || handler == director ||
            (array != 0 && (id == array || handler == array));
    }

    public static void SnapshotObjectives(FateContext* fate, List<ObjectiveActor> output)
    {
        foreach (var obj in Services.Objects)
        {
            if (obj.Address == 0 || obj.ObjectKind is not (ObjectKind.EventNpc or ObjectKind.EventObj or ObjectKind.BattleNpc)) continue;
            var native = (NativeObject*)obj.Address;
            if (obj is IBattleNpc enemy && (enemy.IsDead || enemy.StatusFlags.HasFlag(StatusFlags.Hostile))) continue;
            var knownStarter = fate->MotivationNpc is not (0 or 0xE0000000) && obj.EntityId == fate->MotivationNpc;
            var knownRecipient = fate->ObjectiveNpc is not (0 or 0xE0000000) && obj.EntityId == fate->ObjectiveNpc;
            var linked = Matches(native, fate);
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
        if (lease.Active) return;
        var fate = Services.Fates.FirstOrDefault(f => f.FateId == actor.FateId);
        var obj = Services.Objects.FirstOrDefault(o => o.GameObjectId == actor.Id);
        var player = Services.Objects.LocalPlayer;
        if (fate == null || fate.Address == 0 || obj == null || !obj.IsTargetable || player == null ||
            Vector3.Distance(player.Position, obj.Position) > Math.Max(4, obj.HitboxRadius + 2)) return;
        var valid = new List<ObjectiveActor>();
        SnapshotObjectives((FateContext*)fate.Address, valid);
        if (!valid.Any(a => a.Id == actor.Id && a.Role == actor.Role)) return;
        // A pre-existing dialogue is never adopted as ours.
        if (DialogueOpen) return;
        var target = TargetSystem.Instance();
        if (target == null) return;
        owned = actor;
        var nativeFate = (FateContext*)fate.Address;
        lease.Begin(actor.Id, [0x801A0000u | actor.FateId, nativeFate->ArrayEventHandler.Id], Now);
        lastStatus = InteractionStatus.Waiting;
        lastAdvance = 0;
        log(L.T("Interação enviada ao NPC/objeto do FATE {0}; aguardando o contexto do diálogo.", actor.FateId));
        target->InteractWithObject((NativeObject*)obj.Address, true);
    }

    public static bool DialogueOpen => Visible("Talk") || Visible("SelectYesno") || Visible("SelectString") || Visible("SelectIconString");

    private int MatchingSelectorIndex()
    {
        if (owned == null) return -1;
        var selector = EventHandlerSelector.Instance();
        if (selector == null || selector->Target == null || selector->Target->GetGameObjectId().Id != owned.Id || selector->OptionsCount is < 1 or > 32) return -1;
        var match = -1;
        for (var i = 0; i < selector->OptionsCount; i++)
        {
            var option = selector->Options[i];
            if (option.Handler == null || !lease.Matches(new(owned.Id, option.Handler->Info.EventId.Id))) continue;
            if (match >= 0) return -1; // Ambiguous menus are not guessed.
            match = i;
        }
        return match;
    }

    public InteractionStatus ReadStatus(bool occupied)
    {
        if (owned == null) return InteractionStatus.None;
        var framework = EventFramework.Instance();
        var first = framework == null ? default : new EventSignal(framework->EventState1.ObjectId.Id, framework->EventState1.EventId.Id);
        var second = framework == null ? default : new EventSignal(framework->EventState2.ObjectId.Id, framework->EventState2.EventId.Id);
        var sceneObject = framework == null ? 0 : framework->SceneGameObjectId.Id;
        var result = lease.Observe(Now, occupied || DialogueOpen, first, second, MatchingSelectorIndex() >= 0, sceneObject);
        if (result != lastStatus)
        {
            if (result == InteractionStatus.Owned) log(L.T("Diálogo do FATE {0} reconhecido.", owned.FateId));
            if (result is InteractionStatus.Foreign or InteractionStatus.Expired)
                log($"FATE interaction: {result}; role={owned.Role}; event1={first.EventId:X8}; event2={second.EventId:X8}; actorMatch={first.ObjectId == owned.Id || second.ObjectId == owned.Id}; sceneMatch={sceneObject == owned.Id}; dialogue={DialogueOpen}");
            lastStatus = result;
        }
        if (!lease.Active) owned = null;
        return result;
    }

    public void Advance()
    {
        var c = Services.Conditions;
        var occupied = c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInQuestEvent] || c[ConditionFlag.OccupiedInEvent];
        if (ReadStatus(occupied) != InteractionStatus.Owned || Environment.TickCount64 - lastAdvance < 600) return;
        lastAdvance = Environment.TickCount64;
        var selectedHandler = MatchingSelectorIndex();
        if (selectedHandler >= 0)
        {
            var framework = EventFramework.Instance();
            if (framework != null) framework->InteractWithHandlerFromSelector(selectedHandler);
            return;
        }
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

    public void Cancel() { owned = null; lease.Cancel(); lastStatus = InteractionStatus.None; }
}
