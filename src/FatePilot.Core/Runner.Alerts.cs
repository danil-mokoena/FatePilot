namespace FatePilot.Core;

// 0.8: notifications for important events and one bounded automatic retry after recoverable stops.
public sealed partial class Runner
{
    private int retriesUsed;
    private string? retryReason;
    public int RetriesUsed => retriesUsed;

    private bool AlertEnabled(NotifyKind kind) => kind switch
    {
        NotifyKind.Stopped => options.NotifyOnStop,
        NotifyKind.Goal => options.NotifyOnGoal,
        NotifyKind.PriorityFate => options.NotifyOnPriorityFate,
        NotifyKind.Problem => options.NotifyOnProblem,
        NotifyKind.Tell => options.NotifyOnTell,
        _ => false,
    };

    private void Alert(NotifyKind kind, string message)
    {
        if (!AlertEnabled(kind)) return;
        try { runtime.Notify(kind, message); }
        catch (Exception) { /* a failed sound or chat line must never affect the session */ }
    }

    // Recoverable interruptions (an unexpected dialog, loading screen, zone change or busy travel
    // module) release control, wait for the game to settle and resume — at most MaxRetries times.
    private bool TryRecover(string reason, double now)
    {
        // A purchase already sent must be reconciled, never abandoned: that one always stops.
        if (!options.AutoRetry || retriesUsed >= options.MaxRetries || purchase != null) return false;
        retriesUsed++;
        retryReason = reason;
        // Forget a half-done restock trip; the next selection starts it again if still needed.
        restockStage = RestockStage.None; closeRequested = false;
        nextInstance = 0;
        waitingForPlayerDialog = false;
        runtime.StopMovement();
        runtime.CancelEventInteraction();
        runtime.SetCombat(false);
        Selected = null;
        try { runtime.Suspend(); } catch (Exception) { }
        var message = L.T("Interrupção: {0} Tentando retomar ({1}/{2})…", reason, retriesUsed, options.MaxRetries);
        runtime.Log(message);
        Alert(NotifyKind.Problem, message);
        Enter(RunState.Retrying, message, now);
        return true;
    }

    private void StopOrRecover(string reason, double now)
    {
        if (!TryRecover(reason, now)) Stop(reason, true);
    }

    private void TickRetry(World w, double now)
    {
        if (now - since > 120) { Stop(L.T("Não foi possível retomar: {0}", retryReason ?? ""), true); return; }
        if (now - since < 10 || w.Loading || !w.LoggedIn || w.Occupied || w.DialogueOpen || w.InCombat || w.Casting || w.TravelBusy) return;
        if (w.Job is 0 or (>= 8 and <= 18)) return;
        try { runtime.ResumeSession(); }
        catch (Exception ex) { Stop(L.T("Não foi possível continuar: {0}", ex.GetBaseException().Message), true); return; }
        expectedTerritory = w.Territory;
        sessionJob = w.Job;
        visited.Clear();
        idleSince = noEnemySince = now;
        lastAction = now - 10;
        Enter(RunState.Selecting, L.T("Sessão retomada; procurando FATEs."), now);
        runtime.Log(Status);
    }
}
