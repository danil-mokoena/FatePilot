using System.Numerics;

namespace FatePilot.Core;

public sealed partial class Runner
{
    private int previousItems, previousHandIns;
    private double objectiveProgressAt, lastObjectiveMove = -100, lastInteraction = -100;
    private Vector3? objectiveGoal;
    private ulong interactionActor;
    private ObjectiveRole? interactionRole;
    private int interactionAttempts;
    private double heldSince;
    private bool delivering;
    private int lastObjectiveProgress = -1;
    private double lastStarterDiagnostic = -100;

    private void ResetObjectives(FateInfo fate, double now)
    {
        previousItems = fate.HeldItems;
        previousHandIns = fate.HandedIn;
        objectiveProgressAt = now;
        objectiveGoal = null;
        lastObjectiveMove = lastInteraction = now - 10;
        interactionActor = 0;
        interactionRole = null;
        interactionAttempts = 0;
        heldSince = fate.HeldItems > 0 ? now : -1;
        delivering = false;
        lastObjectiveProgress = fate.Progress;
        lastStarterDiagnostic = now - 10;
        runtime.CancelEventInteraction();
    }

    private void TickStartEvent(World w, double now)
    {
        var f = RefreshFate(w, now);
        if (f == null) return;
        if (f.State != EventState.Preparing)
        {
            runtime.CancelEventInteraction();
            Enter(RunState.Syncing, L.T("Verificando sincronização de nível."), now);
            return;
        }
        if (w.InCombat) { DefendObjective(w, f); return; }
        var npc = w.Objectives.FirstOrDefault(a => a.FateId == f.Id && a.Role == ObjectiveRole.Starter);
        if (npc != null) ApproachAndInteract(w, npc, now);
        else if (now - lastStarterDiagnostic >= 10)
        {
            lastStarterDiagnostic = now;
            Status = L.T("Aguardando identificação do NPC deste FATE.");
            runtime.Log(Status + " " + f.ObjectiveDiagnostic);
        }
        if (now - since > 60) SkipOrFail(L.T("Não foi possível iniciar o FATE pelo NPC."), now);
    }

    private void ObserveCollectionProgress(FateInfo f, double now)
    {
        if (f.HeldItems != previousItems || f.HandedIn != previousHandIns)
        {
            if (f.HeldItems > 0 && previousItems == 0) heldSince = now;
            if (f.HeldItems == 0) { heldSince = -1; delivering = false; }
            if (f.HandedIn > previousHandIns || (f.HeldItems < previousItems && interactionRole == ObjectiveRole.Recipient && now - lastInteraction < 15)) participated = true;
            previousItems = f.HeldItems;
            previousHandIns = f.HandedIn;
            objectiveProgressAt = now;
            interactionAttempts = 0;
        }
    }

    private void TickObjective(World w, FateInfo f, double now)
    {
        if (w.Casting) return;
        var threat = w.Enemies.Where(e => e.TargetingPlayer).MinBy(e => Vector3.Distance(w.Position, e.Position));
        if (threat != null)
        {
            runtime.StopMovement(); objectiveGoal = null;
            runtime.Target(threat.Id); runtime.SetCombat(true);
            return;
        }
        if (w.OwnEventInteraction)
        {
            runtime.StopMovement(); runtime.SetCombat(false);
            runtime.AdvanceEventInteraction();
            return;
        }
        var actors = w.Objectives.Where(a => a.FateId == f.Id).ToArray();
        if (f.EffectiveKind == FateKind.Collection)
        {
            var recipient = actors.FirstOrDefault(a => a.Role == ObjectiveRole.Recipient);
            delivering |= f.HeldItems > 0 && (f.HeldItems >= options.CollectionBatchSize ||
                PendingReturnGoal != ReturnGoal.None || (heldSince >= 0 && now - heldSince >= 45) || f.SecondsLeft < 90 || f.Progress >= 80 || f.State == EventState.Ending);
            if (delivering && f.HeldItems > 0 && recipient != null && !w.InCombat)
            {
                Status = L.T("Entregando itens do FATE: {0}", f.HeldItems);
                ApproachAndInteract(w, recipient, now);
            }
            else if (delivering && f.HeldItems > 0 && recipient == null)
            {
                // 1.3.1: never keep collecting silently when the items should be handed in.
                if (!DefendObjective(w, f))
                {
                    runtime.SetCombat(false);
                    Status = L.T("{0} itens prontos para entrega, mas o NPC de entrega do FATE não foi identificado.", f.HeldItems);
                    if (now - lastStarterDiagnostic >= 10)
                    {
                        lastStarterDiagnostic = now;
                        runtime.Log(Status + " " + f.ObjectiveDiagnostic);
                    }
                }
            }
            else if (!w.InCombat && actors.Where(a => a.Role == ObjectiveRole.Pickup)
                         .MinBy(a => Vector3.Distance(w.Position, a.Position)) is { } pickup)
            {
                Status = L.T("Recolhendo itens do FATE.");
                ApproachAndInteract(w, pickup, now);
            }
            else if (!DefendObjective(w, f))
            {
                runtime.SetCombat(false);
                Status = L.T("Aguardando itens ou inimigos do FATE.");
                if (f.HeldItems > 0 && recipient == null)
                    Status = L.T("Aguardando o NPC de entrega do FATE.");
            }
            if (now - objectiveProgressAt > 180 && !w.InCombat)
                SkipOrFail(L.T("Coleta sem progresso confirmado por 180 segundos."), now);
        }
        else
        {
            if (DefendObjective(w, f)) return;
            runtime.SetCombat(false);
            var escort = actors.Where(a => a.Role == ObjectiveRole.Escort).MinBy(a => Vector3.Distance(w.Position, a.Position));
            if (escort != null)
            {
                Status = L.T("Acompanhando o objetivo do FATE.");
                ApproachObjective(w, escort.Position, Math.Max(4, escort.Hitbox + 3), now);
                objectiveProgressAt = now;
            }
            else
            {
                Status = L.T("Aguardando a próxima etapa do FATE.");
                if (f.EffectiveKind == FateKind.Escort)
                {
                    Status = L.T("Procurando o NPC da escolta.");
                    if (now - lastStarterDiagnostic >= 10) { lastStarterDiagnostic = now; runtime.Log(Status + " " + f.ObjectiveDiagnostic); }
                }
                // 1.3.5: an escort that keeps advancing is not lost, even before its NPC is identified.
                if (f.Progress != lastObjectiveProgress) { lastObjectiveProgress = f.Progress; objectiveProgressAt = now; }
                if (now - objectiveProgressAt > 120 && f.EffectiveKind == FateKind.Escort)
                    SkipOrFail(L.T("Objetivo de escolta não encontrado."), now);
            }
        }
        if (now - since > 1800) SkipOrFail(L.T("Tempo máximo do objetivo excedido."), now);
    }

    private bool DefendObjective(World w, FateInfo f)
    {
        // 1.3.2: enemies attacking this FATE's NPCs (escort, turn-in, defended NPC) count even when
        // the game did not tag them with the FATE; they come right after anything attacking us.
        var protectedIds = w.Objectives.Where(a => a.FateId == f.Id).Select(a => a.Id).ToHashSet();
        bool AttacksNpc(EnemyInfo e) => e.TargetId != 0 && (protectedIds.Contains(e.TargetId) ||
            (e.AttackingNpc && Vector3.Distance(e.Position, f.Position) <= f.Radius + 15));
        var enemy = w.Enemies.Where(e => e.FateId == f.Id || e.TargetingPlayer || AttacksNpc(e))
            .OrderByDescending(e => e.TargetingPlayer).ThenByDescending(AttacksNpc)
            .ThenBy(e => Vector3.Distance(w.Position, e.Position)).FirstOrDefault();
        if (enemy == null) return false;
        runtime.StopMovement(); objectiveGoal = null;
        runtime.Target(enemy.Id); runtime.SetCombat(true);
        return true;
    }

    private void ApproachAndInteract(World w, ObjectiveActor actor, double now)
    {
        runtime.SetCombat(false);
        if (!ApproachObjective(w, actor.Position, Math.Max(2.5f, actor.Hitbox + 1.5f), now)) return;
        if (w.Casting || w.Occupied || now - lastInteraction < 3) return;
        if (interactionActor != actor.Id) { interactionActor = actor.Id; interactionAttempts = 0; }
        if (interactionAttempts >= 5) { SkipOrFail(L.T("Interação do FATE não confirmou progresso."), now); return; }
        runtime.Log(L.T("Interagindo com o objetivo do FATE {0}: {1}.", actor.FateId, actor.Role));
        runtime.Interact(actor);
        interactionRole = actor.Role;
        lastInteraction = now;
        interactionAttempts++;
    }

    private bool ApproachObjective(World w, Vector3 position, float distance, double now)
    {
        if (Vector3.Distance(w.Position, position) <= distance)
        {
            runtime.StopMovement(); objectiveGoal = null;
            return true;
        }
        if (objectiveGoal == null) { lastPosition = w.Position; lastMotion = now; }
        if (Vector3.Distance(w.Position, lastPosition) > 1) { lastPosition = w.Position; lastMotion = now; }
        if (now - lastMotion > 25) { SkipOrFail(L.T("Caminho até o objetivo sem progresso."), now); return false; }
        if (!w.Casting && now - lastObjectiveMove >= 2 && (!w.NavBusy || objectiveGoal == null || Vector3.Distance(position, objectiveGoal.Value) > 3))
        {
            runtime.Move(position, false); objectiveGoal = position; lastObjectiveMove = now;
        }
        return false;
    }
}
