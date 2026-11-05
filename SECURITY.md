# Security review — FatePilot 0.2

Reviewed November 5, 2026. Scope: this project's source and Release assemblies. Methods: manual review, simulated controller tests and assembly metadata inspection. Initial combat was reported working by a user; the new native objective adapters have not been validated live. No process-wide network capture has been performed.

## Findings

No HTTP client, socket, webhook, telemetry service, player-name collection, account identifier collection, credential access or chat collection was identified in FatePilot's runtime code. Its own assemblies do not reference network types, process creation, dynamic assembly loading or project-declared P/Invoke methods. Necessary native game access uses structures supplied by the host.

The only web address in runtime code is `https://ko-fi.com/mokoena`. Dalamud's browser utility opens it only after the user clicks **Buy my cat a treat**. FatePilot adds no character, world, session or tracking parameters. The resulting site visit follows the browser's and Ko-fi's normal behavior.

The logo, cat portrait and six language catalogs are embedded locally. Zone artwork is read from the local game files. The plugin manifest disables the packager's feedback option. There is no proprietary account, server, updater, advertising system or remotely loaded code. Installation, updates and the installer icon are handled by Dalamud and require downloads from GitHub.

## Data inventory

| Information | Purpose | Storage or destination |
|---|---|---|
| Language, route, filters, return city, ticket and budget options | Preferences | Local configuration managed by Dalamud |
| Previous positioning state and preset names | Restoration | `session-recovery.json` in the plugin configuration folder; removed after successful restoration |
| Own character's state, position, job and level | Session control | Transient memory; not exported |
| FATEs, associated NPC/object identifiers and positions, aetherytes | Target, objective and route selection | Memory; no nearby-player history |
| Collection-item, gemstone, ticket/gil counters and previous target identifier | Session and restoration | Memory |
| Most recent 120 session events | Diagnostics | Bounded in-memory queue, not a persistent plugin history |
| Update or restoration failure | Technical notice | Generic host-log message without a player payload |
| Copied diagnostics | Explicit user action | Clipboard only when **Copy diagnostics** is clicked |

Preset labels are user-defined: a recovery file can contain personal text if the user put it in those labels. FatePilot does not upload the file. Object-table enumeration filters NPCs and aetherytes and does not read other characters' names. The previous target is retained only as a transient identifier for restoration.

When the panel is closed and no session is running, periodic world reads for the panel are skipped. Pending temporary-resource cleanup can still finish.

## Applied protections

- Fixed API contracts; no arbitrary chat command assembled from names or configuration.
- Fixed external link opened by a click, with bundled UI images and translations.
- Temporary ability lease, explicit deactivation and release on shutdown.
- Movement-only preset verified after creation; external control conflicts stop the session.
- Cancellation of pending pathfinding and rejection of late results after Stop.
- Restoration of prior settings, including forced-disabled state, while preserving externally changed presets.
- Recovery record restricted to 64 KiB with structural validation before restoration.
- Cancellation only of the session's own Teleport cast; no generic Yes/No dialog acceptance.
- FATE interactions revalidate object role and distance. Talk/Yes-No advancement requires a matching event and object in the native event context, with ownership expiring after 15 seconds. Pre-existing dialogs are not adopted; arbitrary menu choices are not automated.
- Travel-budget checks before requesting a teleport; ticket consumption checked on arrival.
- Deadlines for travel, instance changes, synchronization and combat; temporary FATE exclusion after lack of progress.
- Stops on death, logout, job change, duty/PvP entry, unexpected travel or interaction.
- Regression guards for known sensitive APIs, unexpected endpoints, identity access and missing embedded resources.

## Review limits

Static guards are regression checks, not a permissions sandbox or proof that every possible defect is absent. The plugin runs inside the game process and uses native APIs; client, host or dependency changes may cause failures. The test suite does not execute the adapters in a real client.

The [integrations](INTEGRATIONS.md) have their own code and network behavior. Their entire implementations and the complete game process have not been audited. Some network requests can be triggered by using their APIs. Findings about FatePilot's own telemetry cannot be generalized to all installed software.

Native ticket behavior still needs validation with actual Teleport settings. Arrival checks detect inconsistencies but do not undo completed charges. Stop cannot reverse a zone transition already accepted by the game. Technical privacy measures do not protect an account from penalties for automation; see the warning in the [README](README.md).

## Reproduce and report

Run `./build.ps1` with .NET SDK 10, PowerShell 7 and Dalamud API 15 references. It builds, runs 52 tests, inspects assembly references/methods/resources, packages the output and generates SHA-256 hashes. Automated criteria are in `tests/FatePilot.Tests/PrivacyAudit.cs`. Manually review adapter changes as well.

For a security concern, provide a minimal reproduction and affected version. Do not include credentials, character/account identifiers or unreviewed private logs in public issues. The plugin does not upload support diagnostics automatically.
