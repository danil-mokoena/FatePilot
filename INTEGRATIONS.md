# Integrations

FatePilot uses separately installed components through local IPC contracts. Their names identify technical dependencies, not the origin of FatePilot's implementation or visual identity.

| Role | Component | Interfaces used |
|---|---|---|
| Combat abilities | [WrathCombo](https://github.com/PunishXIV/WrathCombo) | Temporary lease, job preparation, configuration, activation and release |
| Positioning | [BossMod](https://github.com/awgil/ffxiv_bossmod) | Configuration, movement presets and temporary obstacle maps |
| Navigation | [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | Cancelable pathfinding, MoveTo, Stop and reachable-point queries |
| Instance switching | [Lifestream](https://github.com/NightmareXIV/Lifestream) | ChangeInstance, IsBusy, Abort, CanChangeInstance and instance discovery |

Reference installations reviewed during development included BossMod 7.5.7.20, WrathCombo 1.0.4.27 and vnavmesh 1.2.3.14. This is not live compatibility certification. Diagnostics checks the required API availability before starting; upstream contract changes can require adapter updates.

Enable **Instance Switcher** in the travel component, and open the aetheryte's instance menu once so it can discover available instances. Zone teleports and Aetheryte Tickets are requested through native client functions exposed by FFXIVClientStructs.

Do not load multiple positioning variants that register the same IPC channels. FatePilot rejects that setup to avoid controlling the wrong component. Stop other combat, navigation and travel automation before starting.

For positioning, FatePilot creates a temporary movement-only preset, records the previous state and disables follow control for the session. Ability execution uses a separate lease. Ending the session releases that lease, restores prior settings and removes the temporary preset. External control changes interrupt the session; presets changed externally are preserved during normal restoration. A local recovery record supports restoration after interruption.

These are independent programs with their own behavior and permissions. For example, the ability component consults a remote IPC status resource, and navigation can download navigation data. Requests may be triggered through their APIs. FatePilot's lack of its own HTTP client does not make the whole setup offline.

Dependency implementations, DLLs and resources are not bundled in this project. Their licenses and installations remain separate. Exact wire identifiers are centralized in `src/FatePilot/IntegrationIds.cs` and must retain their original spelling for compatibility.

Chocobo summoning uses the local client directly: companion time from `UIState.Buddy.CompanionInfo.TimeLeft`, item readiness through `ActionManager.GetActionStatus`, and `UseAction(Item, 4868, extraParam: 0xFFFF)`. The item is checked again before the call, and the controller waits for an active timer before considering it confirmed. This adds no plugin dependency or network access. Native behavior still requires live validation.
