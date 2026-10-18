# Encounters (EncounterBase 0x2C)

Decoded 2026-10-12 from the 0.589 client. Header of every encounter packet (reader FUN_142b7dda0):
`u16 base 0x2C, i16 sub, u32 (packet +0x14: the pending-encounter / participant id the client keeps at manager
+0x3e0c), u32 encounter id (packet +0x18)`. The client stores the encounter id at game +0x32f28 on every encounter
packet and echoes it in its own encounter requests.

Routing: the zone dispatcher sends 0x2C to the game's virtual handler FUN_142c428b0 (PlayClient vtable +0x2b0). It
takes 0x7D and 0x7E itself and hands every other sub to the encounter manager (game +0x31f00 → FUN_143310250; its
jump table read from the exe's bytes, as Ghidra did not recover it: unknowns.md "Switches Ghidra left undecoded"). The participant state lives
in the manager at game +0x31f08.

## Server → client

| Sub | Packet | Body after the header | Client does |
|---|---|---|---|
| 2 | EncounterPacketPlayerEnter | u64 | FUN_14326ef00(manager): the player entered the encounter |
| 3 | EncounterPacketEncounterComplete | bool, u32, u32, u32, reward block ×2 (FUN_142b95550), … (reader FUN_1432d7200) | builds an EncounterCompleteEvent (scoreboard/rewards), then FUN_14326d9a0 (leave) |
| 4 | EncounterPacketEncounterLaunchFailed | u32 text id (0.695 case 0x1433bd140, exact) | prints that text in the system channel |
| 5 | EncounterPacketObjectiveState | list of ObjectiveData (FUN_143250cf0: u32 ×3, reward block, u32 ×5, u32 pair) | FUN_143272460(manager, objectives) |
| 6 | EncounterPacketEncounterShutdown | u64, u32, u32 (FUN_1432e60d0) | FUN_1432686d0(manager) unless the game flag +0x32ba1 is set |
| 0x66 | EncounterInvitationPacket | u64, u32 text id, u32 (FUN_1432e5ee0) | looks up the text ("String#%d not found" otherwise) and shows the invitation (FUN_1433467f0 on game +0x32488) |
| 0x6a | EncounterStatePacket | (header only) | read, no action |
| 0x6b | EncounterZoneIsReadyPacket | (header) | marks the encounter zone ready, then FUN_142cbe6e0 sends RequestEntrance |
| 0x72 | EncounterDetailsResponsePacket | EncounterDetailsCommon (FUN_1432d6ea0: u32, u32, two lists, u32, u32, bool, bool, u32, u32, objective block FUN_1432508f0 …) | updates the encounter list / details (FUN_143273c70, FUN_1432686c0) |
| 0x78 | EncounterParticipantMessagePacket | u32 text id, u64 player, string (FUN_1432e62e0) | chat-window message naming the "invited_player" |
| 0x79 | EncounterParticipantTerminateEncounterPacket | bool took-too-long | bool set: prints the code string "TookTooLong" (not in CodeStringMappings.txt of either build, so ClientStringProvider FUN_142fdeba0 returns text id 0 and the line is text 0). Then, either way, cancels the encounter (FUN_142ca6d90(game, 0): game +0x327c8 / +0x327cc / +0x3286c reset) and leaves it (FUN_14331a850: one RequestExit 0x6D when it took part, then two ActivityListRefreshRequests 0xA0) |
| 0x7D | (death) | u32, u32 | `RespawnHandler:Show` (FUN_142cbca40) — the death window |
| 0x7E | (death) | — | `RespawnHandler:Hide`, restores the camera / control state |
| 0x82 | EncounterRunningListResponsePacket | List<EncounterDetailsCommon> (FUN_1432db7d0) | walks the list |

### 0.695 dispatch (verified 2026-10-17)

0.695 routes the family the same way: the game handler FUN_142cdc370 (0x7D → FUN_142d55e00, 0x7E inline) and the
encounter manager's vtable +0x18, FUN_1433bd040. Ghidra did not recover its switch; the table is two-level (from the
exe, `movzx eax, byte [0x1433be214 + sub − 2]; mov ecx, [0x1433be1e0 + eax·4]; add rcx, image base; jmp rcx`, for
sub − 2 ≤ 0x80):

| Sub | Case block | Reader (0.695) | Body after the header | Handler |
|---|---|---|---|---|
| 2 | 0x1433bd2b1 | inline | u64, exact | FUN_14331bdb0 (0.589 FUN_14326ef00) |
| 3 | 0x1433bd373 | FUN_143383ff0 | as 0.589 FUN_1432d7200 | EncounterCompleteEvent, game +0x32ba1 = 1, FUN_14331a850 |
| 4 | 0x1433bd140 | inline | u32 text id, exact | FUN_142d8b250 prints it |
| 5 | 0x1433bd653 | FUN_1432fdba0 | objective list | FUN_14331f310 |
| 6 | 0x1433bd729 | FUN_143392ec0 | u64, u32, u32, exact | FUN_143315580 (UI ":exit" only) unless game +0x32ba1 |
| 0x66 | 0x1433bda14 | FUN_143392cd0 | u64, u32 text id, u32, exact | FUN_1433f28b0 on game +0x32488 ("String#%d not found" otherwise) |
| 0x6A | 0x1433be142 | inline | header only | none |
| 0x6B | 0x1433bdb01 | inline | header only, exact | FUN_14331f4a0, game +0x32f28, FUN_14330e140, RequestEntrance FUN_142d57a70 |
| 0x72 | 0x1433bd7a3 | FUN_143392bc0 | details FUN_143383c90 (objective block FUN_1432fd7a0: its string is now read by FUN_142d4b5d0, same i32 length + bytes), bool, u32, int list FUN_143232f90; exact | FUN_143320b20 sets game +0x327c8 = 1 for a participant ("may enter"), manager +0x3e0c = pending id |
| 0x78 | 0x1433bddbb | FUN_1433930d0 | u32 text id, u64 player, string, exact | prints the text naming the player |
| 0x79 | 0x1433bdbcd | inline | bool, exact | see the table above |
| 0x82 | 0x1433be061 | FUN_1433885c0 | list of details | walks the list |

Everything else goes to the default (0x1433be1ab, nothing). The client → server writers are unchanged: 0x67
FUN_142c42b70 {u64 +0x20, u8 +0x28}, 0x69 FUN_1433e9320 {u64 inviter, u8 accept}, 0x6C FUN_142c42f00 {u64}, 0x6D
FUN_142c430b0 and 0x7C FUN_142c42d60 header only (0x7C carries manager +0x3e0c as the header's pending id); all go
out on the World channel (`channel << 5 | 6` with channel 1). The server's bodies matched all of these; the one change
was Terminate's bool (it was true, which made the client print a chat line for the unmapped "TookTooLong", i.e. text id 0, at every duel end).

## Client → server

| Sub | Packet | Body after the header (encounter id in the header) | Sent by |
|---|---|---|---|
| 0x67 | EncounterInvitationResponsePacket | answer | the invitation prompt (FUN_143350240), also auto-answered in Zone_DispatchPacket |
| 0x69 | EncounterDuelInvitationResponsePacket | u64 inviter, u8 accept | FUN_1433501d0 |
| 0x6c | EncounterParticipantRequestEntrancePacket | u32 (manager +0x3e0c), u64 (game +0x32508) | after ZoneIsReady (FUN_142cbe6e0) |
| 0x6d | EncounterParticipantRequestExitPacket | — | leaving (FUN_142ca26b0, FUN_14326d9a0) |
| 0x7c | EncounterParticipantCancelPendingEncounterPacket | u32 (manager +0x3e0c) | cancelling a pending encounter (FUN_142c0cfb0) |

## Content in 0.589

- StaticZoneData has a DUEL_ENCOUNTER_ID column; it is 0 for every zone, so duels were not configured.
- No encounter definition file ships with the client; the live 0.589 server used this base for the death window
  (0x7D / 0x7E), which the server sends (Game/Health.cs).

Matchmaking for encounters is a separate family (EncounterMatchmaking, base 0x8A: queues, match invitations).

## Server (Game/Encounters.cs)

The flow the client requires, verified in a running client with a scripted duel and a Frida probe on the client
handlers (they only run on exactly-read packets):

1. **DetailsResponse (0x72)** to each participant first: details with +0x6491 = true (the receiver takes part) and
   the trailing bool false. The client then sets game +0x327c8 = 1 ("may enter", FUN_143273c70) and keeps the
   header's pending id. Without it the next step sends nothing (FUN_142cbe6e0 checks +0x327c8 == 1).
2. **ZoneIsReady (0x6B)**: the client answers with **RequestEntrance (0x6C)** {pending, encounter, u64}.
3. **PlayerEnter (2)** {u64 player} to every participant, then **State (0x6A)**.
4. Leaving: RequestExit (0x6D), logout, or the server ending it: **Terminate (0x79)** {bool false} + **Shutdown (6)**
   {u64, u32, u32}, to the leaver and, when the encounter ends, to everyone left in it. Terminate resets the client's
   encounter state (Shutdown alone only fires the UI's ":exit" event) and makes it send one RequestExit, which the
   server ignores because the player is already out.

Every encounter packet carries the encounter id as both the pending id and the encounter id. The death window uses the
player's current encounter id (0 outside encounters).

## Where it runs: world channel vs zone

Every encounter request the client sends travels on the **World** channel (channel 1): InvitationResponse 0x67
(FUN_143350240 / 0.695 FUN_1433fc300), DuelInvitationResponse 0x69 (FUN_1433501d0 / FUN_1433fc290), RequestEntrance
0x6C (FUN_142cbe6e0 / FUN_142d57a70), RequestExit 0x6D (FUN_142ca26b0 / FUN_142d3bbd0), CancelPending 0x7C
(FUN_142c0cfb0 / FUN_142ca6d90) — research/trace/architecture/world-channel-service.md §3.4. So live's World back end ran the
encounter **lifecycle** and the zone, which has positions and health, ran the **combat**. Since 2026-10-16 (build step 7
of the world-channel service) the server is split the same way:

| Part | In-process (default, or the service away) | World-channel container linked (`docker/worldchannel`) |
|---|---|---|
| Challenge (/duel), the range / landscape / online checks | Game/Encounters.cs Challenge | zone checks, then **EncounterCreateDuel** (0x2C/0x10051) to the service, which holds the challenge (one minute) |
| Answer (/duelaccept, /dueldecline — zone commands; client 0x69 — World channel) | Encounters.Answer | commands → **EncounterDuelAnswer** (0x10053); 0x69 reaches the service directly |
| Lifecycle packets: Details 0x72, ZoneIsReady 0x6B, PlayerEnter 2, State 0x6A, Terminate 0x79, Shutdown 6 | Encounters.cs | LandmarkEmulator.WorldChannel/EncounterBroker.cs, byte-identical (Encounter numbers are the same in 0.695, sent as given; Zone-channel destination as before) |
| Client requests 0x67 / 0x69 / 0x6C / 0x6D / 0x7C | Encounters.Handle | EncounterBroker.TryTake takes those of its encounters / challenges (ids from 0x40000001); the rest go on to the zone (an encounter the zone started while the service was away) |
| Participant list for the duel rules (Duelling, Hit, the 150 m / landscape tick, the death window's encounter id) | the zone's own registry | **EncounterParticipants** (0x10050: u32 encounter, string kind, u32 state 0 ended / 1 open / 2 running, n × u64 char) pushed on every state change; the zone mirrors it (Encounters.BrokerParticipants) |
| Forfeits: a killing blow, apart / off the landscape, /duelleave, logout | Encounters.Leave | **EncounterForfeit** (0x10052: u32 encounter, u64 char, u8 reason 1 left / 2 defeated / 3 range / 4 logout / 5 landscape / 6 declined); encounter 0 + logout = drop the player's open challenge |
| Chat lines ("You challenged X to a duel.", "Duel: A vs B. Fight!", "You win!" …) | Chat.System | **EncounterNotice** (0x10054: u64 char, string) → the zone prints it through the same Chat.System |
| Death window 0x7D / 0x7E | zone (Health.cs) | zone (Health.cs), unchanged |
| Service link lost | – | the zone ends the mirrored encounters itself with Terminate + Shutdown (Encounters.BrokerLost, spec §7); the service forgets everything when the zone link drops |

Message layouts: LandmarkEmulator.Internal/WorldChannel/EncounterMessages.cs (ours; research/trace/architecture/internal-messages.md §2). The client
sees the same packets, texts and timings either way (challenge timeout one minute, forfeit at 150 m).

Duels: 0.589 has no duel menu, popup or text (searched: data, Lua, UI, every locale, the exe). It only has the protocol,
StaticZoneData DUEL_ENCOUNTER_ID (0 everywhere), the AutoRefuse "DuelInvitation" option and an unused DuelRequest
popup type. The server registers `/duel <name>`, `/duelaccept`, `/dueldecline`, `/duelleave` in its dynamic command
list (Command 0x44). Rules: same landscape within 150 m, challenges last a minute, weapon hits land only between the
two duellists (Abilities → Encounters.Hit), a killing blow ends the duel at 1 health instead of death, moving apart or
changing landscape forfeits.
