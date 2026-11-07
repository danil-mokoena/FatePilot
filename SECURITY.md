# Security review — FatePilot 0.4 experimental

Reviewed November 6, 2026. Scope: this project's source and Release assemblies. Methods: manual review, simulated controller tests and assembly metadata inspection. Initial combat was reported working by a user; the new native objective and shopping adapters have not been validated live. No process-wide network capture has been performed.

## Findings

No HTTP client, socket, webhook, telemetry service, player-name collection, account identifier collection, credential access or chat collection was identified in FatePilot's runtime code. Its own assemblies do not reference network types, process creation, dynamic assembly loading or project-declared P/Invoke methods. Necessary native game access uses structures supplied by the host.

Runtime browser links are restricted to `https://ko-fi.com/mokoena` and the fixed `https://faloop.app` origin. The Faloop path is `/world-name/fate`, derived from the current public game-world name using ASCII letters only; invalid/unavailable names fall back to `/fate`. These links open only when their buttons are clicked. The world-filtered visit shares the public world name and normal browser connection metadata with the website. FatePilot sends no character identifiers, coordinates, session history or reports and does not perform background requests, read credentials or embed a web view.

The logo, cat portrait and six language catalogs are embedded locally. Zone artwork is read from the local game files. The plugin manifest disables the packager's feedback option. There is no proprietary account, server, updater, advertising system or remotely loaded code. Installation, updates and the installer icon are handled by Dalamud and require downloads from GitHub.

## Data inventory

| Information | Purpose | Storage or destination |
|---|---|---|
| Language, route, filters, return city, ticket and budget options | Preferences | Local configuration managed by Dalamud |
| Previous positioning state and preset names | Restoration | `session-recovery.json` in the plugin configuration folder; removed after successful restoration |
| Own character's state, position, job and level | Session control | Transient memory; not exported |
| Current game-world ID, name and data center | Tracker context, including visits | Memory; only the public world name is included in the browser URL on an explicit click, or copied on request |
| FATEs, associated NPC/object identifiers and positions, aetherytes | Target, objective and route selection | Memory; no nearby-player history |
| Collection-item, gemstone, ticket/gil and Greens counters, bag capacity and previous target identifier | Session, restocking and restoration | Memory |
| Verified NPC's localized sheet label and current Talk speaker label | Scope local NPC dialogue progression | Compared transiently; no dialogue body, chat history or speaker labels logged or exported |
| Chocobo remaining time and Greens action availability | Avoid redundant item use and confirm summoning | Memory; no companion name read or exported |
| Most recent 120 session events | Diagnostics | Bounded in-memory queue, not a persistent plugin history |
| Update or restoration failure | Technical notice | Generic host-log message without a player payload |
| Copied diagnostics | Explicit user action | Clipboard only when **Copy diagnostics** is clicked |

Preset labels are user-defined: a recovery file can contain personal text if the user put it in those labels. FatePilot does not upload the file. Object-table enumeration filters NPCs and aetherytes and does not read other characters' names. The previous target is retained only as a transient identifier for restoration.

When the panel is closed and no session is running, periodic world reads for the panel are skipped. Pending temporary-resource cleanup can still finish.

## Applied protections

- Fixed API contracts; no arbitrary chat command assembled from names or configuration.
- Fixed-origin browser links opened by a click, with bundled UI images and translations. URL construction rejects path/query/host injection. Source guards restrict origins and click handlers; the tracker adds no HTTP/socket APIs.
- Temporary ability lease, explicit deactivation and release on shutdown.
- Movement-only preset verified after creation; external control conflicts stop the session.
- Cancellation of pending pathfinding and rejection of late results after Stop.
- Restoration of prior settings, including forced-disabled state, while preserving externally changed presets.
- Recovery record restricted to 64 KiB with structural validation before restoration.
- Cancellation only of the session's own Teleport cast; no generic Yes/No dialog acceptance.
- FATE interactions revalidate object role and distance. A pending request waits up to three seconds for native context without clicking. Talk/Yes-No advancement requires the expected object and FATE director or associated array event; a native selector requires one unambiguous matching handler. Ownership expires after 15 seconds. Pre-existing dialogs are not adopted; arbitrary menu choices are not automated.
- Local-dialogue fallback applies only when global event fields are empty: the exact session NPC must remain targeted, nearby and bound to the selected FATE. The Talk speaker must match that NPC's localized game-sheet label and expose a callback receiver. A subsequent Yes/No must use the same receiver within five seconds of the matched Talk. Other menus, different speakers/actors and populated unrelated events invalidate this fallback. Brief page transitions do not discard the existing lease; its total 15-second deadline remains.
- Restocking is opt-in and restricted to item 4868, vendor 1001787, shop 0x0004000E and the verified 36-gil unit price. It never sells/discards items. Native shop ownership, mode, quantity, capacity and funds are checked before every purchase. Inventory and gil must confirm the receipt before another batch. No automatic retry after an uncertain purchase.
- Chocobo summoning is enabled by default and can be disabled independently of restocking and restricted to item 4868. Live companion time, stock, action availability, animation/queue state and player conditions are checked before use. A sent request waits up to ten seconds for an active companion; a timeout suspends further use for the session. Rejected requests are throttled for sixty seconds. No active-timer extension, arbitrary item-use command or additional plugin is introduced.
- Every new session requires the supervision reminder to be confirmed, including command starts. Closing/canceling it or stopping cancels the pending request; confirmation is consumed once.
- Live settings are copied and validated, then applied at an idle selection boundary. In-flight actions and payment reservations are not replaced or reset; invalid updates preserve active settings.
- Mount/dismount requests permit up to five seconds of transient Occupied state without an open dialog. That grace period never authorizes dialogue callbacks.
- Travel-budget checks before requesting a teleport; ticket consumption checked on arrival.
- Deadlines for travel, instance changes, synchronization and combat; temporary FATE exclusion after lack of progress.
- Stops on death, logout, job change, duty/PvP entry, unexpected travel or interaction.
- Regression guards for known sensitive APIs, unexpected endpoints, identity access and missing embedded resources.

## Review limits

Static guards are regression checks, not a permissions sandbox or proof that every possible defect is absent. The plugin runs inside the game process and uses native APIs; client, host or dependency changes may cause failures. The test suite does not execute the adapters in a real client.

The [integrations](INTEGRATIONS.md) have their own code and network behavior. Their entire implementations and the complete game process have not been audited. Some network requests can be triggered by using their APIs. Findings about FatePilot's own telemetry cannot be generalized to all installed software.

Native ticket behavior still needs validation with actual Teleport settings. Arrival checks detect inconsistencies but do not undo completed charges. Stop cannot reverse a zone transition already accepted by the game. Technical privacy measures do not protect an account from penalties for automation; see the warning in the [README](README.md).

## Reproduce and report

Run `./build.ps1 -ValidateOnly` with .NET SDK 10, PowerShell 7 and Dalamud API 15 references. It builds, runs 119 tests and inspects assembly references/methods/resources without producing release packages or changing the catalog. Automated criteria are in `tests/FatePilot.Tests/PrivacyAudit.cs`. Manually review adapter changes as well.

For a security concern, provide a minimal reproduction and affected version. Do not include credentials, character/account identifiers or unreviewed private logs in public issues. The plugin does not upload support diagnostics automatically.
