using System.Numerics;

namespace FatePilot.Core;

public sealed partial class Runner
{
    private enum RestockStage { None, ToVendor, Approach, Opening, Buying, Closing, ToRoute }
    private RestockStage restockStage;
    private Destination? restockResume;
    private int restockInstance;
    private GreensPurchase? purchase;
    private bool closeRequested;
    public long GreensGilCommitted { get; private set; }

    private void ResetRestock()
    {
        restockStage = RestockStage.None;
        restockResume = null;
        restockInstance = 0;
        purchase = null;
        closeRequested = false;
        GreensGilCommitted = 0;
    }

    private bool TryRestock(World w, double now)
    {
        if (!options.RestockGreens) return false;
        if (!w.GreensInventory.Ready) throw new InvalidOperationException(L.T("O inventário ainda não está disponível para a reposição."));
        if (w.GreensInventory.Count > options.GreensThreshold) return false;
        _ = GreensRestock.Needed(options, w.GreensInventory, GreensGilCommitted);
        restockResume = options.Route.FirstOrDefault(d => d.TerritoryId == w.Territory) ?? options.Route[0];
        restockInstance = restockResume.TerritoryId == w.Territory && w.InstanceCount > 0 ? w.Instance : 0;
        purchase = null; closeRequested = false; nextInstance = 0;
        runtime.StopMovement(); runtime.SetCombat(false);
        restockStage = RestockStage.ToVendor;
        // Do not repeatedly initialize the trip while waiting to dismount/cast.
        Enter(RunState.Restocking, L.T("Indo comprar Gysahl Greens."), now);
        TickRestock(w, now);
        return true;
    }

    private void TickRestock(World w, double now)
    {
        if (w.InCombat) { Stop(L.T("Combate inesperado durante a reposição."), true); return; }
        if (w.GreensShop is GreensShopState.Foreign or GreensShopState.Failed)
        { Stop(w.GreensShopError ?? L.T("A loja não corresponde à reposição solicitada."), true); return; }
        if (w.NavError != null) { Stop(L.T("Navegação: {0}", w.NavError), true); return; }
        var shopPhase = restockStage is RestockStage.Opening or RestockStage.Buying or RestockStage.Closing;
        if ((w.Occupied || w.DialogueOpen) && (!shopPhase || w.GreensShop == GreensShopState.None))
        {
            if (!w.DialogueOpen && now < transitionUntil) return;
            // A plain prompt the player opened on the way (discard item, confirmations): wait, never click.
            if (!shopPhase && w.DialogueOpen && !w.Occupied)
            {
                runtime.StopMovement();
                if (!waitingForPlayerDialog) { waitingForPlayerDialog = true; runtime.Log(L.T("Janela aberta pelo jogador; aguardando ela fechar.")); }
                return;
            }
            StopOrRecover(L.T("Um diálogo ou outra interação interrompeu a sessão."), now); return;
        }
        waitingForPlayerDialog = false;
        if (purchase is { } pending)
        {
            var result = pending.Observe(w.GreensInventory, now);
            if (result is PurchaseResult.Mismatch or PurchaseResult.TimedOut)
            { Stop(L.T("Compra sem confirmação consistente. Nenhuma nova tentativa será enviada."), true); return; }
            if (result != PurchaseResult.Confirmed) return;
            runtime.Log(L.T("Compra confirmada: {0} Gysahl Greens por {1} gil.", pending.Quantity, pending.Cost));
            purchase = null;
        }
        // A session goal wins over restocking; an already sent purchase is reconciled first.
        if (PendingReturnGoal != ReturnGoal.None && restockStage is not (RestockStage.Closing or RestockStage.ToRoute))
        {
            runtime.StopMovement();
            restockStage = RestockStage.Closing; closeRequested = false;
            Enter(RunState.Restocking, SessionGoals.Label(PendingReturnGoal), now);
        }
        switch (restockStage)
        {
            case RestockStage.ToVendor:
                if (w.Territory == GreensRestock.VendorCity.TerritoryId)
                {
                    restockStage = RestockStage.Approach; lastMotion = now; lastPosition = w.Position;
                    Enter(RunState.Restocking, L.T("Aproximando do vendedor de Gysahl Greens."), now);
                }
                else Teleport(GreensRestock.VendorCity, w, now);
                break;
            case RestockStage.Approach:
                if (now - since > 180 || now - lastMotion > 30) { Stop(L.T("Não foi possível alcançar o vendedor de Gysahl Greens."), true); return; }
                if (Vector3.Distance(w.Position, lastPosition) > 1) { lastMotion = now; lastPosition = w.Position; }
                if (w.Casting) return;
                if (w.Mounted || w.Flying)
                { if (Throttle(now, 2)) { transitionUntil = now + 5; runtime.Dismount(); } return; }
                var vendor = w.GreensVendor;
                if (vendor != null && Vector3.Distance(w.Position, vendor.Position) <= Math.Max(3, vendor.Hitbox + 2))
                {
                    runtime.StopMovement(); runtime.OpenGreensShop(vendor);
                    restockStage = RestockStage.Opening;
                    Enter(RunState.Restocking, L.T("Abrindo a loja de Gysahl Greens."), now);
                }
                else if (w.NavReady && !w.NavBusy && Throttle(now, 2)) runtime.Move(vendor?.Position ?? GreensRestock.VendorPosition, false);
                break;
            case RestockStage.Opening:
                if (now - since > 20) { Stop(L.T("A loja de Gysahl Greens não abriu a tempo."), true); return; }
                if (w.GreensShop == GreensShopState.Ready)
                {
                    restockStage = RestockStage.Buying;
                    Enter(RunState.Restocking, L.T("Repondo Gysahl Greens."), now);
                }
                else runtime.AdvanceGreensShop();
                break;
            case RestockStage.Buying:
                if (now - since > 180) { Stop(L.T("Tempo máximo da reposição excedido."), true); return; }
                if (w.GreensShop == GreensShopState.Closed) { Stop(L.T("A loja foi fechada antes de concluir a reposição."), true); return; }
                if (w.GreensShop != GreensShopState.Ready) return;
                var needed = GreensRestock.Needed(options, w.GreensInventory, GreensGilCommitted);
                if (needed == 0)
                {
                    restockStage = RestockStage.Closing; closeRequested = false;
                    Enter(RunState.Restocking, L.T("Fechando a loja e retomando a rota."), now);
                    return;
                }
                purchase = new(Math.Min(needed, GreensRestock.MaxBatch), w.GreensInventory.Count, w.GreensInventory.Gil, now);
                GreensGilCommitted += purchase.Cost;
                runtime.BuyGreens(purchase);
                break;
            case RestockStage.Closing:
                if (now - since > 20) { Stop(L.T("A loja não encerrou a interação a tempo."), true); return; }
                if (w.GreensShop is not (GreensShopState.None or GreensShopState.Closed))
                {
                    if (!closeRequested && w.GreensShop == GreensShopState.Ready) { runtime.CloseGreensShop(); closeRequested = true; }
                    // Opening can be in the NPC selector. Advance only the exact shop so it can be closed without buying.
                    if (w.GreensShop == GreensShopState.Opening) runtime.AdvanceGreensShop();
                    return;
                }
                if (w.Occupied || w.DialogueOpen || w.Casting) return;
                restockStage = RestockStage.ToRoute;
                goto case RestockStage.ToRoute;
            case RestockStage.ToRoute:
                if (PendingReturnGoal != ReturnGoal.None)
                { returning = true; Teleport(ReturnTarget, w, now); return; }
                Teleport(restockResume!, w, now);
                break;
        }
    }
}
