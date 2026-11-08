using System.Numerics;

namespace FatePilot.Core;

// 1.0: walk to the bicolor gemstone trader and open the conversation, then hand control back.
// The menu and the shop are left to the player: nothing inside them is clicked.
public sealed partial class Runner
{
    private bool WantsTrader => PendingReturnGoal is ReturnGoal.Trader or ReturnGoal.Purchase ||
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
            if (PendingReturnGoal == ReturnGoal.Purchase && NextOrder() is { } order) { BeginPurchaseWait(w, order, now); return; }
            Finish(L.T("Conversa com o trocador de gemstones aberta. Escolha a loja e compre o que quiser."));
            return;
        }
        if (w.NavReady && !w.NavBusy && Throttle(now, 2)) runtime.Move(trader?.Position ?? expected.Position, options.UseFlight && w.CanFly && w.Mounted);
    }

    // ---------- 1.1 purchase queue ----------
    private GemstoneOrder? NextOrder() => options.PurchaseQueue.FirstOrDefault(o => o.Remaining > 0);
    private uint buyingItem;
    private int buyingBaseline, buyingWanted;
    private readonly List<(uint ItemId, int Quantity)> purchases = [];

    // Purchases seen this session, for the plugin to mark in the saved queue.
    public List<(uint ItemId, int Quantity)> TakePurchases()
    {
        var done = purchases.ToList();
        purchases.Clear();
        return done;
    }

    // The trader's conversation is open: the player buys, the plugin only watches the bag.
    private void BeginPurchaseWait(World w, GemstoneOrder order, double now)
    {
        try { runtime.Suspend(); } catch (Exception) { }
        buyingItem = order.ItemId;
        buyingBaseline = w.ItemCounts.GetValueOrDefault(order.ItemId);
        buyingWanted = order.Remaining;
        Enter(RunState.Buying, L.T("Compre {0}× {1} na loja do trocador. Assim que a compra aparecer na bolsa, o FatePilot volta aos FATEs.", order.Remaining, order.Name), now);
        Alert(NotifyKind.Goal, Status);
    }

    private void TickBuying(World w, double now)
    {
        if (w.Loading || !w.LoggedIn) return;
        var order = options.PurchaseQueue.FirstOrDefault(o => o.ItemId == buyingItem && o.Remaining > 0);
        var bought = Math.Max(0, w.ItemCounts.GetValueOrDefault(buyingItem) - buyingBaseline);
        if (now - since > 600) { Finish(L.T("A compra não apareceu na bolsa em 10 minutos. Sessão encerrada no trocador.")); return; }
        // Let the player shop; decide only once the trader windows are closed.
        if (w.Occupied || w.DialogueOpen || now - since < 8) return;
        if (bought == 0) { Finish(L.T("Nada foi comprado. Sessão encerrada no trocador.")); return; }
        if (order != null) order.Bought = Math.Min(order.Quantity, order.Bought + bought);
        purchases.Add((buyingItem, bought));
        runtime.Log(L.T("Compra registrada: {0}× {1}.", bought, order?.Name ?? buyingItem.ToString()));
        try { runtime.ResumeSession(); }
        catch (Exception ex) { Stop(L.T("Não foi possível continuar: {0}", ex.GetBaseException().Message), true); return; }
        PendingReturnGoal = ReturnGoal.None;
        returning = false;
        expectedTerritory = w.Territory;
        sessionJob = w.Job;
        visited.Clear();
        idleSince = noEnemySince = now;
        Enter(RunState.Selecting, NextOrder() is { } next
            ? L.T("Compra feita. Próxima da fila: {0}. Voltando aos FATEs.", next.Name)
            : L.T("Compra feita. Voltando aos FATEs."), now);
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
