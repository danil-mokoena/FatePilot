using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using System.Numerics;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace FatePilot;

// One short-lived interaction with an identified FATE object. No global dialogue skipper.
internal sealed unsafe class EventInteraction(Action<string> log)
{
    private ObjectiveActor? owned;
    private readonly InteractionLease lease = new();
    private readonly LocalDialogueGuard localDialogue = new();
    private long lastAdvance;
    private double interactionSentAt = double.NegativeInfinity;
    private InteractionStatus lastStatus;
    private string localEvidence = "not checked";
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

    // The NPC each FATE was offered by while it was preparing (session memory only).
    private static readonly Dictionary<ushort, ulong> Starters = [];

    public static void SnapshotObjectives(FateContext* fate, List<ObjectiveActor> output)
    {
        if (Starters.Count > 64) Starters.Clear();
        var candidates = new List<FateNpcCandidate>();
        foreach (var npc in Services.Objects)
        {
            if (npc.Address == 0 || npc.ObjectKind is not (ObjectKind.EventNpc or ObjectKind.BattleNpc)) continue;
            var native = (NativeObject*)npc.Address;
            candidates.Add(new(npc.GameObjectId, npc.EntityId, npc.BaseId, native->LayoutId, Matches(native, fate),
                npc.IsTargetable, npc is not IBattleNpc battle || !battle.IsDead,
                npc is IBattleNpc b && b.StatusFlags.HasFlag(StatusFlags.Hostile), native->NamePlateIconId, npc.Position, npc.HitboxRadius));
        }
        var starter = fate->State == FateState.Preparing
            ? FateNpcBinding.Resolve(fate->MotivationNpc, fate->FateModeMotivationIcon, fate->Location, fate->Radius, candidates) : null;
        if (starter != null) Starters[fate->FateId] = starter.Id;
        FateNpcCandidate? recipient = null;
        if (fate->Rule == 2)
        {
            // 1.3.1: the turn-in NPC is usually the one that gave the FATE. Try the objective NPC,
            // then the FATE's own NPC, then the exact NPC this FATE was started from.
            if (fate->ObjectiveNpc is not (0 or 0xE0000000))
                recipient = FateNpcBinding.Resolve(fate->ObjectiveNpc, fate->FateModeObjectiveIcon, fate->Location, fate->Radius, candidates);
            recipient ??= FateNpcBinding.Resolve(fate->MotivationNpc, fate->FateModeObjectiveIcon, fate->Location, fate->Radius, candidates);
            if (recipient == null && Starters.TryGetValue(fate->FateId, out var known))
                recipient = candidates.FirstOrDefault(c => c.Id == known && c.Targetable && c.Alive && !c.Hostile);
        }
        if (starter != null) output.Add(new(starter.Id, fate->FateId, ObjectiveRole.Starter, starter.Position, starter.Hitbox));
        if (recipient != null) output.Add(new(recipient.Id, fate->FateId, ObjectiveRole.Recipient, recipient.Position, recipient.Hitbox));
        foreach (var obj in Services.Objects)
        {
            if (obj.Address == 0 || obj.ObjectKind is not (ObjectKind.EventNpc or ObjectKind.EventObj or ObjectKind.BattleNpc)) continue;
            var native = (NativeObject*)obj.Address;
            if (obj is IBattleNpc enemy && (enemy.IsDead || enemy.StatusFlags.HasFlag(StatusFlags.Hostile))) continue;
            var knownStarter = fate->MotivationNpc is not (0 or 0xE0000000) && obj.EntityId == fate->MotivationNpc;
            var knownRecipient = fate->ObjectiveNpc is not (0 or 0xE0000000) && obj.EntityId == fate->ObjectiveNpc;
            var linked = Matches(native, fate);
            if (!knownStarter && !knownRecipient && !linked) continue;
            if (fate->Rule == 2 && obj.IsTargetable && linked && obj.ObjectKind == ObjectKind.EventObj)
                output.Add(new(obj.GameObjectId, fate->FateId, ObjectiveRole.Pickup, obj.Position, obj.HitboxRadius));
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
        localDialogue.Reset();
        var nativeFate = (FateContext*)fate.Address;
        // 1.3.4: FATE pickups (rocks, fragments, crates) run their own event, not the FATE
        // director's; this verified object's own event is part of the same interaction.
        var nativeObj = (NativeObject*)obj.Address;
        var events = new List<uint> { 0x801A0000u | actor.FateId, nativeFate->ArrayEventHandler.Id };
        if (actor.Role == ObjectiveRole.Pickup)
        {
            if (nativeObj->EventId.Id != 0) events.Add(nativeObj->EventId.Id);
            if (nativeObj->EventHandler != null && nativeObj->EventHandler->Info.EventId.Id != 0) events.Add(nativeObj->EventHandler->Info.EventId.Id);
        }
        lease.Begin(actor.Id, [.. events.Distinct()], Now);
        interactionSentAt = Now;
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
        var local = MatchingLocalDialogue(first.IsEmpty && second.IsEmpty && sceneObject is 0 or 0xE0000000);
        var result = lease.Observe(Now, occupied || DialogueOpen, first, second, MatchingSelectorIndex() >= 0, sceneObject, local);
        if (result != lastStatus)
        {
            if (result == InteractionStatus.Owned) log(L.T("Diálogo do FATE {0} reconhecido.", owned.FateId));
            if (result is InteractionStatus.Foreign or InteractionStatus.Expired)
                log($"FATE interaction: {result}; role={owned.Role}; event1={first.EventId:X8}; event2={second.EventId:X8}; actorMatch={first.ObjectId == owned.Id || second.ObjectId == owned.Id}; sceneMatch={sceneObject == owned.Id}; localDialog={local}; {localEvidence}; dialogue={DialogueOpen}");
            lastStatus = result;
        }
        if (!lease.Active) owned = null;
        return result;
    }

    private bool MatchingLocalDialogue(bool globalsEmpty)
    {
        localEvidence = "local actor/menu unavailable";
        if (!globalsEmpty || owned == null || owned.Role is not (ObjectiveRole.Starter or ObjectiveRole.Recipient) ||
            Visible("SelectString") || Visible("SelectIconString")) { localDialogue.Reset(); return false; }
        var npc = Services.Objects.FirstOrDefault(o => o.GameObjectId == owned.Id);
        var fate = Services.Fates.FirstOrDefault(f => f.FateId == owned.FateId);
        var player = Services.Objects.LocalPlayer;
        if (npc == null || npc.Address == 0 || npc.ObjectKind is not (ObjectKind.EventNpc or ObjectKind.BattleNpc) ||
            fate == null || fate.Address == 0 || player == null || Services.Targets.Target?.GameObjectId != owned.Id ||
            Vector3.Distance(player.Position, npc.Position) > Math.Max(4, npc.HitboxRadius + 2))
        { localDialogue.Reset(); return false; }
        var native = (NativeObject*)npc.Address;
        var f = (FateContext*)fate.Address;
        var reference = owned.Role == ObjectiveRole.Starter || f->ObjectiveNpc is 0 or 0xE0000000 ? f->MotivationNpc : f->ObjectiveNpc;
        var bound = Matches(native, f) || (reference is not (0 or 0xE0000000) && (reference == npc.EntityId || reference == (uint)npc.GameObjectId));
        var talk = Visible("Talk"); var yesNo = Visible("SelectYesno");
        var addon = (AtkUnitBase*)Services.GameGui.GetAddonByName(talk ? "Talk" : "SelectYesno").Address;
        var atk = RaptureAtkModule.Instance();
        ulong receiver = 0;
        if ((talk || yesNo) && addon != null && atk != null && atk->AddonCallbackMapping.LongCount is > 0 and <= 1024 &&
            atk->AddonCallbackMapping.TryGetValue(addon->Id, out var entry, false)) receiver = (ulong)entry.EventInterface;
        // Compare only this verified NPC's localized sheet label. Never read/log character names or dialogue text.
        var expected = npc.ObjectKind == ObjectKind.EventNpc
            ? Services.Data.GetExcelSheet<ENpcResident>().GetRowOrDefault(npc.BaseId)?.Singular.ExtractText()
            : npc is IBattleNpc b ? Services.Data.GetExcelSheet<BNpcName>().GetRowOrDefault(b.NameId)?.Singular.ExtractText() : null;
        var speaker = talk ? TalkSpeaker() : null;
        var speakerMatches = DialogueSpeaker.Matches(expected, speaker);
        localEvidence = $"local bound={bound}; speakerAvailable={!string.IsNullOrWhiteSpace(speaker)}; speakerMatch={speakerMatches}; callbackPresent={receiver != 0}; talk={talk}; yesNo={yesNo}";
        return localDialogue.Observe(globalsEmpty, bound, talk, speakerMatches, yesNo, receiver, Now,
            Now - interactionSentAt <= LocalDialogueGuard.ConfirmWindow);
    }

    private static string? TalkSpeaker()
    {
        var talk = (AddonTalk*)Services.GameGui.GetAddonByName("Talk").Address;
        if (talk == null || !talk->IsVisible || !talk->IsReady) return null;
        // The Talk header is the live speaker. UIModule.LastTalkName can be stale or
        // empty for a local NPC conversation and must not decide its ownership.
        var name = talk->AtkTextNode220;
        if (name == null || name->NodeText.StringPtr.Value == null || name->NodeText.BufUsed is < 2 or > 513) return null;
        return Dalamud.Game.Text.SeStringHandling.SeString.Parse(name->NodeText.AsSpan()).TextValue;
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
            ClickTalk();
            return;
        }
        var yesno = (AtkUnitBase*)Services.GameGui.GetAddonByName("SelectYesno").Address;
        if (yesno != null && yesno->IsVisible) yesno->FireCallbackInt(0);
    }

    // Callers must first prove ownership of the native event and actor.
    internal static void ClickTalk()
    {
        var talk = (AtkUnitBase*)Services.GameGui.GetAddonByName("Talk").Address;
        var stage = AtkStage.Instance();
        if (talk == null || !talk->IsVisible || stage == null) return;
        var evt = new AtkEvent
        {
            Target = &stage->AtkEventTarget, Listener = (AtkEventListener*)talk,
            State = new AtkEventState { StateFlags = AtkEventStateFlags.Pooled | AtkEventStateFlags.Unk3 },
        };
        var data = new AtkEventData();
        talk->ReceiveEvent(AtkEventType.MouseDown, 0, &evt, &data);
        talk->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
        talk->ReceiveEvent(AtkEventType.MouseUp, 0, &evt, &data);
    }

    private static bool Visible(string name)
    {
        var addon = (AtkUnitBase*)Services.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible;
    }

    public void Cancel() { owned = null; lease.Cancel(); localDialogue.Reset(); lastStatus = InteractionStatus.None; interactionSentAt = double.NegativeInfinity; }
}
