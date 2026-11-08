using System.Numerics;
using System.Text.Json;

namespace FatePilot.Core;

// The loop is independent of FFXIV. Tick consumes a snapshot and emits small, bounded actions.
public sealed partial class Runner(IRuntime runtime)
{
    public RunState State { get; private set; } = RunState.Stopped;
    public string Status { get; private set; } = L.T("Pronto para configurar.");
    public int Completed { get; private set; }
    public int Unconfirmed { get; private set; }
    public int GemstonesEarned { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public ReturnGoal PendingReturnGoal { get; private set; }
    private double sessionStarted;
    private int previousGemstones;
    public FateInfo? Selected { get; private set; }
    public bool IsRunning => State is not (RunState.Stopped or RunState.Finished or RunState.Faulted);

    private RunOptions options = new();
    private readonly Dictionary<FateKey, double> excludedUntil = [];
    private readonly HashSet<int> visited = [];
    private double since, lastAction = -100, lastMotion, idleSince, noEnemySince;
    private double transitionUntil = -1;
    private Vector3 lastPosition;
    private uint sessionJob;
    private uint expectedTerritory;
    private FateKey selectedKey;
    private bool participated, sawLoading, returning, waitingForPlayerDialog;
    private int nextInstance;
    private Destination? teleportDestination;
    // 0.7: local per-zone history shared with the plugin configuration.
    public ZoneHistory History { get; set; } = new();
    private readonly HashSet<FateKey> seenFates = [];
    private readonly Dictionary<(uint, int, ushort), double> lastAppearance = [];
    private double lastHistoryTick, zoneSince;
    private bool idleMoved;

    public void Start(RunOptions config, World world, double now)
    {
        if (IsRunning) return;
        try
        {
            options = JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(config))!;
            options.Validate();
            if (options.AvoidGemstoneCap && (world.GemstoneCap <= 0 || options.GemstoneCapReserve >= world.GemstoneCap))
                throw new InvalidOperationException(L.T("O limite de gemstones não está disponível ou a reserva é maior que o limite."));
            if (!world.LoggedIn || world.Loading || world.Dead || world.Restricted || world.InCombat || world.Occupied || world.DialogueOpen || world.Casting)
                throw new InvalidOperationException(L.T("Inicie em mundo aberto, vivo, fora de combate e com os diálogos fechados."));
            // Crafters (8-15) and gatherers (16-18) have no combat rotation; the ability
            // module rejects them with an opaque reflection error.
            if (world.Job is 0 or (>= 8 and <= 18))
                throw new InvalidOperationException(L.T("Troque para uma classe de combate antes de iniciar."));
            runtime.Begin(options);
            Completed = Unconfirmed = 0;
            GemstonesEarned = 0;
            ElapsedSeconds = 0;
            PendingReturnGoal = ReturnGoal.None;
            sessionStarted = now;
            ResetRestock();
            ResetCompanion();
            ResetUpkeep();
            pendingOptions = null; OptionsError = null;
            previousGemstones = world.Gemstones;
            Selected = null;
            visited.Clear();
            excludedUntil.Clear();
            nextInstance = 0;
            returning = participated = false;
            expectedTerritory = world.Territory;
            sessionJob = world.Job;
            lastAction = now - 10;
            transitionUntil = -1;
            idleSince = noEnemySince = now;
            seenFates.Clear(); lastAppearance.Clear(); lastHistoryTick = zoneSince = now; idleMoved = false;
            retriesUsed = 0; retryReason = null;
            Enter(RunState.Selecting, L.T("Procurando FATEs elegíveis."), now);
        }
        catch (Exception ex) { Stop(L.T("Não foi possível iniciar: {0}", ex.GetBaseException().Message), true); }
    }

    public void Stop(string? reason = null, bool fault = false)
    {
        reason ??= L.T("Parado pelo usuário.");
        var wasRunning = IsRunning;
        State = fault ? RunState.Faulted : RunState.Stopped;
        Status = reason;
        try { runtime.End(); }
        catch (Exception ex) { Status += L.T(" Restauração: {0}", ex.Message); State = RunState.Faulted; }
        Selected = null;
        CaptureSummary(Status);
        runtime.Log(Status);
        if (wasRunning && reason != L.T("Parado pelo usuário.")) Alert(NotifyKind.Stopped, L.T("Sessão parada: {0}", Status));
    }

    public void Tick(World w, double now)
    {
        if (!IsRunning) return;
        try
        {
            if (State != RunState.Paused) ElapsedSeconds = Math.Max(ElapsedSeconds, now - sessionStarted - pausedTotal);
            if (w.LoggedIn && !w.Loading)
            {
                var gained = Math.Max(0, w.Gemstones - previousGemstones);
                GemstonesEarned += gained;
                if (gained > 0 && State != RunState.Paused) History.For(w.Territory).Gems += gained;
                previousGemstones = w.Gemstones;
            }
            if (!w.LoggedIn && !w.Loading) { Stop(L.T("Sessão encerrada: personagem desconectado."), true); return; }
            if (w.Dead) { Stop(L.T("Personagem derrotado. Resolva a recuperação e inicie novamente."), true); return; }
            if (w.Restricted) { Stop(L.T("Automação encerrada ao entrar em conteúdo instanciado/PvP."), true); return; }
            if (w.Job != 0 && w.Job != sessionJob && State is not (RunState.Paused or RunState.SwitchingJob or RunState.Retrying)) { Stop(L.T("A classe mudou durante a sessão."), true); return; }
            if (State == RunState.Paused) { lastHistoryTick = now; return; }
            if (State == RunState.Retrying) { lastHistoryTick = now; TickRetry(w, now); return; }
            RecordHistory(w, now);
            ObserveCompanion(w, now);
            ObserveFood(w, now);
            ApplyPendingOptions(w);
            if (w.LoggedIn && !w.Loading && options.AvoidGemstoneCap && w.GemstoneCap <= 0)
            { Stop(L.T("Não foi possível consultar o limite de gemstones."), true); return; }
            if (PendingReturnGoal == ReturnGoal.None)
            {
                PendingReturnGoal = SessionGoals.Evaluate(options, Completed, GemstonesEarned, ElapsedSeconds, w.Gemstones, w.GemstoneCap,
                    w.FreeBagSlots, SelfRepairActive ? -1 : w.MinDurability);
                if (PendingReturnGoal != ReturnGoal.None)
                {
                    var goal = L.T("{0} atingida. Retorno após o FATE em andamento.", SessionGoals.Label(PendingReturnGoal));
                    runtime.Log(goal);
                    Alert(NotifyKind.Goal, goal);
                }
            }

            if (State == RunState.Teleporting) { TickTeleport(w, now); return; }
            if (State == RunState.Gateway) { TickGateway(w, now); return; }
            if (State == RunState.SwitchingJob) { TickSwitchJob(w, now); return; }
            if (State == RunState.InstanceChange) { TickInstance(w, now); return; }
            if (w.Loading) { StopOrRecover(L.T("Transição de área não solicitada pelo FatePilot."), now); return; }
            if (w.Territory != expectedTerritory) { StopOrRecover(L.T("A zona mudou fora da rota do FatePilot."), now); return; }
            if (w.TravelBusy) { StopOrRecover(L.T("módulo de viagens iniciou outra tarefa durante a sessão."), now); return; }
            if (options.ExcludeGroupBosses && w.Enemies.Any(e => e.TargetingPlayer && FateRules.IsGroupBoss(e.FateId)))
            { Stop(L.T("Um chefe de grupo entrou em combate com você. Sessão interrompida."), true); return; }
            if (State == RunState.Restocking) { TickRestock(w, now); return; }
            if (State == RunState.Repairing) { TickRepair(w, now); return; }
            if (w.EventInteractionError != null) { StopOrRecover(w.EventInteractionError, now); return; }
            if (Selected != null && (w.OwnEventInteraction || w.EventInteractionPending))
            {
                runtime.StopMovement();
                runtime.SetCombat(false);
                if (w.OwnEventInteraction) runtime.AdvanceEventInteraction();
                return;
            }
            if (w.Occupied || w.DialogueOpen)
            {
                // Our own mount/dismount can briefly set Occupied without a dialogue.
                // Wait only inside the action's deadline and never click during it.
                if (!w.DialogueOpen && now < transitionUntil)
                { runtime.StopMovement(); runtime.SetCombat(false); return; }
                // A plain prompt the player opened (discard item, confirmations) with no NPC
                // event behind it: wait for it to close, never click it, then carry on.
                if (w.DialogueOpen && !w.Occupied)
                {
                    if (!waitingForPlayerDialog) { waitingForPlayerDialog = true; runtime.Log(L.T("Janela aberta pelo jogador; aguardando ela fechar.")); }
                    return;
                }
                runtime.Log($"Interaction stopped in {State}: {w.InteractionDiagnostic ?? "no native details"}");
                StopOrRecover(L.T("Um diálogo ou outra interação interrompeu a sessão."), now); return;
            }
            waitingForPlayerDialog = false;
            if (w.NavError != null)
            {
                // A failed path while idling (hotspot) is not worth a FATE skip or a stop: just keep waiting here.
                if (Selected == null && State == RunState.Waiting)
                { runtime.Log(L.T("Navegação: {0}", w.NavError)); runtime.StopMovement(); idleMoved = true; return; }
                SkipOrFail(L.T("Navegação: {0}", w.NavError), now); return;
            }
            if (HandleUnexpectedCombat(w, now)) return;
            if (companionPending && !w.InCombat && State is (RunState.Selecting or RunState.Waiting or RunState.Landing) && !HasPriorityCompanionThreat(w))
            { runtime.StopMovement(); runtime.SetCombat(false); return; }
            if (TryApplyPause(w, now)) return;
            if (PendingReturnGoal != ReturnGoal.None && State == RunState.HubApproach)
            { nextInstance = 0; runtime.StopMovement(); Enter(RunState.Selecting, SessionGoals.Label(PendingReturnGoal), now); }

            switch (State)
            {
                case RunState.Selecting:
                case RunState.Waiting: Select(w, now); break;
                case RunState.Mounting: TickMount(w, now); break;
                case RunState.Moving: TickMove(w, now); break;
                case RunState.Landing: TickLanding(w, now); break;
                case RunState.Syncing: TickSync(w, now); break;
                case RunState.Fighting: TickFight(w, now); break;
                case RunState.StartingEvent: TickStartEvent(w, now); break;
                case RunState.HubApproach: TickHub(w, now); break;
            }
        }
        catch (Exception ex) { runtime.Log($"{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); Stop(ex.GetBaseException().Message, true); }
    }

    private void Select(World w, double now)
    {
        if (w.InCombat)
        {
            var threat = w.Enemies.Where(e => e.TargetingPlayer).MinBy(e => Vector3.Distance(w.Position, e.Position));
            if (threat != null) { runtime.Target(threat.Id); runtime.SetCombat(true); }
            if (now - since > 120) Stop(L.T("Não foi possível sair do combate para continuar a rota."), true);
            return;
        }
        runtime.SetCombat(false);
        if (PendingReturnGoal != ReturnGoal.None)
        {
            returning = true;
            Teleport(ReturnTarget, w, now);
            return;
        }
        if (TryRestock(w, now)) return;
        if (TryRepair(w, now)) return;
        if (TryAdvanceJobQueue(w, now)) return;
        if (TryEatFood(w, now)) return;
        // Zone time budget: move on to the next route zone after N minutes, between FATEs.
        if (options.MinutesPerZone > 0 && now - zoneSince >= options.MinutesPerZone * 60 &&
            Selection.NextZone(options.Route, w.Territory) is { } budgetZone)
        {
            runtime.Log(L.T("{0} min nesta zona; seguindo para {1}.", options.MinutesPerZone, budgetZone.Name));
            nextInstance = 0;
            Teleport(budgetZone, w, now);
            return;
        }
        if (options.Route.Count > 0 && options.Route.All(d => d.TerritoryId != w.Territory))
        {
            Teleport(options.Route[0], w, now);
            return;
        }
        if (TrySummonCompanion(w, now)) return;
        if (!w.NavReady)
        {
            Note(L.T("Esperando o módulo de navegação carregar o mapa desta zona…"));
            if (now - since > 180) Stop(L.T("módulo de navegação não carregou a malha desta zona em 180 segundos."), true);
            return;
        }
        var fate = Selection.Choose(w, options, excludedUntil, now);
        if (fate != null)
        {
            Selected = fate;
            idleMoved = false;
            selectedKey = Selection.Key(fate, w);
            participated = false;
            ResetObjectives(fate, now);
            noEnemySince = now;
            Enter(RunState.Mounting, L.T("Preparando deslocamento: {0}", fate.Name), now);
            return;
        }
        if (State != RunState.Waiting) { idleSince = now; Enter(RunState.Waiting, L.T("Aguardando FATEs elegíveis."), now); }
        if (TryIdleToHotspot(w, now)) return;
        if (now - idleSince < options.IdleSeconds) return;
        visited.Add(w.Instance);
        var instance = options.RotateInstances ? Selection.NextInstance(w.Instance, w.InstanceCount, visited) : null;
        if (instance.HasValue)
        {
            var hub = options.Route.FirstOrDefault(d => d.TerritoryId == w.Territory);
            if (hub != null)
            {
                nextInstance = instance.Value;
                Teleport(hub, w, now);
                return;
            }
            runtime.Log(L.T("Troca de instância exige adicionar o aetheryte desta zona à rota."));
        }
        var zone = Selection.NextZone(options.Route, w.Territory);
        if (zone != null) { nextInstance = 0; Teleport(zone, w, now); return; }
        visited.Clear();
        idleSince = now;
        if (options.RotateInstances && w.Instance > 0 && w.InstanceCount == 0)
            runtime.Log(L.T("módulo de viagens ainda não conhece as instâncias. Abra uma vez o menu de troca no aetheryte."));
    }

    private FateInfo? RefreshFate(World w, double now)
    {
        var current = w.Fates.FirstOrDefault(f => f.Id == Selected?.Id && (f.StartedAt == selectedKey.StartedAt || Selected.State == EventState.Preparing));
        if (current != null && Selected?.State == EventState.Preparing && current.State == EventState.Running)
            selectedKey = Selection.Key(current, w);
        if (current?.EffectiveKind == FateKind.Collection) ObserveCollectionProgress(current, now);
        var canStillHandIn = current?.EffectiveKind == FateKind.Collection && current.HeldItems > 0 && current.State is EventState.Running or EventState.Ending;
        if (current == null || current.State is EventState.Ended or EventState.Failed || (current.Progress >= 100 && !canStillHandIn))
        {
            runtime.StopMovement();
            runtime.CancelEventInteraction();
            runtime.SetCombat(false);
            if (participated && current != null && (current.State == EventState.Ended || (current.Progress >= 100 && current.State != EventState.Failed)))
            {
                Completed++;
                History.For(expectedTerritory).Fates++;
                runtime.Log(L.T("FATE concluído observado: {0}. Total: {1}.", Selected?.Name, Completed));
            }
            else if (participated) { Unconfirmed++; History.For(expectedTerritory).Failed++; runtime.Log(L.T("FATE terminou/desapareceu sem confirmação de sucesso; não conta para a meta.")); }
            excludedUntil[selectedKey] = now + 300;
            Selected = null;
            Enter(RunState.Selecting, L.T("Verificando o próximo FATE."), now);
            return null;
        }
        Selected = current;
        return current;
    }

    private void TickMount(World w, double now)
    {
        var f = RefreshFate(w, now);
        if (f == null) return;
        if (w.InCombat) { Enter(RunState.Landing, L.T("Preparando combate durante o deslocamento."), now); return; }
        if (options.UseMount && Vector3.Distance(w.Position, f.Position) > 40 && !w.Mounted && now - since < 10)
        {
            if (!w.Casting && Throttle(now, 3)) { transitionUntil = now + 5; runtime.Mount(); }
            return;
        }
        if (w.Casting) return;
        runtime.Move(f.Position, options.UseFlight && w.CanFly && w.Mounted);
        lastPosition = w.Position;
        lastMotion = now;
        Enter(RunState.Moving, L.T("Indo para {0}", f.Name), now);
    }

    private void TickMove(World w, double now)
    {
        var f = RefreshFate(w, now);
        if (f == null) return;
        var close = Vector3.Distance(w.Position, f.Position) < 8
            || (w.CurrentFate == f.Id && MathF.Abs(w.Position.Y - f.Position.Y) < 5 && Vector2.Distance(new(w.Position.X, w.Position.Z), new(f.Position.X, f.Position.Z)) < f.Radius * 0.6f);
        if (close || w.InCombat)
        {
            runtime.StopMovement();
            Enter(RunState.Landing, L.T("Pousando e desmontando."), now);
            return;
        }
        if (Vector3.Distance(w.Position, lastPosition) > 2) { lastPosition = w.Position; lastMotion = now; }
        if (now - lastMotion > 25 || now - since > 240 || (!w.NavBusy && now - since > 3))
            SkipOrFail(L.T("Rota interrompida, sem progresso ou sem ponto alcançável."), now);
    }

    private void TickLanding(World w, double now)
    {
        var f = RefreshFate(w, now);
        if (f == null) return;
        if (w.Mounted || w.Flying)
        {
            if (Throttle(now, 2)) { transitionUntil = now + 5; runtime.Dismount(); }
            if (now - since > 25) SkipOrFail(L.T("Não foi possível pousar/desmontar."), now);
            return;
        }
        if (w.Casting) return;
        if (TrySummonCompanion(w, now)) return;
        Enter(f.State == EventState.Preparing ? RunState.StartingEvent : RunState.Syncing,
            f.State == EventState.Preparing ? L.T("Iniciando o FATE pelo NPC.") : L.T("Verificando sincronização de nível."), now);
    }

    private void TickSync(World w, double now)
    {
        var f = RefreshFate(w, now);
        if (f == null) return;
        if (f.State == EventState.Preparing) { Enter(RunState.StartingEvent, L.T("Iniciando o FATE pelo NPC."), now); return; }
        if (w.CurrentFate != f.Id)
        {
            runtime.Move(f.Position, false);
            lastPosition = w.Position; lastMotion = now;
            Enter(RunState.Moving, L.T("Entrando no limite do FATE."), now);
            return;
        }
        if (w.Level > f.MaxLevel && !w.Synced)
        {
            if (Throttle(now, 2)) runtime.Sync();
            if (now - since > 15) SkipOrFail(L.T("Sincronização de nível não confirmada."), now);
            return;
        }
        noEnemySince = now;
        Enter(RunState.Fighting, L.T("Participando de {0}", f.Name), now);
    }

    private void TickFight(World w, double now)
    {
        if (w.InCombat && Selected?.Id == w.CurrentFate) participated = true;
        var f = RefreshFate(w, now);
        if (f == null) return;
        var finalHandIn = f.EffectiveKind == FateKind.Collection && f.State == EventState.Ending && f.HeldItems > 0;
        if (w.CurrentFate != f.Id && !finalHandIn)
        {
            runtime.SetCombat(false);
            Enter(RunState.Syncing, L.T("Retornando ao limite do FATE."), now);
            return;
        }
        if (now - since > 1200) { Stop(L.T("Tempo máximo de combate excedido."), true); return; }
        if (TryPriorityEnemy(w, f, now)) return;
        Status = L.T("Participando de {0}", f.Name);
        if (f.EffectiveKind is FateKind.Collection or FateKind.Escort or FateKind.Defense)
        {
            TickObjective(w, f, now);
            return;
        }
        var enemy = w.Enemies.Where(e => e.FateId == f.Id || e.TargetingPlayer)
            .OrderByDescending(e => e.TargetingPlayer)
            .ThenBy(e => Vector3.Distance(w.Position, e.Position)).FirstOrDefault();
        if (enemy != null)
        {
            noEnemySince = now;
            runtime.Target(enemy.Id);
            runtime.SetCombat(true);
        }
        else
        {
            runtime.SetCombat(false);
            if (now - noEnemySince > 60) SkipOrFail(L.T("Nenhum inimigo elegível apareceu em 60 segundos."), now);
        }
    }

    // Gemstone-driven returns go to the chosen gemstone trader city when one is set.
    private Destination ReturnTarget => PendingReturnGoal is ReturnGoal.Gemstones or ReturnGoal.CurrencyCap && options.GemstoneDestination != null
        ? options.GemstoneDestination : options.ReturnDestination!;

    private void Teleport(Destination destination, World w, double now)
    {
        // The game teleports mounted or flying, so no dismount. Any wait is shown, never silent.
        if (w.InCombat) { Note(L.T("Esperando sair do combate para teleportar.")); return; }
        if (w.Casting || w.Occupied) { Note(L.T("Esperando a ação atual terminar para teleportar.")); return; }
        runtime.StopMovement();
        runtime.CancelEventInteraction();
        runtime.SetCombat(false);
        teleportDestination = destination;
        sawLoading = false;
        runtime.Teleport(destination);
        Enter(RunState.Teleporting, L.T("Teleportando para {0}.", destination.Name), now);
    }

    private void TickTeleport(World w, double now)
    {
        sawLoading |= w.Loading;
        if (now - since > 90) { Stop(L.T("Teleporte não confirmado em 90 segundos (gil, desbloqueio ou cancelamento)."), true); return; }
        if (w.Loading || !w.LoggedIn || w.Casting || w.Occupied || w.TravelBusy || !sawLoading || w.Territory != Gateways.ArrivalTerritory(teleportDestination!)) return;
        expectedTerritory = w.Territory;
        runtime.ConfirmTeleport();
        // Zones without an aetheryte: we landed in the neighbouring city; continue through its aethernet shard.
        if (!returning && Gateways.For(teleportDestination) is { } gate && w.Territory == gate.ViaTerritory && gate.Territory != w.Territory)
        {
            gatewayAttempts = 0;
            Enter(RunState.Gateway, L.T("Seguindo pelo aethernet até {0}.", teleportDestination!.Name), now);
            return;
        }
        Arrived(w, now);
    }

    private int gatewayAttempts;

    private void TickGateway(World w, double now)
    {
        var gate = Gateways.For(teleportDestination)!;
        if (now - since > 60)
        { Stop(L.T("Não foi possível usar o aethernet para chegar a {0}. Sintonize o portão de aethernet e tente de novo.", teleportDestination!.Name), true); return; }
        if (w.Loading || !w.LoggedIn || w.Occupied || w.Casting) return;
        if (w.Territory == gate.Territory)
        {
            if (w.TravelBusy) return;
            expectedTerritory = w.Territory;
            Arrived(w, now);
            return;
        }
        if (w.Territory != gate.ViaTerritory)
        {
            if (!w.TravelBusy) Stop(L.T("A viagem pelo aethernet terminou em uma zona inesperada."), true);
            return;
        }
        // Ask again only if the travel module went idle without moving us (refused or interrupted).
        if (!w.TravelBusy && gatewayAttempts < 3 && Throttle(now, 12))
        {
            gatewayAttempts++;
            runtime.AethernetTeleport(gate.ShardId);
        }
    }

    private void Arrived(World w, double now)
    {
        zoneSince = now; idleMoved = false;
        if (returning)
        {
            runtime.End();
            State = RunState.Finished;
            Status = L.T("Retorno concluído: {0}.", SessionGoals.Label(PendingReturnGoal));
            CaptureSummary(Status);
            runtime.Log(Status);
            Alert(NotifyKind.Goal, Status);
            return;
        }
        if (restockStage == RestockStage.ToVendor)
        {
            restockStage = RestockStage.Approach; lastMotion = now; lastPosition = w.Position;
            Enter(RunState.Restocking, L.T("Aproximando do vendedor de Gysahl Greens."), now);
            return;
        }
        if (restockStage == RestockStage.ToRoute)
        {
            restockStage = RestockStage.None;
            nextInstance = restockInstance > 0 && restockInstance != w.Instance && restockInstance <= w.InstanceCount ? restockInstance : 0;
        }
        if (PendingReturnGoal != ReturnGoal.None) nextInstance = 0;
        if (nextInstance > 0)
        {
            lastPosition = w.Position; lastMotion = now;
            Enter(RunState.HubApproach, L.T("Aproximando do aetheryte para a instância {0}.", nextInstance), now);
        }
        else
        {
            visited.Clear();
            Enter(RunState.Selecting, L.T("Zona carregada; procurando FATEs."), now);
        }
    }

    private void TickHub(World w, double now)
    {
        if (now - since > 75) { Stop(L.T("Não foi possível trocar de instância. Verifique o aetheryte e o Instance Switcher do módulo de viagens."), true); return; }
        if (w.CanChangeInstance)
        {
            runtime.StopMovement();
            runtime.ChangeInstance(nextInstance);
            Enter(RunState.InstanceChange, L.T("Mudando para a instância {0}.", nextInstance), now);
            return;
        }
        if (w.NearbyAetheryte is { } hub && Vector3.Distance(w.Position, hub) > 8 && w.NavReady && !w.NavBusy)
            runtime.Move(hub, false);
    }

    private void TickInstance(World w, double now)
    {
        if (now - since > 90) { Stop(L.T("módulo de viagens não confirmou a troca de instância em 90 segundos."), true); return; }
        if (w.Loading || !w.LoggedIn || w.TravelBusy || w.Occupied || w.Instance != nextInstance) return;
        if (w.Territory != expectedTerritory) { Stop(L.T("Troca de instância terminou em uma zona inesperada."), true); return; }
        nextInstance = 0;
        Enter(RunState.Selecting, L.T("Instância confirmada; procurando FATEs."), now);
    }

    // Time spent and where FATEs appear, per zone (paused time and loading screens excluded).
    private void RecordHistory(World w, double now)
    {
        var dt = Math.Clamp(now - lastHistoryTick, 0, 2);
        lastHistoryTick = now;
        if (w.Loading || !w.LoggedIn || w.Territory == 0) return;
        History.For(w.Territory).Seconds += dt;
        foreach (var f in w.Fates)
        {
            if (f.TerritoryId != w.Territory || !seenFates.Add(Selection.Key(f, w))) continue;
            // A FATE gets a new key when it goes from "preparing" to "running"; count it once per appearance.
            var appearance = (w.Territory, w.Instance, f.Id);
            if (lastAppearance.TryGetValue(appearance, out var seenAt) && now - seenAt < 900) continue;
            lastAppearance[appearance] = now;
            History.RecordSpot(w.Territory, f.Position);
            if (options.PriorityFates.Contains(f.Id)) Alert(NotifyKind.PriorityFate, L.T("FATE priorizado apareceu: {0}", f.Name));
        }
    }

    // While waiting with nothing eligible, fly once to where FATEs most often appear in this zone.
    private bool TryIdleToHotspot(World w, double now)
    {
        if (!options.IdleToHotspot || idleMoved || PendingReturnGoal != ReturnGoal.None || w.InCombat || w.Casting ||
            !w.NavReady || w.NavBusy || History.Hotspot(w.Territory) is not { } spot || System.Numerics.Vector3.Distance(w.Position, spot) < 60)
            return false;
        // Try to mount for a few seconds only; if the game refuses (swimming, no mount), walk instead.
        if (options.UseMount && !w.Mounted && now - since < 8)
        {
            if (Throttle(now, 3)) { transitionUntil = now + 5; runtime.Mount(); }
            return true;
        }
        idleMoved = true; // one attempt per wait, whatever happens next
        try { runtime.Move(spot, options.UseFlight && w.CanFly && w.Mounted); }
        catch (Exception ex) { runtime.Log(L.T("Navegação: {0}", ex.Message)); return false; }
        Status = L.T("Aguardando FATEs no ponto onde mais aparecem nesta zona.");
        return true;
    }

    // User-requested skip of the current FATE (5-minute ignore, then pick the next one).
    public void Skip(double now)
    {
        if (!IsRunning || Selected == null || State is RunState.Teleporting or RunState.Gateway or RunState.InstanceChange or RunState.Retrying) return;
        SkipOrFail(L.T("Pulado pelo usuário."), now);
    }

    private void SkipOrFail(string reason, double now)
    {
        if (Selected == null) { Stop(reason, true); return; }
        runtime.Log(L.T("Ignorando {0} por 5 minutos: {1}", Selected.Name, reason));
        excludedUntil[selectedKey] = now + 300;
        Selected = null;
        runtime.CancelEventInteraction();
        runtime.StopMovement();
        runtime.SetCombat(false);
        Enter(RunState.Selecting, reason, now);
    }

    // A visible reason for a wait: status always, log only when it changes.
    private void Note(string message)
    {
        if (Status == message) return;
        Status = message;
        runtime.Log(message);
    }

    private bool Throttle(double now, double seconds)
    {
        if (now - lastAction < seconds) return false;
        lastAction = now;
        return true;
    }

    private void Enter(RunState state, string message, double now)
    {
        State = state;
        Status = message;
        since = now;
        runtime.Log(message);
    }
}
