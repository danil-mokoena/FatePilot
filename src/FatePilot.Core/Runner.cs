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
    private int previousGemstones;
    public FateInfo? Selected { get; private set; }
    public bool IsRunning => State is not (RunState.Stopped or RunState.Finished or RunState.Faulted);

    private RunOptions options = new();
    private readonly Dictionary<FateKey, double> excludedUntil = [];
    private readonly HashSet<int> visited = [];
    private double since, lastAction = -100, lastMotion, idleSince, noEnemySince;
    private Vector3 lastPosition;
    private uint sessionJob;
    private uint expectedTerritory;
    private FateKey selectedKey;
    private bool participated, sawLoading, returning;
    private int nextInstance;
    private Destination? teleportDestination;

    public void Start(RunOptions config, World world, double now)
    {
        if (IsRunning) return;
        try
        {
            options = JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(config))!;
            options.Validate();
            if (!world.LoggedIn || world.Loading || world.Dead || world.Restricted || world.InCombat || world.Occupied || world.Casting)
                throw new InvalidOperationException(L.T("Inicie em mundo aberto, vivo, fora de combate e com os diálogos fechados."));
            runtime.Begin(options);
            Completed = Unconfirmed = 0;
            GemstonesEarned = 0;
            previousGemstones = world.Gemstones;
            Selected = null;
            visited.Clear();
            excludedUntil.Clear();
            nextInstance = 0;
            returning = participated = false;
            expectedTerritory = world.Territory;
            sessionJob = world.Job;
            lastAction = now - 10;
            idleSince = noEnemySince = now;
            Enter(RunState.Selecting, L.T("Procurando FATEs elegíveis."), now);
        }
        catch (Exception ex) { Stop(L.T("Não foi possível iniciar: {0}", ex.Message), true); }
    }

    public void Stop(string? reason = null, bool fault = false)
    {
        reason ??= L.T("Parado pelo usuário.");
        State = fault ? RunState.Faulted : RunState.Stopped;
        Status = reason;
        try { runtime.End(); }
        catch (Exception ex) { Status += L.T(" Restauração: {0}", ex.Message); State = RunState.Faulted; }
        Selected = null;
        runtime.Log(Status);
    }

    public void Tick(World w, double now)
    {
        if (!IsRunning) return;
        try
        {
            if (w.LoggedIn && !w.Loading)
            {
                GemstonesEarned += Math.Max(0, w.Gemstones - previousGemstones);
                previousGemstones = w.Gemstones;
            }
            if (!w.LoggedIn && !w.Loading) { Stop(L.T("Sessão encerrada: personagem desconectado."), true); return; }
            if (w.Dead) { Stop(L.T("Personagem derrotado. Resolva a recuperação e inicie novamente."), true); return; }
            if (w.Restricted) { Stop(L.T("Automação encerrada ao entrar em conteúdo instanciado/PvP."), true); return; }
            if (w.Job != 0 && w.Job != sessionJob) { Stop(L.T("A classe mudou durante a sessão."), true); return; }

            if (State == RunState.Teleporting) { TickTeleport(w, now); return; }
            if (State == RunState.InstanceChange) { TickInstance(w, now); return; }
            if (w.Loading) { Stop(L.T("Transição de área não solicitada pelo FatePilot."), true); return; }
            if (w.Territory != expectedTerritory) { Stop(L.T("A zona mudou fora da rota do FatePilot."), true); return; }
            if (w.TravelBusy) { Stop(L.T("módulo de viagens iniciou outra tarefa durante a sessão."), true); return; }
            if (options.ExcludeGroupBosses && w.Enemies.Any(e => e.TargetingPlayer && FateRules.IsGroupBoss(e.FateId)))
            { Stop(L.T("Um chefe de grupo entrou em combate com você. Sessão interrompida.")); return; }
            if (w.Occupied)
            {
                if (w.OwnEventInteraction && Selected != null)
                {
                    runtime.StopMovement();
                    runtime.SetCombat(false);
                    runtime.AdvanceEventInteraction();
                    return;
                }
                Stop(L.T("Um diálogo ou outra interação interrompeu a sessão.")); return;
            }
            if (w.NavError != null) { SkipOrFail(L.T("Navegação: {0}", w.NavError), now); return; }

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
        catch (Exception ex) { Stop(ex.Message, true); }
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
        if (options.ReturnAfterFates > 0 && Completed >= options.ReturnAfterFates)
        {
            returning = true;
            Teleport(options.ReturnDestination!, w, now);
            return;
        }
        if (options.Route.Count > 0 && options.Route.All(d => d.TerritoryId != w.Territory))
        {
            Teleport(options.Route[0], w, now);
            return;
        }
        if (!w.NavReady)
        {
            if (now - since > 180) Stop(L.T("módulo de navegação não carregou a malha desta zona em 180 segundos."), true);
            return;
        }
        var fate = Selection.Choose(w, options, excludedUntil, now);
        if (fate != null)
        {
            Selected = fate;
            selectedKey = Selection.Key(fate, w);
            participated = false;
            ResetObjectives(fate, now);
            noEnemySince = now;
            Enter(RunState.Mounting, L.T("Preparando deslocamento: {0}", fate.Name), now);
            return;
        }
        if (State != RunState.Waiting) { idleSince = now; Enter(RunState.Waiting, L.T("Aguardando FATEs elegíveis."), now); }
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
                runtime.Log(L.T("FATE concluído observado: {0}. Total: {1}.", Selected?.Name, Completed));
            }
            else if (participated) { Unconfirmed++; runtime.Log(L.T("FATE terminou/desapareceu sem confirmação de sucesso; não conta para a meta.")); }
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
            if (!w.Casting && Throttle(now, 3)) runtime.Mount();
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
            if (Throttle(now, 2)) runtime.Dismount();
            if (now - since > 25) SkipOrFail(L.T("Não foi possível pousar/desmontar."), now);
            return;
        }
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
            if (w.InCombat) { Stop(L.T("Combate começou fora do FATE selecionado."), true); return; }
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
        if (now - since > 1200) Stop(L.T("Tempo máximo de combate excedido."), true);
    }

    private void Teleport(Destination destination, World w, double now)
    {
        if (w.InCombat || w.Casting || w.Occupied) return;
        if (w.Mounted || w.Flying)
        {
            if (Throttle(now, 2)) runtime.Dismount();
            if (now - since > 60) Stop(L.T("Desmonte antes do teleporte."), true);
            return;
        }
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
        if (w.Loading || !w.LoggedIn || w.Casting || w.Occupied || w.TravelBusy || !sawLoading || w.Territory != teleportDestination!.TerritoryId) return;
        expectedTerritory = w.Territory;
        runtime.ConfirmTeleport();
        if (returning)
        {
            runtime.End();
            State = RunState.Finished;
            Status = L.T("Meta de {0} FATEs atingida. Retorno concluído.", Completed);
            runtime.Log(Status);
            return;
        }
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
