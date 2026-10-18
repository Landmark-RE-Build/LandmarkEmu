# TASKS E.14 — public chat channel on / off and chat bubble colour (audit #91, lead L65)

Branch `e-chat` (worktree `<workspace>\wt-chat`), commit e873dd6. Client: 0.695 (`<0.695 test copy>\Landmark64.exe`,
decompile `re/decomp_all_695`). [P] = proven from the client code, [I] = inferred.

## 1. Public channels, ChatChannelOn / Off

### 1.1 How a public line names its channel [P]
- The PacketChat trailing block after the message is the same in 0.589 (writer FUN_1432a93e0) and 0.695 (writer
  FUN_1433562c0, reader FUN_143356a90): `vec4 position (+0x460)`, `u64 (+0x478)`, `u32 (+0x484)`, `u32 (+0x480)`,
  `u32 (+0x470)`, `u8 (+0x238)`, `u8 (+0x488)`. That is 38 bytes. Then:
  - on channel 9 only, a `u16` public channel id (+0x20; the ctor FUN_143359fc0 defaults it to -1);
  - on channel 10 only, the room name string (+0x28).
- The second field is a **u64, not a string** (FUN_142c02460 writes 8 bytes). social-service.md §3.1, the old Chat.cs
  comments and L65 ("the unknown string after the position") said string.
  - Because of that, `RoomNameOf` in both Game/Chat.cs and SocialServer/ChatService.cs read the room name 4 bytes off.
    Room (channel 10) lines found no room, so they reached nobody.
  - Now fixed: the room name is read after the fixed 38 bytes. The TESTING-NEEDED line "Two players: /join a room …
    chat in it" is still unchecked; it should now pass.
- The dispatcher FUN_143364980 (case 1 → channel 9) passes `FUN_143b67f50(id)` as the line's channel name.
  FUN_143b67f50 returns "General" for 0 and null for every other id. So **General = public id 0**, and it is the only
  public channel the 0.695 client can show.

### 1.2 The client sets up General itself [P]
- The chat processor setup FUN_143367e40 (runs once, flag at +8) does three things:
  - Adds channels 0, 3, 4, 0xd, 7, 5, 6, 8, 1, 2, 10 and 9 to the default tab.
  - Calls `FUN_14335e8e0(this, 9, 0, "General", 1)`. This takes numbered slot 0 (channel 9 always uses slot 0;
    rooms start at slot 1). The slot's command is the index + 1, so `/1` sends PacketChat channel 9, id 0
    (send site FUN_143367ce0 copies the slot's channel / id / name into the packet).
  - Removes the separate "General" tab when settings +0x14f2 is 0. That byte is
    `GameSettings.Get/SetChatGeneralChannelSeperateTabEnabled` (FUN_1435f55f0; setter FUN_14348ec50 saves
    "GeneralSeperateTab"). Closing that tab clears the setting (FUN_143360270).
- The room join path refuses "General" and the reserved tab names as a room name (FUN_143367xxx near
  part_0100.c:11858, result 0x11).
- So in 0.695 the server does not have to "add" General. The Lua globals OnAddChatChannel / OnRemoveChatChannel are
  never called (agrees with the 10-17 sweep). The 0.695 "Chat Options" switch for General is only the separate tab;
  General itself has no on/off there.

### 1.3 Who sends ChatChannelOn / Off [P]
- The only builders are the Lua natives `Ui.ChannelOn` FUN_1435cb4b0 and `Ui.ChannelOff` FUN_1435cb760.
  - The 0.589 addresses FUN_143521850 / FUN_143521b30 in social-service.md are a hash-table resize in 0.695.
  - Layout: `{string}` read from Lua stack top, sent on the World channel (sender FUN_1435d4540 / FUN_1435d4390,
    third argument 1).
- The client has no answer class for either (RTTI lists only the two C2S classes).
- refscan:
  - 1435cb4b0: only `ptr@.rdata:0x1447a76e8`.
  - 1435cb760: only `ptr@.rdata:0x1447a76f8`.
  - Both pointers are in the Lua binding table.
- deadcode.py: "live pointer to …" for both. That is the binding-table root, nothing else.
- None of these calls either native:
  - UI/Default/*.lua and Resources/Scripts/*.lua (grep).
  - UI/ScriptsBase.bin (luadis output and a raw `ChannelOn|ChannelOff` byte search).
  - The AS3 exports re/as3_695 and re/as3.
  - A binary-safe grep of the whole Resources / UI / CommonData / Locale trees.
  - The exe's strings (only the binding names and the class names).
- **Verdict:** sendable by any Lua that calls them. No shipped 0.695 script or window does, so the unmodified client
  never sends 0x22 / 0x23.
  - This is not EQN / PS2 dead code: it is reachable through Lua.
  - The server can itself make the client call them (ClientLua.RunWithArgs("Ui.ChannelOff", "General") through the
    FirstTimeEvent Script packet). That is not useful in play, but it works as a test.

### 1.4 What was built
- **Game/PublicChannels.cs** (local social service):
  - The server keeps the characters who left General, since everyone is in it by default (the client registers it at
    every login).
  - ChatChannelOn / Off `{string}` with "General" (case-insensitive, like the client's FUN_142b50af0) removes or adds
    the character. Other names are logged and ignored. Nothing is sent back.
  - The list is cleared on logout [I]: the client re-registers General at each login.
- **Game/Chat.cs**:
  - `TrailingFixedLength` (38), `PublicChannelOf(rest)`, `Public = 9`, `GeneralChannelId = 0`.
  - Channel-9 lines naming another id are dropped in `Handle`, before routing: no client could name them, since
    FUN_143b67f50 returns null.
  - In `Route`, General lines go only to members, and a sender who left General is dropped [I]. No client text exists
    for that case.
- **Social service (SOCIAL_ADDRESS)**:
  - `ISocialService.ChatChannel`. RemoteSocialService forwards the request as ClientRequestForward Command 0x1E / 0x22
    or 0x23 (`SocialMessages.ChatChannelOn / Off`; SocialHub case → `ChatService.HandleChannel`).
  - ChatService keeps the same leftGeneral set for every world. It is cleared in `WentOffline`, so a world change within
    the presence grace keeps it [I].
  - It filters channel-9 audiences and has the same room-name fix.
- **Dispatch:** InteractionHandler cases 0x22 / 0x23.
- **Docs:** docs/protocol.md §13 has the trailing block, public ids and the Command chat subs; docs/gameplay.md updated.

## 2. Chat bubble colour (Command 0x1E sub 0x15)

### 2.1 Client trace [P]
- **C→S:** Lua `Ui.SetChatColorFgBgSz(fg, bg, size)` FUN_1435d2a20 (needs exactly 3 numbers) calls FUN_142d4c740.
  - That builds CommandPacketSetChatBubbleColor `{u32 fg (+0x20), u32 bg (+0x24), u32 size (+0x28), u64 guid (+0x30) =
    DAT_145430c58 (own guid)}` and sends it on the Zone channel (sender FUN_142c42410 third argument 0, writer
    FUN_142c13c00).
  - It also writes the values into the local character entity: +0x219c fg, +0x21a0 bg, +0x21a4 size.
  - refscan 142d4c740: one call, from 1435d2a20. refscan 1435d2a20: only `ptr@.rdata:0x1447a76d8` (Lua binding table).
  - deadcode: "live pointer to 0x1435d2a20". The same Lua-only verdict as §1.3: no shipped script calls it.
- **S→C:** the Command dispatcher FUN_1433baf00 case 0x15 → FUN_1433bbef0.
  - It reads the same layout with FUN_1433833e0: u16 family, i16 sub, u32, u32, u32, u64.
  - FUN_142d5d290 finds the entity by guid and sets +0x219c = fg, +0x21a0 = bg, +0x21a4 = size.
  - deadcode: live (dispatcher).
- **LightweightToFullPc** (PlayerUpdate case 0x5B; FUN_142cdfa60, blob reader FUN_142c21350): the character blob's
  u32 at +0x8 / +0xc / +0x10 are the 2nd-4th u32 after the transient id. They are copied to the same three entity
  fields. FullNpc / Vehicle / Prop do the same from their +0x1c / +0x20 / +0x24 (FUN_142d532f0).
- **Defaults** for a new entity (FUN_142e88840): fg FUN_143b94310 = 0xFFFFFF, bg FUN_143b941a0 = 0xD4E2F0, size 1.
  - The emulator used to send 0 / 0 / 0 in LightweightToFullPc.

### 2.2 What was built
- **Database:** character columns `chatBubbleFg` / `chatBubbleBg` / `chatBubbleSize` (int unsigned, defaults
  0xFFFFFF / 0xD4E2F0 / 1).
  - Hand-written migration `20261031141400_ChatBubbleColor`, plus snapshot and CharacterContext mapping.
  - `WorldSession.ChatBubble` / `SetChatBubble` save only these three columns, the same way SetBio does.
- **Game/ChatBubbleColor.cs:**
  - `Handle` reads fg / bg / size and ignores the client's guid. If the values are unchanged it does nothing.
    Otherwise it saves them and sends S→C 0x15 `{fg, bg, size, sender guid}` (22 bytes) to the other players on the
    landscape. The sender's own client has already applied them.
  - `SendOwn` at the first ClientIsReady (the entity exists then) re-sends the player's saved colours to themselves,
    only when they are not the defaults. The client forgets them between logins.
- **LightweightToFullPc:** writes the character's values (or the defaults) in blob u32 2-4.
- **Dispatch:** InteractionHandler case 0x15.

## 3. Open leads (not settled; for OPEN-LEADS by whoever merges)
- **L65 follow-up, display of bubble colours:** no reader of entity +0x219c / +0x21a0 / +0x21a4 shows in the
  decompile.
  - The overhead SpeechBubbleWidget rows ("Character.ActiveChatBubbles", FUN_142ece660) carry PositionX / Y and text
    only.
  - Try next: Frida read-watch on a character's +0x219c while a bubble shows; the native OverHeadChatBubbleElement
    (part_0108 :10676) draw path.
- **Who called Ui.ChannelOn / Off and SetChatColorFgBgSz on live:** no 0.695 script does.
  - Candidates: a removed Chat Options page (P5310 predates 0.695?), or a server-driven Lua call (Command 0x53
    RunClientLua / FirstTimeEvent Script).
  - Try next: the 0.589 ScriptsBase.bin / UI Lua for these names; the forum / wiki for a "chat colour" option.
- **General membership lifetime:** session-only here [I]. P1826 / P1893 ("General unusable after logging out in it")
  hint live kept membership over logout before the P4311 fix.
- **Remaining trailing fields:** u64 +0x478 (passed to the guild / officer handlers, cases 5 / 6), u32 +0x484 /
  +0x480 / +0x470, u8 +0x238 (shout case 3), bool +0x488 (tell case 1). Their meanings are not settled.
- **Admin-family id:** the client also registers cCommandPacketSetChatBubbleColor under cPacketIdAdminBase
  (0x15001f00, part_0107.c:9147). Its purpose is not traced.
- **World change:** does FUN_143367e40 run again after a world change? Its +8 flag is never seen reset.

## 4. Builds
- `dotnet build` (-v q) succeeded for LandmarkEmulator.Database.Character, .Internal, .SocialServer, .WorldServer and
  the whole LandmarkEmulator.sln (0 errors).

## 5. Test steps (0.695 test copy, dev world; the migration adds 3 columns on start)
1. **Room chat (regression for the room-name fix).** Two players `/join test`, one types in the room tab: the other
   sees it; a third player outside the room does not.
2. **General.** Player A types `/1 hello`: B (same or other landscape; with SOCIAL_ADDRESS, another world) sees it in
   the default tab, labelled General.
3. **Leave General.** Server-side, have A's client call `Ui.ChannelOff("General")`: dev command (dev_commands.txt)
   `luacode Ui.ChannelOff("General")` targeted at A (ClientLua.RunCode).
   - Log: "A left General."
   - B's `/1` no longer reaches A, and A's `/1` is dropped (debug log).
   - `Ui.ChannelOn("General")` restores both.
   - Relog A: A is in General again.
4. **Bubble colour.** `luacode Ui.SetChatColorFgBgSz(16711680, 0, 2)` for A.
   - Log "set chat bubble colours".
   - DB `character.chatBubbleFg` = 16711680.
   - B gets 0x1E/0x15 (packet log), and B's later LightweightToFullPc of A carries it.
   - Relog A: A gets 0x15 with its own guid at ClientIsReady.
   - Verify through the client's state (entity +0x219c on A and B) with Frida, since no visible use is known (§3).
