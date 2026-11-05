<p align="center"><img src="src/FatePilot/Assets/logo.png" alt="FatePilot guiding star" width="140" /></p>
<h1 align="center">FatePilot</h1>
<p align="center"><strong>Chart your route. Find your destiny.</strong><br />By Mokoena</p>
<p align="center"><a href="#installation">Installation</a> · <a href="#dependencies">Dependencies</a> · <a href="#features">Features</a> · <a href="SECURITY.md">Privacy &amp; security</a> · <a href="https://github.com/danil-mokoena/FatePilot/releases">Releases</a></p>

FatePilot is an independent Dalamud plugin that coordinates FATE sessions across selected zones and instances. Set a route, choose a return goal, manage teleport costs and follow the session from a compact, multilingual dashboard.

**Status:** experimental 0.2.0 · **Dalamud API:** 15 · **Build:** .NET 10 / C#

> [!WARNING]
> **Use at your own risk.** Square Enix prohibits unauthorized third-party tools and automated or unattended play. Using this plugin may result in account suspension or a permanent ban. See the official [third-party tools statement](https://na.finalfantasyxiv.com/lodestone/topics/detail/68f4c4d3bdea34f732ee8db3425122b8b8d6ec3b) and [prohibited activities policy](https://support.na.square-enix.com/faqarticle.php?id=5382&kid=68216).
>
> FATE farming is associated with botting and RMT in [community discussions](https://www.reddit.com/r/ffxivdiscussion/comments/12ki4a0/fate_botting_in_endwalker_zones/). Claims that FATEs receive particular enforcement priority are community perceptions, not a confirmed official policy. FatePilot offers no protection against detection or account penalties. You are responsible for the decision to use it.

> [!IMPORTANT]
> **This is a preview build.** Initial combat has been reported working by a user. The new collection, NPC interaction and escort adapters still need in-game validation, as do the revised UI and full travel/ticket cycle. The 52 local tests cover simulated controller behavior, translations and static privacy checks; they do not execute the native adapters in a live client.

## Features

| Area | Available in 0.2 |
|---|---|
| FATE selection | Combat, collection, escort and defense; bounded NPC activation; level/progress/time filters, bonus preference and an ignore list |
| Collection | Ground pickups, objective enemies and repeated NPC hand-ins; default batch of 15, with partial deliveries after 45 seconds or near the end |
| Escort and defense | Follow the associated friendly objective and fight event enemies |
| Travel | Zone image cards grouped by expansion, reorderable routes, discovered instance rotation, mounts, unlocked flight and level sync |
| Session goals | Return to a selected city after a chosen number of observed successful FATEs |
| Teleport costs | Aetheryte Ticket switch, minimum fare, explicit gil fallback and a session gil budget |
| Recovery | Stuck-route handling, operation timeouts, control conflict detection and restoration of previous settings |
| Interface | Slate and gold design, compact Start/Stop controls, session FATE/gemstone counters, active-event timer and local diagnostics |
| Group bosses | Known large-group bosses such as Daivadipa and Chi excluded by default |

The plugin does not start farming simply because it is loaded. Start a session explicitly from the panel or command.

## Requirements

- FFXIV on Windows with XIVLauncher and a working **Dalamud API 15** installation.
- All four integrations in the [dependency table](#dependencies), installed and enabled separately.
- A combat job supported and configured in the ability integration, with IPC control available.
- Unlocked aetherytes for every selected route/return destination. A mount for mounted travel and unlocked flight for flying routes.
- Available gil or Aetheryte Tickets according to the chosen payment settings.
- For instance rotation: an instanced zone, Instance Switcher enabled and the available instances discovered at an aetheryte.

You do **not** need the .NET SDK to install the compiled plugin. The SDK and PowerShell requirements below are for building from source.

## Installation

1. Install and enable the [required dependencies](#dependencies).
2. Open **Dalamud Settings → Experimental → Custom Plugin Repositories**.
3. Add the following URL, enable the entry and save:

   ```text
   https://raw.githubusercontent.com/danil-mokoena/FatePilot/main/pluginmaster.json
   ```

4. Open the plugin installer with `/xlplugins`, search for **FatePilot** and install it.
5. Open the panel with `/fatepilot`.

Use the URL above for installation. It points to the plugin catalog; the normal GitHub project page is for browsing the source and documentation. Future releases will use the same catalog URL.

For local development, extract the plugin ZIP from [Releases](https://github.com/danil-mokoena/FatePilot/releases), keep the two DLLs, dependency JSON and manifest together, and add the full path to `FatePilot.dll` under **Dev Plugin Locations** instead. Do not load a development copy and the installed copy simultaneously.

## Dependencies

| Component | Role | Setup notes |
|---|---|---|
| [WrathCombo](https://github.com/PunishXIV/WrathCombo) | Combat abilities | IPC control must be available; FatePilot requests a temporary lease |
| [BossMod](https://github.com/awgil/ffxiv_bossmod) | Positioning | Use the original compatible plugin; do not load conflicting variants together |
| [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | Pathfinding | The current zone's navigation mesh must be ready |
| [Lifestream](https://github.com/NightmareXIV/Lifestream) | Instance switching | Enable Instance Switcher and open the aetheryte's instance menu once to discover the options |

These integrations are installed separately. Their implementations are not bundled with FatePilot. The **Diagnostics** tab checks the required APIs before a session starts. See [integration details](INTEGRATIONS.md) for compatibility and restoration behavior.

## First session

Stop other combat, movement and travel automation first. In **Session**, choose a language, a return city and a FATE goal. In **Route**, click zone cards under each expansion to select a route, then use the arrows to reorder it. If a zone has multiple unlocked aetherytes, choose its arrival point. Review **Costs** and **FATEs** before starting.

The catalog reads the client's overworld zones and loading artwork locally: 48 zones in the data used for this build. Zones without a direct unlocked teleport destination remain visible but cannot be added. Some older zones share regional artwork. The selected route repeats until a goal or Stop ends the session.

### Collection and escorts

Collection uses the selected event's item and NPC identifiers, independent of the game's language. By default, 15 held items trigger a hand-in trip. Smaller batches are delivered after 45 seconds holding items, with fewer than 90 seconds remaining, at 80% progress or during the ending phase. After a hand-in, collection resumes if the event remains active. A batch is never treated as a fixed completion requirement. Attackers are handled before approaching the recipient.

The batch size is configurable in **FATEs**. Collection and escort participation can also be disabled independently. Interactions are limited to objects associated with the selected FATE, with bounded attempts and timeouts. Unsupported dialogue flows stop rather than click arbitrary menu choices. These native flows are experimental and need supervised validation.

Known large-group encounters are excluded by default. This is a maintained ID list, not a universal difficulty detector; add other unsuitable events to your ignore list. The gemstone counter sums observed increases during the session, excluding the starting balance. It cannot measure rewards lost to the currency cap or distinguish simultaneous gains and spending. The timer shows the currently active FATE's remaining time; there is no Faloop integration or respawn prediction in this release.

For initial functional testing, select one familiar zone and a goal of one FATE, then observe the entire cycle. This helps validate behavior; it does not reduce or remove the account-policy risk. Follow the [validation checklist](VALIDATION.md) before relying on additional features.

### Tickets and gil

Enable **Use Aetheryte Tickets** to request ticket payment for eligible paid teleports. Free destinations do not consume tickets. A minimum fare lets smaller trips use gil, and the fallback to gil when tickets run out is an explicit separate choice. The budget also applies to the return trip.

Open the game's Teleport window once before the first ticket test. FatePilot uses separate native entry points for ordinary and ticket teleports rather than accepting arbitrary Yes/No dialogs. Actual confirmation and consumption behavior still needs testing with your client settings. An inconsistency stops the session, but cannot undo a charge already made by the game.

### Stopping

Use **Stop** or `/fatepilot stop`. FatePilot cancels its pending paths, releases its ability lease and restores the previous positioning setup. It attempts to cancel its own Teleport while it is still being cast; an area transition already accepted by the game may finish afterward.

If a previous session left a recovery record, use **Restore previous session** while stopped. Death, logout, a job change, duty/PvP entry, unexpected travel and control conflicts also interrupt the session.

## Languages

English · 日本語 · Deutsch · Français · **Português (Brasil)** · Español

**Auto** follows the official game-client language. Brazilian Portuguese and Spanish can be selected manually and are remembered. FATE and zone names continue to follow the game client's language. All UI catalogs are included locally; native-speaker review is still welcome.

## Commands

| Command | Action |
|---|---|
| `/fatepilot` | Open the panel |
| `/fatepilot start` | Start with the saved settings |
| `/fatepilot stop` | Stop and restore previous settings |
| `/fatepilot status` | Print the current state to local chat |
| `/fatepilot recover` | Restore a previous session's local recovery record while stopped |

## Scope and limitations

Supported overworld rules include combat, collection, escort and defense. A preparing event is eligible only when it exposes a starter NPC. Event-specific mechanics or dialogue variants may still be unsupported. Completion means an observed successful end after participation; a disappearing FATE without confirmation does not count toward the return goal.

Profiles, persistent history, job queues, repairs, consumables, voucher purchases, party coordination and time/inventory/equipment stop conditions are not implemented in 0.2. There are no GM detection or concealment features.

### Next milestones

The next priority is live validation of collection hand-ins, NPC-started events and escorts, followed by fixes for individual event mechanics. External tracker integration remains a separate future investigation.

## Privacy and security

FatePilot has no telemetry service, account system or automatic data-upload client. Settings and recovery data are local; the most recent 120 diagnostic events stay in memory. The logo, cat portrait and language catalogs are bundled. Diagnostic copying and the Ko-fi link require an explicit click.

Dependencies have their own behavior and may make network requests, including requests triggered through their APIs. The security review covers FatePilot's own code and compiled assemblies; it is not an audit of the entire game process. Read the [security review and data inventory](SECURITY.md).

## Building and contributing

Requires **.NET SDK 10**, **PowerShell 7** and local **Dalamud API 15** development references.

```powershell
./build.ps1
# Or select a specific SDK executable:
./build.ps1 -Dotnet 'C:/path/to/dotnet.exe'
```

The script builds Release, runs the tests, inspects the assemblies, prepares the matching catalog and produces plugin/source ZIPs plus SHA-256 hashes in `artifacts/`. The SDK and packager are pinned to 15.0.0. Build-time package restoration may access package servers.

`src/FatePilot.Core` contains the game-independent session controller. `src/FatePilot` contains the UI and adapters. `tests/FatePilot.Tests` contains the behavioral and privacy checks. Exact external API names are centralized in `IntegrationIds.cs`.

To regenerate release metadata separately, run `./prepare-repository.ps1 -ReleaseDirectory './artifacts' -Tag 'v0.2.0'`. It verifies the DLL/manifest version before writing `pluginmaster.json`; it does not publish anything itself. Publish the release ZIP before updating the public catalog.

For bug reports, include versions, reproduction steps and expected/observed behavior. Review diagnostics before sharing them; character and account names are not required. Translation corrections are welcome.

## Support

<p><a href="https://ko-fi.com/mokoena"><img src="src/FatePilot/Assets/cat.png" alt="Mokoena's cat" width="192" /></a></p>

**[♥ Buy my cat a treat](https://ko-fi.com/mokoena)** — optional support for the project and a very important snack budget.

---

FatePilot is an independent project by Mokoena. It is not affiliated with or endorsed by Square Enix or the Dalamud team. FINAL FANTASY XIV belongs to Square Enix.
