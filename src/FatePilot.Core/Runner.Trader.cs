using System.Numerics;

namespace FatePilot.Core;

// 1.0: walk to the bicolor gemstone trader and open the conversation, then hand control back.
// The menu and the shop are left to the player: nothing inside them is clicked.
public sealed partial class Runner
{
    private bool WantsTrader => PendingReturnGoal == ReturnGoal.Trader ||
        (options.VisitGemstoneTrader && PendingReturnGoal is ReturnGoal.Gemstones or ReturnGoal.CurrencyCap);

    // "Go to the trader now": a short session whose only goal is the trader.
    public void StartTraderTrip(RunOptions config, World world, double now)
    {
        var target = config.GemstoneDestination ?? config.ReturnDestination;
        if (GemstoneTraders.In(world.Territory) == null && (target == null || GemstoneTraders.In(target.TerritoryId) == null))
        {
            Stop(L.T("Não há trocador de gemstones nesta zona. Escolha em Inventário a cidade para trocar gemstones."), true);
            return;
        }
        Start(config, world, now);
        if (!IsRunning) return;
        PendingReturnGoal = ReturnGoal.Trader;
        runtime.Log(SessionGoals.Label(ReturnGoal.Trader));
    }

    private void EnterTrader(World w, double now)
    {
        expectedTerritory = w.Territory;
        lastMotion = now; lastPosition = w.Position;
        Enter(RunState.ToTrader, L.T("Indo até o trocador de gemstones."), now);
    }

    private void TickTrader(World w, double now)
    {
        if (w.InCombat)
        {
            defenseResume = RunState.ToTrader;
            runtime.StopMovement(); runtime.SetCombat(false);
            Enter(RunState.Defending, L.T("Defendendo de um inimigo fora do FATE."), now);
            return;
        }
        if (w.NavError != null) { Stop(L.T("Navegação: {0}", w.NavError), true); return; }
        if (w.Occupied || w.DialogueOpen)
        {
            if (!w.DialogueOpen && now < transitionUntil) return;
            if (w.DialogueOpen && !w.Occupied) { runtime.StopMovement(); Note(L.T("Janela aberta pelo jogador; aguardando ela fechar.")); return; }
            Stop(L.T("Um diálogo ou outra interação interrompeu a sessão."), true);
            return;
        }
        if (Vector3.Distance(w.Position, lastPosition) > 1) { lastMotion = now; lastPosition = w.Position; }
        if (now - since > 180 || now - lastMotion > 30) { Stop(L.T("Não foi possível alcançar o trocador de gemstones."), true); return; }
        if (w.Casting) return;
        var expected = GemstoneTraders.In(w.Territory);
        if (expected == null) { Stop(L.T("Não há trocador de gemstones nesta zona. Escolha em Inventário a cidade para trocar gemstones."), true); return; }
        var trader = w.GemstoneTrader;
        if (trader != null && Vector3.Distance(w.Position, trader.Position) <= Math.Max(3, trader.Hitbox + 2.5f))
        {
            if (w.Mounted || w.Flying) { if (Throttle(now, 2)) { transitionUntil = now + 5; runtime.Dismount(); } return; }
            runtime.StopMovement();
            runtime.TalkTo(trader);
            Finish(L.T("Conversa com o trocador de gemstones aberta. Escolha a loja e compre o que quiser."));
            return;
        }
        if (w.NavReady && !w.NavBusy && Throttle(now, 2)) runtime.Move(trader?.Position ?? expected.Position, options.UseFlight && w.CanFly && w.Mounted);
    }

    // Session done (return goal reached or trader reached): release control and report.
    private void Finish(string status)
    {
        runtime.End();
        State = RunState.Finished;
        Status = status;
        CaptureSummary(Status);
        runtime.Log(Status);
        Alert(NotifyKind.Goal, Status);
    }
}
