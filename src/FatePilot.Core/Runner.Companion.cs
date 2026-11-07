namespace FatePilot.Core;

public sealed partial class Runner
{
    private bool companionPending, companionDisabled;
    private double companionDeadline, nextCompanionAttempt;
    public string? CompanionStatus { get; private set; }

    private void ResetCompanion()
    {
        companionPending = companionDisabled = false;
        companionDeadline = nextCompanionAttempt = double.NegativeInfinity;
        CompanionStatus = null;
    }

    private bool HasPriorityCompanionThreat(World w) => options.PrioritizeForlorn && Selected != null &&
        w.CurrentFate == Selected.Id && w.Enemies.Any(e => e.NameId is 6737 or 6738 &&
            (e.FateId == Selected.Id || (e.FateId == 0 && System.Numerics.Vector3.Distance(e.Position, Selected.Position) <= Selected.Radius)));

    private void ObserveCompanion(World w, double now)
    {
        if (!companionPending || w.Loading || !w.LoggedIn) return;
        if (w.CompanionSecondsLeft is > 0)
        {
            companionPending = false;
            GreensUsed++;
            CompanionStatus = L.T("Chocobo invocado; retomando a rota.");
            runtime.Log(CompanionStatus);
        }
        else if (now >= companionDeadline)
        {
            // A sent item action is not repeated without observing its result.
            // Resume farming, but disable further item use for this session.
            companionPending = false;
            companionDisabled = true;
            CompanionStatus = L.T("Invocação não confirmada. Uso de Greens suspenso nesta sessão; a rota continua.");
            runtime.Log(CompanionStatus);
            Alert(NotifyKind.Problem, CompanionStatus);
        }
    }

    private bool TrySummonCompanion(World w, double now)
    {
        if (!options.SummonChocobo || companionPending || companionDisabled || now < nextCompanionAttempt ||
            PendingReturnGoal != ReturnGoal.None || !w.LoggedIn || w.Loading || w.Dead || w.Restricted ||
            w.InCombat || w.Mounted || w.Flying || w.Casting || w.Occupied || w.DialogueOpen || w.TravelBusy ||
            w.OwnEventInteraction || w.EventInteractionPending || HasPriorityCompanionThreat(w) ||
            w.CompanionSecondsLeft is not 0 || !w.GreensInventory.Ready || w.GreensInventory.Count <= 0 || !w.CanSummonChocobo)
            return false;
        runtime.StopMovement(); runtime.SetCombat(false);
        nextCompanionAttempt = now + 60;
        if (!runtime.UseGysahlGreens())
        {
            CompanionStatus = L.T("O jogo recusou o uso de Gysahl Greens; a rota continua.");
            runtime.Log(CompanionStatus);
            return false;
        }
        companionPending = true;
        companionDeadline = now + 10;
        transitionUntil = companionDeadline;
        CompanionStatus = L.T("Usando Gysahl Greens; aguardando o chocobo.");
        Status = CompanionStatus;
        runtime.Log(CompanionStatus);
        return true;
    }
}
