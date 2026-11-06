using System.Numerics;

namespace FatePilot.Core;

public enum RunState { Stopped, Selecting, Mounting, Moving, Landing, Syncing, Fighting, Waiting, HubApproach, InstanceChange, Teleporting, Finished, Faulted, StartingEvent, Restocking, Defending }
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
    public bool HasReturnGoal => ReturnAfterFates > 0 || ReturnAfterGemstones > 0 || ReturnAfterMinutes > 0 || AvoidGemstoneCap;
    public Destination? ReturnDestination { get; set; }
    public List<ushort> ExcludedFates { get; set; } = [];
    public bool UseTickets { get; set; }
    public bool PayGilWhenTicketsRunOut { get; set; }
    public int TicketMinimumFare { get; set; }
    public int MaximumTeleportGil { get; set; }
    public bool RestockGreens { get; set; }
    public int GreensThreshold { get; set; } = 10;
    public int GreensTarget { get; set; } = 99;
    public int GreensGilLimit { get; set; } = 10000;

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
    }
}

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
    public VendorActor? GreensVendor { get; init; }
    public GreensShopState GreensShop { get; init; }
    public string? GreensShopError { get; init; }
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
    void Log(string message);
    void Interact(ObjectiveActor actor);
    void AdvanceEventInteraction();
    void CancelEventInteraction();
    void OpenGreensShop(VendorActor actor);
    void AdvanceGreensShop();
    void BuyGreens(GreensPurchase purchase);
    void CloseGreensShop();
}
