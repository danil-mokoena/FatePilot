using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Numerics;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using World = FatePilot.Core.World;
using Action = System.Action;

namespace FatePilot;

internal sealed partial class GameRuntime : IRuntime, IDisposable
{
    private readonly Navigation nav = new();
    private readonly Combat combat;
    private readonly Travel travel;
    private readonly EventInteraction interaction;
    private readonly GreensShop greensShop = new();
    private readonly GemstoneShop gemstoneShop = new();
    private bool session, ownsTravelService, companionEnabled;
    private ulong previousTarget, ownedTarget;
    private readonly Queue<string> messages = new();
    public string[] Messages => messages.ToArray();
    public bool NeedsRecovery => combat.NeedsRecovery;
    public GameRuntime() { combat = new(Log); travel = new(Log); interaction = new(Log); }
    public long ReservedGil => travel.ReservedGil;
    public int UsedTickets => travel.UsedTickets;
    public int TicketCount => travel.Tickets;

    public void Log(string message)
    {
        messages.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
        while (messages.Count > 120) messages.Dequeue();
    }

    public List<(string Name, bool Available)> Dependencies()
    {
        var pi = Services.Plugin;
        return [
            ("vnavmesh · " + L.T("módulo de navegação: navegação cancelável"), pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(IntegrationIds.NavigationNavPathfindCancelable).HasFunction
                && pi.GetIpcSubscriber<List<Vector3>, bool, object>(IntegrationIds.NavigationPathMoveTo).HasAction
                && pi.GetIpcSubscriber<object>(IntegrationIds.NavigationPathStop).HasAction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.NavigationNavIsReady).HasFunction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.NavigationPathIsRunning).HasFunction
                && pi.GetIpcSubscriber<Vector3,float,float,Vector3?>(IntegrationIds.NavigationQueryMeshNearestPointReachable).HasFunction),
            ("BossMod · " + L.T("módulo de posicionamento: presets atuais + configuração"), pi.GetIpcSubscriber<List<string>>(IntegrationIds.PositioningPresetsGetActiveList).HasFunction
                && pi.GetIpcSubscriber<List<string>, bool>(IntegrationIds.PositioningPresetsSetActiveList).HasFunction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.PositioningPresetsSetForceDisabled).HasFunction
                && pi.GetIpcSubscriber<IReadOnlyList<string>, bool, List<string>>(IntegrationIds.PositioningConfiguration).HasFunction
                && pi.GetIpcSubscriber<string, bool, bool>(IntegrationIds.PositioningPresetsCreate).HasFunction
                && pi.GetIpcSubscriber<string, string?>(IntegrationIds.PositioningPresetsGet).HasFunction
                && pi.GetIpcSubscriber<string, bool>(IntegrationIds.PositioningPresetsDelete).HasFunction
                && pi.GetIpcSubscriber<Vector3,float,bool,bool>(IntegrationIds.PositioningObstacleMapGenerate).HasFunction
                && pi.GetIpcSubscriber<object>(IntegrationIds.PositioningObstacleMapGetGenerationStatus).HasFunction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.PositioningObstacleMapHasTempMap).HasFunction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.PositioningObstacleMapClearTempMap).HasFunction),
            ("WrathCombo · " + L.T("módulo de habilidades: controle temporário"), pi.GetIpcSubscriber<string,string,string?,Guid?>(IntegrationIds.AbilitiesRegisterForLeaseWithCallback).HasFunction
                && pi.GetIpcSubscriber<Guid,bool,object>(IntegrationIds.AbilitiesSetAutoRotationState).HasFunction
                && pi.GetIpcSubscriber<Guid,object>(IntegrationIds.AbilitiesSetCurrentJobAutoRotationReady).HasFunction
                && pi.GetIpcSubscriber<Guid,object,object,object>(IntegrationIds.AbilitiesSetAutoRotationConfigState).HasFunction
                && pi.GetIpcSubscriber<Guid,object>(IntegrationIds.AbilitiesReleaseControl).HasAction),
            ("Lifestream · " + L.T("módulo de viagens: zonas + instâncias"), pi.GetIpcSubscriber<uint,byte,bool>(IntegrationIds.TravelTeleport).HasFunction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.TravelIsBusy).HasFunction
                && pi.GetIpcSubscriber<object>(IntegrationIds.TravelAbort).HasAction
                && pi.GetIpcSubscriber<int,object>(IntegrationIds.TravelChangeInstance).HasAction
                && pi.GetIpcSubscriber<int>(IntegrationIds.TravelGetCurrentInstance).HasFunction
                && pi.GetIpcSubscriber<int>(IntegrationIds.TravelGetNumberOfInstances).HasFunction
                && pi.GetIpcSubscriber<bool>(IntegrationIds.TravelCanChangeInstance).HasFunction)
        ];
    }

    public void Begin(RunOptions options)
    {
        var missing = Dependencies().Where(x => !x.Available).Select(x => x.Name).ToList();
        if (missing.Count > 0) throw new InvalidOperationException(L.T("API indisponível: ") + string.Join(", ", missing));
        if (Services.Plugin.InstalledPlugins.Count(p => p.IsLoaded && p.InternalName is IntegrationIds.PositioningPlugin or IntegrationIds.ConflictingPositioningPlugin) != 1
            || !Services.Plugin.InstalledPlugins.Any(p => p.IsLoaded && p.InternalName == IntegrationIds.PositioningPlugin))
            throw new InvalidOperationException(L.T("Carregue somente o módulo de posicionamento compatível. Versões alternativas podem disputar a mesma interface."));
        if (Services.Plugin.GetIpcSubscriber<bool>(IntegrationIds.TravelIsBusy).InvokeFunc()
            || Services.Plugin.GetIpcSubscriber<bool>(IntegrationIds.NavigationPathIsRunning).InvokeFunc())
            throw new InvalidOperationException(L.T("Há navegação/viagem em andamento. Pare a outra automação antes de iniciar."));
        ValidateOptions(options);
        previousTarget = Services.Targets.Target?.GameObjectId ?? 0;
        ownedTarget = 0;
        session = true;
        combat.Begin();
        travel.Begin(options);
        companionEnabled = options.SummonChocobo;
        upkeepOptions = options;
    }

    private void ValidateOptions(RunOptions options)
    {
        var unlocked = Destinations();
        if (options.RestockGreens)
        {
            GreensShop.ValidateData();
            if (!unlocked.Any(d => d.AetheryteId == GreensRestock.VendorCity.AetheryteId && d.TerritoryId == GreensRestock.VendorCity.TerritoryId))
                throw new InvalidOperationException(L.T("Desbloqueie o aetheryte de Limsa Lominsa para repor Gysahl Greens."));
            var inventory = GreensShop.Inventory();
            if (!inventory.Ready || inventory.Count <= options.GreensThreshold)
                _ = GreensRestock.Needed(options, inventory, 0);
        }
        foreach (var destination in options.Route.Concat(options.ReturnDestination is { } ret ? new[] { ret } : []).Concat(options.GemstoneDestination is { } gem ? new[] { gem } : []))
            if (!unlocked.Any(d => d.AetheryteId == destination.AetheryteId && d.TerritoryId == destination.TerritoryId))
                throw new InvalidOperationException(L.T("Aetheryte indisponível para este personagem: ") + destination.Name);
    }

    public void UpdateOptions(RunOptions options)
    {
        ValidateOptions(options);
        travel.UpdateOptions(options);
        companionEnabled = options.SummonChocobo;
        upkeepOptions = options;
    }

    public void End()
    {
        interaction.Cancel();
        List<string> errors = [];
        void Attempt(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex.Message); } }
        Attempt(greensShop.Close);
        greensShop.Reset();
        gemstoneShop.Reset();
        Attempt(nav.Stop);
        Attempt(combat.End);
        Attempt(travel.End);
        if (ownsTravelService)
        {
            Attempt(() => Services.Plugin.GetIpcSubscriber<object>(IntegrationIds.TravelAbort).InvokeAction());
            ownsTravelService = false;
        }
        if (ownedTarget != 0 && Services.Targets.Target?.GameObjectId == ownedTarget)
            Services.Targets.Target = Services.Objects.FirstOrDefault(x => x.GameObjectId == previousTarget);
        ownedTarget = previousTarget = 0;
        session = false;
        companionEnabled = false;
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
    }

    public void ValidateControl()
    {
        combat.CleanupMap();
        if (session) combat.HealthCheck();
    }

    public void Update(World world)
    {
        TickRepair();
        if (session && world.LoggedIn && !world.Dead && !world.Restricted && !world.Loading && !world.Occupied)
            nav.Update();
    }

    public unsafe World ReadWorld()
    {
        var pi = Services.Plugin;
        var c = Services.Conditions;
        var loading = c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51];
        var player = Services.Objects.LocalPlayer;
        if (player == null) return new World { Loading = loading };
        var fateManager = FateManager.Instance();
        var currentFate = fateManager == null ? (ushort)0 : fateManager->GetCurrentFateId();
        var territory = Services.Client.TerritoryType;
        var zone = Services.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory);
        var flightSet = zone?.AetherCurrentCompFlgSet.RowId ?? 0;
        var state = PlayerState.Instance();
        var canFly = flightSet != 0 && state != null && state->IsAetherCurrentZoneComplete(flightSet);
        var fates = new List<FateInfo>();
        var enemies = new List<EnemyInfo>();
        var objectives = new List<ObjectiveActor>();
        Vector3? nearestHub = null;
        if (!loading)
        {
            foreach (var f in Services.Fates)
            {
                if (f.Address == 0 || f.FateId == 0) continue;
                var native = (FateContext*)f.Address;
                EventInteraction.SnapshotObjectives(native, objectives);
                fates.Add(new(f.FateId, f.Name.TextValue, f.TerritoryType.RowId, (EventState)(byte)f.State,
                    f.Level, f.MaxLevel, f.Progress, f.TimeRemaining, f.Position, f.Radius,
                    native->Rule == 1, f.HasBonus, f.StartTimeEpoch)
                {
                    Kind = FateRules.FromRule(native->Rule),
                    CanStart = native->MotivationNpc is not (0 or 0xE0000000) || objectives.Any(o => o.FateId == f.FateId && o.Role == ObjectiveRole.Starter),
                    ObjectiveDiagnostic = $"FATE={f.FateId}; starterRef={native->MotivationNpc:X8}; recipientRef={native->ObjectiveNpc:X8}; array={native->ArrayEventHandler.Id:X8}; starters={objectives.Count(o => o.FateId == f.FateId && o.Role == ObjectiveRole.Starter)}; recipients={objectives.Count(o => o.FateId == f.FateId && o.Role == ObjectiveRole.Recipient)}; pickups={objectives.Count(o => o.FateId == f.FateId && o.Role == ObjectiveRole.Pickup)}; escorts={objectives.Count(o => o.FateId == f.FateId && o.Role == ObjectiveRole.Escort)}; rule={native->Rule}; item={native->EventItem}; objectiveIcon={native->FateModeObjectiveIcon}",
                    HeldItems = native->Rule == 2 ? EventInteraction.ItemCount(native->TurnInEventItem != 0 ? native->TurnInEventItem : native->EventItem != 0 ? native->EventItem : native->ReqEventItem) : 0,
                    HandedIn = native->HandInCount,
                    Chain = native->FATEChain,
                });
            }
            foreach (var obj in Services.Objects)
            {
                if (obj.ObjectKind == ObjectKind.Aetheryte && obj.IsTargetable
                    && (nearestHub == null || Vector3.Distance(player.Position, obj.Position) < Vector3.Distance(player.Position, nearestHub.Value)))
                    nearestHub = obj.Position;
                // 1.3.3: an enemy is something the game lets us auto-attack. The "hostile" status flag
                // is also set on friendly FATE NPCs while they fight, which made FatePilot target them.
                if (obj is IBattleNpc npc && npc.IsTargetable && !npc.IsDead && Attackable(npc))
                {
                    var fateId = ((NativeObject*)npc.Address)->FateId;
                    var victim = npc.TargetObject;
                    var attackingNpc = victim is { ObjectKind: ObjectKind.EventNpc or ObjectKind.BattleNpc } && !Attackable(victim);
                    enemies.Add(new(npc.GameObjectId, fateId, npc.Position, npc.HitboxRadius, npc.TargetObjectId == player.GameObjectId)
                        { NameId = npc.NameId, TargetId = npc.TargetObjectId, AttackingNpc = attackingNpc });
                }
            }
        }
        T Optional<T>(string gate, T fallback) => pi.GetIpcSubscriber<T>(gate).HasFunction ? pi.GetIpcSubscriber<T>(gate).InvokeFunc() : fallback;
        var occupied = c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInQuestEvent] || c[ConditionFlag.OccupiedInEvent]
            || c[ConditionFlag.OccupiedInCutSceneEvent] || c[ConditionFlag.WatchingCutscene] || c[ConditionFlag.OccupiedSummoningBell];
        var interactionStatus = interaction.ReadStatus(occupied);
        var gameWorld = player.CurrentWorld.ValueNullable;
        var currency = CurrencyManager.Instance();
        return new World
        {
            LoggedIn = Services.Client.IsLoggedIn, Loading = loading, Dead = player.IsDead,
            Restricted = Services.Client.IsPvP || c[ConditionFlag.BoundByDuty] || c[ConditionFlag.BoundByDuty56] || c[ConditionFlag.BoundByDuty95],
            Occupied = occupied, DialogueOpen = EventInteraction.DialogueOpen || GreensShop.IsOpen || GemstoneShop.IsOpen,
            InteractionDiagnostic = occupied || EventInteraction.DialogueOpen ?
                $"occupied={c[ConditionFlag.Occupied]}; questEvent={c[ConditionFlag.OccupiedInQuestEvent]}; event={c[ConditionFlag.OccupiedInEvent]}; cutscene={c[ConditionFlag.OccupiedInCutSceneEvent] || c[ConditionFlag.WatchingCutscene]}; dialog={EventInteraction.DialogueOpen}; lease={interactionStatus}" : null,
            Casting = player.IsCasting || c[ConditionFlag.MountOrOrnamentTransition],
            InCombat = c[ConditionFlag.InCombat], Mounted = c[ConditionFlag.Mounted], Flying = c[ConditionFlag.InFlight],
            CanFly = canFly, Synced = fateManager != null && fateManager->SyncedFateId != 0 && fateManager->SyncedFateId == currentFate,
            CurrentFate = currentFate, Level = player.Level, Job = player.ClassJob.RowId, Territory = territory,
            Instance = Optional(IntegrationIds.TravelGetCurrentInstance, 0), InstanceCount = Optional(IntegrationIds.TravelGetNumberOfInstances, 0),
            CanChangeInstance = Optional(IntegrationIds.TravelCanChangeInstance, false), TravelBusy = Optional(IntegrationIds.TravelIsBusy, false),
            NavReady = Optional(IntegrationIds.NavigationNavIsReady, false), NavBusy = session && nav.Busy, NavError = nav.Error,
            Position = player.Position, Rotation = player.Rotation, NearbyAetheryte = nearestHub, Fates = fates, Enemies = enemies,
            MeleeJob = player.ClassJob.ValueNullable?.Role is 1 or 2,
            ZoneAetherytes = loading ? [] : ZoneAetherytes(territory),
            Objectives = objectives, OwnEventInteraction = interactionStatus == InteractionStatus.Owned,
            EventInteractionPending = interactionStatus == InteractionStatus.Waiting,
            EventInteractionError = interactionStatus == InteractionStatus.Foreign ? L.T("O diálogo não corresponde ao NPC e ao evento esperados. Consulte o diagnóstico.") :
                interactionStatus == InteractionStatus.Expired ? L.T("A interação com o NPC não terminou em 60 segundos. Consulte o diagnóstico.") : null,
            Gemstones = EventInteraction.ItemCount(26807),
            GemstoneCap = currency == null ? 0 : (int)currency->GetItemMaxCount(26807),
            CurrentWorld = gameWorld is { } current ? TrackerWorld.FromCurrent(current.RowId, current.Name.ToString(),
                current.DataCenter.ValueNullable?.Name.ToString() ?? "", current.IsPublic, loading) : null,
            GreensInventory = loading ? new(false, 0, 0, 0) : GreensShop.Inventory(),
            CompanionSecondsLeft = loading ? null : ReadCompanionSeconds(),
            CanSummonChocobo = session && companionEnabled && !loading && CanUseGysahlGreens(),
            GreensVendor = loading ? null : GreensShop.Vendor(),
            GreensShop = greensShop.Read(occupied), GreensShopError = greensShop.Error,
            FreeBagSlots = loading ? -1 : FreeBagSlots(), MinDurability = loading ? -1 : MinDurability(),
            FoodSecondsLeft = loading ? null : ReadFoodSeconds(),
            FoodCount = loading ? 0 : FoodCount(upkeepOptions.FoodItemId, upkeepOptions.FoodHq),
            CanUseFood = !loading && upkeepOptions.FoodItemId != 0 && CanUseItem(upkeepOptions.FoodHq ? upkeepOptions.FoodItemId + 1_000_000 : upkeepOptions.FoodItemId),
            CanSelfRepair = !loading && CanSelfRepair(), RepairBusy = RepairBusy,
            GemstoneTrader = loading ? null : TraderActor(),
            ItemCounts = loading ? new Dictionary<uint, int>() : QueueItemCounts(),
        };
    }

    // The job's real level from the player's level table (the synced level inside a FATE is lower).
    public static unsafe int JobLevel(uint job)
    {
        var state = PlayerState.Instance();
        var row = Services.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job);
        if (state == null || row == null) return 0;
        var index = row.Value.ExpArrayIndex;
        return index >= 0 && index < state->ClassJobLevels.Length ? state->ClassJobLevels[index] : 0;
    }

    // Visible trader menus and exchange shops, as raw values, for the "copy window data" button.
    public static unsafe string DescribeTraderWindows()
    {
        var text = new System.Text.StringBuilder();
        foreach (var name in new[] { "SelectString", "SelectIconString", "ShopExchangeCurrency", "ShopExchangeItem", "InclusionShop" })
        {
            var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)Services.GameGui.GetAddonByName(name).Address;
            if (addon == null || !addon->IsVisible) continue;
            text.AppendLine($"[{name}] values={addon->AtkValuesCount}");
            for (var i = 0; i < Math.Min((int)addon->AtkValuesCount, 600); i++)
            {
                var value = addon->AtkValues[i];
                string shown;
                try { shown = value.GetValueAsString(); } catch (Exception) { shown = "?"; }
                text.AppendLine($"{i}\t{value.Type}\t{shown}");
            }
        }
        return text.Length == 0 ? "" : "FatePilot trader windows\n" + text;
    }

    private unsafe Dictionary<uint, int> QueueItemCounts()
    {
        var counts = new Dictionary<uint, int>();
        var inventory = InventoryManager.Instance();
        if (inventory == null) return counts;
        foreach (var id in upkeepOptions.PurchaseQueue.Select(o => o.ItemId).Distinct())
            counts[id] = inventory->GetInventoryItemCount(id, false, false, false) + inventory->GetInventoryItemCount(id, true, false, false);
        return counts;
    }

    private static VendorActor? TraderActor()
    {
        if (GemstoneTraders.In(Services.Client.TerritoryType) is not { } trader) return null;
        var found = Services.Objects.Where(o => o.ObjectKind == ObjectKind.EventNpc && o.BaseId == trader.NpcId && o.IsTargetable).ToArray();
        return found.Length == 1 ? new(found[0].GameObjectId, found[0].Position, found[0].HitboxRadius) : null;
    }

    // Starts the conversation with the verified trader next to the player; GemstoneShop then
    // only acts inside that conversation.
    public void TalkTo(VendorActor trader) { gemstoneShop.Reset(); gemstoneShop.TalkTo(trader); }

    public GemstoneBuyStep BuyGemstones(uint itemId, string name, int quantity) => gemstoneShop.Step(itemId, name, quantity);

    // 1.5: unlocked aetherytes of a zone with their world positions (from the game's level data).
    private uint aetheryteZone;
    private double aetheryteRead = double.NegativeInfinity;
    private List<AetherytePoint> zoneAetherytes = [];
    private List<AetherytePoint> ZoneAetherytes(uint territory)
    {
        var now = Environment.TickCount64 / 1000d;
        if (territory == aetheryteZone && now - aetheryteRead < 30) return zoneAetherytes;
        aetheryteZone = territory; aetheryteRead = now;
        var sheet = Services.Data.GetExcelSheet<Aetheryte>();
        zoneAetherytes = Destinations().Where(d => d.TerritoryId == territory).Select(d =>
        {
            var row = sheet.GetRowOrDefault(d.AetheryteId);
            if (row is not { } a || a.Territory.RowId != territory || a.Level.Count == 0 || a.Level[0].ValueNullable is not { } level) return null;
            return new AetherytePoint(d, new Vector3(level.X, level.Y, level.Z));
        }).Where(p => p != null).Select(p => p!).ToList();
        return zoneAetherytes;
    }

    public List<Destination> Destinations()
    {
        if (Services.Objects.LocalPlayer == null) return [];
        var sheet = Services.Data.GetExcelSheet<Aetheryte>();
        var list = Services.Aetherytes.Select(a => sheet.GetRowOrDefault(a.AetheryteId)).Where(a => a.HasValue && a.Value.IsAetheryte)
            .Select(a => new Destination(a!.Value.Territory.RowId, a.Value.RowId,
                L.T("{0} — {1}", a.Value.Territory.Value.PlaceName.Value.Name.ExtractText(), a.Value.PlaceName.Value.Name.ExtractText())))
            .DistinctBy(d => d.AetheryteId).ToList();
        // Zones without an aetheryte become reachable through a neighbouring city's aethernet.
        foreach (var gate in Gateways.All)
        {
            if (list.Any(d => d.TerritoryId == gate.Territory) || !list.Any(d => d.AetheryteId == gate.ViaAetheryteId)) continue;
            var zone = Services.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(gate.Territory)?.PlaceName.Value.Name.ExtractText();
            var via = sheet.GetRowOrDefault(gate.ViaAetheryteId)?.PlaceName.Value.Name.ExtractText();
            if (!string.IsNullOrEmpty(zone) && !string.IsNullOrEmpty(via))
                list.Add(new Destination(gate.Territory, gate.ViaAetheryteId, L.T("{0} — via {1}", zone, via)));
        }
        return list.OrderBy(d => d.Name).ToList();
    }

    public void Move(Vector3 position, bool fly) => nav.Move(position, fly);
    public void Interact(ObjectiveActor actor) { Target(actor.Id); interaction.Begin(actor); }
    public void AdvanceEventInteraction() => interaction.Advance();
    public void CancelEventInteraction() => interaction.Cancel();
    public void OpenGreensShop(VendorActor actor) { Target(actor.Id); greensShop.Open(actor); }
    public void AdvanceGreensShop() => greensShop.Advance();
    public void BuyGreens(GreensPurchase purchase) => greensShop.Buy(purchase);
    public void CloseGreensShop() => greensShop.Close();
    public void StopMovement() => nav.Stop();
    public unsafe void Mount() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
    public unsafe void Dismount() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
    public unsafe void Sync()
    {
        var manager = FateManager.Instance();
        if (manager != null && manager->GetCurrentFateId() != 0) manager->LevelSync();
    }
    public void SetCombat(bool enabled) => combat.SetEnabled(enabled);
    // Auto-attack (action 7) is only usable on things the player can fight.
    internal static unsafe bool Attackable(IGameObject obj) =>
        obj.Address != 0 && ActionManager.CanUseActionOnTarget(7, (NativeObject*)obj.Address);

    public void Target(ulong id)
    {
        var candidate = Services.Objects.FirstOrDefault(x => x.GameObjectId == id);
        // Never select a friendly NPC as a combat target.
        if (candidate is IBattleNpc && !Attackable(candidate)) return;
        var target = Services.Objects.FirstOrDefault(x => x.GameObjectId == id);
        if (target == null) return;
        ownedTarget = id;
        Services.Targets.Target = target;
    }
    public void Teleport(Destination destination)
    {
        travel.Teleport(destination);
    }
    public void ConfirmTeleport() => travel.ConfirmArrival();
    // Only while the local player is dead: the one Yes/No the game shows then is the return-to-home-point
    // prompt (or a raise offer). Nothing else is clicked.
    public unsafe void AcceptRespawn()
    {
        if (Services.Objects.LocalPlayer is not { IsDead: true }) return;
        var yesno = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)Services.GameGui.GetAddonByName("SelectYesno").Address;
        if (yesno != null && yesno->IsVisible) yesno->FireCallbackInt(0);
    }
    public void ChangeInstance(int instance)
    {
        ownsTravelService = true;
        Services.Plugin.GetIpcSubscriber<int, object>(IntegrationIds.TravelChangeInstance).InvokeAction(instance);
    }
    public void AethernetTeleport(uint shardId)
    {
        ownsTravelService = true;
        var accepted = Services.Plugin.GetIpcSubscriber<uint, bool>(IntegrationIds.TravelAethernetTeleportById).InvokeFunc(shardId);
        if (!accepted) Log(L.T("O módulo de viagens recusou o aethernet; nova tentativa em instantes."));
    }
    public void AcknowledgeTravelComplete() => ownsTravelService = false;
    public void Recover() => combat.Recover();
    public void Dispose()
    {
        try { End(); }
        finally { combat.Dispose(); }
    }
}
