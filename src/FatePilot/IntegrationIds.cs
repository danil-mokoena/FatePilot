namespace FatePilot;

// External API contracts. Exact wire identifiers are required for interoperability.
// No implementation or branding is imported from these services.
internal static class IntegrationIds
{
    public const string AbilitiesLeaseCallback = "FatePilot.WrathComboCallback";
    public const string AbilitiesRegisterForLeaseWithCallback = "WrathCombo.RegisterForLeaseWithCallback";
    public const string AbilitiesReleaseControl = "WrathCombo.ReleaseControl";
    public const string AbilitiesSetAutoRotationConfigState = "WrathCombo.SetAutoRotationConfigState";
    public const string AbilitiesSetAutoRotationState = "WrathCombo.SetAutoRotationState";
    public const string AbilitiesSetCurrentJobAutoRotationReady = "WrathCombo.SetCurrentJobAutoRotationReady";
    public const string ConflictingPositioningPlugin = "BossModReborn";
    public const string NavigationNavIsReady = "vnavmesh.Nav.IsReady";
    public const string NavigationNavPathfindCancelable = "vnavmesh.Nav.PathfindCancelable";
    public const string NavigationPathIsRunning = "vnavmesh.Path.IsRunning";
    public const string NavigationPathMoveTo = "vnavmesh.Path.MoveTo";
    public const string NavigationPathStop = "vnavmesh.Path.Stop";
    public const string NavigationQueryMeshNearestPointReachable = "vnavmesh.Query.Mesh.NearestPointReachable";
    public const string PositioningAutorotationMiscAINormalMovement = "BossMod.Autorotation.MiscAI.NormalMovement";
    public const string PositioningAutorotationMiscAIStayCloseToTarget = "BossMod.Autorotation.MiscAI.StayCloseToTarget";
    public const string PositioningConfiguration = "BossMod.Configuration";
    public const string PositioningFollowPreset = "VBM Multibox";
    public const string PositioningObstacleMapClearTempMap = "BossMod.ObstacleMap.ClearTempMap";
    public const string PositioningObstacleMapGenerate = "BossMod.ObstacleMap.Generate";
    public const string PositioningObstacleMapGetGenerationStatus = "BossMod.ObstacleMap.GetGenerationStatus";
    public const string PositioningObstacleMapHasTempMap = "BossMod.ObstacleMap.HasTempMap";
    public const string PositioningPlugin = "BossMod";
    public const string PositioningPresetsCreate = "BossMod.Presets.Create";
    public const string PositioningPresetsDelete = "BossMod.Presets.Delete";
    public const string PositioningPresetsGet = "BossMod.Presets.Get";
    public const string PositioningPresetsGetActiveList = "BossMod.Presets.GetActiveList";
    public const string PositioningPresetsSetActiveList = "BossMod.Presets.SetActiveList";
    public const string PositioningPresetsSetForceDisabled = "BossMod.Presets.SetForceDisabled";
    public const string TravelAbort = "Lifestream.Abort";
    public const string TravelAethernetTeleportById = "Lifestream.AethernetTeleportById";
    public const string TravelCanChangeInstance = "Lifestream.CanChangeInstance";
    public const string TravelChangeInstance = "Lifestream.ChangeInstance";
    public const string TravelGetCurrentInstance = "Lifestream.GetCurrentInstance";
    public const string TravelGetNumberOfInstances = "Lifestream.GetNumberOfInstances";
    public const string TravelIsBusy = "Lifestream.IsBusy";
    public const string TravelTeleport = "Lifestream.Teleport";
}
