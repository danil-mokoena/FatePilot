namespace FatePilot.Core;

public sealed partial class Runner
{
    // 1.4: dying no longer ends the session. FatePilot accepts the game's own "return to home point"
    // (or a raise) prompt, waits until the character is back on its feet, then teleports back to the
    // zone it was farming and carries on. Nothing else is clicked while dead.
    private bool TickDeath(World w, double now)
    {
        if (w.Dead)
        {
            if (!reviving)
            {
                reviving = true;
                deadSince = now;
                runtime.StopMovement();
                runtime.CancelEventInteraction();
                runtime.SetCombat(false);
                Selected = null;
                runtime.Log(L.T("Personagem derrotado: voltando ao ponto de retorno."));
                Alert(NotifyKind.Problem,L.T("Personagem derrotado: voltando ao ponto de retorno."));
            }
            Status = L.T("Personagem derrotado: voltando ao ponto de retorno.");
            if (Throttle(now, 2)) runtime.AcceptRespawn();
            if (now - deadSince > 300) { reviving = false; Stop(L.T("O personagem continua derrotado após 5 minutos."), true); }
            return true;
        }
        if (!reviving) return false;
        if (w.Loading || !w.LoggedIn || w.Occupied) { Status = L.T("Voltando à vida…"); return true; }
        reviving = false;
        lastMotion = now; lastPosition = w.Position;
        idleSince = noEnemySince = now;
        if (w.Territory != expectedTerritory)
        {
            var back = options.Route.FirstOrDefault(d => d.TerritoryId == expectedTerritory) ?? options.Route.FirstOrDefault();
            if (back == null) { Stop(L.T("Revivido fora da rota e sem zona para voltar."), true); return true; }
            runtime.Log(L.T("Revivido; voltando para {0}.", back.Name));
            Teleport(back, w, now);
            return true;
        }
        Enter(RunState.Selecting, L.T("Revivido; procurando FATEs."), now);
        return true;
    }
}
