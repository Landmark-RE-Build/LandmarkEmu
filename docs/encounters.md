# Encounters (EncounterBase 0x2C)

Decoded 2026-10-12 from the 0.589 client. Header of every encounter packet (reader FUN_142b7dda0):
`u16 base 0x2C, i16 sub, u32 (packet +0x14: the pending-encounter / participant id the client keeps at manager
+0x3e0c), u32 encounter id (packet +0x18)`. The client stores the encounter id at game +0x32f28 on every encounter
packet and echoes it in its own encounter requests.

Routing: the zone dispatcher sends 0x2C to the game's virtual handler FUN_142c428b0 (PlayClient vtable +0x2b0). It
takes 0x7D and 0x7E itself and hands every other sub to the encounter manager (game +0x31f00 → FUN_143310250; jump
table decoded with re/tools/decodeswitch.py, case blocks decompiled with re/decomp_jt.py). The participant state lives
in the manager at game +0x31f08.

## Server → client

| Sub | Packet | Body after the header | Client does |
|---|---|---|---|
| 2 | EncounterPacketPlayerEnter | u64 | FUN_14326ef00(manager): the player entered the encounter |
| 3 | EncounterPacketEncounterComplete | bool, u32, u32, u32, reward block ×2 (FUN_142b95550), … (reader FUN_1432d7200) | builds an EncounterCompleteEvent (scoreboard/rewards), then FUN_14326d9a0 (leave) |
| 4 | EncounterPacketEncounterLaunchFailed | (header) | system message to the "player" |
| 5 | EncounterPacketObjectiveState | list of ObjectiveData (FUN_143250cf0: u32 ×3, reward block, u32 ×5, u32 pair) | FUN_143272460(manager, objectives) |
| 6 | EncounterPacketEncounterShutdown | u64, u32, u32 (FUN_1432e60d0) | FUN_1432686d0(manager) unless the game flag +0x32ba1 is set |
| 0x66 | EncounterInvitationPacket | u64, u32 text id, u32 (FUN_1432e5ee0) | looks up the text ("String#%d not found" otherwise) and shows the invitation (FUN_1433467f0 on game +0x32488) |
| 0x6a | EncounterStatePacket | (header only) | read, no action |
| 0x6b | EncounterZoneIsReadyPacket | (header) | marks the encounter zone ready, then FUN_142cbe6e0 sends RequestEntrance |
| 0x72 | EncounterDetailsResponsePacket | EncounterDetailsCommon (FUN_1432d6ea0: u32, u32, two lists, u32, u32, bool, bool, u32, u32, objective block FUN_1432508f0 …) | updates the encounter list / details (FUN_143273c70, FUN_1432686c0) |
| 0x78 | EncounterParticipantMessagePacket | u32 text id, u64 player, string (FUN_1432e62e0) | chat-window message naming the "invited_player" |
| 0x79 | EncounterParticipantTerminateEncounterPacket | bool | system message to the "listener" |
| 0x7D | (death) | u32, u32 | `RespawnHandler:Show` (FUN_142cbca40) — the death window |
| 0x7E | (death) | — | `RespawnHandler:Hide`, restores the camera / control state |
| 0x82 | EncounterRunningListResponsePacket | List<EncounterDetailsCommon> (FUN_1432db7d0) | walks the list |

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

The flow the client requires, verified with re/test/scenarios/duel.json and adhoc/encprobe.js (client handlers that
only run on exactly-read packets):

1. **DetailsResponse (0x72)** to each participant first: details with +0x6491 = true (the receiver takes part) and
   the trailing bool false. The client then sets game +0x327c8 = 1 ("may enter", FUN_143273c70) and keeps the
   header's pending id. Without it the next step sends nothing (FUN_142cbe6e0 checks +0x327c8 == 1).
2. **ZoneIsReady (0x6B)**: the client answers with **RequestEntrance (0x6C)** {pending, encounter, u64}.
3. **PlayerEnter (2)** {u64 player} to every participant, then **State (0x6A)**.
4. Leaving: RequestExit (0x6D), logout, or the server ending it: **Terminate (0x79)** {bool} + **Shutdown (6)**
   {u64, u32, u32}. The client answers a shutdown with one more encounter request.

Every encounter packet carries the encounter id as both the pending id and the encounter id. The death window uses the
player's current encounter id (0 outside encounters).

Duels: 0.589 has no duel menu, popup or text (searched: data, Lua, UI, every locale, the exe). It only has the protocol,
StaticZoneData DUEL_ENCOUNTER_ID (0 everywhere), the AutoRefuse "DuelInvitation" option and an unused DuelRequest
popup type. The server registers `/duel <name>`, `/duelaccept`, `/dueldecline`, `/duelleave` in its dynamic command
list (Command 0x44). Rules: same landscape within 150 m, challenges last a minute, weapon hits land only between the
two duellists (Abilities → Encounters.Hit), a killing blow ends the duel at 1 health instead of death, moving apart or
changing landscape forfeits.
