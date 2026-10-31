# Harness plan for TESTING-NEEDED.md (2026-10-18)

Goal: move as many of the 424 unchecked `- [ ]` items in `<workspace>\TESTING-NEEDED.md` (as of 2026-10-18)
from the person to the coordinator's harness. The person keeps only what needs real input, a judgement of looks or
feel, or a decision.

Target: **the dev server** (one-server layout, user decision 2026-10-18), the patched 0.695 test copy `<0.695 test copy>`,
two test characters: A = TestPlayer (account tester, char 1), B = TestPlayer2 (account tester2, char 2; the admin on dev
through `ADMIN_ACCOUNT=tester2`). No Frida, no real mouse / keyboard, no web.

Scripts: `<workspace>\re\test\harness\` (lib.sh + 16 scripts). Item ids below are the order of the `- [ ]` lines in
TESTING-NEEDED.md on 2026-10-18 (the "line" column gives the line number then); the file grows at the top, so match
by the text when it has changed.

## How the scripts see the client's own reaction

The harness cannot click, and the 0.695 client writes **no chat log file** (there is no ChatLog in `<0.695 test copy>\Logs`;
earlier "chatlog" proofs came from the Frida agent `re/test/client.js`). lib.sh therefore puts a small **Lua tap** into
the client after each login (dev command `luacode`, i.e. ClientLua.RunCode), which wraps the shipped UI scripts' own
entry points (Resources\Scripts: ChatManager.lua, NotificationManager.lua, Gaq.lua):

| What | Wrapped function | Captured as |
| --- | --- | --- |
| every chat / system line the chat window gets | `ChatManager.AddMessage` (OnReceiveSay, OnPrint) | `C:<channel>:<sender>:<text>` |
| centre / toast notifications (loot, harvest, system) | `NotificationManager.OnNotification` | `N:<type>:<text>` |
| Game Action Queue popups (friend / group / guild invites, warp offers, reviews) | `Gaq.AddGameAction` | `G:<type>:<id>:<text>` |
| Lua prints | `print` | `P:<text>` |
| UI data sources (friends list, guild roster / News, Voxel Vision, claim dialogs, time) | `HXT.dd(name)` via `SoeGui.GetDataSource` | `D:<name>:<row>:<col=value;…>` |
| answering a popup | `HXT.q(pattern, 1/0)` → `GameActionQueue.Respond(id, choice)` (the popup's own Accept / Decline) | `Q:respond …` |

`HXT.fl()` sends what was captured as `/say HX<n> …` lines (≤ 4 × 360 characters per flush: the chat flood guard
allows a burst of 5); the world logs them as `[chat N] <name>: HX<n> …` and lib.sh collects them into
`out/<run>/tap-<name>.txt`. Other evidence: world / social / gateway container logs, client log files
(ClientInGamePurchase.log, Landmark.log, VoxelFarm.log, NetInfo.log, CommandQueue.log, VoxelDataErrors.log,
FileOpenErrors.log, AssetFailure.log), the dev database, PrintWindow screenshots (`LOOK` lines: left for the person).

**Calibration 2026-10-18 (0.695 test copy) changed the tap** (lib.sh tap section has the details):
- ClientLua.RunCode writes each comma as `\44`; a digit right after it makes `\441...` ("decimal escape too large"),
  so TAP8 (`string.sub(s,1441)`), `ds` (`…,20)`) and `gaq` (`…,0)`) never ran. lua() now puts a space between a comma
  and a digit. (Server-side the fix would be `\044`; recorded, not changed.)
- Chat: the 0.695 chat window never calls `ChatManager:AddMessage`; ClientChatProcessor::PrintMessageInChatTab fills
  the tab data sources listed in `Chat.MainChatWindow` (`ChatTabMessagesDs`, rows Message / Timestamp; Chat.swf
  ChatDocumentClass.as 355 / 818). `HXT.ch()` reads them at every flush; lines are now `C:<text as shown>`.
- Way out: the `/say HX` echo hit the client's own "chat threshold" after one line. The tap now runs a bound no-op
  console command per line, `/hxt <name> <line>`, which CommandQueue.log records in full ("Doing: hxt …"); nothing goes
  to the server. `;` splits commands there, so the tap turns `;` into a space; `hxt` is bound to `HXT.nop()` because a
  bound command with arguments runs as `<code>( _G[ [[args]] ] )` (ProcessCommandEvent template). tap_flush waits for a
  `#F<n>` marker and re-installs the tap once if it never comes.
- Loot pickups ("Lightstone x3") arrive as chat rows, not notifications (CAL-LOOT; 11-items 304 regex updated).
  Notifications are proven with `sysmsg key` → `N:SystemMessage:…` (CAL-NOTE).

**00-calibrate.sh proves each channel first.** Assumptions it checks: that 0.695 still routes chat through the Lua
ChatManager, that GameActionQueue rows are in the `GameData.GameActionQueue` data source with a numeric `Guid`, and
that `GameActionQueue.Respond(id, 1|0)` answers friend / group / guild popups (client.js used a separate native for the
group popup's buttons). If CHAT, NOTE or GAQ fail, fix the tap in lib.sh before trusting client checks of the others
(server checks still stand). The tap is installed once per login; if the UI Lua state turned out to be rebuilt on a
re-zone (tap files going empty after travel in 06 / 07 / 13), call `tap_install <name>` after each travel. Regexes marked `CAL` in the scripts were written from the server code / older scenarios
and not yet seen live: a first-run FAIL there may be the regex, not the server.

## Prerequisites (coordinator)

- Dev images rebuilt and the dev world running (`landmarkemu-world-dev-world-1`); the scripts never rebuild or
  restart (99-outage.sh stops / starts service containers only with `ALLOW_OUTAGE=1`, the world only with
  `ALLOW_WORLD_RESTART=1`).
- `DEV_SERVER` defaults to `127.0.0.1:<WORLD_PORT>` from `LandmarkEmu/docker/worlds/dev.env` (19000); the gateway keeps
  that address. Other overrides: `WORLD_CTR`, `DB_CTR`, `SOCIAL_CTR`, `GATEWAY_CTR` (auto-detected `landmarkemu-*`).
- TestPlayer is also the user's character: every script aborts if a Landmark client outside `<test copies>` is running
  (`USER_OK=1` overrides). Clients are closed at the end of every script (trap).
- Scripts change dev data (sites of TestPlayer2 deleted / re-claimed, props, items, mail, a test guild): fine on dev
  (2026-10-18), but 08-sites.sh / 09-story.sh / 10-games.sh use TestPlayer2's site, never TestPlayer's.
- Results: `re/test/harness/out/<script>-<time>/results.txt` (PASS / FAIL / SKIP / LOOK lines with the item id),
  `tap-*.txt`, `*.png`; one line per check appended to `re/test/harness/out/results.tsv`.

## Run order

| # | Script | Clients | ~Game time | Covers (item ids) |
| --- | --- | --- | --- | --- |
| 1 | 00-calibrate.sh (`WARM=1` after a restart) | A, B | 5 min | the evidence channels |
| 2 | 01-chat.sh | A, B | 8 min | 27 65 78 86 153 250-252 254 256 323 343 410 |
| 3 | 02-friends-groups.sh | A, B | 9 min | 35 162-166 179 259 273 291 356 411 |
| 4 | 03-guild.sh | A, B | 8 min | 35 100 104 347 358 359 |
| 5 | 04-admin.sh | B (admin), A | 8 min | 84 97 241-249, gateway profiler |
| 6 | 05-combat.sh | A | 7 min | 1 11 17 24 26 37 47 227 297 298 300 309 412 |
| 7 | 06-travel.sh | A | 9 min | 2 42 48 51 80 260 313-315 322 361 371 408 409 |
| 8 | 07-caverns.sh | A | 8 min | 50 71 79 88 108 119 149 152 167 169 306 307 357 |
| 9 | 08-sites.sh | B (owner), A | 10 min | 5 29 33 34 53 81 82 146 289 316 317 328-331 333 336 346 403 413 414 |
| 10 | 09-story.sh | B | 10 min | 38 60 68 77 91 159-161 180 181 183 185 186 368 369 393 394 397-402 404 |
| 11 | 10-games.sh | B, A | 10 min | 49 92 355 365-367 372 374 376 388 389 + score / running-edit lines |
| 12 | 11-items.sh | A, B | 9 min | 16 55 62 63 66 70 76 83 101 229 258 266 292 294 302 304 305 |
| 13 | 12-goals-store.sh | A, B | 9 min | 13 14 19 20 22 41 46 56 89 93 131 132 144 208 209 228 303 338 339 385 |
| 14 | 13-terrain-world.sh | A | 10 min | 23 54 99 106 116-120 124 128 214 217 230-232 272 420 |
| 15 | 14-services.sh | B, A | 6 min | 21 107 125 126 128 133 134 142 179 190 196 197 202 204-206 250 252 254 |
| 16 | 99-outage.sh (opt-in, last) | A, B | 8 min | 129 143 178 192-195 201 255 |

Order reasons: calibration first; chat / social before the scripts that depend on popups; 05-combat's whole-log
recipe check (227) is more useful after gathering, so a second run late in the day is worth it; 13 and 14 read
start-up lines; the outage script last because it disturbs the dev services.

## Counts

| Class | Items | Meaning |
| --- | --- | --- |
| HARNESS | 101 | fully checked by a script, with the client's own reaction (tap line, client log, data source) or a DB row as proof |
| PARTIAL | 137 | a script checks part (server path, log, DB, screenshot for the person); the rest stays with the person (noted per item) |
| USER | 149 | needs real input, a judgement of looks / feel, a window with no dev or Lua opener, or a decision |
| BLOCKED | 26 | harness-checkable in principle, but needs a second world / the FakeWorld (one-server layout since 2026-10-18) or a dev setting (`CAVERN_LANDSCAPES=all`) |
| OBSOLETE / DUPLICATE | 11 | see below |

Of the PARTIAL items, 23 have no script yet ("-" in the table): they need a restart / config change by the
coordinator, a new character, a 15-minute wait, or link ids still to be found (182 IF Gate, 188 partial loot chest,
308 Lost Chest with a friend, 30 titles over heads) or a container dev does not run yet (12, 127, 198, 199).

## Already obsolete or duplicated in TESTING-NEEDED.md

- 44 Crimson Parlor keeps the outfit — "(you confirmed 2026-10-17)": tick it.
- 348-351 Build site vendors (player shops) — the Vendor Booth is decorative by default since 89d587f (item 63,
  `VENDOR_SHOPS=true` brings the shop back); only valid with that toggle.
- 353 "Get an outfit and a decoration" from the free store — the store sells at live prices now (S25); the free
  catalogue is the `FREE_STORE=true` toggle.
- 342 "Right-click a dead player > Revive Player: in 0.589 …" — a 0.589 question; the branch tests 0.695 only.
- 189 "Not built, for the record" — a note, not a test (it sits behind a checkbox).
- 281 "Test accounts made: lptest1 …" — information, not a test.
- 59 duplicates 49 (zone control label over each point); 236 duplicates 39 (EVENT_ISLAND_EXPIRE_ON_CLOSE decision).
- 81 (Moving platforms "does not move yet") is superseded by item 3 (the platform travels dock to dock); 102 / 4 are the
  same Translocation Pad check twice; 101 ("nothing makes loot ownerless yet") is superseded by 83 (`lootfree`).
- Sections still pointing at the isolated lmst test stack (S31-S34, S42, S44, S45, S49, S54: "isolated test stack
  only", `re/test/socialtest/...`, world 91, FakeWorld): the commands are stale for the one-server layout; the checks
  that remain meaningful run in 14-services.sh when the matching container exists on dev.
- Cross-world items (85, 95, 154-157, 170-177, 135-138, 140, 220-224) cannot run on the one-server layout.
- Not ticked yet although the harness reported them done: the 2026-10-12 sections' "Verified by harness" items were
  Frida-based; they stay open until these scripts pass them.

## USER-only list (what remains for the person)

Grouped; ids as in the table (short text in brackets). Screenshots from the scripts (`LOOK` lines) cover many of the
"looks" items for a first glance.

**Real movement / mouse / keyboard (feel, timing, input the harness cannot send)** — 28 items: 3 (Magical Platform: place one, stand on it —…); 4 (Translocation Pads: hop, stand still on th…); 6 (Walk backwards / sideways: backpedal slowe…); 7 (Sprint Boots (give 2178 1) + Shift while r…); 8 (give 1120 1 jump + Space in the air = doub…); 9 (Two players: the other sees sprint / glide…); 18 (Two players: one swims (surface, then unde…); 25 (Team site game: hitting a teammate earns g…); 31 (Grappling hook and swimming of another pla…); 43 (Walk on build-site props after travel (wal…); 67 (L121: walk, then jump — the dev log shows …); 102 (Translocation Pad (prop palette, innate): …); 103 (Spring-loaded Flinger (flinger + target cu…); 105 (Abomination spit: it should now be dodgeab…); 109 (Out of reach: stand on a wall top, a ledge…); 215 (Chop one with an axe: wood is Burled, 5 hi…); 240 (Two players (or a second window): with pla…); 253 (Swing a weapon at each other: hits land on…); 299 (A shield weapon's Power Attack (Flame Boun…); 325 (Hold right-click with a sword: power attac…); 344 (Trees and plants now get their full prop f…); 370 (With floating combat text on (console: tog…); 386 (Hold the left mouse with the pick for ~5 s…); 391 (Two players: both join, they cannot hurt e…); 406 (With two players: both join; the 30 s coun…); 407 (Team Deathmatch table: two players end up …); 417 (Hold a power attack (charge) for a few sec…); 423 (Spam the add / remove tool, including the …)

**Looks: colours, models, effects, animations (judge by eye; screenshots from the scripts help)** — 39 items: 10 (Tooltips: chest armour (1466) and accessor…); 36 (Crimson Parlor: others see your new look a…); 57 (Item tooltips of weapons list their affini…); 58 (Six-Shooters alternate right / left hand s…); 61 (Holiday Sweater (Green) / (Blue): all gree…); 69 (Abomination (creature abomination 0 9): it…); 72 (Highfall Catapult (a site with one in Play…); 73 (Bows, staffs, wands and rayguns: each shot…); 74 (Hover a weapon attack on the action bar: t…); 110 (Floors and stairs: creatures on an outpost…); 111 (Abomination: hide behind a wall within 16 …); 112 (Wandering: idle cavern creatures stroll ar…); 114 (Surface: kobold near a steep cliff or crat…); 184 (Bubble interrupt (2.5, P6107-P6113 recipe)…); 216 (A Desert landscape (391 / 397 / 418): cact…); 263 (Look at your sites for holes where you use…); 264 (Outposts are now the 0.695 ruins, re-stamp…); 271 (Look for: at the spawn, the 0.589 client s…); 284 (Weapon swings: the strike now plays to the…); 285 (Fill / Line / Smooth shapes: the server st…); 287 (Loading: with DirectThreadCount=2, fresh s…); 288 (Armour colours in the world.); 301 (Do attacks feel right (reach, cone width, …); 310 (In a cavern: Chompers don't move but spray…); 311 (Standing on the surface above a cavern: no…); 320 (Hit a creature: does the reticle flash a h…); 321 (Let a creature hit you from the side or be…); 326 (Hit a creature (Chaos Caverns / Undergroun…); 327 (Floating damage numbers: they need floatin…); 341 (Die near another player: does their kill f…); 345 (Use a food buff, travel to another landsca…); 360 (Place a BOSS spawner (e.g. "Spawner: Ruins…); 373 (Hover a weapon in your bag: its attacks sh…); 379 (Spawn points (blue/yellow/neutral markers)…); 380 (Random Node, Timer Node and Counter Node p…); 382 (Nothing else should feel different. If a L…); 387 (Dig with the pick on flat ground and on a …); 422 (Removed / built terrain looks the same fro…); 424 (Terrain v2 (layers): build, remove, sphere…)

**Windows and menus with no dev or Lua opener (Site Management, settings rows, Links, Designs, Showcase pages, editor, Game Finder, tooltips, drag and drop)** — 64 items: 15 (Lumen Station: "Recipe: X" for a prop you …); 28 (Site Management → Default Access: a visito…); 32 (Saving a design stores its screenshot (des…); 40 (Combat Node + Barren Grunt spawner, linked…); 52 (IF Gate settings read If / Is / Value; the…); 87 (Trial key: take the key from the letter, m…); 90 (Creature abilities: Combat Node ranks / sp…); 96 (On someone else's build site without Get I…); 139 (Gallery and likes: publish one of your sit…); 141 (Showcase gallery entry: the site's card sh…); 145 (Craft / place a Crimson Parlor on your sit…); 148 (Rotate a site's Starting Location, then vi…); 158 (Open a creature spawner's settings: both r…); 187 (Spawner name and duplicate (6.30): rename …); 200 (Contest walk-on (a site entered in a votin…); 203 (Showcase gallery page: the site's rating e…); 210 (Building Block Basics: Build Mode, Blocks …); 212 (Modular Thinking: place the Modular Castle…); 219 (Open the Game Finder, nothing ticked, Sear…); 225 (With a second 0.695 client on world 91 wai…); 226 (Dev stack (no SOCIAL_ADDRESS): the Game Fi…); 237 (Build Mode → Mesh Shapes tab: categories C…); 238 (With no tool active, click a mesh shape: t…); 239 (Remove tool, Paint, Smooth and the Fill Br…); 257 (One of each accessory: give 1607 2, equip …); 265 (Build mode > Designs: the castle pieces no…); 267 (Overflow "Retrieve all" with a full bag: r…); 268 (As Player: build mode > Designs > your de…); 274 (A trade with another player and a Player S…); 276 (Inspect (0.695): right-click another playe…); 277 (Mail through the social service (only with…); 286 (Material counts: 1 item per voxel for buil…); 290 (Site Management → Screenshots → take one: …); 293 (Shift-drag a stack (e.g. Lightstones): pic…); 295 (Guild window (N), as leader: add a rank, r…); 296 (Hold a weapon and Alt-hover it / the attac…); 312 (Place a Mailbox prop on your site and clic…); 318 (With a second player allowed to build on y…); 319 (Heal or delete your blocks, reopen Voxel V…); 332 (Walk around your site: is the build area n…); 334 (Map (M) "Show Claimable Land": do sites sh…); 335 ((Needs a character without a site) place a…); 337 (Designs window > pick one of your designs …); 352 (Click the HUD Showcase button: does "Signe…); 354 (Gallery tab: your site is listed; Visit a …); 362 (Open the in-game bug report window, submit…); 363 (Inside someone's build site, use "report t…); 364 (Game editor Add on an empty spawn point ro…); 375 (Game Settings tab: expand General Settings…); 377 (Prop Settings tab: does it list Neutral Sp…); 378 (Add on an empty row: starts placing that s…); 381 (Drag a prop setting slider (e.g. a Timer's…); 383 (Walk onto a site with a running game and p…); 384 (Walk onto a contest-entered site of anothe…); 390 (Build a small story on your site with an A…); 392 (In build mode open a Timer Filter's / Dice…); 395 (In build mode, select a placed prop (torch…); 396 (Remove a link from the list; "remove all";…); 405 (Place a Deathmatch Game Table on your site…); 415 (With a friend nearby in Build Mode: you se…); 416 (Click one of your placed props with the pr…); 418 (Tints (2026-10-13 fix): log in fresh, open…); 419 (Right-click one of your props outside buil…); 421 (Build costs: every material with ∞ in the …)

**Character select, launcher, the real install** — 9 items: 64 (Log in the normal way (launcher → characte…); 98 (Login queue (needs character select, so a …); 262 (Deleted characters: delete a character tha…); 269 (Also: /admin works for Player (added as a…); 270 (Your 0.695 copy (<0.695 install>) throug…); 275 (Log out to character select (Escape → Logo…); 278 (Put it in your real Landmark folder (renam…); 279 (Look and feel: dragging the window, the se…); 280 (When the game closes (Exit Game, or a cras…)

**Decisions and questions for the person** — 9 items: 39 (Decision: EVENT_ISLAND_EXPIRE_ON_CLOSE (of…); 75 (Question: under the harness (game window n…); 122 (Preload (optional): OUTPOST_PRELOAD=true i…); 123 (Dev world later: once the above passes, se…); 213 (Tell me if the order of any steps feels wr…); 218 (Look (not required): set "ProceduralPlacem…); 233 (Heights + proof of the bitmap names (close…); 282 (Your decision: the LaunchPad.exe in the pu…); 283 (<0.695 install>'s undo backup was made b…)

**Blocked on the one-server layout** (need a second world or the FakeWorld): 85, 95, 135-138, 140, 154-157, 170-177,
191, 220-224; 113 needs `CAVERN_LANDSCAPES=all` on dev.

## Sections (S-numbers in the table)

- S1: Melee sweeps, platforms, evac, pads, site border — (2026-10-18, d11cead)
- S2: Sprint, boots, glide, movement stats, keywords, in-game currency purchase — (2026-10-18, 1f6bbd1)
- S3: Showcase following + daily mail — (2026-10-18, 52af835)
- S4: Lumen Station hides owned unlocks, Regalia, creature weapons — (2026-10-18, 1a5b831 / 827fb93)
- S5: Swimmers met later — (2026-10-18, ac6ecf8)
- S6: Settings, cross-world trips, store — (2026-10-18, be8f5dc; on dev)
- S7: Parlor Pack in the client's store — (2026-10-18, 1c91d45; on dev)
- S8: Live defaults, gear passives, duels — (2026-10-18, ccffce5; on dev)
- S9: Site permissions — (2026-10-17, 936f360) — needs a second player on your site
- S10: Wire audit fixes — (2026-10-17, 16a789f / 4d83ca8 / 158ff76; on dev) — needs two players for most
- S11: Site pathing, event islands — (2026-10-17, 73dc97c / e08249f; on dev)
- S12: Combat Node rows, Player Studio fee — (2026-10-17, 70f7d05 / fbf760c)
- S13: Spire collision, Parlor — (2026-10-17, up to the deploy after 4c… / Spire hold)
- S14: Native 0.695 numbering, Player Studio, grief, underground — (2026-10-17, up to 4bd29d9)
- S15: Zone control, cavern graveyards, pass-3 fixes — (2026-10-17, edf630b / 0fb9cc4 / 88fa629)
- S16: Pioneer lands and Barren recipes — (2026-10-17, 0b3247c / 4afe1d7)
- S17: Leads pass 2 — (2026-10-17, up to 5ae6bdd; on dev)
- S18: Ticket hardening — (2026-10-17, 322d6eb; on dev)
- S19: Chat markup tokens, run command name, outfit tints, movement state — (2026-10-17, 1487cde / a7b54d6; on dev)
- S20: Play Mode hiding, chat channels / bubble colour — (2026-10-17, merges e-playmode / e-chat; steps in trace/0695…
- S21: Outpost banner and Local Travel on every cave landscape — (2026-10-17, merge e-outposts; steps in trace/0695/a…
- S22: Platforms, site inventory, loot claims, admin / session fixes, Game Finder across worlds, travel queue — (2026…
- S23: Membership, trial key, member benefits, graveyards, goals, /playdead — (2026-10-17, merges of e-membership / e…
- S24: Creature weapons and Game Table events — (2026-10-17, merge e-story; steps in trace/0695/agent-story.md §3)
- S25: Store at live prices (Lumens) and founder items for everyone — (2026-10-17, merge 141bc77; report trace/0695/a…
- S26: Travel to a friend on another world — (2026-10-17, E.13)
- S27: Get Inspired (copy permission) — (2026-10-17)
- S28: Admin commands unlocked, /item, grief, telemetry — (2026-10-17, commits f7e2307, 2dbdadd, 1d17dde, e004002)
- S29: Guild News tab, ownerless loot — (2026-10-17, commits e789614, 0f682fa)
- S30: Movers, guild invitations, Abomination spit, terrain memory — (2026-10-17, commits 22dd72f, 9fbb106, 6f0cd86, …
- S31: Creature pathing — (2026-10-17, agent, TASKS 6.61; uncommitted; isolated test stack first, then a dev world re…
- S32: Chaos Caverns on every island — (2026-10-16, agent, TASKS 3.14 F2; uncommitted; isolated test stack first)
- S33: Environment Server in its own container — (2026-10-16, agent, TASKS B.3 steps 3-6; isolated test stack only)
- S34: Player Studio, contests and portraits in the shared commerce container — (2026-10-16, agent, TASKS B.4 step 8;…
- S35: Small fixes — (2026-10-16 / 17; commits 64fd230, 8ace728, bd7e2f3; on the dev world after its next restart)
- S36: Facing the Starting Location's arrow on arrival — (2026-10-16, TASKS 2.16 / P5825; commit ed74045; needs a dev…
- S37: Chaos Caverns: line of sight, crystal timer, first-time help, /who — (2026-10-16; commits 800f0af, c10c2c5, 57…
- S38: Travel between worlds from the teleport window — (2026-10-16, TASKS B.10; commit 0afe874; isolated test stack …
- S39: Creature health / damage on spawners — (2026-10-16, TASKS 2.14; commit ab7d8b0; needs a dev world restart)
- S40: Friend requests need the other player's OK — (2026-10-16, TASKS 5.4; commits 132c846 + c0198d2; needs a dev wo…
- S41: Outposts in every Chaos Cavern — (2026-10-16, TASKS 6.8; commit e181565; needs a dev world restart)
- S42: Groups across worlds — (2026-10-16, agent, TASKS B.9; isolated social test stack, not the dev servers)
- S43: Story tools: Story Node lists, announce lines, triggering character, counters in Edit Mode, duplicates — (2026…
- S44: Gateway routing by registration — (2026-10-16, agent, TASKS B.2 / B.5; isolated test stack, not the dev server…
- S45: Build-site likes through the world-channel container — (2026-10-16, agent; isolated test stack, not the dev se…
- S46: Server monitor (/admin/monitor) — (2026-10-16, agent, TASKS B.7; needs a dev world restart)
- S47: Tutorial goals Getting Crafty / Building Block Basics / Modular Thinking — (2026-10-16, agent; needs a dev wor…
- S48: Trees from the procedural placement sets — (2026-10-16, agent, TASKS §7; needs a dev world restart)
- S49: Game Finder across worlds — (2026-10-16, agent; isolated social test stack, not the dev servers)
- S50: Goal-only and retired recipes no longer drop at random — (2026-10-16, agent, item-sources audit; needs a dev w…
- S51: Event islands and the Events world — (2026-10-16, agent, TASKS 0.T2 / 3.19; needs a dev world restart)
- S52: Mesh Shapes tab and tool-switch effects — (2026-10-16, agent, TASKS 3.1; needs a dev world restart)
- S53: Admin slash commands — (2026-10-16, agent; needs a dev world restart)
- S54: Duels through the world-channel container — (2026-10-16, agent; two clients; isolated test stack, not the dev …
- S55: Gear, goals, Spires, new-player sites, deleted characters — (2026-10-16, agent; needs a dev world restart)
- S56: Builds that may have lost voxels — (2026-10-15, Claude)
- S57: 0.695 features, day 2 — (2026-10-15, Claude)
- S58: Designs tab crash — (2026-10-15 night)
- S59: 0.695 client against the server — (2026-10-15)
- S60: Phase B code splits — (2026-10-15)
- S61: LaunchPad replacement — (2026-10-15 night)
- S62: Building, combat and loading fixes — (2026-10-14 night)
- S63: Deleting a site keeps your build — (2026-10-12)
- S64: Site screenshots — (2026-10-12)
- S65: Party frames — (2026-10-12)
- S66: Vaults and stack splitting — (2026-10-12)
- S67: Bag sort, guild ranks / description / tag — (2026-10-12)
- S68: Weapon combat: named attacks, affinity states, armour effects — (2026-10-12)
- S69: Cave Seeker — (2026-10-12)
- S70: Daily goals, loot toasts, harvest messages — (2026-10-12)
- S71: Outposts, Lost Chests / Crystals, treasure chests — (2026-10-12)
- S72: Monsters where launch had them — (module audit, 2026-10-12)
- S73: Placed Mailbox opens your mail — (module audit, 2026-10-12)
- S74: Spire Local Travel (underground portal crystals) — (UI audit, 2026-10-12)
- S75: Site Management: Special Access list — (UI audit, 2026-10-12)
- S76: Voxel Vision (builders on a site) — (UI audit, 2026-10-12)
- S77: Combat feedback: hit marker, damage-direction arrows — (UI audit, 2026-10-12)
- S78: Death and respawn, chat rooms, gathering drops — (audit rounds, 2026-10-12)
- S79: Combat: power attack Energy, hit impact effects — (Combat round 1, 2026-10-12)
- S80: Site access levels, prop rules, upkeep timer, Starting Location — (Claims / Props rounds, 2026-10-12)
- S81: Build sites: launch size, buffer, refusal texts, delete reply — (Claims rebuild round 1, 2026-10-12)
- S82: Player Studio: submit a design, peer review, result mail — commits 656c993, 4ea5527
- S83: Death, revive, duels, trees — commits 0ce255a, c3c627c, 1d8cfc8, (this)
- S84: Build site voting; guild invites after a relog — commits 5ce7dee, a5f81d8
- S85: Build site vendors (player shops) — commit 74826bc
- S86: Showcase: free store + build site gallery; winner banner — commit 429f110
- S87: Group invite popup Accept / Decline + creatures after a teleport — next commit
- S88: Guild invitation popup + roster promote / demote / kick — commits 1f49853 and next
- S89: Boss display for BOSS spawners (crash-safe version) — next commit
- S90: Crash fix: travelling while placing a prop — next commit
- S91: Bug reports and build site reports — commit b83fb4b
- S92: Site game queues — commit 86b1048
- S93: Story creatures: click + walk route + area events — commits 582a14a, ff7e21b
- S94: PvP damage numbers in site games — commit 7e409a0
- S95: Fell out of the world → safe point — commit ba7b0cc
- S96: Adventure end → Chaos Cavern; weapon tooltip names — commit 4313057
- S97: Game editor (Game Table) + visible spawn points — commits 4313057, 99c4b0a, 9816440, 27b6fd8
- S98: Hardening (limits) — commit 4b83046
- S99: Site game observers + contest voting — commit after a08d050
- S100: Build contests — commit after 236f53c
- S101: Held mining / tool swing rate (still open)
- S102: Digging: cleaner hole rims — commit after f0f6cce
- S103: Game Table settings — commit after a953832
- S104: Zone control — commit after 08f1ae1
- S105: Adventure games — commit after f378b52
- S106: Prop settings window — next commit after 5c51ddd
- S107: Story tools (Links) and Play Mode — commits c859069, 5c51ddd
- S108: Site games (instanced games) — commit 12d2cd0
- S109: Teleports / respawns (re-zone hang fixes) — commit 12d2cd0
- S110: Tells, group invites, cast refusals, prop limits (2026-10-12)
- S111: Build cursors, moving props, revive (2026-10-12)

## Classification table

Class: HARNESS / PARTIAL / USER / BLOCKED / OBSOLETE / DUPLICATE. Script: where it is checked ("-" = no script yet).

| Id | Line | Sec | Item | Class | Script | Checked by the harness / left for the person |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 7 | S1 | Melee sweep (L170): @TestPlayer wield 3031, @TestPlayer creature kobold 0 3, left-click swings: l… | HARNESS | 05-combat | server sweep round trip with the client's targeter reply; kobold hit once; air 0 hit; ranged; toggle off |
| 2 | 8 | S1 | Escape → Evac to Safety (non-admin character): on open ground → back on the ground and you can … | PARTIAL | 06-travel | Evac via the client's own Lua entry if present + "evacuated to"; "you can move" by hand |
| 3 | 9 | S1 | Magical Platform: place one, stand on it — it travels dock to dock and carries you; no "area" l… | USER |  | riding the platform needs movement input; look for area labels |
| 4 | 10 | S1 | Translocation Pads: hop, stand still on the arrival pad — no bounce back for 2.5 s. | USER |  | walking onto the pad needs input |
| 5 | 11 | S1 | Walk onto a build site across its border: Build Site Tourist / daily-goal visit counts. | PARTIAL | 08-sites | visitor teleported across the border in steps; real walking by hand |
| 6 | 14 | S2 | Walk backwards / sideways: backpedal slower (0.65×), strafe 0.75×, short speed-up / slow-down (… | USER |  | movement feel |
| 7 | 15 | S2 | Sprint Boots (give 2178 1) + Shift while running: ~25 % faster for ~5 s; tools still work after… | USER |  | Shift input |
| 8 | 16 | S2 | give 1120 1 jump + Space in the air = double jump; give 243 1 / give 244 1 hold Shift, release … | USER |  | jump / Shift input |
| 9 | 19 | S2 | Two players: the other sees sprint / glide and smoother start / stop. | USER |  | two players moving |
| 10 | 20 | S2 | Tooltips: chest armour (1466) and accessory (1620) show their affinity keyword in its colour. | USER |  | tooltip colours |
| 11 | 21 | S2 | Toggles: movementeffects off + relog, charstats off bring back the old behaviour. | PARTIAL | 05-combat | toggles answered in the log; "old behaviour" by feel |
| 12 | 22 | S2 | With the commerce service (COMPOSE_PROFILES=commerce, COMMERCE_ADDRESS=commerce:20400): Followi… | PARTIAL | - | only with a commerce container on dev; Following tab by hand (not scripted) |
| 13 | 26 | S3 | Showcase page on character A → Following tab: follow character B by name, tags fantasy and cast… | PARTIAL | 12-goals-store | client store loaded (ClientInGamePurchase.log); following by name in the Showcase page by hand |
| 14 | 29 | S3 | B publishes a tagged site to the Gallery (or @TestPlayer psreview <id> approve a design); @A fol… | PARTIAL | 12-goals-store | followdigest dry run; mailing needs a follow row |
| 15 | 34 | S4 | Lumen Station: "Recipe: X" for a prop you already know is not listed; using a recipe item takes… | USER |  | Lumen Station list contents |
| 16 | 37 | S4 | Regalia: @TestPlayer give 900344 1 / give 900345 1, wear each: Noble's = armour with a long blue… | PARTIAL | 11-items | outfits worn + screenshots; colours by eye |
| 17 | 39 | S4 | Creature weapons (off by default): @TestPlayer kobold, then @TestPlayer creatureweapon near Wield… | PARTIAL | 05-combat | weapon actor applied (log) + screenshot |
| 18 | 44 | S5 | Two players: one swims (surface, then under water); the other arrives from more than 200 m away… | USER |  | swimming needs input |
| 19 | 49 | S6 | Members get no monthly Lumens / discount by default (live); owners set MEMBER_MONTHLY_LUMENS / … | HARNESS | 12-goals-store | no stipend line by default |
| 20 | 50 | S6 | New character: Founder's Pack items arrive in the bag, Year Zero gifts by mail. | PARTIAL | 12-goals-store | existing character's kind-26 grants; a NEW character needs character creation |
| 21 | 51 | S6 | .env owner settings: MOTD_SUBJECT / MOTD_BODY show at login; STORE_PRICES changes a Showcase pr… | PARTIAL | 14-services | needs .env changes + restart (coordinator); admin entry line checked |
| 22 | 54 | S7 | Crimson Parlor without the Fantasy Parlor Pack: its faces / cat eyes / fantasy skin tints are l… | HARNESS | 12-goals-store | Lumen purchase through the client's own store call + "Added/Updated new store" in ClientInGamePurchase.log; Unlock button look by hand |
| 23 | 59 | S8 | Travel → Events world: the Halloween Party Island is open (2016 calendar until 20 Nov, then Win… | HARNESS | 13-terrain-world | Events world loads (client VoxelFarm / Landmark logs) |
| 24 | 60 | S8 | Outposts / higher Reward Chests: Ninja Mask / Samurai Helmet unlockers and Skeletal Bodysuits d… | PARTIAL | 05-combat | season setting in the start-up log; drops are random |
| 25 | 62 | S8 | Team site game: hitting a teammate earns grief warnings (GRIEF_AWARDS, on as live; our threshol… | USER |  | needs two players hitting each other in a team game |
| 26 | 63 | S8 | Gear: Coat of Crashing Waves sometimes bursts ice around you when hit; Armored Brawler's Double… | PARTIAL | 05-combat | struck with the coat equipped -> burst in the log; visuals by eye |
| 27 | 65 | S8 | /duel <name>, /duelaccept: the duel ends without a stray empty system line (was printed at ever… | HARNESS | 01-chat | tap: no empty chat line at the duel end |
| 28 | 68 | S9 | Site Management → Default Access: a visitor standing on the site gets the new level at once (no… | USER |  | Default Access change needs Site Management (no dev path) |
| 29 | 69 | S9 | Special Access → set a visitor to No Touching while Default Access is Play Around: they can no … | HARNESS | 08-sites | No Touching refuses A's prop edit (server path the client uses) |
| 30 | 74 | S10 | A player's title shows over their head for others, and changes when they pick another. | PARTIAL | - | title items exist (dev give / activate); overhead text for others by eye - not scripted yet |
| 31 | 75 | S10 | Grappling hook and swimming of another player are visible; the grapple objective (Getting Aroun… | USER |  | grapple / swim input |
| 32 | 76 | S10 | Saving a design stores its screenshot (design tile shows the picture). | USER |  | design save screenshot |
| 33 | 77 | S10 | Picking up a prop you may not pick up shows the refusal message (no permission, Play Mode, ches… | PARTIAL | 08-sites | refusal text in A's tap; which UI shows it by eye |
| 34 | 78 | S10 | Renaming a prop that allows a name works. | HARNESS | 08-sites | propname -> customName stored |
| 35 | 79 | S10 | Leaving / kicking a guild member removes them from everyone's roster; an expired party invite p… | HARNESS | 02-friends-groups, 03-guild | party invite expiry leaves B's queue; guild kick leaves A's roster data source |
| 36 | 80 | S10 | Crimson Parlor: others see your new look after Purchase. | USER |  | look |
| 37 | 81 | S10 | Desperado's Deadly Arsenal (admin wield 3038): LMB Run n' Gun, RMB Get 'em Good, E All or Nothi… | PARTIAL | 05-combat | LMB / RMB attacks logged; E ability needs input |
| 38 | 84 | S11 | On your site in Play Mode, a story creature behind a wall walks round through the doorway inste… | PARTIAL | 09-story | path log + screenshot |
| 39 | 86 | S11 | Decision: EVENT_ISLAND_EXPIRE_ON_CLOSE (off) — live expired left-over sites when an event islan… | USER |  | decision |
| 40 | 89 | S12 | Combat Node + Barren Grunt spawner, linked: the node's settings show Combat Tactics, then "Abil… | USER |  | settings window rows (no dev opener) |
| 41 | 91 | S12 | Player Studio: submitting a design shows "Listing Fee: 130" (260 at price 2000) and "Minimum Pr… | PARTIAL | 12-goals-store | psfee log; window values by eye |
| 42 | 96 | S13 | Travel to another landscape's Spire, and back home: you land ON the deck by the portal crystal … | HARNESS | 06-travel | pos on the deck, not falling (two landscapes) |
| 43 | 98 | S13 | Walk on build-site props after travel (walls, floors): they should be solid — every prop is now… | USER |  | walking into props |
| 44 | 99 | S13 | Crimson Parlor: Purchase changes your look and keeps your outfit (you confirmed 2026-10-17). | OBSOLETE |  | user confirmed 2026-10-17 (tick it) |
| 45 | 102 | S14 | General regression after the numbering change (every Claims / Mail / Store / site-game window):… | PARTIAL | several | covered across scripts; window-by-window regression by hand |
| 46 | 105 | S14 | Player Studio: a design priced 100 costs 130 Lumens to another player; the creator gets a "Sold… | PARTIAL | 12-goals-store | pstake: Lumens paid + Sold mail row; exact 130 / 52 noted for review |
| 47 | 106 | S14 | Grief (dev only, GRIEF_AWARDS off): grief award 1500 → chat "You have entered grief level 1." a… | HARNESS | 05-combat | grief lines in the client's chat + lock refusal + reset line |
| 48 | 108 | S14 | Underground: Local Travel to a crystal lands you on a graveyard plinth with the crystal on it. | PARTIAL | 06-travel | graveyard report + screenshot of the plinth |
| 49 | 111 | S15 | Zone control game (Team Zone Control table + Control Point): a label over each control point ("… | PARTIAL | 10-games | point taken (log) + screenshot of labels / bar |
| 50 | 115 | S15 | Cavern graveyards (test world only for now; off on dev until you OK it): graveyard go cavern la… | PARTIAL | 07-caverns | graveyard go cavern + screenshot |
| 51 | 118 | S15 | Warp: Spire → underground crystal costs 1 Lumen now (was 2); crystal → crystal 2. | HARNESS | 06-travel | Lumens 5 -> 4 -> 2 in the DB |
| 52 | 119 | S15 | IF Gate settings read If / Is / Value; the Combat Node has a Combat Tactics drop-down. | USER |  | settings window rows |
| 53 | 120 | S15 | F5 (Edit → Play) on your site: chat shows "The story on this Build Site has been saved!". | HARNESS | 08-sites | tap: "...has been saved!" |
| 54 | 123 | S16 | Pioneer lands are now live's zones 464-468 (flat plains with paths and a coast, the names "Pion… | PARTIAL | 13-terrain-world | pos vs the server's surface on 464 / 466; looks by eye |
| 55 | 128 | S16 | Barren creature props are no longer innate: placing a Barren Grunt from the Props tab says you … | HARNESS | 11-items | refusal + "You have learned a new prop" in the tap, learned prop in the log |
| 56 | 134 | S17 | Mail: when someone mails you (or a system mail arrives) a "You have N message(s)" prompt with I… | HARNESS | 12-goals-store | new-mail prompt in the tap + screenshot |
| 57 | 136 | S17 | Item tooltips of weapons list their affinity and Ranged / Melee; pet figurines "Pet". | USER |  | tooltips |
| 58 | 137 | S17 | Six-Shooters alternate right / left hand shots; cannon, raygun, recon blaster shots leave the s… | USER |  | visual |
| 59 | 138 | S17 | Zone control game: a label over each control point shows who holds it ("Not Controlled", a team… | DUPLICATE |  | same as 49 (zone control label) |
| 60 | 139 | S17 | Reset Node: everyone on the site sees "The story on this Build Site has been reset!". | HARNESS | 09-story | tap: "...has been reset!" |
| 61 | 140 | S17 | Holiday Sweater (Green) / (Blue): all green / navy blue now. | USER |  | colours by eye (sweater ids not in the scripts) |
| 62 | 141 | S17 | Cozy Campfire refused while jumping / falling / gliding (now from the client's own flags), fine… | HARNESS | 11-items | falling: refusal text in the tap; standing: consumed |
| 63 | 144 | S18 | Vendor Booth (89d587f): placing one gives a plain decoration — no shop window on click, no "My … | PARTIAL | 11-items | no shop window logged + screenshot |
| 64 | 146 | S18 | Log in the normal way (launcher → character select → Play) on dev: you enter the world as befor… | USER |  | character select / launcher |
| 65 | 151 | S19 | Type /say ~[~font color=~q~#FF0000~q~~]~hello in chat: everyone sees the text as typed with pla… | HARNESS | 01-chat | B's tap shows the tokens as text |
| 66 | 154 | S19 | Festive flight suits (wield 900322 / 900324): Merry Red now deep red, Festive Blue blue (were n… | PARTIAL | 11-items | screenshots; front view by eye |
| 67 | 156 | S19 | L121: walk, then jump — the dev log shows "movement state" changes; tell me if a jump shows 0x2… | USER |  | jump input |
| 68 | 157 | S19 | Story creature text (L96, 9fd07a0): a Story Node with Text Location "bubble" on a spawner's cre… | PARTIAL | 09-story | npc say lines; bubble position by eye |
| 69 | 159 | S19 | Abomination (creature abomination 0 9): its goo now visibly flies from its mouth (single and tr… | USER |  | visual |
| 70 | 161 | S19 | Spire Resupply Chest: drop or delete your Adventurer's pick / axe, then use the chest by the Sp… | HARNESS | 11-items | tools back + tap line; nothing when complete |
| 71 | 163 | S19 | Outposts: archers ("Ranged" spawners, Fireshot, Frostshot …) and casters (Flamemage, Stormmance… | PARTIAL | 07-caverns | garrison / sight lines; dodging by hand |
| 72 | 166 | S19 | Highfall Catapult (a site with one in Play Mode, or creature catapult 0 18): you now see the fl… | USER |  | visual |
| 73 | 167 | S19 | Bows, staffs, wands and rayguns: each shot now draws a flying arrow / bolt in the weapon's affi… | USER |  | visual |
| 74 | 169 | S19 | Hover a weapon attack on the action bar: the tooltip should now list its keywords in colour (e.… | USER |  | tooltips |
| 75 | 171 | S19 | Question: under the harness (game window not focused, no input) a character holding a weapon ke… | USER |  | question about real mouse focus |
| 76 | 176 | S19 | Wear an outfit, then equip chest armour: both stay on (armour in the Armor slot, outfit in Appe… | HARNESS | 11-items | equipped log shows outfit + armour |
| 77 | 180 | S20 | Switch a site to Play: story tools disappear for everyone (Scoreboard and Game Table stay); bac… | PARTIAL | 09-story | server hide line; what each player sees by eye |
| 78 | 183 | S20 | Chat rooms: lines now reach the room's members (the server read the room name 4 bytes off befor… | HARNESS | 01-chat | room line reaches B; ChannelOff; bubble colour request |
| 79 | 190 | S21 | outpost 0, walk out and back in: the client's own entry banner with the outpost's name and "Cre… | PARTIAL | 07-caverns | banner / name in the tap + screenshot; digging / building refusal |
| 80 | 192 | S21 | On another cave landscape (cavelands, travel to 391): the Spire's Local Travel lists that lands… | HARNESS | 06-travel | 391 warp network with its crystals |
| 81 | 196 | S22 | Moving platforms (harness: the Magical Platform pack places as platform + 2 docks on the own si… | PARTIAL | 08-sites | placement only; superseded by item 3 (movement) |
| 82 | 199 | S22 | Site inventory: props placed from bag items count as the site's items; deleting the site return… | HARNESS | 08-sites | paidByItem row; item back after the delete |
| 83 | 200 | S22 | Ownerless loot: lootfree 1579 5 → walking near claims it (others see it go). The rule for when … | PARTIAL | 11-items | claim by walking near (teleport) in the log; others see it go by eye |
| 84 | 202 | S22 | Admin: /run default resets (no freeze at 0); /clienttime modes; pathquery dx dy dz; relogin to … | PARTIAL | 04-admin | /run default, /clienttime echo, pathquery; char-select relogin and party frame by hand |
| 85 | 207 | S22 | Game Finder across worlds (two-world stack + a second client): a match on world 93 → Join Now m… | BLOCKED | - | needs a second world (one-server layout) |
| 86 | 214 | S23 | /playdead: the pose by eye, and its line as an emote ("You play dead.", no "says:"; fixed in 98… | PARTIAL | 01-chat | emote lines in both taps; pose by eye |
| 87 | 215 | S23 | Trial key: take the key from the letter, mail it to a friend's character, the friend uses it → … | USER |  | mailing an item through the compose window |
| 88 | 217 | S23 | Graveyards by eye: centrepieces on the cave floor and the surface; die underground → respawn at… | PARTIAL | 07-caverns | respawn after dying underground (log); creatures leaving the 30 m zone by hand |
| 89 | 219 | S23 | Goals: at a Replicator, give 51 250 and craft Cut Agate → goal 728 completes (trace/0695/agent-… | HARNESS | 12-goals-store | station 53 + craft 1558 -> goal 728 |
| 90 | 220 | S23 | Creature abilities: Combat Node ranks / specials, the ability name in combat lines (trace/0695/… | USER |  | combat lines on creatures, Combat Node specials (no dev opener) |
| 91 | 225 | S24 | Spawner Equip / Unequip weapon Links on a biped creature (Highfall 2597): relaxed stand ↔ weapo… | PARTIAL | 09-story | storyweapons log; stance by eye |
| 92 | 228 | S24 | Game Table 2313 "On Game Ended" linked to a Reward Chest: gamestart, gamescore 5, gameend → the… | HARNESS | 10-games | On Game Ended -> chest loot for the winner |
| 93 | 236 | S25 | By eye: the five letters in the inbox (Founder's Pack 1-3, the Year Zero letter, giveaways) and… | PARTIAL | 12-goals-store | letters stored (DB); reading them by eye |
| 94 | 239 | S25 | Settings: FREE_STORE=true brings back the free catalogue; FOUNDER_ENTITLEMENTS=false takes the … | PARTIAL | - | setting change + restart (coordinator) |
| 95 | 243 | S26 | Two players on two worlds of the same gateway (dev two-world stack: GATEWAY=1 up.sh, world2.sh)… | BLOCKED | - | needs a second world |
| 96 | 249 | S27 | On someone else's build site without Get Inspired in your access: select blocks and Ctrl-C (or … | USER |  | selection + Ctrl-C input |
| 97 | 255 | S28 | Admin slash commands now reach the server for admins (WorldServer "Admins"): the admin flag in … | HARNESS | 04-admin | admin slash commands through Ui.ProcessChatCommand |
| 98 | 264 | S28 | Login queue (needs character select, so a real Play click): on the dev or test world type the d… | USER |  | character select Play click |
| 99 | 268 | S28 | Telemetry: after a play session the world's Telemetry/<date>.jsonl should hold Lua metrics / UI… | HARNESS | 13-terrain-world | Telemetry/<date>.jsonl has lines |
| 100 | 271 | S29 | Guild window (N) → News tab: shows a row for each member joining, leaving, being kicked, promot… | HARNESS | 03-guild | GuildLogEntries rows after join / kick |
| 101 | 276 | S29 | Ownerless loot: nothing makes loot ownerless yet (when live did is lead L141); a non-owner can … | PARTIAL | 11-items | B never takes A's bag (log) |
| 102 | 282 | S30 | Translocation Pad (prop palette, innate): placing it puts down two pads 5 m apart (harness: ver… | USER |  | walk-on needs movement |
| 103 | 289 | S30 | Spring-loaded Flinger (flinger + target cube) and Spring-loaded Launcher: step on the flinger →… | USER |  | client physics |
| 104 | 292 | S30 | Guild invitations: invite someone, let them decline → you see "[name] has declined the invitati… | HARNESS | 03-guild | decline lines in both taps; revoke line (invitation id CAL) |
| 105 | 295 | S30 | Abomination spit: it should now be dodgeable by strafing; every other attack is a spread of thr… | USER |  | dodging |
| 106 | 297 | S30 | Memory: after visiting a second island and waiting 15+ minutes, the world's memory should drop … | PARTIAL | 13-terrain-world | gc line; the 15-minute drop needs a long wait |
| 107 | 317 | S31 | No change at rest: start-up without errors; /admin/monitor (admin token) shows "Pathing Informa… | PARTIAL | 14-services | monitor page tables (curl); tick numbers by eye |
| 108 | 320 | S31 | Around walls (the main check): outpost 0 (or any outpost with walls and doorways). Wake a garri… | PARTIAL | 07-caverns | pathing report (Blocked / Clear) |
| 109 | 324 | S31 | Out of reach: stand on a wall top, a ledge or a high ruin floor with no way up: a chaser stands… | USER |  | movement onto ledges |
| 110 | 327 | S31 | Floors and stairs: creatures on an outpost's upper floors and stairs stay on them (not sunk int… | USER |  | visual |
| 111 | 330 | S31 | Abomination: hide behind a wall within 16 m: it walks round until it sees you, then spits (no s… | USER |  | visual |
| 112 | 332 | S31 | Wandering: idle cavern creatures stroll around their spot but never into a wall or off a ledge. | USER |  | visual |
| 113 | 333 | S31 | A newly stamped island outpost (cavelands, then travel to an unstamped island so its outposts a… | BLOCKED | - | CAVERN_LANDSCAPES=all on dev first |
| 114 | 336 | S31 | Surface: kobold near a steep cliff or crater rim (off a build site): a chasing kobold goes roun… | USER |  | visual |
| 115 | 338 | S31 | Load: with several players fighting in different caverns, docker stats memory for the world con… | PARTIAL | - | needs several players (docker stats by the coordinator) |
| 116 | 353 | S32 | Start-up: log "Caves: Chaos Caverns on 23 more landscape(s) (CavernLandscapes = all): 391 (582)… | PARTIAL | 13-terrain-world | start-up line (skips unless CAVERN_LANDSCAPES=all) |
| 117 | 357 | S32 | First visit stamps: cavelands (lists every island: caverns, outposts, state). Travel to an isla… | PARTIAL | 13-terrain-world | cavelands (skips unless all) |
| 118 | 362 | S32 | An island's outpost in the client: outpost 128 (= island 391's first outpost; island z starts a… | PARTIAL | 13-terrain-world | island outpost streamed (skips unless all) |
| 119 | 367 | S32 | Trips: Travel → An Underground Adventure ten times (or cave): lands in caverns of different rea… | PARTIAL | 07-caverns, 13-terrain-world | trip lines |
| 120 | 372 | S32 | Cavern life elsewhere: on an island, cavern creatures appear around you underground (creatures … | PARTIAL | 13-terrain-world | sight on an island (skips unless all) |
| 121 | 374 | S32 | Restart: restart the test world after an island was stamped: no "Stamped" lines for it ("… plac… | PARTIAL | - | restart by the coordinator |
| 122 | 378 | S32 | Preload (optional): OUTPOST_PRELOAD=true in socialtest.env: the islands are stamped one after a… | USER |  | optional preload decision |
| 123 | 380 | S32 | Dev world later: once the above passes, set CAVERN_LANDSCAPES=all in docker/worlds/dev.env (bac… | USER |  | decision / config |
| 124 | 398 | S33 | Dev world, in-process (after its next restart, no envserver): build / remove / paint / smooth w… | HARNESS | 13-terrain-world | build through the store, undo / redo, no warnings |
| 125 | 402 | S33 | Start-up and import: envserver log "Copied the world's terrain: … N files …; K ruin(s) from out… | PARTIAL | 14-services | skips without an envserver container |
| 126 | 409 | S33 | Login fetch and re-zones: log in at home: the edited cells load as on the dev world (Spire base… | PARTIAL | 14-services | skips without an envserver container |
| 127 | 413 | S33 | Build tools through the service: build / remove / paint / smooth / Heal tool / undo / redo / co… | PARTIAL | - | needs an envserver container on dev; tools by hand (not scripted) |
| 128 | 417 | S33 | Digs and heals: regendelay 60, dig with a pick outside sites: hole + items; after ~60 s "Terrai… | HARNESS | 13-terrain-world, 14-services | dig heals after 60 s |
| 129 | 421 | S33 | Service down mid-session: docker stop lmst-envserver-envserver-1 with the client in the world: … | PARTIAL | 99-outage | opt-in outage |
| 130 | 429 | S33 | Measure: docker stats memory of the world and the envserver (world should drop by the overlays … | PARTIAL | - | docker stats by the coordinator |
| 131 | 447 | S34 | 0.695 Player Studio numbering (fix, dev world too): the 0.695 client's InGamePurchase subs were… | PARTIAL | 12-goals-store | no "Commerce request 0x35" at login; submit by hand |
| 132 | 452 | S34 | Portrait packet (fix, dev world too): portraittest <your character guid> Portrait <icon id> (de… | HARNESS | 12-goals-store | "sent True" + no client read error |
| 133 | 455 | S34 | Start-up and import: commerce log "Commerce database lmemu_commerce ready", "Player Studio impo… | PARTIAL | 14-services | skips without a commerce container |
| 134 | 462 | S34 | Your products on world 91: log in; your Designs window still shows your listed / waiting design… | PARTIAL | 14-services | skips without a commerce container |
| 135 | 466 | S34 | Peer review across worlds: ps 901 3f in the FakeWorld file: its log shows Wanderer: InGamePurch… | BLOCKED | - | FakeWorld (second world) not on the one-server layout |
| 136 | 470 | S34 | Take a design across worlds: pstake 901 <id>: FakeWorld log GrantDesignReply … result 0 … snaps… | BLOCKED | - | FakeWorld |
| 137 | 473 | S34 | A design listed on world 92, taken on 91: psdesign 901 7001 50 Wanderers Copy (uses the snapsho… | BLOCKED | - | FakeWorld |
| 138 | 478 | S34 | Your own review popup gets world-92 products: psdesign 902 7002 30 Roamer Thing, then relog (th… | BLOCKED | - | FakeWorld |
| 139 | 481 | S34 | Gallery and likes: publish one of your sites to the gallery (Build Site Management, three tags)… | USER |  | gallery publish window |
| 140 | 484 | S34 | One contest list for both worlds: contestphase 1 entries (dev command); the Contest window list… | BLOCKED | - | FakeWorld part; contest entry itself in 12 |
| 141 | 491 | S34 | Showcase gallery entry: the site's card shows its contest name ("Community Showcase") and listi… | USER |  | Showcase card look |
| 142 | 492 | S34 | Portraits: portraittest <guid> Portrait <icon id> stores the image in the service (commerce log… | PARTIAL | 14-services | skips without a commerce container |
| 143 | 495 | S34 | Commerce stopped (docker stop lmst-commerce-commerce-1): submit a design → the client's own "Co… | PARTIAL | 99-outage | opt-in outage |
| 144 | 501 | S34 | Without COMMERCE_ADDRESS (GATEWAY=1 WC=1 bash re/test/socialtest/up.sh after down.sh, or the de… | PARTIAL | 12-goals-store | in-process paths covered by 12 |
| 145 | 505 | S35 | Craft / place a Crimson Parlor on your site (Build Mode → Crafting category): clicking it opens… | USER |  | clicking the Parlor prop |
| 146 | 507 | S35 | Props To You: drag your site's Starting Location somewhere else and save — "Place the Starting … | PARTIAL | 08-sites | Starting Location moved by propmove -> objective line |
| 147 | 509 | S35 | Restart the dev world twice: trees, creature camps and loot chests stand in the same places bot… | PARTIAL | - | two dev world restarts (coordinator) |
| 148 | 513 | S36 | Rotate a site's Starting Location, then visit the site from the gallery (or travel to your own … | USER |  | facing on arrival |
| 149 | 518 | S37 | Outpost creatures behind a ruin wall or cave rock don't notice you; archers / casters walk roun… | PARTIAL | 07-caverns | sight report |
| 150 | 521 | S37 | After killing an outpost's Elite, the Lost Crystal can be used for 15 minutes; after that it is… | PARTIAL | - | 15-minute wait (longer than a script) |
| 151 | 523 | S37 | First Underground Adventure, first outpost you approach, first crystal you use: each shows its … | PARTIAL | - | help pages need first-time state (fte clear) and a look |
| 152 | 525 | S37 | Use a crystal and accept "Eureka!": you go to another outpost as before. | HARNESS | 07-caverns | "Eureka!" offer in the tap, accepted through the queue |
| 153 | 526 | S37 | /who lists the players online on the world; /who Law only names containing "Law". | HARNESS | 01-chat | /who lines in the tap |
| 154 | 532 | S38 | Open the Spire's Landscapes window: the "Worlds" list shows Liberation and Courage. Pick Courag… | BLOCKED | - | second world |
| 155 | 534 | S38 | With a friend online on the other world: they don't see you go offline / online while you trave… | BLOCKED | - | second world |
| 156 | 535 | S38 | In a group with someone on the other world: the group stays together. | BLOCKED | - | second world |
| 157 | 536 | S38 | Log out on world 93 and log back in: you are on world 93. | BLOCKED | - | second world |
| 158 | 542 | S39 | Open a creature spawner's settings: both rows show, with sliders, and keep their values after c… | USER |  | settings window |
| 159 | 543 | S39 | Health 300 %: in Play Mode the creature takes about three times as many hits; its health bar st… | PARTIAL | 09-story | multiplier logged; hit count by hand |
| 160 | 544 | S39 | Damage 25 % and 300 %: its hits on you are much weaker / stronger. | PARTIAL | 09-story | multiplier logged; feel by hand |
| 161 | 545 | S39 | Any changed creature drops no loot bag when killed; at 100 / 100 it drops as before. | HARNESS | 09-story | no loot bag from a changed creature |
| 162 | 551 | S40 | Two players: A types /friend add B. A sees "Friend request sent to B."; B gets the popup. B cli… | HARNESS | 02-friends-groups | popup answered through GameActionQueue.Respond; list data source |
| 163 | 553 | S40 | Again with Dismiss: A sees "B has declined your friend request." and the list doesn't change. | HARNESS | 02-friends-groups | dismiss line; list unchanged |
| 164 | 554 | S40 | No answer for 2 minutes: A sees "Your friend request for B has timed out." | HARNESS | 02-friends-groups | 2-minute timeout line |
| 165 | 555 | S40 | Asking B twice while the first request waits: the client's "no friend spam" line, no second pop… | HARNESS | 02-friends-groups | no-spam line |
| 166 | 556 | S40 | B with "Automatically Refuse Friend Requests" ticked (or /autofriendson): A gets the declined l… | HARNESS | 02-friends-groups | auto-refuse line |
| 167 | 562 | S41 | Travel → An Underground Adventure a few times: every cavern you land in should have an outpost … | HARNESS | 07-caverns | three trips, each with cavern creatures / outpost |
| 168 | 563 | S41 | One cavern never shows the same ruin twice. | PARTIAL | - | needs many trips (log comparison) |
| 169 | 564 | S41 | Lost Crystal lockouts still work at an outpost numbered above 63 (outpost 70, use the crystal, … | HARNESS | 07-caverns | lockout text + kind-25 row |
| 170 | 576 | S42 | Invite across worlds: in the client /invite Wanderer: chat "Wanderer has been invited to your p… | BLOCKED | - | FakeWorld |
| 171 | 580 | S42 | Row of a member elsewhere: Wanderer's row shows the landscape name, full health, and no distanc… | BLOCKED | - | FakeWorld |
| 172 | 584 | S42 | Party chat: /p hello in the client: the fake world shows a ChatBase line "hello" for Wanderer. … | BLOCKED | - | FakeWorld |
| 173 | 586 | S42 | Invite from the other world: /leave first (window closes; the fake world gets RemoveGroup reaso… | BLOCKED | - | FakeWorld |
| 174 | 590 | S42 | Third member, leader, kick (P4906): with you leading, /invite Roamer, group 902 accept: a third… | BLOCKED | - | FakeWorld |
| 175 | 594 | S42 | Logout / world change grace: leave 901: within a second Wanderer's row shows as link-dead (grey… | BLOCKED | - | FakeWorld |
| 176 | 598 | S42 | Disband: form a group again (invite + accept), /party disband (or the frame menu) as leader: th… | BLOCKED | - | FakeWorld |
| 177 | 600 | S42 | Chaos Cavern group trip stays on its world: grouped with Wanderer only, take the Travel menu's … | BLOCKED | - | FakeWorld |
| 178 | 602 | S42 | Service outage: docker stop lmst-social-social-1, then /invite Wanderer: chat "Groups are unava… | PARTIAL | 99-outage | opt-in social outage |
| 179 | 606 | S42 | Dev stack (no SOCIAL_ADDRESS), two clients on one world: invite, accept, party frames (health /… | HARNESS | 02-friends-groups, 14-services | dev-world groups between two clients (+ social log when it runs) |
| 180 | 613 | S43 | Story Node lists (2.23): place a Story Node (2344), open its settings: Mood shows Angry / Calm … | PARTIAL | 09-story | creature takes the node's emote (log); list contents by eye |
| 181 | 619 | S43 | Announce Emotes in Chat (row 24): tick it on that node and click again: chat shows lines like "… | HARNESS | 09-story | announce lines in the tap |
| 182 | 622 | S43 | Triggering character (2.34): IF Gate set to "Triggering Character: Is a Real Player" = 1, a Lin… | PARTIAL | - | IF Gate triggering character (needs the IF Gate link ids; add to 09 after calibration) |
| 183 | 627 | S43 | Text expires in the same chain (P5964): node A (text "one", 3 s) and node B (text "two"); spawn… | HARNESS | 09-story | second node's line through the same creature |
| 184 | 630 | S43 | Bubble interrupt (2.5, P6107-P6113 recipe): two Story Nodes with 5 s bubbles and different text… | USER |  | bubble replacement visual |
| 185 | 634 | S43 | Counters in Edit Mode (2.19): playmode off, prop 2901 0 0 -3: the disc shows 00; propset <hex> … | PARTIAL | 09-story | setting applied + screenshot of the disc |
| 186 | 637 | S43 | Logic Filter reset (2.4): switch → filter "...Turn on the blue light."; filter "When the blue l… | HARNESS | 09-story | torch not toggled by the silent reset |
| 187 | 640 | S43 | Spawner name and duplicate (6.30): rename a creature spawner ("Bob"), set Reaction = Ally, Resp… | USER |  | rename + Duplicate through the prop menu |
| 188 | 644 | S43 | Partial loot chest (C.6): a Reward Chest someone looted once → as owner, pick it up: refused wi… | PARTIAL | - | needs B to loot A's Reward Chest (add after 10 runs) |
| 189 | 646 | S43 | Not built, for the record: Story Node Weapon Visuals and spawner "Spawn With Weapons Equipped" … | OBSOLETE |  | "Not built, for the record" - not a test |
| 190 | 660 | S44 | Start-up: gateway log "Serving world 1", "Zone back end registered: Zone '…' (world 1, every la… | PARTIAL | 14-services | skips without a gateway container |
| 191 | 663 | S44 | Channel 1 to the world channel: /join test (or a friend request) → WC log "Channel 1: 1 packet(… | BLOCKED | - | world-channel routing through the gateway (needs a WC container) |
| 192 | 665 | S44 | World channel stopped mid-session: docker stop lmst-worldchannel-worldchannel-1 → gateway log "… | PARTIAL | 99-outage | opt-in outage |
| 193 | 671 | S44 | Zone stopped with the world channel up: docker kill lmst-world-world-1 → gateway "Zone back end… | PARTIAL | 99-outage | opt-in, restarts the world |
| 194 | 677 | S44 | Both stopped: stop the WC, then kill the world → NetInfo.log channel=0, channel=1 and channel=3… | PARTIAL | 99-outage | opt-in, restarts the world |
| 195 | 680 | S44 | Login with no zone: stop the world, log in (re/dlogin.sh) → gateway "no back end serves landsca… | PARTIAL | 99-outage | opt-in, restarts the world |
| 196 | 685 | S44 | Re-zone (Spire / teleport to another landscape) with the gateway: no new gateway log lines exce… | PARTIAL | 14-services | skips without a gateway container |
| 197 | 695 | S45 | Start-up: WC log (docker logs lmst-worldchannel-worldchannel-1) shows "World-channel database l… | PARTIAL | 14-services | skips without a WC container |
| 198 | 699 | S45 | Walk onto a site of another character that is listed in the gallery and press Like. The like co… | PARTIAL | - | needs a gallery-listed site of another character and a WC container (not scripted) |
| 199 | 703 | S45 | Like the same site again: count unchanged, chat "You have already rated this build site." Your … | PARTIAL | - | refusal texts through the WC (not scripted; 08 checks the zone path, 346) |
| 200 | 706 | S45 | Contest walk-on (a site entered in a voting contest): the vote button state matches the rules a… | USER |  | contest walk-on button state |
| 201 | 709 | S45 | Zone fallback: docker stop lmst-worldchannel-worldchannel-1, like another gallery site: answere… | PARTIAL | 99-outage | opt-in outage |
| 202 | 713 | S45 | Dev command ratevote <claim id> (dev_commands.txt) with the container running goes through the … | PARTIAL | 14-services | skips without a WC container |
| 203 | 715 | S45 | Showcase gallery page: the site's rating equals the walk-on count (the zone's kind-11 copy). | USER |  | gallery page rating |
| 204 | 721 | S46 | In game type /admin; in the admin menu press Server monitor (top). The page opens in the in-gam… | PARTIAL | 14-services | page content via curl; in-game browser by hand |
| 205 | 726 | S46 | Same page in a desktop browser: copy the address from the in-game page (or the /admin URL with … | HARNESS | 14-services | curl: "Admins only" / page with the token |
| 206 | 729 | S46 | Place a Game Table on your site: "Instanced Game Information - Server" shows the game with its … | HARNESS | 14-services | game table on the monitor page |
| 207 | 731 | S46 | Optional (dev only): with AdminMonitorPush=true in the world's environment, an admin on that si… | PARTIAL | - | config change |
| 208 | 744 | S47 | Completing Becoming a Luminary adds "Getting Crafty" to the Journal (Getting Started) with the … | PARTIAL | 12-goals-store | objectives completed; Journal by eye |
| 209 | 747 | S47 | Getting Crafty: place a Replicator on your site → next step; craft Cut Plain Wood, a Farseer Pi… | PARTIAL | 12-goals-store | objective rows |
| 210 | 750 | S47 | Building Block Basics: Build Mode, Blocks tab [Shift-4]; Add tool [1]; place 200 blocks; Delete… | USER |  | build-mode input |
| 211 | 754 | S47 | New characters no longer start with the Modular Castle designs (only the 6 basic castle pieces)… | PARTIAL | - | needs a new character |
| 212 | 756 | S47 | Modular Thinking: place the Modular Castle Long Room; paste the Modular Castle Entry; Ctrl-Z; C… | USER |  | build-mode input |
| 213 | 761 | S47 | Tell me if the order of any steps feels wrong (the live order is inferred), and whether free-fo… | USER |  | opinion |
| 214 | 771 | S48 | Home landscape (406), Old Growth: the forest now has the sets' mix — clumps of big diamond / gn… | PARTIAL | 13-terrain-world | start-up line; forest look by eye |
| 215 | 776 | S48 | Chop one with an axe: wood is Burled, 5 hits (8 on the big pines / hollowed ones) under TREE_HEALTH=hits (health: 10 / 19 / 29 swings). It comes bac… | USER |  | chopping needs an axe swing at a tree |
| 216 | 778 | S48 | A Desert landscape (391 / 397 / 418): cacti in clumps, paddle / small / thornleaf trees, dead t… | USER |  | looks |
| 217 | 783 | S48 | No tree inside a build site or on the Spire; teleporting / logging in next to a tree doesn't le… | PARTIAL | 13-terrain-world | tree report; stuck-next-to-tree by hand |
| 218 | 784 | S48 | Look (not required): set "ProceduralPlacementDecor": true in WorldServer.json (or env), restart… | USER |  | optional config + look |
| 219 | 798 | S49 | Open the Game Finder, nothing ticked, Search: "Searching…" with the counts of world 91 alone (P… | USER |  | Game Finder window |
| 220 | 801 | S49 | Into fakeworld_cmd.txt: games 9001:406:100000012:1:2 9002:406:10000000C:0:2 9003:406:100000052:… | BLOCKED | - | FakeWorld |
| 221 | 805 | S49 | search 777 0 on: "Players in matchmaking" becomes 2; docker logs lmst-fakeworld shows "Matchmak… | BLOCKED | - | FakeWorld |
| 222 | 808 | S49 | Stop, tick only Team Games, Search: of the fake world's games only 9002 (Teams, PvE, inactive) … | BLOCKED | - | FakeWorld |
| 223 | 810 | S49 | Place or switch off a Game Table on world 91 while search 777 0 on runs: the fake world's next … | BLOCKED | - | FakeWorld |
| 224 | 812 | S49 | Link loss: bash re/test/socialtest/fakeworld.sh stop while searching → the fake world's games a… | BLOCKED | - | FakeWorld |
| 225 | 816 | S49 | With a second 0.695 client on world 91 waiting in a site game on the same landscape: Search sti… | USER |  | Game Finder window |
| 226 | 818 | S49 | Dev stack (no SOCIAL_ADDRESS): the Game Finder behaves as on 2026-10-15 (counts of this world, … | USER |  | Game Finder window |
| 227 | 826 | S50 | World log at start: "N achievements …" as before, no new warnings; play a while (gather on ore … | HARNESS | 05-combat | whole world log: no goal-only / wisp recipe found |
| 228 | 829 | S50 | Finish a 500-kill weapon goal you are close to (e.g. Assassin's Choice): its "Recipe: …" still … | PARTIAL | 12-goals-store | needs the 500-kill objective id (GOAL500_OBJ) |
| 229 | 831 | S50 | A character that already owns one of those recipe items can still use it (learns the recipe / p… | HARNESS | 11-items | owned recipe item still teaches |
| 230 | 840 | S51 | World start: console.log has "event islands available [460, 461, 462], Events world: zone 460 (… | HARNESS | 13-terrain-world | start-up line |
| 231 | 841 | S51 | Travel → Landscapes: a second world "Events" with one landscape "Halloween Party"; the main wor… | HARNESS | 13-terrain-world | client VoxelFarm / Landmark log lines, no bitmap errors |
| 232 | 848 | S51 | On the island: Travel → Landscapes highlights the Events world; place a build site (normal flag… | PARTIAL | 13-terrain-world | mining on the island; site placement by hand |
| 233 | 851 | S51 | Heights + proof of the bitmap names (closes L13 if it passes). Close every other client; with t… | USER |  | height bake uses a Frida hook (coordinator tool, not this harness) |
| 234 | 865 | S51 | After the bake (dev world restart so the grids load): travelling to the island lands at its Spi… | PARTIAL | - | after the bake |
| 235 | 867 | S51 | Switching: set EVENT_ISLAND=winter, restart: the Events world now lists "Winter Holiday Party" … | PARTIAL | - | EVENT_ISLAND=winter + restart (coordinator) |
| 236 | 870 | S51 | Decision for you: sites left on an island when it closes. Live expired them: Dec 2015 "feel fre… | DUPLICATE |  | same decision as 39 |
| 237 | 879 | S52 | Build Mode → Mesh Shapes tab: categories Cones / Cylinders / Polyhedrons / Other list shapes wi… | USER |  | Mesh Shapes tab |
| 238 | 881 | S52 | With no tool active, click a mesh shape: the Add tool comes up with that shape on the cursor; t… | USER |  | clicking a shape |
| 239 | 885 | S52 | Remove tool, Paint, Smooth and the Fill Brush (Fill Stamp) with a mesh shape: each works like w… | USER |  | tools with mesh shapes |
| 240 | 887 | S52 | Two players (or a second window): with player B 20-50 m away, A places a mesh shape with Add: B… | USER |  | two players building |
| 241 | 898 | S53 | /target <name> of a character who is not in view (offline is fine): chat prints "Admin Target s… | HARNESS | 04-admin | "Admin Target set to Guid" |
| 242 | 900 | S53 | /goto <name> (also /warp, partial names work) with a second character online, on another landsc… | HARNESS | 04-admin | arrives within 5 m |
| 243 | 902 | S53 | /hideme: the other player sees you vanish (and does not see you move); /hideme off (or /hideme … | PARTIAL | 04-admin | server hide + screenshots of the other view |
| 244 | 904 | S53 | /run 30 then run (faster), /run default (normal), /run inc 5 / dec 5. The client prints "Player… | PARTIAL | 04-admin | client line; the actual speed by feel |
| 245 | 907 | S53 | /servertime hour 22 (night at once, for everyone online), /servertime pause (sun stops; again =… | PARTIAL | 04-admin | log + GameData.Time rows + screenshot |
| 246 | 909 | S53 | /clienttime pause / /clienttime hour 6 / /clienttime advance 3600: only your own client's clock… | PARTIAL | 04-admin | server echo + screenshot |
| 247 | 911 | S53 | /prop 489 (a mailbox) while looking at the ground near you: the prop appears there facing you, … | HARNESS | 04-admin | prop stored; wipe "disabled" |
| 248 | 913 | S53 | /friend forceadd <name>: the name lands on your friends list without an invitation; /friend rem… | HARNESS | 04-admin | friends data source |
| 249 | 915 | S53 | /vehicle 1, /setmodel 1, /ammo add 10, /weaponset dump, /placedeffect x y, /profile list: a cha… | HARNESS | 04-admin | six "not built" lines |
| 250 | 925 | S54 | Both characters on one landscape, within 150 m. A: /duel <B name>. A sees "You challenged B to … | HARNESS | 01-chat, 14-services | challenge lines (in-process or WC) |
| 251 | 929 | S54 | B: /dueldecline → A "B declined your duel.", B "Duel declined." (WC log "B declined A's duel.")… | HARNESS | 01-chat | decline line |
| 252 | 931 | S54 | B: /duelaccept → both get "Duel: A vs B. Fight!" within a second. WC log: "Encounter 1073741825… | HARNESS | 01-chat, 14-services | "Fight!" / encounter started |
| 253 | 934 | S54 | Swing a weapon at each other: hits land only between the two (health bars drop; a third player … | USER |  | hits between the two players need weapon input |
| 254 | 938 | S54 | New duel; one walks more than 150 m away (or travels to another landscape): the walker gets "Yo… | HARNESS | 01-chat, 14-services | forfeit lines (reason 1) |
| 255 | 941 | S54 | Service outage: start a duel, then docker stop lmst-worldchannel-worldchannel-1. Zone log: "Wor… | PARTIAL | 99-outage | opt-in outage |
| 256 | 945 | S54 | The regression scenario re/test/scenarios/duel.json against the plain dev stack (no container) … | HARNESS | 01-chat | in-process duel lifecycle |
| 257 | 950 | S55 | One of each accessory: give 1607 2, equip one Bracer of Survival, drag the second into the othe… | USER |  | dragging into the second slot |
| 258 | 953 | S55 | Tool wear: wield 3257 (Farseer Pick) and mine; durabilityof 3257 in the log shows wear going up… | HARNESS | 11-items | wear rises; Adventurer's tools never wear |
| 259 | 956 | S55 | Group dailies: two characters in a party within 200 m; one chops / mines: both get the harvest … | PARTIAL | 02-friends-groups | harvest by A counted for B (log) |
| 260 | 958 | S55 | Spires: landscape 417 (and a classic one, e.g. landscape 391): you arrive on a Spire platform n… | PARTIAL | 06-travel | on 417's Spire platform + mining refused; looks by eye |
| 261 | 962 | S55 | New-player sites: a new character still starts on a Pioneer land (now never on the Spire's spot… | PARTIAL | - | needs a new character |
| 262 | 963 | S55 | Deleted characters: delete a character that is someone's friend / in a guild; within a minute i… | USER |  | character deletion at character select |
| 263 | 970 | S56 | Look at your sites for holes where you used those materials. | USER |  | looking at sites |
| 264 | 971 | S56 | Outposts are now the 0.695 ruins, re-stamped after the fix: tell me if cave rock still pokes th… | USER |  | looking at ruins |
| 265 | 986 | S57 | Build mode > Designs: the castle pieces now sit in their own "Starter Castle" tab (not "Collect… | USER |  | Designs tab |
| 266 | 989 | S57 | Item uses that are refused (a 9th Build Site Expansion, a known prop recipe, a known design) no… | PARTIAL | 11-items | tap + screenshot of the box |
| 267 | 991 | S57 | Overflow "Retrieve all" with a full bag: resources (ore, gems...) go to the resources tab. Veri… | USER |  | "Retrieve all" button + tab look |
| 268 | 997 | S58 | As Player: build mode > Designs > your designs > click "Crash test: old site design" (a copy o… | USER |  | Designs window click as Player |
| 269 | 999 | S58 | Also: /admin works for Player (added as an admin). | USER |  | Player's own client |
| 270 | 1005 | S59 | Your 0.695 copy (<0.695 install>) through LaunchPad against the Docker servers: log in, walk … | USER |  | the user's own 0.695 copy through LaunchPad |
| 271 | 1008 | S59 | Look for: at the spawn, the 0.589 client shows a purple glow on the nearby figure and the 0.695… | USER |  | visual |
| 272 | 1015 | S60 | Dig with the pick on open ground, and use the build tools (add / remove / smooth) — terrain edi… | PARTIAL | 13-terrain-world | dig + store build lines; real tools by hand |
| 273 | 1017 | S60 | Add a friend, ignore someone, guild chat and a mail with an item (social interface). | PARTIAL | 02-friends-groups | friend add / ignore; guild chat and mail with an item by hand |
| 274 | 1018 | S60 | A trade with another player and a Player Studio listing (world-channel / commerce interfaces). | USER |  | trade window |
| 275 | 1019 | S60 | Log out to character select (Escape → Logout) and press Play again, on the 0.695 client: the co… | USER |  | character select |
| 276 | 1022 | S60 | Inspect (0.695): right-click another player → Inspect; their gear, Luminary Score, build sites … | USER |  | Inspect menu |
| 277 | 1024 | S60 | Mail through the social service (only with SOCIAL_ADDRESS set; off on your dev servers): write … | USER |  | compose window |
| 278 | 1039 | S61 | Put it in your real Landmark folder (rename the original to LaunchPad.exe.orig first), press Pl… | USER |  | LaunchPad on the real install |
| 279 | 1040 | S61 | Look and feel: dragging the window, the settings tabs, dropping a .zip pack on the window. | USER |  | LaunchPad look and feel |
| 280 | 1041 | S61 | When the game closes (Exit Game, or a crash), LaunchPad comes back to the front. A crash shows … | USER |  | LaunchPad / Exit Game |
| 281 | 1046 | S61 | Test accounts made: lptest1 (native dev server), lpdocker1 (Docker); passwords are in my scratc… | OBSOLETE |  | information, not a test (test accounts made) |
| 282 | 1051 | S61 | Your decision: the LaunchPad.exe in the public GitHub release still has the undo bug. Rebuild a… | USER |  | decision (release asset) |
| 283 | 1053 | S61 | <0.695 install>'s undo backup was made by the old LaunchPad; undoing that copy would now repo… | USER |  | information / decision |
| 284 | 1064 | S62 | Weapon swings: the strike now plays to the end (it was cut off), and a swing repeats about ever… | USER |  | feel |
| 285 | 1066 | S62 | Fill / Line / Smooth shapes: the server stores exactly what your client draws; if a shape still… | USER |  | look |
| 286 | 1068 | S62 | Material counts: 1 item per voxel for building and mining; the tray stops updating after a relo… | USER |  | material tray |
| 287 | 1070 | S62 | Loading: with DirectThreadCount=2, fresh starts load in ~15 s; tell me if one ever sticks at 50… | USER |  | loading feel |
| 288 | 1071 | S62 | Armour colours in the world. | USER |  | look |
| 289 | 1074 | S63 | Build something on a site, then Site Management → Delete: your materials come back, and Designs… | HARNESS | 08-sites | "Deleted site ... material item" + new design row |
| 290 | 1078 | S64 | Site Management → Screenshots → take one: the window finishes (close button back), the shot sho… | USER |  | screenshot window |
| 291 | 1082 | S65 | In a party, the Party window shows each member's health and energy bars moving as they fight, t… | PARTIAL | 02-friends-groups | screenshot of the party frame after damage |
| 292 | 1086 | S66 | Place a Rustic Stone Vault on your site and click it: the Vault window opens; drag items betwee… | PARTIAL | 11-items | vault opens (log) + screenshot; dragging by hand |
| 293 | 1088 | S66 | Shift-drag a stack (e.g. Lightstones): pick an amount — a new stack appears where you drop it; … | USER |  | Shift-drag |
| 294 | 1092 | S67 | Inventory (I) → the sort dropdown (e.g. by name): the bag reorders and stays that way after a r… | HARNESS | 11-items | sort applied, order kept after relog |
| 295 | 1093 | S67 | Guild window (N), as leader: add a rank, rename one, tick / untick its permissions, remove it; … | USER |  | guild window |
| 296 | 1100 | S68 | Hold a weapon and Alt-hover it / the attack buttons: names like "Basic Attack: Shatter" with th… | USER |  | Alt-hover |
| 297 | 1101 | S68 | Fight a cavern monster with a few weapons: frozen / burning / knocked-down effects show on it a… | PARTIAL | 05-combat | states logged + screenshot |
| 298 | 1103 | S68 | Your buff bar shows Flow / Frenzy / Supercharge / Exalt … while they last. | PARTIAL | 05-combat | player state logged; buff bar by eye |
| 299 | 1104 | S68 | A shield weapon's Power Attack (Flame Bound Bastion, Blight-Warden's Pain): block a hit from th… | USER |  | blocking needs input |
| 300 | 1106 | S68 | Charged attacks (e.g. Frost Lord's Fury right-click, Staff of Frozen Sorrow left-click): hold t… | PARTIAL | 05-combat | charged attack logged; holding by hand |
| 301 | 1108 | S68 | Do attacks feel right (reach, cone width, damage)? The numbers are ours — tell me what feels of… | USER |  | feel |
| 302 | 1114 | S69 | Use a Cave Seeker on the surface: caves glow green through the terrain; use it again: they fade… | PARTIAL | 11-items | log + screenshot |
| 303 | 1117 | S70 | Journal (J) → Daily: three daily goals; doing one (e.g. kill 50 enemies, harvest wood) counts u… | PARTIAL | 12-goals-store | objective counts; Journal by eye |
| 304 | 1119 | S70 | Picking up loot (bags, harvest) shows "Copper Ore x12"-style toasts top-left. | HARNESS | 11-items | loot toast notification in the tap |
| 305 | 1120 | S70 | Swinging an axe at a rock / a pick in a build site: the game's own centred message ("It is not … | HARNESS | 11-items | harvest message notification in the tap |
| 306 | 1127 | S71 | Find an Outpost (Travel → An Underground Adventure, explore): the outpost name shows in chat as… | HARNESS | 07-caverns | outpost name in the tap |
| 307 | 1128 | S71 | Kill the Elite: does the Lost Crystal light up? Click it out of combat: channel, loot, then the… | PARTIAL | 07-caverns | crystal used after the wake; lighting by eye |
| 308 | 1130 | S71 | Open a Lost Chest / treasure chest with a friend within 50 m: both get loot bags. | PARTIAL | - | needs two clients at the chest (add to 07 with login_b) |
| 309 | 1138 | S72 | Walk near a Fiery Fungus on the surface: it swells, then explodes and hurts you; shoot one from… | HARNESS | 05-combat | fungus explosion line |
| 310 | 1140 | S72 | In a cavern: Chompers don't move but spray when you're close; the Abomination spits from a dist… | USER |  | creature behaviour |
| 311 | 1142 | S72 | Standing on the surface above a cavern: nothing underground attacks you. | USER |  | standing above a cavern |
| 312 | 1145 | S73 | Place a Mailbox prop on your site and click it: your inbox opens (as at the Spire's mailbox). | USER |  | clicking the mailbox prop |
| 313 | 1154 | S74 | At the Spire, click the big crystal: does the travel menu open? Is there a "Local Travel" choic… | PARTIAL | 06-travel | warp network opened + screenshot |
| 314 | 1156 | S74 | On the Local Travel map, switch to the cave layer and click a crystal: confirm window with the … | HARNESS | 06-travel | warp to an underground crystal |
| 315 | 1158 | S74 | Underground, walk to a Portal Crystal, click it: the map opens; travel back to the Spire. | HARNESS | 06-travel | crystal -> Spire |
| 316 | 1164 | S75 | Site Management → your site → permissions: add a player by name with a level: listed with that … | PARTIAL | 08-sites | Special Access data source (may need the window open) |
| 317 | 1173 | S76 | Build something on your site, open the site info window (top centre) → Voxel Vision: expand Bui… | PARTIAL | 08-sites | Voxel Vision data source |
| 318 | 1175 | S76 | With a second player allowed to build on your site: both names listed, each highlighting their … | USER |  | second builder on the site |
| 319 | 1176 | S76 | Heal or delete your blocks, reopen Voxel Vision: the count drops. | USER |  | healing / deleting blocks |
| 320 | 1185 | S77 | Hit a creature: does the reticle flash a hit marker for a moment? | USER |  | visual |
| 321 | 1186 | S77 | Let a creature hit you from the side or behind: does an arrow appear by the reticle pointing at… | USER |  | visual |
| 322 | 1194 | S78 | Die on your own site and choose the free respawn: do you come back at your site rather than a f… | PARTIAL | 06-travel | respawn line after dying on the site |
| 323 | 1195 | S78 | Two players: /join a room with a password, chat in it, /leave: messages only reach room members… | HARNESS | 01-chat | room join with password, leave without error 22 |
| 324 | 1196 | S78 | Mine and chop for a while: occasional Lumens / Adventure Essence / recipe drops; mining inside … | PARTIAL | - | long gathering session |
| 325 | 1207 | S79 | Hold right-click with a sword: power attacks until the blue bar is low, then roughly one every … | USER |  | holding right-click |
| 326 | 1209 | S79 | Hit a creature (Chaos Caverns / Underground Adventure) with sword, bow and staff: do you see im… | USER |  | visual |
| 327 | 1210 | S79 | Floating damage numbers: they need floating combat text on (console: togglefloatingcombattext) … | USER |  | visual |
| 328 | 1217 | S80 | Site Management > Upkeep tab: the days shown match the timer (7 on day one, +1 per day you log … | PARTIAL | 08-sites | upkeep data source |
| 329 | 1219 | S80 | Other player on your site with No Touching: they can't use your Replicator / chests; with Play … | PARTIAL | 08-sites | build access allows the edit; chest rules by hand |
| 330 | 1222 | S80 | Press F5 (Play Mode), try to place or pick up a prop: refused with the Play Mode text. | HARNESS | 08-sites | Play Mode placement refusal in the tap |
| 331 | 1223 | S80 | Teleport to your own site from the Travel menu: you arrive at the Starting Location (NW corner;… | PARTIAL | 08-sites | arrival line at the site |
| 332 | 1235 | S81 | Walk around your site: is the build area now much bigger (about 210 m wide, 180 m tall)? Can yo… | USER |  | walking the site |
| 333 | 1237 | S81 | Right-click a Build Site Flag while you already own a site: the max-sites message, no placement… | HARNESS | 08-sites | max-sites line in the tap |
| 334 | 1238 | S81 | Map (M) "Show Claimable Land": do sites show? (the client now gets the site list it asks for) | USER |  | map |
| 335 | 1239 | S81 | (Needs a character without a site) place a site close to another one: the client refuses with "… | USER |  | placement cursor |
| 336 | 1241 | S81 | Site Management > Delete on your own landscape: no error text; from another landscape: "must be… | PARTIAL | 08-sites | no "Unable to recover build site" after a delete |
| 337 | 1251 | S82 | Designs window > pick one of your designs > Submit to Player Studio: fill name, description, pr… | USER |  | submit window |
| 338 | 1253 | S82 | Log the OTHER character in: a popup about items waiting for review appears. Accept it: does the… | PARTIAL | 12-goals-store | review offer in B's tap |
| 339 | 1255 | S82 | Back on the first character: open mail. Is there a Player Studio "accepted" mail with the desig… | HARNESS | 12-goals-store | result mail row |
| 340 | 1256 | S82 | Repeat with thumbs down (and the rejection box): "rejected" mail. | PARTIAL | - | reject path: as 339 with psreview reject (not scripted separately) |
| 341 | 1261 | S83 | Die near another player: does their kill feed show it? (KnockedOut is now sent to everyone.) | USER |  | kill feed |
| 342 | 1262 | S83 | Right-click a dead player > Revive Player: in 0.589 this probably does nothing (the client can'… | OBSOLETE |  | 0.589 question; on 0.695 see revivechannel (reviveby) |
| 343 | 1264 | S83 | /duel <name> next to another player, they type /duelaccept; hit each other: hits land only betw… | HARNESS | 01-chat | in-process duel |
| 344 | 1266 | S83 | Trees and plants now get their full prop form when you see them: chop a tree and pick a plant a… | USER |  | walking into trees |
| 345 | 1268 | S83 | Use a food buff, travel to another landscape: is the buff icon still shown with the right time … | USER |  | food buff icon |
| 346 | 1274 | S84 | Walk onto another player's build site: does the walk-on window offer voting (and not after you … | PARTIAL | 08-sites | refusal for a site not in the gallery |
| 347 | 1275 | S84 | Get a guild invite and log out without answering; log back in within 5 minutes: does the invite… | HARNESS | 03-guild | guild invite back after relog |
| 348 | 1281 | S85 | Get a "Build Site Vendor" from the Showcase store (Decorations › Build Site Vendor) and place i… | OBSOLETE |  | vendor shops are off by default (VENDOR_SHOPS, item 63) |
| 349 | 1282 | S85 | Showcase › My vendors: list something from your bag (count and price; 10% fee in coins). | OBSOLETE |  | vendor shops off by default |
| 350 | 1283 | S85 | The other player clicks the vendor: does the Vendor window list it? Add to Cart, Confirm: coins… | OBSOLETE |  | vendor shops off by default |
| 351 | 1285 | S85 | Try to pick up the vendor while it still has stock: refused ("vendor inventory not empty"). | OBSOLETE |  | vendor shops off by default |
| 352 | 1296 | S86 | Click the HUD Showcase button: does "Signed in as <your name>" show? Browse Outfits / Decoratio… | USER |  | Showcase page |
| 353 | 1297 | S86 | Get an outfit and a decoration (try 10 of one): they appear in your bag; equip the outfit. | OBSOLETE |  | free store replaced by live prices (S25); FREE_STORE toggle |
| 354 | 1298 | S86 | Gallery tab: your site is listed; Visit a site (with two players, visit the other's). | USER |  | gallery visit |
| 355 | 1299 | S86 | Play a Deathmatch to the end: the banner reads "<winner> Wins!" (Team Deathmatch: "Blue team Wi… | HARNESS | 10-games | winner banner text + screenshot |
| 356 | 1305 | S87 | With two players: /invite Name. The other gets the popup. Decline: the inviter reads "X decline… | HARNESS | 02-friends-groups | decline / accept through the queue |
| 357 | 1307 | S87 | Teleport (Landscapes window, your own site) and then stand still: creatures and trees nearby sh… | HARNESS | 07-caverns | creatures streamed after a teleport |
| 358 | 1312 | S88 | Two players: one in a guild invites the other (/guildinvite Name or the Guild window): does the… | HARNESS | 03-guild | guild invite popup accepted |
| 359 | 1314 | S88 | Guild window (N) roster, right-click a member: Promote makes them Officer, Demote back to Membe… | PARTIAL | 03-guild | kick; promote / demote by hand |
| 360 | 1320 | S89 | Place a BOSS spawner (e.g. "Spawner: Ruins: Barren BOSS (1 Limit)"), enter Play Mode and fight … | USER |  | boss bar visual |
| 361 | 1326 | S90 | Start placing a prop (palette or bag), and without placing it travel (Travel window, death wind… | HARNESS | 06-travel | no crash travelling while placing |
| 362 | 1332 | S91 | Open the in-game bug report window, submit a report: check the new file in the server's Reports… | USER |  | bug report window |
| 363 | 1333 | S91 | Inside someone's build site, use "report this build site": a file appears in Reports/sites. | USER |  | report site window |
| 364 | 1334 | S91 | Game editor Add on an empty spawn point row now starts placing it for free (no materials needed… | USER |  | game editor row |
| 365 | 1338 | S92 | Two players: set a Game Table's Max Players to 1 (game editor), one joins and starts; the other… | HARNESS | 10-games | A queued + queue entry in the tap |
| 366 | 1340 | S92 | The first player leaves: the queued player's entry turns into "Join Now"; pressing it joins the… | HARNESS | 10-games | A joins when the place frees |
| 367 | 1341 | S92 | "Leave Queue" on the entry takes you out of the queue. | PARTIAL | 10-games | leave queue (log) |
| 368 | 1347 | S93 | In Play Mode, hover a spawned story creature: does the interaction cursor show? Use (click / E)… | PARTIAL | 09-story | clicking the creature runs Links; cursor by eye |
| 369 | 1349 | S93 | Linking Area: link its "When a creature enters this area..." to something visible, let a story … | HARNESS | 09-story | creature enters the area |
| 370 | 1355 | S94 | With floating combat text on (console: togglefloatingcombattext), two players in a Deathmatch h… | USER |  | two players hitting |
| 371 | 1360 | S95 | If you ever fall through the terrain / out of the world, you should be put back at the nearest … | HARNESS | 06-travel | client's safe-point request after falling |
| 372 | 1366 | S96 | Complete an adventure (link a switch to the Adventure table's "Give reward and teleport to a Ch… | HARNESS | 10-games | adventure end -> cavern |
| 373 | 1368 | S96 | Hover a weapon in your bag: its attacks should read Basic Attack / Power Attack (not "Ability N… | USER |  | tooltips |
| 374 | 1375 | S97 | Open the editor in game: in build mode select the Game Table and use its Settings (the same but… | PARTIAL | 10-games | editor opens (log) + screenshot |
| 375 | 1377 | S97 | Game Settings tab: expand General Settings and Game Type Settings. Do the rows show sensible na… | USER |  | editor rows |
| 376 | 1379 | S97 | Press Disable Game, change a value (e.g. Time Limit), Apply, Enable Game, then start a round: d… | PARTIAL | 10-games | settings applied to the next round |
| 377 | 1381 | S97 | Prop Settings tab: does it list Neutral Spawn Point (required) / Team Spawn Point - team? Place… | USER |  | editor tab |
| 378 | 1383 | S97 | Add on an empty row: starts placing that spawn point if you have one in your bag (or its materi… | USER |  | editor row |
| 379 | 1384 | S97 | Spawn points (blue/yellow/neutral markers) are now visible on sites: check they look right. | USER |  | look |
| 380 | 1385 | S97 | Random Node, Timer Node and Counter Node props (story widgets) are now visible when placed (Cou… | USER |  | look |
| 381 | 1391 | S98 | Drag a prop setting slider (e.g. a Timer's length) back and forth quickly, close and reopen the… | USER |  | slider dragging |
| 382 | 1393 | S98 | Nothing else should feel different. If a Links window ever answers "failed" during normal editi… | USER |  | feel |
| 383 | 1400 | S99 | Walk onto a site with a running game and press the walk-on window's "Observe" button (if shown)… | USER |  | observe button |
| 384 | 1402 | S99 | Walk onto a contest-entered site of another player: does the Recommend button appear and work o… | USER |  | recommend button |
| 385 | 1409 | S100 | Build Site Management → Contest (enter competition): does the drop-down list "Community Showcas… | PARTIAL | 12-goals-store | contest entry (log); drop-down by eye |
| 386 | 1415 | S101 | Hold the left mouse with the pick for ~5 s: does it keep swinging at about one dig a second? (T… | USER |  | holding the mouse |
| 387 | 1421 | S102 | Dig with the pick on flat ground and on a mountain top: do the hole edges look smooth? Any floa… | USER |  | look |
| 388 | 1428 | S103 | Open a Game Table's settings in build mode, move the sliders, close; start a round: does the ti… | HARNESS | 10-games | round uses 120 s / limit 3 |
| 389 | 1435 | S104 | Team Zone Control table + 2-3 Control Points; two players on different teams: contest a point (… | PARTIAL | 10-games | point taken; two-player contest by hand |
| 390 | 1443 | S105 | Build a small story on your site with an Adventure Game Table; walk on: the window shows "Adven… | USER |  | walk-on window |
| 391 | 1445 | S105 | Two players: both join, they cannot hurt each other, both score for kills. | USER |  | two players |
| 392 | 1451 | S106 | In build mode open a Timer Filter's / Dice Roll / Damage Node / creature spawner's settings (it… | USER |  | settings window |
| 393 | 1453 | S106 | Move a slider, close the window, open it again: is the value kept? Relog: still kept? | HARNESS | 09-story | setting stored in the DB |
| 394 | 1454 | S106 | Story Node: set Emote to "wave", type a line in its Text window and Save; link a switch to "Act… | HARNESS | 09-story | npc say + emote |
| 395 | 1464 | S107 | In build mode, select a placed prop (torch, door, switch, timer, dice, logic filter, spawner), … | USER |  | Links window |
| 396 | 1466 | S107 | Remove a link from the list; "remove all"; does the list refresh? | USER |  | Links window |
| 397 | 1467 | S107 | Press F5 (Play Mode) on your own site: creatures from spawners appear; press F5 again: they go … | HARNESS | 09-story | Play Mode spawns / Edit Mode removes |
| 398 | 1468 | S107 | In Play Mode click a Metal Switch: linked lights turn on/off. | HARNESS | 09-story | switch toggles the torch |
| 399 | 1469 | S107 | Timer Filter: link something to "Start this timer" and the timer's "completes" to a light; wait… | HARNESS | 09-story | timer runs |
| 400 | 1470 | S107 | Kill a spawned creature: does it come back after ~30 s? Do its "dies" links fire? | HARNESS | 09-story | dies link + respawn |
| 401 | 1471 | S107 | Travel Points: link a spawner's "When this creature spawns" to a Travel Point's "The creature w… | HARNESS | 09-story | travel point arrival |
| 402 | 1473 | S107 | Faction: link a switch to a spawner's "Set this creature's faction to Neutral": it stops attack… | HARNESS | 09-story | neutral |
| 403 | 1474 | S107 | Starting Location: place one on your site, travel to "My Sites": you arrive on it. In Play Mode… | HARNESS | 08-sites | arrival registered at the site |
| 404 | 1476 | S107 | Doors / gates / portcullises / shutters: link a switch's "When this switch turns on" to "Open t… | PARTIAL | 09-story | door state logged + screenshot; swing direction by eye |
| 405 | 1485 | S108 | Place a Deathmatch Game Table on your site; walk off and back on: does the walk-on window show … | USER |  | walk-on window |
| 406 | 1487 | S108 | With two players: both join; the 30 s countdown starts; hit each other with weapons; kills coun… | USER |  | two players fighting |
| 407 | 1489 | S108 | Team Deathmatch table: two players end up on different teams; same-team hits do nothing. | USER |  | two players |
| 408 | 1496 | S109 | Die in the world and use the free respawn on the death window: loading screen, then alive at th… | HARNESS | 06-travel | death + respawn |
| 409 | 1497 | S109 | Travel (Landscapes window, Spire, caves) a few times in a row quickly: no freeze. | HARNESS | 06-travel | rapid travel, client alive |
| 410 | 1502 | S110 | /whisper (or /tell) someone: only they see "[You] whispers: …"; you see "To [them]: …". Whisper… | HARNESS | 01-chat | whisper / reply / not found in the taps |
| 411 | 1504 | S110 | Invite someone already in a party, or invite twice: the client's own lines ("… is already in a … | HARNESS | 02-friends-groups | already-in-party / pending lines |
| 412 | 1506 | S110 | Drink a healing potion at full health: refused, and the hotbar slot does NOT start its cooldown… | PARTIAL | 05-combat | refused + potion kept; cooldown by eye |
| 413 | 1507 | S110 | Build Mode on your site: the prop limit line shows "n/50 Chests"; place / pick up a chest and i… | PARTIAL | 08-sites | prop limit line sent + screenshot |
| 414 | 1508 | S110 | Let a site lapse (or use Delete): the letter reads with line breaks, no "<br>". | HARNESS | 08-sites | lapse letter without <br> |
| 415 | 1513 | S111 | With a friend nearby in Build Mode: you see their tool cursor (box / sphere / selection) move a… | USER |  | two builders |
| 416 | 1515 | S111 | Click one of your placed props with the prop tool: the move gizmo opens and you can move / rota… | USER |  | gizmo |
| 417 | 1517 | S111 | Hold a power attack (charge) for a few seconds, several times in a row: every release lands, no… | USER |  | holding the charge |
| 418 | 1519 | S111 | Tints (2026-10-13 fix): log in fresh, open build mode → Tints, pick a colour and click your sto… | USER |  | tints |
| 419 | 1521 | S111 | Right-click one of your props outside build mode: no menu. Press Tab into build mode and right-… | USER |  | right-click menu |
| 420 | 1523 | S111 | Missing voxels: build something on your site, walk far away (out of sight), come back: everythi… | PARTIAL | 13-terrain-world | cube still there after leaving range + no VoxelDataErrors |
| 421 | 1525 | S111 | Build costs: every material with ∞ in the Materials tray (e.g. Halloween Black, terrain texture… | USER |  | materials tray |
| 422 | 1527 | S111 | Removed / built terrain looks the same from far away and while flying around (coarse copies ref… | USER |  | look |
| 423 | 1529 | S111 | Spam the add / remove tool, including the biggest brush: no stretch where clicks do nothing. | USER |  | input |
| 424 | 1530 | S111 | Terrain v2 (layers): build, remove, sphere tool on your site; walk/fly away and back; relog. No… | USER |  | look |
