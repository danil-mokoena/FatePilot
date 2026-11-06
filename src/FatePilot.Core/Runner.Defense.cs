using System.Numerics;

namespace FatePilot.Core;

public sealed partial class Runner
{
    private RunState defenseResume;

    private bool HandleUnexpectedCombat(World w, double now)
    {
        var canDefend = State is RunState.Selecting or RunState.Waiting or RunState.HubApproach ||
            (Selected != null && w.CurrentFate != Selected.Id &&
                State is RunState.Mounting or RunState.Moving or RunState.Landing or RunState.Syncing or RunState.StartingEvent or RunState.Fighting);
        if (State != RunState.Defending)
        {
            if (!w.InCombat || !canDefend) return false;
            defenseResume = State;
            runtime.StopMovement(); runtime.CancelEventInteraction(); runtime.SetCombat(false);
            Enter(RunState.Defending, L.T("Defendendo de um inimigo fora do FATE."), now);
        }
        if (!w.InCombat)
        {
            runtime.SetCombat(false);
            var resume = Selected != null ? RunState.Mounting : defenseResume == RunState.HubApproach ? RunState.HubApproach : RunState.Selecting;
            lastMotion = now; lastPosition = w.Position;
            Enter(resume, L.T("Combate encerrado; retomando a rota."), now);
            return true;
        }
        if (now - since > 120) { Stop(L.T("Não foi possível sair do combate para continuar a rota."), true); return true; }
        if (w.Mounted || w.Flying)
        {
            if (Throttle(now, 2)) { transitionUntil = now + 5; runtime.Dismount(); }
            return true;
        }
        var threat = w.Enemies.Where(e => e.TargetingPlayer).MinBy(e => Vector3.Distance(w.Position, e.Position));
        if (threat != null) { runtime.Target(threat.Id); runtime.SetCombat(true); }
        else runtime.SetCombat(false);
        return true;
    }

    private bool TryPriorityEnemy(World w, FateInfo fate, double now)
    {
        if (!options.PrioritizeForlorn || w.CurrentFate != fate.Id || fate.State != EventState.Running) return false;
        var enemy = w.Enemies.Where(e => e.NameId is 6737 or 6738 &&
            (e.FateId == fate.Id || (e.FateId == 0 && Vector3.Distance(e.Position, fate.Position) <= fate.Radius)))
            .OrderByDescending(e => e.NameId == 6738).ThenBy(e => Vector3.Distance(w.Position, e.Position)).FirstOrDefault();
        if (enemy == null) return false;
        runtime.StopMovement(); objectiveGoal = null;
        runtime.Target(enemy.Id); runtime.SetCombat(true);
        noEnemySince = now;
        Status = L.T("Priorizando Forlorn Maiden / The Forlorn.");
        return true;
    }
}
