<p align="center"><img src="src/FatePilot/Assets/logo.png" alt="FatePilot guiding star" width="140" /></p>
<h1 align="center">FatePilot</h1>
<p align="center"><strong>Chart your route. Find your destiny.</strong><br />By Mokoena</p>
<p align="center"><a href="#installation">Installation</a> · <a href="#dependencies">Dependencies</a> · <a href="#features">Features</a> · <a href="SECURITY.md">Privacy &amp; security</a> · <a href="https://github.com/danil-mokoena/FatePilot/releases">Releases</a></p>

FatePilot is an independent Dalamud plugin that coordinates FATE sessions across selected zones and instances. Set a route, choose a return goal, manage teleport costs and follow the session from a compact, multilingual dashboard.

**Version:** 1.5.0 · **Dalamud API:** 15 · **Build:** .NET 10 / C#

### New in 1.5

- **Chain FATEs.** When a FATE that belongs to a chain ends well, FatePilot stays where it is for its next part (30 seconds by default, Route → "Wait for the next part of chain FATEs"; 0 turns it off) instead of flying off to another FATE, and takes it as soon as it appears. The chain is read from the game's own FATE data.
- **Teleport-aware travel.** FATEs are now chosen by travel cost, not straight-line distance: flying there, or teleporting to the zone's aetheryte closest to the FATE, whichever is cheaper (a teleport counts as 200 yalms of flying). When teleporting wins, FatePilot teleports first and flies the rest. On by default (Route → "Teleport to the aetheryte closest to the FATE when it pays off"); teleport costs follow your gil and ticket settings as usual.

175 local tests pass, with zero build warnings/errors.

### New in 1.4 — how FATEs are played, reworked

A study of how the widely used open-source FATE farming scripts handle each kind of FATE led to a simpler, sturdier model:

- **Positioning.** FatePilot now picks a spot at attack reach beside the target, on the target's own side and height, and walks there itself with the navigation module, measuring the real (3D) distance: 2.5 yalms for tanks and melee, 20 for ranged and casters, plus the target's size. Enemies below a ledge or up on a rock are reached instead of stared at; it never moves while casting and stops once in reach.
- **Conversations with FATE NPCs.** Once FatePilot has talked to a verified FATE NPC or object, any conversation window opened while it is still your target is that conversation, whatever event the NPC runs: pages are advanced, the Item Request window is filled and handed over, and the start/hand-over Yes/No is accepted right after its pages. Any other question in that conversation is answered No. Windows of anyone else are never clicked, and an unrecognised window never stops the session.
- **Collection FATEs.** Fight the FATE's monsters (their drops are the items), hand in a batch of 7 (adjustable) or whatever you carry once the FATE is ending, pick up objects only when there is nothing to fight, and hand in at once when there is nothing else to do.
- **Escort and defence FATEs.** Fight the FATE's monsters and anything hitting you or its NPCs; otherwise stay with the escorted NPC, or simply inside the circle when it cannot be told apart. A FATE that keeps advancing is never abandoned.
- **Knocked out of a FATE.** Any FATE (not only escorts): FatePilot walks straight back in instead of re-routing, and level-syncs again if needed.
- **Arriving.** A path that ends inside the FATE lands there even when its centre sits on a rock; a path that ends short is retried; stuck while mounted, it rises 10 yalms and tries again.
- **Death.** The session no longer stops: FatePilot accepts the game's return-to-home-point prompt (or a raise), waits until you are back on your feet and teleports back to the zone it was farming. While dead, that prompt is the only thing it clicks.
- **Compact panel.** Continue shows as soon as a pause is requested (pauses wait for a safe moment) and cancels it, or resumes a paused session.

173 local tests pass, with zero build warnings/errors.

### Fixed in 1.3.6

- **No more "The dialogue does not match the expected NPC and event" with FATE NPCs and pickups.** Some FATE NPCs (e.g. the Oyster Hunter in Costa del Sol) run their own event for the opening pages and the "Might those be ... you carry?" turn-in, and items on the ground run their own gathering event; some also report themselves by their short 32-bit handle. Any event of the very NPC or object FatePilot verified and interacted with is now ours. Menus still only ever pick the FATE's own option, and windows from anyone else are never clicked.
- **An unrecognised interaction no longer stops the session.** It is dropped (nothing is clicked), logged with details, and the FATE goes on; only 4 in 2 minutes stop the session.
- **Collection FATEs stop wandering.** Picking up items and handing them in no longer wait for the game's in-combat flag, which stays on while a pet or companion fights (Beastmaster, chocobo). FatePilot used to leave the item it was gathering to hit any FATE monster nearby, then walk back. Now it fights only what attacks you, collects, hands in, and attacks FATE monsters only when there is nothing to pick up.
- README characters (·, —, →) restored after the 1.3.5 page showed them garbled.

169 local tests pass, with zero build warnings/errors.

### Fixed in 1.3.5

- **Escort FATEs follow the escorted NPC again** (e.g. *Consigned, Sealed, and Undelivered* in Costa del Sol). The game also sets its "hostile" status on a friendly NPC that is on the move or fighting, so the escorted NPC was dropped as if it were an enemy and FatePilot had no one to follow. FATE NPCs are now told apart from enemies the same way as in combat (only what you can attack is an enemy). When the escorted NPC still cannot be found, the status says so and the log records the FATE details.
- **An escort that keeps advancing is never abandoned.** FatePilot used to give up after 2 minutes without seeing the escorted NPC — even standing inside the circle — and mount for another FATE. As long as the FATE's progress keeps rising, it now stays.
- **Falling behind an escort no longer abandons the FATE.** An escort's circle moves with the NPC; when you drop out of it, FatePilot now walks after the NPC instead of re-routing to where the circle used to be, which gave up after a few seconds and mounted for another FATE.

167 local tests pass, with zero build warnings/errors.

### Fixed in 1.3.4

- **Height-aware destinations.** Walkable ground is now searched at the destination's own height first (4, then 8, then 20 yalms up/down) before the wide search. Before, a single 100-yalm-tall search could pick a cave or ledge under or above the target, leaving the character stuck below it.
- **Collection FATEs hand in by themselves.** In the conversation FatePilot opened with the verified turn-in NPC, it now fills the "Item Request" window the same way you would (open the slot, pick the offered item) and presses Hand Over. The turn-in NPC's own event is accepted as part of that conversation.
- **Collection pickups no longer interrupt the session.** Rocks, fragments and crates of a collection FATE run their own event; that event of the verified object is now accepted as part of the interaction instead of being treated as a foreign dialogue ("The dialogue does not match the expected NPC and event").

166 local tests pass, with zero build warnings/errors.

### Fixed in 1.3.3

- **No more targeting friendly FATE NPCs.** Enemies are now recognised the same way the game decides what you can auto-attack. The game's "hostile" status is also set on friendly NPCs while they fight (escorted merchants, defending soldiers), which made FatePilot target them and stand still instead of attacking the monsters. FatePilot also refuses to select a friendly NPC as a combat target.

166 local tests pass, with zero build warnings/errors.

### Fixed in 1.3.2

- **Escort and defence FATEs protect their NPC.** Enemies attacking the escorted NPC (or any friendly NPC inside the FATE area) are now fought right after anything attacking you, even when the game did not tag them with the FATE. Before, those attackers were ignored and FatePilot could stand next to the NPC while it was being beaten.

166 local tests pass, with zero build warnings/errors.

### Fixed in 1.3.1

- **Collection FATEs hand in again.** The turn-in NPC is now also recognised as the NPC that offered the FATE (the objective NPC first, then the FATE's own NPC, then the exact NPC FatePilot started it from). When the items are ready and the turn-in NPC still cannot be identified, FatePilot stops collecting and says so in the status (with details in the log) instead of collecting forever.

165 local tests pass, with zero build warnings/errors.

### New in 1.3

- **Experience bar** for a job that is still levelling, in the compact panel and in the session page: job, level, EXP earned this session, levels gained and the bar itself, read from the game's own experience bar (hidden at the job's max level, limited jobs included). Hover it for current / needed EXP.
- **Grand Company seals earned** this session, next to gemstones, with your balance and the seal cap on hover (warns when the cap is reached). Seals spent at your Grand Company never count as a loss.
- The session page shows EXP, seals and gemstones per hour after five minutes.

164 local tests pass, with zero build warnings/errors.

### New in 1.2

- **Automatic gemstone purchase** (on by default; Inventory → Gemstone purchase queue → Buy automatically in the trader's shop). Inside the conversation FatePilot itself opened with the verified trader, it picks the shop in the menu, finds the queued item by its name in the shop's list, asks for the quantity your balance covers and accepts only a confirmation that names that item. When the bag shows the purchase, it closes the shop and goes back to the FATEs. Anything unexpected — a window that is not the trader's, a confirmation for something else, the game refusing the purchase (full bags, shared FATE rank) — stops the automation and leaves the purchase to you, with the reason in the status; the queue keeps working as in 1.1.
- **New trader item picker**: a table like a schedule, with filter pills (All, Materials, Housing, Orchestrion, Gear, Other — with game icons and counts), search by name or category, sort by price, your gemstone balance, and per item: price, how many you already have, how many your balance covers right now, and an Add button (adding again increases the quantity; unique items you already own are marked).

163 local tests pass, with zero build warnings/errors. The automatic purchase needs in-game validation.

### New in 1.1

- **Gemstone purchase queue** (Inventory → Gemstone purchase queue): pick items from your gemstone city's trader (list, icons and prices read from the game data — Old Sharlayan, Radz-at-Han, Tuliyollal and Solution Nine), set quantities and order. When your balance covers the next item, FatePilot finishes the current FATE, travels to the trader, opens the conversation and waits while you buy. As soon as the purchase shows up in your bags it is marked in the queue and the session goes **back to the FATEs** — then on to the next item, again and again. Leaving the shop without buying ends the session at the trader.
- For now the buy click itself is yours: the exchange shop is not mapped in the client structures, so FatePilot does not click inside it blindly. **Copy the exchange window data** (with the shop open) provides what is needed to make the purchase automatic.

161 local tests pass, with zero build warnings/errors.

### New in 1.0

- **Route that follows your level** (Route → Route for my level): on start, on level up and on job change (the job queue included), the route is rebuilt with the zones that have the most FATEs for the job's real level — never the lower level you are synced to inside a FATE.
- **Simulation** (the eye next to Start): shows what a session would do right now with your settings — the next step, every FATE in the zone with the reason it is or is not eligible, a timeline of decisions and the chosen FATE on the Map. Nothing is moved or clicked.
- **Trip to the gemstone trader**: after a gemstone return (on by default) or with Inventory → Go to the trader now, FatePilot walks to the bicolor gemstone trader of the city or zone (all 24 traders, ShB to DT) and opens the conversation. Picking the shop and buying stay with you; nothing inside the conversation or shop is clicked. **Copy the exchange window data** captures that window so the purchase itself can be automated in a later version.
- **In-game validation checklist** (Diagnostics): every feature that still needs a check in game, with how to test it, ✓/✗ marks and a copyable result.
- **Themes for every capital** — Limsa Lominsa, Gridania, Ul'dah, Ishgard, Kugane, Rhalgr's Reach, Doman Enclave, The Crystarium, Eulmore, Old Sharlayan, Radz-at-Han, Tuliyollal, Solution Nine — plus **Classic**, the blue gradient windows of the game's Classic UI and the first Final Fantasy. Chocobo stays the default.

159 local tests pass, with zero build warnings/errors.

### Fixed in 0.9.1

- **Teleporting works mounted or flying.** FatePilot used to insist on dismounting before every teleport; in the air that never succeeds, so a session started while flying sat on "Looking for eligible FATEs" forever. It now teleports straight away, like the game allows.
- **No silent waits.** When the game really cannot teleport yet (in combat, in the middle of an action) or the navigation mesh is still loading, the status says so and the log records it once.

### New in 0.9

- **Map page.** The zone's own map from the game, centred on you, with a live layer on top: every FATE's area with a progress ring and its icon, the current FATE highlighted with the path to it, prioritized and ignored FATEs, the spot where FATEs appear most often, the aetheryte, enemies (red when they target you) and an arrow that turns with your character. Zoom with the slider, the buttons or the mouse wheel; hover a FATE for its level, progress, time left, map coordinates and distance. Optionally shown in the compact panel too.
- **Themes** (Settings → Appearance): Chocobo (the original gold), Ishgard, Gridania, Limsa Lominsa, Ul'dah and Sharlayan, each previewed as a miniature of the window. Applies to the window and the compact panel.
- **Keyboard shortcuts** (Settings → Keyboard shortcuts): pause/resume, skip the current FATE, stop, start (still with the confirmation) and show/hide the window. Ctrl and/or Alt are required, and shortcuts are ignored while you type in chat. New `/fatepilot toggle` and `/fatepilot skip` commands work as game macros on your hotbar.
- **Shareable profiles** (Route → Share profiles): copy a saved profile as a `FATEPILOT1:` text code and paste someone else's to preview it (route with your client's zone names, locked aetherytes flagged) before saving it as a new profile. Codes carry only session settings; the job queue (your gearsets) is left out. Codes are size-limited, validated and never applied without your click.

155 local tests pass, with zero build warnings/errors. The map, themes and shortcuts need in-game validation.

### Fixed in 0.8.1 (full code review)

- **Second Gysahl Greens restock no longer stops the session.** After a completed purchase the shop stayed tracked, so any later Yes/No (a FATE start prompt, a discard confirmation) marked it as a foreign shop and the next restock stopped at once. The shop is now released as soon as it closes, and on pause, retry or job switch.
- **Waiting at the FATE hotspot is safer:** an unreachable spot no longer stops the whole session (FatePilot just waits where it is), and if the mount never comes (swimming, no mount) it walks after a few seconds instead of retrying forever.
- **A FATE that goes from "preparing" to "running" is counted once** in the zone history, and a prioritized FATE alerts only once.
- **Restock trips:** a prompt you open on the way to the vendor (discarding an item) is waited out instead of stopping the session; an automatic retry now forgets a half-done trip, so a later arrival is never mistaken for the vendor trip; a purchase already sent is never abandoned by a retry (that case still stops for review).
- **Self-repair:** a momentary refusal by the game (animation lock right after combat) is retried a minute later; repair is turned off for the session only after three refusals in a row.
- **Achievements:** group-boss FATEs show a warning instead of the Prioritize button while "Skip known group bosses" is on, since that option would never let them run. A group boss engaging you is now reported as a problem stop.
- The Achievements data no longer rebuilds every frame if the game data cannot be read; it retries every 30 seconds.

151 local tests pass, with zero build warnings/errors.

### New in 0.8

- **Alerts** (Settings → Alerts): one of the game's 16 chat sounds (`<se.1>`–`<se.16>`, with a test button) and a local chat line when the session stops on its own, a goal is reached or the return finishes, a prioritized FATE appears, repair/food/Chocobo fail or a retry happens, and when a tell arrives during a session. For tells only the fact that one arrived is used; sender and text are never read or stored. Nothing leaves the game.
- **Automatic retry** (Settings → Automatic retry, on by default, 1–5 attempts per session): after a recoverable interruption — an unexpected dialog, unexpected loading screen or zone change, or a busy travel module — FatePilot gives control back, waits for the game to settle (up to 2 minutes), adopts the current zone and job and continues. Death, logout, instanced content and group bosses always stop the session.

144 local tests pass, with zero build warnings/errors. Alerts and retries need in-game validation.

### New in 0.7

- **Route for my level.** From the game data (each FATE's level and its zone), FatePilot picks the unlocked zones with the most FATEs between your level −2 and the allowed levels above (e.g. level 11 → Central Shroud, Lower La Noscea, Middle La Noscea). One click on the Route page, or per gearset on the Jobs page.
- **History by zone** on the Session page: time, gemstones per hour, FATEs per hour and failure rate for every zone you farm, best zone first. Stored locally, cleared with one click.
- **Prefer bonus FATEs** (FATEs page, off by default): a bonus FATE wins over a nearer one if it is no more than the chosen number of yalms farther.
- **Wait where FATEs appear** (Route page, on by default): with nothing eligible, FatePilot flies once to the spot where FATEs appeared most often in that zone, learned from your history.
- **Minutes per zone** (Route page): move to the next route zone after N minutes, between FATEs.
- Achievements and Jobs: row icons and buttons centered, gearset buttons aligned in one column, "All earned" capitalized.

141 local tests pass, with zero build warnings/errors. The level route, idle positioning and zone budget need in-game validation.

### New in 0.6

- **Achievements page.** Every open-world FATE achievement from the game data, in your client language, grouped like the in-game log:
  - *Bosses and specific FATEs* (37, e.g. Mischief Managed, Don't Go into the Light, Terrible Ttokrrone): each is linked to the FATE it names. **Prioritize** makes FatePilot pick that FATE first whenever it is up; the route button sets the route to its zone. Each FATE's zone is resolved from the game's own layout files (all 41 FATEs behind the 37 boss achievements resolve).
  - *By zone* (18 Free Market Friend achievements, shared FATE rank 3 in Shadowbringers/Endwalker and rank 4 in Dawntrail): one click sets the route to that zone.
  - *FATE counts* (Date with Destiny and Destiny's Child) with "current of total" progress bars.
  - Search, "hide earned", and "N to go" per group. Completion and progress come from the game itself; open the in-game Achievements window once per login so the game loads them.
- **Prioritized FATEs** are chosen before nearer ones (the ignore list still wins).
- **FATE zones** are read from the game's layout files (the FATE's location id is matched in each zone's event layout), so routes work immediately; zones seen during sessions are also remembered.

Spawn timers for boss FATEs are not available: the game does not expose them and community trackers have no supported public API. The Faloop page keeps the click-only link for your world.

137 local tests pass, with zero build warnings/errors. The Achievements page, progress reading and priority selection need in-game validation.

### New in 0.5

- **Pause and resume.** Pause now, or schedule a pause after the current FATE, from the session header, the compact panel or `/fatepilot pause`. Pausing releases movement, rotation and pathing so you can play by hand; counters, spending and the route are kept and paused time is not counted. Resuming adopts your current zone and job.
- **Jobs page: per-level job queue.** Pick combat gearsets (with their job icons and current levels) and a target level for each. Between FATEs, when the current job reaches its target, FatePilot equips the next gearset and continues; when the queue is finished it returns to the return city.
- **Inventory page.**
  - *Gemstones:* balance vs. cap bar, a "return at this balance" slider up to the cap, and an optional city with a gemstone trader (Tuliyollal, Solution Nine, Old Sharlayan, Radz-at-Han, The Crystarium, Eulmore) used when the return is caused by gemstones.
  - *Repair:* lowest equipped durability, Dark Matter count and automatic self-repair between FATEs (Repair All, confirmed by the durability increase; disabled for the session if not confirmed).
  - *Food:* pick a meal from your bags (with its icon); FatePilot eats it out of combat before Well Fed runs out and confirms the new timer.
  - *Limits:* return when free bag slots or gear durability fall below a value.
- **Route profiles.** Save every setting under a name (route, filters, goals, costs, inventory and jobs) and load it with one click.
- **NPC-started FATEs: the start prompt is confirmed.** After the conversation pages, the "Yes" of the FATE start prompt is now clicked: it is accepted right after FatePilot's own conversation or interaction with the bound FATE NPC (still targeted and in range). The hand-over between the last page and the prompt no longer stops the session.
- **Your own prompts no longer stop the session.** A confirmation you open yourself (for example discarding an item) is waited out and never clicked; the session continues when it closes. Unrelated NPC conversations still stop it.
- **Session summary** after each session: FATEs and gemstones (with per-hour rates), time, gil, Greens, food, repairs and job changes.
- **Interface.** Grouped sidebar (Session · Character · Tools) with About and Settings at the bottom, consistent margins on every page, game icons in the compact panel (gemstones, gil) and the plugin names in Settings → Dependencies.

136 local tests pass, with zero build warnings/errors. Pause/resume, the job queue, self-repair, food and the gemstone-trader return still need supervised in-game validation.

### New in 0.4.2

- **Compact session panel.** Starting a session hides the full window and shows a small panel with the current FATE, its progress and remaining time, gemstones, elapsed time and gil, the next FATE and short alerts (return pending, low Greens). It has **Stop**, **Skip** (ignore this FATE for 5 minutes) and a button to reopen the full window. Stopping closes the panel and brings the full window back; a minimize button in the full window returns to the panel.
- **Zones without an aetheryte.** The Dravanian Hinterlands can now be added to a route. FatePilot teleports to Idyllshire and takes the city's aethernet gate (Prologue Gate) into the zone. Requires the Idyllshire aetheryte and the gate attuned; the route card explains this on hover.
- **Dependency check** in Settings: each required plugin shows green (ready) or red (missing or incompatible), with a **Check again** button.
- Interface polish: consistent inner spacing in every panel (nothing touches or overflows the border, including route controls and the session header), a Chocobo icon for the companion section, and route buttons that always stay inside their panel.

### New in 0.4.1

- Multi-page NPC conversations before a FATE keep advancing across narration, the player's own lines and other speakers, as long as the same verified native callback drives the dialogue. The interaction window grew from 15 to 60 seconds, so long conversations reach the Yes confirmation.
- Starting on a crafter or gatherer job shows *Switch to a combat class or job before starting*; other errors show their real cause.
- **Next step** panel on Session (the FATE being run or the one that would be picked, with distance and reason), status light and session timer, reason chips on FATE rows (*Current target*, *Group boss*, *Skipped*, *Level too high*), gemstones per hour, readable Diagnostics log and urgency colors on timers.

124 local tests pass, with zero build warnings/errors. Native layout, multi-page dialogue, the aethernet gateway and Chocobo summoning still need supervised in-game validation; local tests do not prove these native actions work.

### New in 0.4

- Sidebar navigation, grouped settings panels and a persistent session header with compact Start/Stop controls.
- Local game icons for FATEs, aetherytes, gil, tickets and supplies. FATE rows show completion bars and remaining time alongside their names.
- A Supplies page with current Greens stock, editable restock threshold/target, a stock bar and separate shopping/travel budgets.
- Optional **Automatically summon the chocobo**, independent of restocking and on by default and configurable in **Supplies**. Uses one Gysahl Greens outside combat when the companion is inactive and the game permits it, including after landing. Active companions are not extended. An unconfirmed summon suspends further item use for that session while the route continues.
- A supervision reminder before every new session, including command starts. Confirm it to begin; cancel or Stop leaves automation inactive.
- All new controls and messages in English, Japanese, German, French, Brazilian Portuguese and Spanish. Includes the 0.3.1 NPC speaker fix.

### Fixed in 0.3.1

NPC dialogue ownership now reads the speaker directly from the visible Talk window. The previous cached speaker field could remain empty, causing Panaq Attack to stop on its first dialogue page despite interacting with the correct NPC. The verified conversation can advance through multiple pages and its matching Yes confirmation, then proceed to level sync and combat. This requires no additional dialogue plugin. Native behavior still needs in-game confirmation.

### New in 0.3

- Session goals for earned gemstones and elapsed minutes, alongside completed FATEs. The first observed goal is retained; a current FATE continues, including partial collection hand-ins, before returning to the selected city. Existing timeouts and interruption checks remain in force.
- Optional gemstone-cap reserve based on the cap reported by the client. It requests a return when free capacity reaches the reserve; the current FATE's reward can still overflow. This is not a guarantee against currency loss.
- Compact FATE completion bars and remaining time in the header and event list; session time, goal progress, gemstone balance and return reason.
- Edit routes, filters, goals and costs while running. Valid changes apply after the current FATE or operation; a pending notice explains the delay. Counters and spending reservations are preserved. Language changes are immediate.
- Priority for Forlorn Maiden and The Forlorn within the selected FATE, including collection and escort objectives. Identification uses game IDs rather than translated names. A native dialog already in progress must finish first.
- Handle ordinary attackers outside the selected FATE, then resume travel. Known group-boss exclusions remain in force.
- Nearest-first selection, including NPC-started FATEs. Bonus preference only breaks equal-distance ties.
- NPC binding supports server entity IDs, local object handles and unique native FATE associations. Local multi-page conversations can be recognized by their verified NPC, localized speaker and callback, even when global event fields are empty. Confirmation must use the same callback; unrelated dialogs remain excluded. No additional dialogue plugin is required by this implementation.
- Optional Gysahl Greens threshold/target restocking with a separate gil budget and confirmed purchases from a verified Limsa Lominsa vendor. Finish the current FATE, replenish supplies and resume the route. Summoning is a separate option added in 0.4.
- A Faloop tab showing the **current** world and data center, including visits. Its click-only browser link selects that world on the website. Live reports and timers are not imported.

[Faloop design and remaining work](FALOOP-DESIGN.md) explains the companion link and the supported read API still to be verified for future report retrieval. Native NPC interactions and shop purchases remain experimental.

Validate development without creating release ZIPs or changing the catalog: `./build.ps1 -ValidateOnly`. Use `-Dotnet` for a local SDK path if needed.

> [!WARNING]
> **Use at your own risk.** Square Enix prohibits unauthorized third-party tools and automated or unattended play. Using this plugin may result in account suspension or a permanent ban. See the official [third-party tools statement](https://na.finalfantasyxiv.com/lodestone/topics/detail/68f4c4d3bdea34f732ee8db3425122b8b8d6ec3b) and [prohibited activities policy](https://support.na.square-enix.com/faqarticle.php?id=5382&kid=68216).
>
> FATE farming is associated with botting and RMT in [community discussions](https://www.reddit.com/r/ffxivdiscussion/comments/12ki4a0/fate_botting_in_endwalker_zones/). Claims that FATEs receive particular enforcement priority are community perceptions, not a confirmed official policy. FatePilot offers no protection against detection or account penalties. You are responsible for the decision to use it.

> [!IMPORTANT]
> **This is an experimental build.** Combat, travel and resuming after outside-FATE defense have been observed in user diagnostics. Earlier versions stopped partway through long NPC conversations; 0.4.1 keeps the verified conversation through every page, and its native behavior is not yet confirmed in game. Tests simulate controller behavior and do not execute native adapters in a live client.

### Fixed in 0.2.1

- Wait briefly for the game's event context after requesting an NPC interaction, instead of stopping immediately when Occupied is raised.
- Recognize both the FATE director and its associated array event. Select a native event-menu entry only when it uniquely belongs to the expected NPC and FATE.
- Allow bounded mount/dismount transitions without treating them as unrelated dialogs, and wait for the transition cast to finish.
- Record interaction requests, recognition and failure context in local Diagnostics.
- Start expansion groups collapsed in Route.

## Features

| Area | Available in 0.4 experimental |
|---|---|
| FATE selection | Combat, collection, escort and defense; bounded NPC activation; level/progress/time filters, bonus preference and an ignore list |
| Collection | Ground pickups, objective enemies and repeated NPC hand-ins; default batch of 15, with partial deliveries after 45 seconds or near the end |
| Escort and defense | Follow the associated friendly objective and fight event enemies |
| Travel | Zone image cards grouped by expansion, reorderable routes, discovered instance rotation, mounts, unlocked flight, level sync and zones without an aetheryte reached through a neighbouring city's aethernet (Dravanian Hinterlands via Idyllshire) |
| Session goals | Return by completed FATEs, earned gemstones, elapsed time, a gemstone balance (with an optional gemstone-trader city), free bag slots, gear durability or a finished job queue |
| Teleport costs | Aetheryte Ticket switch, minimum fare, explicit gil fallback and a session gil budget |
| Supplies and inventory | Chocobo summoning and Greens restocking; self-repair with Dark Matter, food upkeep and bag/durability limits on the Inventory page |
| Recovery | Stuck-route handling, operation timeouts, control conflict detection and restoration of previous settings |
| Interface | Slate and gold sidebar, compact session panel with Stop/Skip, next-step panel, reason chips, local game icons, live settings edits, completion bars, dependency check and supervision reminder |
| Faloop | Click-only world-filtered companion link; no report or timer import |
| Group bosses | Known large-group bosses such as Daivadipa and Chi excluded by default |

The plugin does not start farming simply because it is loaded. Request a session from the panel or command, then confirm the supervision reminder.

## Requirements

- FFXIV on Windows with XIVLauncher and a working **Dalamud API 15** installation.
- All four integrations in the [dependency table](#dependencies), installed and enabled separately.
- A combat job supported and configured in the ability integration, with IPC control available. Crafters and gatherers are refused at start.
- Unlocked aetherytes for every selected route/return destination. For The Dravanian Hinterlands: the Idyllshire aetheryte and its Prologue Gate aethernet attuned. A mount for mounted travel and unlocked flight for flying routes.
- Available gil or Aetheryte Tickets according to the chosen payment settings.
- For instance rotation: an instanced zone, Instance Switcher enabled and the available instances discovered at an aetheryte.
- For optional Greens restocking: Limsa Lominsa's main aetheryte unlocked, bag space and gil for purchases and travel.

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

If the installer still shows a question mark after updating from 0.1, clear Dalamud's in-memory image cache: `/xldev` → **Plugins → Clear cached images/icons**, then reopen the installer. Use `/xldev` again to close the developer bar. Restarting the game also recreates this cache. The current package and catalog both include the icon URL; reinstalling FatePilot is unnecessary for a stale image cache.

For local development, extract the plugin ZIP from [Releases](https://github.com/danil-mokoena/FatePilot/releases), keep the two DLLs, dependency JSON and manifest together, and add the full path to `FatePilot.dll` under **Dev Plugin Locations** instead. Do not load a development copy and the installed copy simultaneously.

## Dependencies

| Component | Role | Setup notes |
|---|---|---|
| [WrathCombo](https://github.com/PunishXIV/WrathCombo) | Combat abilities | IPC control must be available; FatePilot requests a temporary lease |
| [BossMod](https://github.com/awgil/ffxiv_bossmod) | Positioning | Use the original compatible plugin; do not load conflicting variants together |
| [vnavmesh](https://github.com/awgil/ffxiv_navmesh) | Pathfinding | The current zone's navigation mesh must be ready |
| [Lifestream](https://github.com/NightmareXIV/Lifestream) | Instance switching and aethernet | Enable Instance Switcher and open the aetheryte's instance menu once to discover the options. Also used for the Idyllshire aethernet gate into The Dravanian Hinterlands |

These integrations are installed separately. Their implementations are not bundled with FatePilot. **Settings → Dependencies** shows each integration in green (ready) or red (missing or incompatible) with a *Check again* button; the same APIs are verified again before every session starts. See [integration details](INTEGRATIONS.md) for compatibility and restoration behavior.

## First session

Stop other combat, movement and travel automation first. Choose the language in **Settings**, then set a return city and a FATE goal in **Session**. In **Route**, click zone cards under each expansion to select a route, then use the arrows to reorder it. If a zone has multiple unlocked aetherytes, choose its arrival point. Review **Supplies** and **FATEs**, then press Start and confirm the supervision reminder.

The catalog reads the client's overworld zones and loading artwork locally: 48 zones in the data used for this build. Zones without a direct unlocked teleport destination remain visible but cannot be added. Some older zones share regional artwork. The selected route repeats until a goal or Stop ends the session.

You can edit settings while running. Changes apply after the current FATE, travel or restocking operation; the panel shows pending or invalid changes. Invalid edits leave active settings intact. A return already requested by a reached goal completes with its existing destination; later edits remain saved for the next session. An empty route stays in the current zone. Keep a route destination when automatic restocking is enabled.

### Collection and escorts

Collection uses the selected event's item and NPC identifiers, independent of the game's language. By default, 15 held items trigger a hand-in trip. Smaller batches are delivered after 45 seconds holding items, with fewer than 90 seconds remaining, at 80% progress or during the ending phase. After a hand-in, collection resumes if the event remains active. A batch is never treated as a fixed completion requirement. Attackers are handled before approaching the recipient.

The batch size is configurable in **FATEs**. Collection and escort participation can also be disabled independently. Interactions are limited to objects associated with the selected FATE, with bounded attempts and timeouts. Unsupported dialogue flows stop rather than click arbitrary menu choices. These native flows are experimental and need supervised validation.

Known large-group encounters are excluded by default. This is a maintained ID list, not a universal difficulty detector; add other unsuitable events to your ignore list. The gemstone counter sums observed increases during the session, excluding the starting balance. It cannot measure rewards lost to the currency cap or distinguish simultaneous gains and spending. The timer shows the currently active FATE's remaining time. The Faloop page links to the website, but does not fetch reports or predict respawns.

For initial functional testing, select one familiar zone and a goal of one FATE, then observe the entire cycle. This helps validate behavior; it does not reduce or remove the account-policy risk. Follow the [validation checklist](VALIDATION.md) before relying on additional features.

### Tickets and gil

Enable **Use Aetheryte Tickets** to request ticket payment for eligible paid teleports. Free destinations do not consume tickets. A minimum fare lets smaller trips use gil, and the fallback to gil when tickets run out is an explicit separate choice. The budget also applies to the return trip.

Open the game's Teleport window once before the first ticket test. FatePilot uses separate native entry points for ordinary and ticket teleports rather than accepting arbitrary Yes/No dialogs. Actual confirmation and consumption behavior still needs testing with your client settings. An inconsistency stops the session, but cannot undo a charge already made by the game.

### Gysahl Greens

In **Supplies**, enable **Automatically summon the chocobo** to use Greens when the companion is inactive. This requires the companion to be unlocked and the game to permit the item. It waits until outside combat and unmounted, and confirms the companion timer after the request. Missing stock or unavailable item use leaves the route running. If a sent request is not confirmed within ten seconds, further Greens use is suspended for the session. A rejected request is throttled for at least sixty seconds. Stop prevents new requests but cannot undo an item action already accepted by the game.

For optional **Gysahl Greens** restocking, choose a minimum remaining quantity, a target from 1 to 999, and a session purchase budget in **Supplies**. The verified vendor is Bango Zango in Limsa Lominsa; local game data lists 36 gil each. Batches contain at most 99 items, and both inventory and gil changes must confirm each purchase before another is sent. Teleports use their separate budget. An uncertain receipt stops without retrying a possibly completed purchase. Stop cannot undo a purchase already accepted by the game. Summoning and shopping remain experimental and can be enabled independently.

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
| `/fatepilot start` | Open the supervision reminder before starting |
| `/fatepilot stop` | Stop and restore previous settings |
| `/fatepilot status` | Print the current state to local chat |
| `/fatepilot pause` | Pause now and give character control back (counters and route are kept) |
| `/fatepilot resume` | Resume a paused session |
| `/fatepilot recover` | Restore a previous session's local recovery record while stopped |

## Scope and limitations

Supported overworld rules include combat, collection, escort and defense. A preparing event is eligible only when it exposes a starter NPC. Event-specific mechanics or dialogue variants may still be unsupported. Completion means an observed successful end after participation; a disappearing FATE without confirmation does not count toward the return goal.

Persistent history across sessions, automatic potion use, voucher purchases, party coordination and GM detection or concealment are not implemented. Route profiles, pause/resume, job queues, self-repair, food upkeep, bag/durability limits and the session summary are included.

### Next milestones

Next: automatic gemstone exchange (validated in game first), then a minimap, themes, hotkeys and shareable profiles. Live validation of NPC-started events, the aethernet gateway, self-repair and job switching continues alongside. External tracker integration remains a separate future investigation.

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

To regenerate release metadata separately, run `./prepare-repository.ps1 -ReleaseDirectory './artifacts' -Tag 'v0.4.0'`. It verifies the DLL/manifest version before writing `pluginmaster.json`; it does not publish anything itself. Publish the release ZIP before updating the public catalog.

For bug reports, include versions, reproduction steps and expected/observed behavior. Review diagnostics before sharing them; character and account names are not required. Translation corrections are welcome.

## Support

<p><a href="https://ko-fi.com/mokoena"><img src="src/FatePilot/Assets/cat.png" alt="Mokoena's cat" width="192" /></a></p>

**[♥ Buy my cat a treat](https://ko-fi.com/mokoena)** — optional support for the project and a very important snack budget.

---

FatePilot is an independent project by Mokoena. It is not affiliated with or endorsed by Square Enix or the Dalamud team. FINAL FANTASY XIV belongs to Square Enix.
