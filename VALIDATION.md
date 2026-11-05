# Experimental build validation

## Completed outside the game

- Release compilation: zero errors and zero warnings.
- 52 passing tests covering simulated controller behavior, repeated and partial hand-ins, escort following, NPC activation, group-boss filtering, gemstone gains, costs, translations, sensitive API access and embedded assembly resources.
- Review of the contracts used against available integration source code.
- Manifest, author, version, package-content and SHA-256 checks.

## Pending: live client

The user reported that initial combat works in 0.1.0. This does not validate the new 0.2 objective adapters or the rest of the following checklist.

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

If a failure occurs, stop and review local diagnostics. **Copy diagnostics** acts only when clicked; inspect the text before sharing. There is no automatic support upload.

Record client, Dalamud and dependency versions, the step performed, expected behavior and observed behavior. Character/account names are not needed.
