# FatePilot 0.4 interface

This experimental revision builds on 0.3.1 and retains its NPC dialogue correction.

## Layout

- Persistent left navigation: Session, Route, FATEs, Supplies, Faloop, Diagnostics, About and Settings.
- A compact status panel stays visible above the scrolling page, with Start/Stop and the active FATE progress and timer.
- Session groups counters, return goals and movement preferences into outlined panels.
- FATE rows place their native icon beside the name. Progress and remaining time sit to the right when there is room and below the name at smaller widths. A checked row means the FATE is included; known group bosses remain disabled while their exclusion setting is enabled.
- Supplies groups Greens into an inventory row: item, current stock, buy-at threshold and target stock, followed by a stock bar and session budget. Further explanation and presets are collapsed by default.
- A companion panel above stock offers independent, Chocobo summoning (enabled by default), current companion time and the last local summoning status. It does not extend active timers.
- Travel costs show gil and ticket icons. Route aetheryte selectors show the map symbol. Textures are obtained from the local game through Dalamud; the plugin downloads no artwork.
- Language settings have a dedicated page. Every new label and the start reminder are translated into all six supported languages.

## Supervision reminder

Every new session requests explicit confirmation, including `/fatepilot start`. The plugin does not acquire combat/navigation control before confirmation. Cancel, closing the reminder/window, or `/fatepilot stop` cancels the pending start. Confirmation is consumed once, and settings are captured when the user confirms.

English text: “FatePilot moves your character, starts combat and interacts with NPCs automatically. Stay nearby and monitor the session. Do not leave your character playing unattended.”

## Validation scope

Build and controller/privacy/translation checks are run locally. Native layout at different game UI scales, all navigation pages, icon availability and the supervision modal still require in-game review. A design preview, when supplied, is a mockup and not evidence of native rendering.

Faloop remains an explicit browser link. Live timer/report retrieval is outside this visual revision and would require a separately reviewed, optional network integration.
