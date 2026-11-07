# Experimental build validation

## 0.4 interface and companion validation

119 local tests pass with zero compilation warnings/errors. Eleven new companion tests cover default enablement, saved preferences and live settings, readiness/stock/active-timer checks, a single request and confirmation, timeout without repetition, refusal throttling, landing before NPC interaction, combat/Stop/death/goal interruptions, resummoning after expiry and Forlorn priority. The session-start gate is also tested. Translation keys and placeholders match in all six languages; source and compiled privacy checks pass.

The HTML design preview was reviewed in the browser, including navigation, supply inputs and the start reminder. It is a mockup, not a capture or test of native ImGui rendering. Game icons were checked against local game data. No game inputs, plugin reload or installation were performed during development.

Pending in game: inspect all sidebar pages and icons at normal and enlarged UI scale; confirm that Start and `/fatepilot start` require the reminder and Cancel/Stop keep automation inactive. In Supplies check Chocobo summoning is enabled, with Greens in the bag and companion unlocked: observe one use and an active timer, no use while already active, resummoning after expiry, and route continuation if stock or action availability is missing. Check a complete purchase/return/summon cycle separately. Stop during summoning must prevent later requests; the game may finish a previously accepted item action.

The user had not yet tested 0.3.1 at the time of this revision. Its native Talk-header reader, multi-page advancement and Yes confirmation remain pending; the correction is included unchanged in 0.4.

## 0.3.1 dialogue hotfix

User diagnostics on 0.3.0 confirmed travel, combat, completion counting and brief outside-FATE defense/resumption. Panaq Attack (1880) opened Talk, then stopped with an owned NPC binding and callback but no speaker match. Version 0.3.1 compares the localized NPC label with the live Talk header instead of UIModule.LastTalkName. It retains NPC/FATE binding, proximity, callback continuity and bounded interaction ownership. It does not read or record the dialogue body.

107 local tests pass with zero compile warnings/errors. New regression coverage checks complete speaker labels across whitespace, case and Unicode composition, rejects missing/wrong labels, and exercises five verified Talk pages followed by the matching Yes confirmation, level sync and combat. These tests simulate the dialogue evidence and runner; they do not validate native UI pointers or clicks in game.

Pending: update to 0.3.1 and observe an NPC-started FATE without manual clicks, including every Talk page, Yes confirmation, sync and combat. Capture Diagnostics if speakerAvailable/speakerMatch is false or if advancement fails. No extra dialogue plugin is required by this implementation.

## 0.3 experimental validation (historical)

No 0.3 game-client validation has been performed. The user reported NPC failures on 0.2.1: Salty Showdown timed out after 60 seconds without an interaction request; Panaq Attack sent an interaction but stopped after three seconds with both event IDs empty and a visible Talk window. A stopped runner cannot subsequently sync or engage a manually started event.

105 tests pass with zero compile warnings/errors. Coverage includes the previous controller checks, goal accounting, cap reserve, current-world changes, fixed-origin URL construction, verified NPC binding, multi-page local dialogue and confirmation ownership, dialogue-to-sync-to-combat, nearest-first selection, Forlorn priority, defense outside FATEs, live settings updates, and bounded Greens shopping with receipt confirmation and resumption. All six translation catalogs have matching keys and placeholders. These are simulations and static checks, not successful native game actions.

Still to test in game: NPC lookup and all dialogue pages/confirmation, automatic sync after activation, Forlorn spawning mid-combat or during collection, outside-FATE defense/resumption, edits during a FATE and teleport, complete Greens purchase/return cycle, cap readout/goals, and UI bars/translations at different scales. The public Faloop link `https://faloop.app/behemoth/fate` was verified in a browser: it selected Behemoth and normalized the address to `/fate`. Native current-world mapping across visits still needs in-game validation. No supported public report API has been verified; timers are not imported.

Validate locally with `./build.ps1 -ValidateOnly`; the following section records the released 0.2.1 baseline.

## Completed outside the game

- Release compilation: zero errors and zero warnings.
- 60 passing tests covering simulated controller behavior, repeated and partial hand-ins, escort following, NPC activation, delayed dialog context, dismount transitions, ownership rejection/timeouts, group-boss filtering, gemstone gains, costs, translations, sensitive API access and embedded assembly resources.
- Review of the contracts used against available integration source code.
- Manifest, author, version, package-content and SHA-256 checks.

## Pending: live client

The user reported initial combat working, but subsequent NPC tests on 0.2.1 still failed as described above. Version 0.3 adds object-handle/FATE-association binding and scoped local dialogue recognition. Retest without manual clicks; simulations do not establish that a native interaction succeeded.

The following is a test plan, not a list of observed in-game results. Supervised testing helps find functional defects; it does not remove the account-policy risk described in the README.

1. Load the plugin and check all four modules in Diagnostics. Open/close the panel, check the logo and cat portrait, switch through the six languages and look for clipped text.
2. Outside combat, with other automation stopped, choose a familiar zone, disable instance rotation and set a one-FATE goal with an unlocked return city. Check Stop during movement and restoration of prior settings.
3. Restart and observe selection, mounting, navigation, landing, synchronization, ability execution and positioning. Confirm the completion counter and return trip. Absence of an error alone is not evidence that every stage works.
4. After instance discovery at an aetheryte, enable rotation and observe a complete instance change. Then test a two-zone route.
5. Test an eligible paid destination with a ticket available and compare inventory/gil before and after. Test the switch off with known game settings. Leave gil fallback disabled until this flow is verified, then check minimum fare and budget behavior.
6. Compare presets and ability settings before and after Stop. Test recovery from a controlled interruption only after the normal flow works.
7. Check that **Buy my cat a treat** opens only the displayed Ko-fi address and only after clicking. Local tests inspect this call site; they do not open the browser automatically.
8. Check the installer icon, expanded cat portrait, heart button and centered About layout. In Route, inspect expansion groups and zone artwork, disabled destinations, selection, reordering and arrival-aetheryte selection.
9. Start an NPC-triggered collection FATE. Observe ground pickups and enemy drops, return at 15 items, confirmed hand-in and resumed collection. Repeat a second batch within the same event. Test a partial batch after 45 seconds and near the ending phase. Verify that the FATE counter changes only after observed success, not after each batch.
10. Follow an escort through quiet intervals and combat waves. Check Stop during an NPC interaction and confirm that unrelated dialogs are not advanced. Record unsupported interaction flows without bypassing ownership checks.
11. Confirm Daivadipa/Chi are ignored with the default filter and compare gemstone increases with the session counter. Check the active-event timer; respawn forecasting is not included.
12. Retest a preparing FATE without manually clicking its NPC. Diagnostics should distinguish the approach, interaction request, recognized dialogue and Running transition. If it stops, capture the event-handler diagnostics and state. Do not interpret a successful manual start as automatic activation.

If a failure occurs, stop and review local diagnostics. **Copy diagnostics** acts only when clicked; inspect the text before sharing. There is no automatic support upload.

Record client, Dalamud and dependency versions, the step performed, expected behavior and observed behavior. Character/account names are not needed.
