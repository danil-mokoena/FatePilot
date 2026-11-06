# Faloop integration — 0.3 design

By Mokoena. Companion link implemented in 0.3; in-plugin report retrieval remains a design.

## Intended experience

Open **Faloop** in FatePilot. The top of the panel identifies the character's current world and data center. World visits change that context automatically; home world is never substituted when current-world information is unavailable. Loading and logout clear the context instead of displaying stale data.

The eventual read-only view would show special FATEs for that world and instance, with the boss/FATE name, zone, reported state, respawn window and age of the underlying report. Opening the panel must not join a FATE, teleport, start combat, send a report or change the group-boss exclusion setting.

| Panel area | Content | Behavior |
|---|---|---|
| Context | Current world · data center | Follow visits; clear while changing worlds |
| Source | Faloop · connection/freshness state | Distinguish downloaded-at from last-reported-at |
| Event cards | Special FATE, zone, instance, reported window | Community estimate; no exact-spawn guarantee |
| Actions | Refresh, open source | Explicit actions; no reporting or combat control |
| Empty state | No data / no access / offline | Never interpret missing data as cooldown |

## Implemented first step

- Local current-world metadata from the host's `IPlayerCharacter.CurrentWorld` and the game's world/data-center sheets.
- A localized tracker panel, copy-world-name action and click-only `https://faloop.app/world-name/fate` link. The path uses a validated ASCII game-world name and a fixed HTTPS origin; unknown/unavailable names use `/fate`.
- No automatic browser launch, network client, API polling, account, credentials or reports.
- The browser link selects the current world on Faloop. Timers are not displayed inside FatePilot.
- Existing automation filters remain unchanged, including default exclusion of known large-group bosses.

The name **Faloop** identifies the optional external service. It is not a plugin dependency or an affiliation claim.

## Evidence and unresolved contract

The [Faloop FATE page](https://faloop.app/fate) was inspected through its normal guest UI. Its public route structure was checked, and opening [the Behemoth FATE link](https://faloop.app/behemoth/fate) on November 6, 2026 selected Behemoth/Primal from a previous Excalibur selection. Faloop normalized the address back to `/fate`. Initial setup notes that timers are unavailable on some data centers. This observed URL behavior is not a guaranteed long-term API contract.

A supported public third-party read API has **not** been verified. This does not establish that one does not exist. Do not read browser tokens or local storage, scrape authenticated data, or treat internal endpoints as a supported integration contract. Confirm an appropriate interface and its usage terms before adding report retrieval. No contact or access request has been sent to the service operators.

## Second step, once a suitable interface is confirmed

1. Resolve the game's current world to the service's documented identifier. Reject unknown mappings; never silently use a home world or arbitrary default.
2. Add an explicit opt-in for in-plugin requests. Explain that the service receives the requested world/event filter and normal connection metadata. Do not send character names, account/character IDs, coordinates, nearby-player data, session history or automatic reports.
3. Keep a bounded in-memory response cache. Respect documented rate limits, timeouts and retry guidance. Stop requests on opt-out/logout and cancel the prior request on world changes. Discard late responses whose world or generation no longer matches.
4. Treat report age separately from fetch age. Show unavailable/stale data plainly; reconnecting does not make an old report current. Preserve the source's uncertainty and distinguish a reported spawn from an open probability window.
5. Use only structured, validated fields. External labels are display data, never commands, URLs or instructions. Any browser action remains restricted to the verified service origin.
6. Keep the tracker informational. Event selection must not import a tracker row into automated combat, especially Daivadipa, Chi and other large-group encounters.

No embedded login or new account is planned. If supported access cannot be obtained, keep the click-only companion view and describe its limitation honestly.

## Acceptance cases

- A visitor sees the visited world; a data-center visit updates both fields.
- Loading/logging out clears stale context; an unknown world shows unavailable.
- A late reply for the previous world is discarded.
- No report, closed window and old report are distinct states.
- The source going offline does not stop ordinary FATE automation.
- Viewing a boss report never triggers movement, targeting, interaction or combat.
- Opting out stops requests and clears tracker data.
- All user-visible strings exist in English, Japanese, German, French, Brazilian Portuguese and Spanish.

## Other 0.3 work

Session goals are implemented locally with tests: earned gemstones, elapsed time and a gemstone-cap reserve. Completing a selected FATE and its hand-ins remains subject to the existing safety checks and timeouts; a failure can still stop the session.

Gysahl Greens restocking is implemented with separate trigger/target quantities, a session purchase budget, verified vendor/shop/item identity, observed receipt confirmation and resumption of the prior route. Item 4868, price 36 gil, vendor Bango Zango (1001787), shop 0x0004000E and location were checked in local game data. The native shop/path/return flow has not been validated in a live client.
