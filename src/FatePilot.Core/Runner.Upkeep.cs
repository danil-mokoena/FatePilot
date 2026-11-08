namespace FatePilot.Core;

// 0.5: pause/resume, food, self-repair, per-level job queue and the end-of-session summary.
public sealed partial class Runner
{
    // ---------- Pause ----------
    private bool pauseRequested;
    private double pausedAt, pausedTotal;
    public bool PauseAfterFate { get; private set; }
    public bool IsPaused => State == RunState.Paused;

    // Pause as soon as it is safe (never mid-teleport, mid-gateway or mid-purchase).
    public void RequestPause() { if (IsRunning && !IsPaused) pauseRequested = true; }

    public void TogglePauseAfterFate() { if (IsRunning && !IsPaused) PauseAfterFate = !PauseAfterFate; }

    private bool SafeToPause => State is not (RunState.Teleporting or RunState.Gateway or RunState.InstanceChange
        or RunState.Restocking or RunState.SwitchingJob or RunState.Repairing or RunState.Paused or RunState.Retrying or RunState.ToTrader or RunState.Buying);

    private bool TryApplyPause(World w, double now)
    {
        var afterFate = PauseAfterFate && Selected == null && State is RunState.Selecting or RunState.Waiting;
        if (!(pauseRequested && SafeToPause) && !afterFate) return false;
        pauseRequested = PauseAfterFate = false;
        runtime.StopMovement();
        runtime.CancelEventInteraction();
        runtime.SetCombat(false);
        Selected = null;
        // Hand control back to the player: no movement preset, rotation lease or path while paused.
        runtime.Suspend();
        pausedAt = now;
        Enter(RunState.Paused, L.T("Sessão pausada. Os contadores e a rota foram mantidos."), now);
        runtime.Log(Status);
        return true;
    }

    public void Resume(World w, double now)
    {
        if (!IsPaused) return;
        if (!w.LoggedIn || w.Loading || w.Dead || w.Restricted || w.InCombat || w.Occupied || w.DialogueOpen || w.Casting)
        { Status = L.T("Para continuar, fique fora de combate e com os diálogos fechados."); return; }
        if (w.Job is 0 or (>= 8 and <= 18))
        { Status = L.T("Troque para uma classe de combate antes de continuar."); return; }
        try { runtime.ResumeSession(); }
        catch (Exception ex) { Stop(L.T("Não foi possível continuar: {0}", ex.GetBaseException().Message), true); return; }
        pausedTotal += now - pausedAt;
        // The player may have moved, changed zone or job while paused: adopt the current state.
        expectedTerritory = w.Territory;
        sessionJob = w.Job;
        visited.Clear();
        idleSince = noEnemySince = now;
        lastAction = now - 10;
        Enter(RunState.Selecting, L.T("Sessão retomada; procurando FATEs."), now);
        runtime.Log(Status);
    }

    // ---------- Food ----------
    private bool foodPending, foodDisabled;
    private double foodDeadline, nextFoodAttempt;
    private float foodBefore;
    public int FoodUsed { get; private set; }
    public int GreensUsed { get; private set; }

    private void ObserveFood(World w, double now)
    {
        if (!foodPending || w.Loading || !w.LoggedIn) return;
        if (w.FoodSecondsLeft is { } left && left > foodBefore + 30)
        {
            foodPending = false; FoodUsed++;
            runtime.Log(L.T("Comida renovada ({0} min).", (int)(left / 60)));
        }
        else if (now >= foodDeadline)
        {
            foodPending = false; foodDisabled = true;
            runtime.Log(L.T("Uso de comida não confirmado; suspenso nesta sessão. A rota continua."));
            Alert(NotifyKind.Problem, L.T("Uso de comida não confirmado; suspenso nesta sessão. A rota continua."));
        }
    }

    private bool TryEatFood(World w, double now)
    {
        if (!options.UseFood || options.FoodItemId == 0 || foodPending || foodDisabled || now < nextFoodAttempt ||
            PendingReturnGoal != ReturnGoal.None || w.InCombat || w.Mounted || w.Flying || w.Casting || w.Occupied ||
            w.DialogueOpen || w.TravelBusy || w.FoodSecondsLeft is not { } left || left >= options.FoodRefreshMinutes * 60 ||
            w.FoodCount <= 0 || !w.CanUseFood)
            return false;
        runtime.StopMovement(); runtime.SetCombat(false);
        nextFoodAttempt = now + 60;
        if (!runtime.UseFood(options.FoodItemId, options.FoodHq))
        { runtime.Log(L.T("O jogo recusou a comida; nova tentativa em 1 minuto.")); return false; }
        foodPending = true; foodBefore = left; foodDeadline = now + 8; transitionUntil = foodDeadline;
        Status = L.T("Comendo para renovar o bônus de comida.");
        return true;
    }

    // ---------- Self-repair ----------
    private bool repairDisabled;
    private int durabilityBefore, repairRefusals;
    private double nextRepairAttempt;
    public int Repairs { get; private set; }
    private const int RepairRefusalLimit = 3;

    private bool SelfRepairActive => options.SelfRepair && !repairDisabled;

    private bool TryRepair(World w, double now)
    {
        if (!SelfRepairActive || w.MinDurability < 0 || w.MinDurability >= options.RepairBelowPercent || PendingReturnGoal != ReturnGoal.None ||
            w.InCombat || w.Mounted || w.Flying || w.Casting || w.Occupied || w.DialogueOpen || w.TravelBusy || w.RepairBusy || now < nextRepairAttempt)
            return false;
        // The game can refuse the Repair action for a moment (animation lock, just after combat):
        // try again a minute later and give up for the session only after a few refusals.
        if (!w.CanSelfRepair) { RepairRefused(L.T("Reparo próprio indisponível (Dark Matter ou nível de artesão). Desativado nesta sessão."), now); return false; }
        runtime.StopMovement(); runtime.SetCombat(false);
        if (!runtime.StartRepair()) { RepairRefused(L.T("O jogo recusou a janela de reparo. Reparo desativado nesta sessão."), now); return false; }
        repairRefusals = 0;
        durabilityBefore = w.MinDurability;
        Enter(RunState.Repairing, L.T("Reparando o equipamento com Dark Matter."), now);
        return true;
    }

    private void RepairRefused(string finalMessage, double now)
    {
        nextRepairAttempt = now + 60;
        if (++repairRefusals < RepairRefusalLimit) { runtime.Log(L.T("Reparo indisponível agora; nova tentativa em 1 minuto.")); return; }
        repairDisabled = true;
        runtime.Log(finalMessage);
        Alert(NotifyKind.Problem, finalMessage);
    }

    private void TickRepair(World w, double now)
    {
        if (w.InCombat) { Enter(RunState.Selecting, L.T("Reparo interrompido por combate."), now); return; }
        if (w.RepairBusy && now - since < 20) return;
        if (w.MinDurability > durabilityBefore)
        { Repairs++; runtime.Log(L.T("Equipamento reparado: {0}% → {1}%.", durabilityBefore, w.MinDurability)); }
        else
        { repairDisabled = true; runtime.Log(L.T("Reparo não confirmado. Reparo próprio desativado nesta sessão.")); Alert(NotifyKind.Problem, L.T("Reparo não confirmado. Reparo próprio desativado nesta sessão.")); }
        Enter(RunState.Selecting, L.T("Procurando FATEs elegíveis."), now);
    }

    // ---------- Job queue ----------
    private uint expectedJob;
    public int JobSwitches { get; private set; }

    // Between FATEs: if the current job reached its target, equip the next gearset in the queue.
    private bool TryAdvanceJobQueue(World w, double now)
    {
        if (!options.UseJobQueue || options.JobQueue.Count == 0 || PendingReturnGoal != ReturnGoal.None ||
            w.InCombat || w.Mounted || w.Flying || w.Casting || w.Occupied || w.DialogueOpen || w.TravelBusy) return false;
        var queue = options.JobQueue;
        var index = queue.FindIndex(j => j.Job == w.Job);
        JobGoal? next;
        if (index < 0) next = queue[0];                                    // not on a queued job yet: start the queue
        else if (w.Level < queue[index].TargetLevel) return false;         // keep levelling this one
        else if (index + 1 < queue.Count) next = queue[index + 1];
        else
        {
            PendingReturnGoal = ReturnGoal.JobQueueDone;
            runtime.Log(L.T("{0} atingida. Retorno após o FATE em andamento.", SessionGoals.Label(PendingReturnGoal)));
            return false;
        }
        if (next.Job == w.Job) return false;
        runtime.StopMovement(); runtime.SetCombat(false);
        runtime.Suspend();
        if (!runtime.EquipGearset(next.Gearset))
        { Stop(L.T("O jogo recusou o gearset {0}.", next.Name), true); return true; }
        expectedJob = next.Job;
        Enter(RunState.SwitchingJob, L.T("Trocando para {0} (meta: nível {1}).", next.Name, next.TargetLevel), now);
        runtime.Log(Status);
        return true;
    }

    private void TickSwitchJob(World w, double now)
    {
        if (now - since > 20) { Stop(L.T("A troca de classe não foi confirmada em 20 segundos."), true); return; }
        if (w.Job != expectedJob || w.Casting || w.Occupied || w.Loading) return;
        try { runtime.ResumeSession(); }
        catch (Exception ex) { Stop(L.T("Não foi possível preparar a nova classe: {0}", ex.GetBaseException().Message), true); return; }
        sessionJob = w.Job;
        JobSwitches++;
        Enter(RunState.Selecting, L.T("Classe trocada; procurando FATEs."), now);
    }

    // ---------- Summary ----------
    public SessionSummary? LastSummary { get; private set; }

    private void CaptureSummary(string reason) =>
        LastSummary = new(Completed, Unconfirmed, GemstonesEarned, ElapsedSeconds, GreensGilCommitted, GreensUsed, FoodUsed, Repairs, JobSwitches, reason, DateTime.Now);

    private void ResetUpkeep()
    {
        pauseRequested = PauseAfterFate = false;
        pausedAt = pausedTotal = 0;
        foodPending = foodDisabled = repairDisabled = false;
        nextFoodAttempt = nextRepairAttempt = double.NegativeInfinity;
        repairRefusals = 0;
        FoodUsed = GreensUsed = Repairs = JobSwitches = 0;
        expectedJob = 0;
    }
}
