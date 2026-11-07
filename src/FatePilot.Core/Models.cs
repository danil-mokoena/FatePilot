using System.Numerics;

namespace FatePilot.Core;

public enum RunState { Stopped, Selecting, Mounting, Moving, Landing, Syncing, Fighting, Waiting, HubApproach, InstanceChange, Teleporting, Finished, Faulted, StartingEvent, Restocking, Defending, Gateway, Paused, SwitchingJob, Repairing, Retrying }
public enum NotifyKind { Stopped, Goal, PriorityFate, Problem, Tell }
public enum EventState { Other, Preparing = 3, Running = 4, Ending = 5, Ended = 7, Failed = 8 }
public enum FateKind { Unknown, Combat, Collection, Defense, Escort }
public enum ObjectiveRole { Starter, Recipient, Escort, Pickup }
public sealed record ObjectiveActor(ulong Id, ushort FateId, ObjectiveRole Role, Vector3 Position, float Hitbox = 0);

public sealed record Destination(uint TerritoryId, uint AetheryteId, string Name);
public sealed record FateInfo(ushort Id, string Name, uint TerritoryId, EventState State, int Level,
    int MaxLevel, int Progress, float SecondsLeft, Vector3 Position, float Radius, bool IsCombat, bool Bonus, long StartedAt)
{
    public FateKind Kind { get; init; }
    public FateKind EffectiveKind => Kind == FateKind.Unknown && IsCombat ? FateKind.Combat : Kind;
    public bool CanStart { get; init; }
    public int HeldItems { get; init; }
    public int HandedIn { get; init; }
    public string? ObjectiveDiagnostic { get; init; }
}
public readonly record struct FateKey(uint Territory, int Instance, ushort Id, long StartedAt);
public sealed record EnemyInfo(ulong Id, ushort FateId, Vector3 Position, float Hitbox, bool TargetingPlayer)
{
    public uint NameId { get; init; }
}

public sealed class RunOptions
{
    public List<Destination> Route { get; set; } = [];
    public bool RotateInstances { get; set; } = true;
    public bool UseMount { get; set; } = true;
    public bool UseFlight { get; set; } = true;
    public bool PreferBonus { get; set; } = true;
    public bool PrioritizeForlorn { get; set; } = true;
    public bool IncludeCollection { get; set; } = true;
    public int CollectionBatchSize { get; set; } = 15;
    public bool IncludeEscort { get; set; } = true;
    public bool ExcludeGroupBosses { get; set; } = true;
    public int LevelAbovePlayer { get; set; } = 3;
    public int MaximumProgress { get; set; } = 85;
    public int MinimumSecondsLeft { get; set; } = 90;
    public int IdleSeconds { get; set; } = 30;
    public int ReturnAfterFates { get; set; } = 10;
    public int ReturnAfterGemstones { get; set; }
    public int ReturnAfterMinutes { get; set; }
    public bool AvoidGemstoneCap { get; set; }
    public int GemstoneCapReserve { get; set; } = 200;
    public bool HasReturnGoal => ReturnAfterFates > 0 || ReturnAfterGemstones > 0 || ReturnAfterMinutes > 0 || AvoidGemstoneCap || ReturnWhenBagSlotsBelow > 0 || ReturnWhenDurabilityBelow > 0 || (UseJobQueue && JobQueue.Count > 0);
    public Destination? ReturnDestination { get; set; }
    // Optional city with a gemstone trader, used when the return is caused by gemstones.
    public Destination? GemstoneDestination { get; set; }
    public List<ushort> ExcludedFates { get; set; } = [];
    // FATEs chosen from the Achievements page: picked first whenever they are eligible.
    public List<ushort> PriorityFates { get; set; } = [];
    // 0.7: weigh bonus FATEs against distance, wait where FATEs usually appear, time budget per zone.
    public bool PreferRewards { get; set; }
    public int BonusWorthYalms { get; set; } = 200;
    public bool IdleToHotspot { get; set; } = true;
    public int MinutesPerZone { get; set; }
    // 0.8: notifications (game chat sound + local chat line) and automatic retry.
    public bool NotifySound { get; set; } = true;
    public int NotifySoundEffect { get; set; } = 6;
    public bool NotifyChat { get; set; } = true;
    public bool NotifyOnStop { get; set; } = true;
    public bool NotifyOnGoal { get; set; } = true;
    public bool NotifyOnPriorityFate { get; set; } = true;
    public bool NotifyOnProblem { get; set; } = true;
    public bool NotifyOnTell { get; set; } = true;
    public bool AutoRetry { get; set; } = true;
    public int MaxRetries { get; set; } = 2;
    public bool UseTickets { get; set; }
    public bool PayGilWhenTicketsRunOut { get; set; }
    public int TicketMinimumFare { get; set; }
    public int MaximumTeleportGil { get; set; }
    public bool RestockGreens { get; set; }
    public bool SummonChocobo { get; set; } = true;
    public int GreensThreshold { get; set; } = 10;
    public int GreensTarget { get; set; } = 99;
    public int GreensGilLimit { get; set; } = 10000;

    // 0.5 upkeep: bag/durability return limits, self-repair, food, job queue.
    public int ReturnWhenBagSlotsBelow { get; set; }
    public int ReturnWhenDurabilityBelow { get; set; }
    public bool SelfRepair { get; set; }
    public int RepairBelowPercent { get; set; } = 30;
    public bool UseFood { get; set; }
    public uint FoodItemId { get; set; }
    public bool FoodHq { get; set; }
    public int FoodRefreshMinutes { get; set; } = 5;
    public bool UseJobQueue { get; set; }
    public List<JobGoal> JobQueue { get; set; } = [];

    public void Validate()
    {
        if (TicketMinimumFare < 0 || MaximumTeleportGil < 0)
            throw new InvalidOperationException(L.T("Os limites de gastos não podem ser negativos."));
        if (CollectionBatchSize is < 1 or > 99)
            throw new InvalidOperationException(L.T("O lote de coleta deve ter entre 1 e 99 itens."));
        if (IdleSeconds < 10 || MinimumSecondsLeft < 0 || LevelAbovePlayer < 0 || MaximumProgress is < 1 or > 100 || ReturnAfterFates < 0)
            throw new InvalidOperationException(L.T("Os filtros de FATEs contêm valores inválidos."));
        if (ReturnAfterGemstones is < 0 or > 1000000 || ReturnAfterMinutes is < 0 or > 10080 || GemstoneCapReserve is < 0 or > 1000000)
            throw new InvalidOperationException(L.T("As metas de gemstones, tempo ou reserva contêm valores inválidos."));
        if (HasReturnGoal && ReturnDestination == null)
            throw new InvalidOperationException(L.T("Escolha uma cidade de retorno para usar as metas da sessão."));
        if (RestockGreens && (GreensThreshold is < 0 or > 998 || GreensTarget <= GreensThreshold || GreensTarget > 999 || GreensGilLimit <= 0))
            throw new InvalidOperationException(L.T("Reposição: use um estoque de 1 a 999, maior que o mínimo, e um orçamento positivo."));
        if (RestockGreens && Route.Count == 0)
            throw new InvalidOperationException(L.T("Adicione uma zona à rota para retornar após comprar Gysahl Greens."));
        if (Route.Any(x => x.TerritoryId == 0 || x.AetheryteId == 0) || Route.Select(x => x.TerritoryId).Distinct().Count() != Route.Count)
            throw new InvalidOperationException(L.T("Escolha um aetheryte por zona, sem repetir zonas."));
        if (ReturnWhenBagSlotsBelow is < 0 or > 140 || ReturnWhenDurabilityBelow is < 0 or > 99 || RepairBelowPercent is < 1 or > 99 || FoodRefreshMinutes is < 1 or > 60)
            throw new InvalidOperationException(L.T("Os limites de bolsa, durabilidade, reparo ou comida contêm valores inválidos."));
        if (MinutesPerZone is < 0 or > 600 || BonusWorthYalms is < 0 or > 2000)
            throw new InvalidOperationException(L.T("O tempo por zona ou o peso do bônus contém valores inválidos."));
        if (NotifySoundEffect is < 1 or > 16 || MaxRetries is < 0 or > 5)
            throw new InvalidOperationException(L.T("O som do aviso ou o número de tentativas contém valores inválidos."));
        if (UseFood && FoodItemId == 0)
            throw new InvalidOperationException(L.T("Escolha uma comida da bolsa ou desative o uso de comida."));
        if (UseJobQueue && (JobQueue.Count == 0 || JobQueue.Any(j => j.TargetLevel is < 1 or > 100 || j.Job is 0 or (>= 8 and <= 18))))
            throw new InvalidOperationException(L.T("A fila de classes precisa de gearsets de combate com nível-alvo entre 1 e 100."));
    }
}

// One step of the per-level job queue: play this gearset until its job reaches TargetLevel.
// Cities with a bicolor gemstone trader (territory → aetheryte), checked in game data.
public static class GemstoneTraders
{
    public static readonly IReadOnlyDictionary<uint, uint> Cities = new Dictionary<uint, uint>
    {
        [819] = 133,   // The Crystarium
        [820] = 134,   // Eulmore
        [962] = 182,   // Old Sharlayan
        [963] = 183,   // Radz-at-Han
        [1185] = 216,  // Tuliyollal
        [1186] = 217,  // Solution Nine
    };
    public static bool Has(Destination d) => Cities.TryGetValue(d.TerritoryId, out var a) && a == d.AetheryteId;
}

public sealed class JobGoal
{
    public int Gearset { get; set; }
    public uint Job { get; set; }
    public string Name { get; set; } = "";
    public int TargetLevel { get; set; } = 100;
}

public sealed record SessionSummary(int Completed, int Unconfirmed, int Gemstones, double Seconds, long Gil, int GreensUsed, int FoodUsed, int Repairs, int JobSwitches, string Reason, DateTime EndedAt);

// Immutable snapshots: never retain Dalamud game-object wrappers across frames.
public sealed record World
{
    public bool LoggedIn { get; init; }
    public bool Dead { get; init; }
    public bool Restricted { get; init; }
    public bool Loading { get; init; }
    public bool Occupied { get; init; }
    public bool Casting { get; init; }
    public bool InCombat { get; init; }
    public bool Mounted { get; init; }
    public bool Flying { get; init; }
    public bool CanFly { get; init; }
    public bool Synced { get; init; }
    public ushort CurrentFate { get; init; }
    public int Level { get; init; }
    public uint Job { get; init; }
    public uint Territory { get; init; }
    public int Instance { get; init; }
    public int InstanceCount { get; init; }
    public bool CanChangeInstance { get; init; }
    public Vector3 Position { get; init; }
    // Facing, in radians (0 = south / +Z), used only to draw the player arrow on the map.
    public float Rotation { get; init; }
    public Vector3? NearbyAetheryte { get; init; }
    public bool TravelBusy { get; init; }
    public bool NavReady { get; init; }
    public bool NavBusy { get; init; }
    public string? NavError { get; init; }
    public IReadOnlyList<FateInfo> Fates { get; init; } = [];
    public IReadOnlyList<EnemyInfo> Enemies { get; init; } = [];
    public IReadOnlyList<ObjectiveActor> Objectives { get; init; } = [];
    public bool OwnEventInteraction { get; init; }
    public bool EventInteractionPending { get; init; }
    public bool DialogueOpen { get; init; }
    public string? EventInteractionError { get; init; }
    public string? InteractionDiagnostic { get; init; }
    public int Gemstones { get; init; }
    public int GemstoneCap { get; init; }
    public TrackerWorld? CurrentWorld { get; init; }
    public GreensInventory GreensInventory { get; init; } = new(false, 0, 0, 0);
    public float? CompanionSecondsLeft { get; init; }
    public bool CanSummonChocobo { get; init; }
    public VendorActor? GreensVendor { get; init; }
    public GreensShopState GreensShop { get; init; }
    public string? GreensShopError { get; init; }
    public int FreeBagSlots { get; init; } = -1;
    public int MinDurability { get; init; } = -1;
    public float? FoodSecondsLeft { get; init; }
    public int FoodCount { get; init; }
    public bool CanUseFood { get; init; }
    public bool CanSelfRepair { get; init; }
    public bool RepairBusy { get; init; }
}

public interface IRuntime
{
    void Begin(RunOptions options);
    void UpdateOptions(RunOptions options);
    void End();
    void Move(Vector3 position, bool fly);
    void StopMovement();
    void Mount();
    void Dismount();
    void Sync();
    void SetCombat(bool enabled);
    void Target(ulong id);
    void Teleport(Destination destination);
    void ConfirmTeleport();
    void ChangeInstance(int instance);
    void AethernetTeleport(uint shardId);
    void Log(string message);
    void Interact(ObjectiveActor actor);
    void AdvanceEventInteraction();
    void CancelEventInteraction();
    void OpenGreensShop(VendorActor actor);
    void AdvanceGreensShop();
    void BuyGreens(GreensPurchase purchase);
    void CloseGreensShop();
    bool UseGysahlGreens();
    // Release / retake game control without losing session counters (pause, job switch).
    void Suspend();
    void ResumeSession();
    bool EquipGearset(int index);
    bool UseFood(uint itemId, bool hq);
    bool StartRepair();
    void Notify(NotifyKind kind, string message);
}
