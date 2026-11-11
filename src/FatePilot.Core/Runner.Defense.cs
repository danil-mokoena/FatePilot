using System.Numerics;

namespace FatePilot.Core;

public sealed partial class Runner
{
    private RunState defenseResume;
    public const float MeleeReach = 2.5f, RangedReach = 20f;
    private Vector3? approachGoal;
    private double lastApproach = double.NegativeInfinity;

    // 1.4: attack an enemy and get within reach of it ourselves. The movement module measures its
    // distance flat (X/Z only), so an enemy below a ledge or up on a rock counted as "in range" and
    // the character just stood there. FatePilot now picks a spot at attack reach beside the enemy,
    // on the enemy's own side and height (2.5 yalms for tanks and melee, 20 for ranged and casters,
    // plus the enemy's size), measures the real 3D distance and walks there with the navigation
    // module. It never moves while casting and stops once in reach.
    private void Engage(World w, EnemyInfo e, double now)
    {
        runtime.Target(e.Id);
        runtime.SetCombat(true);
        if (w.Casting) return;
        var reach = (w.MeleeJob ? MeleeReach : RangedReach) + e.Hitbox + 0.5f;
        if (Vector3.Distance(w.Position, e.Position) <= reach + 1f)
        {
            if (approachGoal != null) { runtime.StopMovement(); approachGoal = null; }
            return;
        }
        var away = w.Position - e.Position;
        away.Y = 0;
        var direction = away.LengthSquared() < 0.01f ? Vector3.UnitX : Vector3.Normalize(away);
        var spot = e.Position + direction * MathF.Max(0.5f, reach - 0.5f);
        if (approachGoal is { } goal && w.NavBusy && Vector3.Distance(goal, spot) < 3) return;
        if (now - lastApproach < 1.5) return;
        lastApproach = now;
        approachGoal = spot;
        Status = L.T("Aproximando do alvo.");
        try { runtime.Move(spot, false); }
        catch (InvalidOperationException) { approachGoal = null; }
    }

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
            var resume = Selected != null ? RunState.Mounting : defenseResume is RunState.HubApproach or RunState.ToTrader ? defenseResume : RunState.Selecting;
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
        if (threat != null) Engage(w, threat, now);
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
        objectiveGoal = null;
        Engage(w, enemy, now);
        noEnemySince = now;
        Status = L.T("Priorizando Forlorn Maiden / The Forlorn.");
        return true;
    }
}
