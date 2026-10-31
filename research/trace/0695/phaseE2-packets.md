# Phase E part 2 — agent pe2-packets (2026-10-26)

Work list: OPEN-LEADS L169, L171, L172, L174, L175, L176, L177, L189, L190; new lead L202 (Claims 0xF9 sub 0xE3); the
REACHABLE-GAPS-695.md list (9 items, plus the 3 the coordinator added after the regeneration). Code: worktree
`<workspace>\wt-pe2-packets`, branch `local/pe2-packets`, commit **43987ab** (not merged). `dotnet test
LandmarkEmulator.sln` in the worktree: 1964 passed, 0 failed, 0 skipped.

Note for the coordinator: L171-L177 were already **built on 2026-10-18** (commits 7a12d42, da8feb2, d69050d, e0c161c,
471aa71, all on client-0.695), but OPEN-LEADS.md never got the closure lines. I checked each commit's client claims
against the 0.695 decompile (spot checks are listed per lead). The OPEN-LEADS text below records them as closed.

---

### L169. PurchaseBundleWithInGameCurrency 0xD0: meaning of Lua args 1 / 3 and the 0x4B result fields
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - [P] Native FUN_1435e9400 checks 3 numeric arguments. It sends PacketClientInGamePurchase 0xD0 {+0x10 = arg1, +0x14 = arg3, +0x18 = 0} through FUN_1435ec120. Arg 2 is never read (agent-leadsC.md, re-read today).
  - [P] 0x4B cPlayerInGamePurchaseResult: the client parses it and drops it (FUN_142cdfa60 case 0x4a → FUN_142c25490 → common exit).
  - **[P] New: where the caller was.** The in-game browser exposes a JavaScript bridge that runs Lua: **`SOEInGame.EvalLUA`**. The evidence:
    - Browser\Resources\x64\BrowserCefChildProcess64.exe has the strings "SOEInGame" and "EvalLUA" (file offsets 0x83d28 / 0x83d38).
    - The same file holds the host list the bridge serves. It is store hosts only: `https://store(-main|-qa|-stage|-local).landmarkthegame.com/` and the same set on `eqnlandmark.com` (offsets 0x83d48-0x83ec0).
    - Landmark64.exe runs the script in FUN_143b5bb70 "BrowserCefViewImpl::ProcessEvalLuaMessage" (re/decomp_all_695/part_0139.c:30384). It also has "Non-permitted URL blocked!".
  - So the InGamePurchaseStoreScreen natives that no shipped script calls were called by live's **web store page** (store.landmarkthegame.com), served by Daybreak. That covers PurchaseBundleWithInGameCurrency, CanPurchaseStoreBundle, EquipStoreBundle, AddStoreBundleToOrder and the others. The argument order lived in that page's JavaScript, which is server-only content.
  - The page is not archived. The archive.org CDX check of 2026-10-18 found **no captures of store.landmarkthegame.com** (OPEN-LEADS L137 entry).
- **Searched:**
  - re/out/luabind_695.tsv:944.
  - All 2,064 .lua / .xml / .bin files under the 0.695 install's UI and Resources folders (grep -a, no hits).
  - The Browser folder (63 files).
  - re/as3_695 and re/as3: no hits for "InGameCurrency".
  - Earlier passes: lua_all, ScriptsBase.bin, design/ (agent-leadsC.md).
  - The archive.org CDX listing (L137).
- **Server:** matches already. StoreBundles.HandlePurchase (Game/StoreBundles.cs:238) takes whichever of fields 1 / 2 is one of the player's bundle ids as the bundle, and the other as the quantity. That is correct for both possible argument orders. The 0x4B echo cannot matter, because the client drops it [P]. Our own Showcase pages cannot call EvalLUA, since the host list names only Daybreak's store hosts, so 0xD0 stays unreachable on our server.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-packets, best evidence):** the caller was live's web store page: the in-game browser's JS bridge SOEInGame.EvalLUA (BrowserCefChildProcess64.exe, store*.landmarkthegame.com / eqnlandmark.com hosts only; Landmark64 FUN_143b5bb70 ProcessEvalLuaMessage) ran the InGamePurchaseStoreScreen natives, so the argument order lived in Daybreak's page JS (no archive.org captures of store.landmarkthegame.com, L137). The 0x4B result is dropped by the client [P]. Server accepts both orders (StoreBundles.HandlePurchase); no change. (trace/0695/phaseE2-packets.md)`

### L171. BaseBroadcastPacket 0x4F: an operator "system broadcast"
- **Verdict:** RESOLVED (built 2026-10-18, 7a12d42)
- **Answer:**
  - [P] Reader FUN_142c17410, re-read today. The body is:
    - u16 opcode
    - i16 kind (+0x10)
    - name block FUN_142c25b90: {u32 +0xb8, u32 +0xbc, u32 +0xc0, string first +0x18, string last +0x48}, with the full name joined into +0x78
    - string message (StringFixed<256>, +0xc8)
    - u32 text id (+0x1e8), used only when the message is empty
  - [P] Handler FUN_142cd9130:
    - kind 0 → the message on its own;
    - kinds 1-3 → the CodeStringMappings format, else "System Broadcast: %s" / "Broadcast from %s: %s";
    - the line goes out through FUN_142d557f0.
  - [P] None of the six *BroadcastFormat keys is in gameData695/CodeStringMappings.txt or re/out/text_695.tsv (checked today), so the fallback text is what players see.
  - [P] The client has no sender (AdminCommandPacketBroadcast 0x43C-0x43E / 0x46C exist only as id-table names).
  - No design source mentions an in-game broadcast. Searched: design/forum, design/wiki for "system broadcast", "server broadcast" and maintenance / shutdown warnings. The only "broadcast" hits are Landmark Live streams.
- **Server:** matches. Game/Broadcasts.cs writes the same layout. The dev command `broadcast` and the admin page's Broadcast box send it. ShutdownBroadcasts defaults to false = live (shutdown warnings stay WorldShutdownNotice). Covered by ServerNoticeLayoutTests.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-18 (7a12d42; verified 2026-10-26 pe2-packets) [P]:** layout i16 kind, name block {u32×3, first, last}, StringFixed<256> message, u32 text id (FUN_142c17410 / FUN_142cd9130); no *BroadcastFormat keys in 0.695, so "System Broadcast: %s". No client sender, no live mention: sent only on operator request (Game/Broadcasts.cs, dev "broadcast", admin page); ShutdownBroadcasts default false = live.`

### L172. PacketClientPendingKickFromServer 0x65: countdown kick warning
- **Verdict:** RESOLVED (built 2026-10-18, 7a12d42)
- **Answer:**
  - [P] Zone dispatcher FUN_142d1ccf0 case 0x2b → reader FUN_142c4a0d0: {StringFixed<128> message (+0x10), u32 seconds}. Re-read today.
  - [P] The handler formats "KickedFromServerTimedMessage" with player / seconds / message and calls FUN_142cdd200. Neither KickedFromServerTimedMessage nor TimeRemainingSeconds is in CodeStringMappings.txt (checked today), so the visible line is empty.
  - No live trigger in any source. Searched: forum for "kicked from the server", "countdown", "disconnected in", "server message".
- **Server:** matches. Game/Kicks.cs: `@Name kick [seconds] [reason]` sends readable warnings plus 0x65 (PendingKickPacket default true) and then KickedFromServer 0x53. The shutdown countdown sends 0x65 once at ≤ 30 s. Covered by layout tests.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-18 (7a12d42; verified 2026-10-26 pe2-packets) [P]:** {StringFixed<128> message, u32 seconds} (case 0x2b, FUN_142c4a0d0); KickedFromServerTimedMessage missing from CodeStringMappings so the line is empty. No live trigger in any source: sent with the admin kick (Game/Kicks.cs) and at ≤ 30 s of a shutdown.`

### L174. Guild invitations list, News row removal, full guild data
- **Verdict:** RESOLVED (built 2026-10-18, da8feb2)
- **Answer:**
  - [P] GuildInvitations 0x83/0x14 (reader FUN_142ee5040, handler FUN_142ef25f0) queues the GAQGuildInvite popup (FUN_1433da9d0) for each invitation of type 1 addressed to the player.
  - [P] GuildDataFull 0x12 (reader FUN_142ee4820) empty empties the client's guild list.
  - [P] GuildRemoveLogUpdate 0x1B (reader FUN_142ee5580, handler FUN_142ef3a50) {u32 n × row id, u64 guild}.
  - The 0.695 GuildWindow AS3 has no invitations view and no News delete action.
- **Server:** matches, in both the zone (Game/Guilds.cs:27-29, 209-238, 1336, 1361, 1523) and the social service (SocialServer/GuildService.cs:23, 215-223, 938, 1114-1144):
  - GuildInvitations is sent at every world entry;
  - an empty GuildDataFull goes to a player in no guild;
  - RemoveLog is sent when the 100-row News cap drops rows.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-18 (da8feb2; verified 2026-10-26 pe2-packets) [P]:** 0x14 at every world entry (client queues the invite popup itself for type-1 invitations), empty 0x12 for a player in no guild, 0x1B when the News cap drops rows; zone Guilds.cs and SocialServer GuildService.cs the same.`

### L175. PacketTargetClientNotOnline 0x1A: which live path sent it
- **Verdict:** CLOSED (best evidence) (built 2026-10-18, d69050d)
- **Answer:**
  - [P] Zone case 6, reader FUN_142c4af20: {u64, StringFixed<32> name}.
  - [P] The handler looks up PlayerNotOnline through ClientStringProvider vtable +0x20 (FUN_142fdd650 → +0x10 FUN_142fdd4b0). That fills `[*player*]` with the **local** player (FUN_142fdd4b0 uses DAT_1454319c0 + 0x282c8), so the line names the reader, not the target. The name is passed as a printf argument the text has no %s for.
  - [P] The client has no 0x19 ClientToTargetClientsZoneWrapped sender. Which messages live's world wrapped was a server-only routing rule.
  - No source quotes the line. Searched: forum / wiki for "not online at this" and "is not online".
- **Server:** matches the best evidence. Game/TargetNotOnline.cs sends the same client text through ShowSystemMessage 0x69 with player = the target, for Teleport to Friend, trade and Inspect. Setting TargetNotOnlinePacket (default false) sends 0x1A instead.
  - The default is the readable choice, because live's use of 0x1A for these actions is not recorded and the client's own 0x1A line would name the reader.
  - If the coordinator reads the live-default rule strictly, the default could be flipped. I did not change it: what live did is not proven.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-packets, best evidence; built d69050d):** {u64, StringFixed<32>} (FUN_142c4af20); the client's PlayerNotOnline fills [*player*] with the local player (FUN_142fdd650 → FUN_142fdd4b0), and 0x19 has no client sender, so live's triggers were server-internal routing with no record (forum / wiki searched). Server: same text via ShowSystemMessage with the target's name; TargetNotOnlinePacket (default false) sends 0x1A.`

### L176. InstancedGamePlayerRemovalPacket 0x10D/0x14: side effects our removal may miss
- **Verdict:** RESOLVED (built 2026-10-18, e0c161c)
- **Answer:**
  - [P] FUN_1434e8cd0 (re-read today) runs FUN_1434e2330, the same reset Request type 6 / 0 runs (FUN_1434e93e0, its only other caller).
  - [P] It then calls FUN_142d4eaa0(client, 0) when the camera mode is 0x20 (observer).
  - [P] The +0x40 block (channel 0x11 via FUN_14336f480, OnCloseGameInProgressDialog) runs only while +0x40 is still set.
  - So our old sequence missed only the observer-camera exit.
- **Server:** matches. RemovePlayer ends with 0x10D/0x14, and a game that goes away also ends its observers' watch (InstancedGames.cs:599, 1071).
- **OPEN-LEADS text:** `- **CLOSED 2026-10-18 (e0c161c; verified 2026-10-26 pe2-packets) [P]:** FUN_1434e8cd0 = the shared reset FUN_1434e2330 + leave camera mode 0x20 (FUN_142d4eaa0); the server now sends the removal after its sequence and ends observers' watch when a game goes.`

### L177. Recipe cooldowns and closing the crafting window
- **Verdict:** RESOLVED (built 2026-10-18, 471aa71; source found today)
- **Answer:**
  - [P] ZoneCraftingWindow 0x42/0x24 {u32 sub, u32, bool}: bool 0 closes the window (FUN_142f381b0). The client never closes it by distance on its own.
  - [P] Every recipe's COOLDOWN_TIME is 0 in the 0.695 Recipes.txt, so PlayerRecipeDataUpdate has nothing to carry.
  - **New [P]:** live closed the window as the player walked away. design/wiki/pages/Update Notes 02.20.14.wiki: "The crafting window will now close automatically when you move away from the crafting station."
- **Server:** matches. Crafting.CloseWindowsAway, setting CraftingWindowCloseAway, default true. The "[I] undocumented" note is replaced by the patch-note citation (Crafting.cs, docs/gameplay.md) in local/pe2-packets 43987ab.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-packets; built 471aa71) [P]:** server closes a station window its player left (0x42/0x24 bool 0; the client has no distance close), default CraftingWindowCloseAway=true is live: Update Notes 02.20.14 "The crafting window will now close automatically when you move away from the crafting station." No recipe cooldowns (COOLDOWN_TIME 0 in all 2,363 rows). Citation added in local/pe2-packets 43987ab.`

### L189. SendSelf CastKeySeed (+0x753f0)
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - [P] How the client uses the seed. FUN_142e0f810 (part_0071.c:2294-2302) takes the counter uVar2 = player +0x753f4, increments it, and computes `key = ((uVar2 & 0xff) ^ seed) * 0x502c3f11`. It then builds the CastAttempt (ctor FUN_142e074f0, writer FUN_142e006f0 / FUN_142e00600):
    - +0x18 u64 caster guid (+0x278 getter)
    - +0x20 u32 key
    - +0x28 u32 counter
    - +0x2c ability
  - [P] The seed's only reader is that function (grep 0x753f0: the SendSelf reader FUN_142c19bd0 and FUN_142e0f810). The counter +0x753f4 has no other reader.
  - [P] What the key is: the `CastHash` that every server answer echoes (CastSuccess / CastFail / AbilityEnd / targeters, Game/Abilities.cs:351-410). The pending cast is stored with {guid, key} (FUN_142e257b0). So the key is the cast's identity.
  - Whether live checked it, or drew a new seed per login, was server-only. There is one capture (0x5BB9EB0B, non-zero and random-looking) and no second session to compare. Searched: re/, captures, docs (only the 1098 SendSelf capture), forum.
- **Server:** changed in local/pe2-packets 43987ab:
  - WorldSession.CastKeySeed: each session draws a random non-zero seed, sent in every SendSelf, like live's non-zero value.
  - Abilities.CastKey / CastKeyMatches check each CastAttempt's hash and only log a mismatch (Debug); the answer echoes the client's hash either way.
  - Docs updated: sendself.md, unknowns.md.
  - Not a gameplay choice, so no setting.
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-packets, best evidence):** the seed makes the CastAttempt cast hash ((counter +0x753f4 & 0xff) ^ seed) * 0x502c3f11 (FUN_142e0f810; hash +0x20, counter +0x28), the cast identity every answer echoes [P]. Live's seed policy / check was server-only (one capture, 0x5BB9EB0B). Server: random non-zero seed per session, hash checked and mismatches only logged (local/pe2-packets 43987ab).`

### L190. CurrentAdventureId (+0x114) and PlayerUpdate 0x36 SetCurrentAdventure
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - [P] The reader FUN_142c2b4b0 reads, in order: u16, u32 sub, u64 player (+0x18), u32 adventure id (+0x20), list<u32> (+0x28, FUN_142c34b60), list<u32> (+0x40), list<bool> (+0x58, FUN_142c40000).
  - [P] Dispatcher case 0x35 (FUN_142cdfa60) applies them only when the guid is the local player (pcdata +0x30). It stores the id at pcdata +0x114 and the lists at +0x118 / +0x130 / +0x148 (FUN_142fd3a90 / 39f0 / 3950).
  - [P] It then calls FUN_142f2dd50(pcdata + 0x268b0). +0x268b0 is the client quest manager (the same object the Quest 0x34 dispatcher FUN_142f2cd80 runs on, part_0080.c:12222), and FUN_142f2dd50 is an **empty function** in 0.695 and in 0.589 (FUN_142e8f1b0, functions.tsv "same").
  - [P] Nothing reads +0x114 / +0x118 / +0x130 / +0x148 (grep of pcdata offsets via `0xf18) + 0x11x/0x13x/0x14x/0x15x`: no hits).
  - [P] No Lua, AS3 or ScriptsBase name has "CurrentAdventure" or "AdventureId". The only "Adventure" natives are the claim Play Mode ones (ToggleAdventureStatus, SetClaimAdventureFlag).
  - [P] No data table. AdminCommandPacketIdAdventureAdd / AdventureSetPhase exist only as id-table names (re/out/strings.tsv).
  - [I] So this is EQN's adventure (quest-chain with phases) system. Live's server kept it, the 0.695 client compiled its hook out, and Landmark players could never see it. Meaning of the lists [I]: phases, objectives and completion flags.
- **Server:** matches. SendSelf CurrentAdventureId = 0 as in the capture; 0x36 is not sent. sendself.md row updated (43987ab).
- **OPEN-LEADS text:** `- **CLOSED 2026-10-26 (pe2-packets, best evidence):** layout {u64 player, u32 adventure, list<u32>, list<u32>, list<bool>} [P]; stored at pcdata +0x114/+0x118/+0x130/+0x148 and handed to the quest manager's (+0x268b0) hook FUN_142f2dd50, an empty function in 0.695 and 0.589; no reader, Lua, AS3 or table; AdventureAdd / SetPhase only as id names. EQN adventure system with no Landmark use; server sends 0 / nothing (matches the capture).`

### L202 (new). Claims 0xF9 sub 0xE3 received six times on the dev world
- **Verdict:** RESOLVED
- **Answer:**
  - **Proven from git history and the server code [P].** The 0xE3 was our own gateway translation, not the client.
  - From **723cc6c (2026-10-15 10:33)** to **003dc1f (2026-10-17)**, ClientWire695 rewrote incoming 0.695 Claims subs before WorldSession.Receive counted them. In 723cc6c, `data = ClientWire695.ToServer(data)` sits at WorldSession.cs:1089, ahead of `OpcodeStats.Received` at 1105 / 1121.
  - The 0.695-only subs 0x45 / 0x46 / 0x63 got internal numbers 0x80 | sub (`Family Claims = new(0xF9, 0x00, 1, 0x45, 2, new uint[] { 0x45, 0x46, 0x63 }, 0x80)`). So PlayerEnteredClaim 0x63 arrived as 0xE3.
  - The handler for internal 0xE3 came only in **3211881 (2026-10-15 22:46)**. Until a build with it ran, the Claims default branch counted it as unanswered.
  - The raw key in opcode_stats.tsv is `in F900E34A 6` = F9 00, sub E3, first body byte 0x4A, the low byte of claim id 74. After 003dc1f the same packet appears natively as `in F900634D/4E/4F` (63 times).
  - 003dc1f's message itself lists "PlayerEnteredClaim 0x63 (were internal … 0xE3 …)".
  - [P] Client side, checked as asked:
    - Claims subs are written as one byte, `(char)*(int *)(packet + 0x10)` (FUN_142d9e650).
    - All 71 `= 0xf9;` base assignments in re/decomp_all_695 are followed by constant subs, none 0xE3, and there are no combined u64 constants.
    - The only PlayerEnteredClaim builder (FUN_142dc8ec0) uses the constant 99.
- **Server:** matches (native numbering since 003dc1f; Claims.cs handles 0x63). The misleading comment in Claims.cs's default branch is corrected in 43987ab.
- **OPEN-LEADS text:** `### L202. Claims 0xF9 sub 0xE3 in the dev world's opcode_stats` / `- **RESOLVED 2026-10-26 (pe2-packets) [P]:** PlayerEnteredClaim 0x63 renumbered by our own gateway translation (ClientWire695, 723cc6c, internal 0x80 | sub) before the 0xE3 handler existed (3211881); raw "F9 00 E3 4A" = claim 74. No 0.695 builder sends 0xE3 (Claims sub is one byte, FUN_142d9e650; all 71 base-0xF9 builders use constant subs). Comment fixed in local/pe2-packets 43987ab.`

---

## REACHABLE-GAPS-695: verdicts (regenerated list, 12 classes)

The scan only reads LandmarkEmulator.WorldServer's ZoneMessageOpcode use, so it missed several kinds of handling:
- subs handled through private constants before a switch (Claims 0x45 / 0x46);
- the CommerceServer's raw sub bytes;
- Commerce.FromWire;
- packets the server writes with a family base + sub byte (Inventory 0x2D/0x1E, VoxelConstruction 0xF4/7);
- enum names it couldn't link to the class (AddLightweightProp).

Only **UpdateTemporaryAppearance** was a real gap. It is now built.

verdicts.tsv lines (to add; not applied by me):

```
PlayerUpdatePacketUpdateTemporaryAppearance	sent	E2 pe2-packets 2026-10-26: built (local/pe2-packets 43987ab): Game/TemporaryAppearance.cs, sent by admin /setmodel (AdminCommands.SetModel, 0x4F0) to the landscape as 26 00 09 00 00 00 {u32 model, u64 character} (0.695 case 8, reader FUN_142c4e180 → FUN_142c2ce10 → FUN_142eca100); later arrivals via LightweightToFullPc u32 before the title (case 0x5a → entity +0x24a0, FUN_142eb8600). The client has no other path into SwitchToTemporaryAppearance, so the GM tool was its only live trigger
PacketInGamePurchaseCommerceTemplateProductCreate	handled	E2 pe2-packets 2026-10-26: 0x45/0x30 mapped onto ProductCreate 0x2F by Commerce.FromWire (Game/Commerce.cs:97; InteractionHandler.cs:395) → Commerce.Handle case SubProductCreate (Commerce.cs:373) in-process, RemoteCommerceService.InGamePurchaseRequest line 437 CreateProduct with the commerce service
PacketInGamePurchaseCommerceProductRemove	handled	E2 pe2-packets 2026-10-26: 0x45/0x32 = Commerce.SubProductRemove: Commerce.cs:385 (in-process), CommerceServer/ProductService.cs:86 (ClientPackets.SubProductRemove, via RemoteCommerceService.cs:464)
PacketInGamePurchaseCommerceRequestAccountStore	handled	E2 pe2-packets 2026-10-26: 0x45/0x37 = SubRequestAccountStore: Commerce.cs:382 SendAccountStore; CommerceServer/ProductService.cs:78 → ClientPackets.AccountStore 0x39 (84 received in play, answered)
PacketInGamePurchaseCommerceGetProductForReviewRequest	handled	E2 pe2-packets 2026-10-26: 0x45/0x40 = SubGetProductForReview: Commerce.cs:376; RemoteCommerceService.cs:449 (reviewer rule MayReview, then the service) → CommerceServer/ProductService.cs:92 OfferReview
ClaimsPacketSetAdventureFlags	handled	E2 pe2-packets 2026-10-26: 0xF9/0x45 Claims.HandleRequest before the switch (Claims.cs:244 SetAdventureFlagsSub, :280 → SetAdventureFlags {u64 claim, u64 mask, u8})
ClaimsPacketStartClaimPlacement	handled	E2 pe2-packets 2026-10-26: 0xF9/0x46 Claims.HandleRequest before the switch (Claims.cs:226 StartClaimPlacementSub, :269 → BeginPlacement)
InstancedGameAddOrRemoveRuleRequestPacket	handled	E2 pe2-packets 2026-10-26: 0x10D/0x18 InstancedGames.cs:927 → InstancedGames.Rework.cs HandleRework (SubAddOrRemoveRule = 0x18 at :43, handled from :425: {u64 game, u32 rule type, u8 add, u64 trigger}, PlayerAddable + CanEdit + RuleFits checks)
InventoryPacketShowActivationFailure	sent	E2 pe2-packets 2026-10-26: 0x2D/0x1E {u32 text id} written by ItemUses.Refuse (Game/ItemUses.cs:69, :112; 6 callers: Claims, RecipeBook, Unlockers, storage expansions); 0.695 FUN_142f202e0 case 0x1E → "Activation Failure" dialog
PlayerUpdatePacketRemoveNotifications	unreachable	E2 pe2-packets 2026-10-26: 0x26/6 (case 5): list of guids → FUN_1433fb370 removes an indicator FUN_1433ef4f0 made from AddNotifications 0x26/5 (case 4) and calls Lua MiniMap:KillIndicator, which no 0.695 Lua / ScriptsBase / as3_695 defines; the server never sends AddNotifications (no system adds entity notification indicators), so there is nothing to remove
PlayerUpdatePacketAddLightweightProp	sent	E2 pe2-packets 2026-10-26: enum PlayerUpdateAddLightweightPropWire 0x260062 (wire 26 00 62 00 00 00; 0.695 id cPlayerUpdatePacketIdAddLightweightProp 0x62, dispatcher FUN_142cdfa60 case 0x61 → reader FUN_142c273d0, handler FUN_142cde1c0); Network/Message/Model/PlayerUpdateAddLightweightProp.cs, built in Props.cs:947/2327, Gathering.cs:754/861, LootChests.cs:123; opcode_stats out 260062xx ≈ 12,000+
VoxelConstructionPacketResponse	sent	E2 pe2-packets 2026-10-26: 0xF4/7 {u32 request type, i32 result, bool} by Building.SendResponse (Game/Building.cs:131; FUN_143162830 case 7, results → FUN_143161180 texts): 0 done, 1 GeneralError, 0xC InsufficentMaterials, 0xE InsufficentPermissions, 0x11 ExceedsMaxTotalTemplateVoxelsCount (+ UI.PurchaseVoxelExpanders), 0x1A OnlyClaimOwnerMayPlaceOrCopyTemplateInstances, for undo/redo (sub 3), ApplyDiff, paste (sub 2) and off-grid paste; 0xD InsufficentProps is not used: a paste places the props it can and lists the rest with ZoneMissingPropList 9 / ZoneUnknownPropList 8 (BuildingClipboard.cs:759-859) [I that live did the same]
```

Notes on items that needed checking:
- **0x45 InGamePurchase:** the subs are the 0.695 numbers (native numbering since 003dc1f). opcode_stats has `in 45003700 84` and `45004000 41`, both answered by the code above.
- **VoxelConstructionPacketResponse:** coverage-695.md:2204 lists cVoxelConstructionPacketIdResponse 0x7 as "id-table entry with no client class". That is wrong: the class exists (rtti 144715428) and FUN_143162830 case 7 reads it. The "unknown opcode" in the scan is the same miss.
- **PlayerUpdateAddLightweightProp.cs:** the header comment cites 0.589 addresses (FUN_142b8dc00, FUN_142c44700). The 0.695 ones are FUN_142c273d0 and FUN_142cde1c0. This is cosmetic and was not changed.

## New leads

None that stay open. One improvement for the tools, not a lead: re/tools/reachable.py could also read the CommerceServer's
ClientPackets / ProductService sub constants, consts compared before a switch (Claims.HandleRequest), and
`EnqueueMessage(<Base>, …)` writes that start with a sub constant (ItemUses, Building). That would stop these false gaps.

## Runtime captures needed

None required. Optional confirmation of L189 / the build:
1. As an admin on the 0.695 test client, run `/setmodel <id of a Models.txt creature, e.g. a kobold>`. Watch the admin's own character and a second client (TestPlayer2): both should switch model. Then run `/setmodel 0`: both should switch back.
2. With world logging at Debug, cast a few abilities. No "is not this session's key" line should appear, which confirms the cast-hash formula with a non-zero seed.

## Summary

All 10 leads now have closure text:
- **RESOLVED:** L171, L172, L174, L176, L177, L202.
- **CLOSED on best evidence:** L169, L175, L189, L190.
- L171-L177 had in fact been built on 10-18. I re-checked them against the 0.695 decompile, and for L177 I found the live source: Update Notes 02.20.14, crafting window closes on walking away.
- L202 was our own gateway translation: PlayerEnteredClaim 0x63 → internal 0xE3, between 723cc6c and 3211881. No 0.695 code sends 0xE3.
- New for L169: the store natives without a shipped caller were run by live's web store through the in-game browser's `SOEInGame.EvalLUA` bridge, which is limited to store.landmarkthegame.com / eqnlandmark.com.

All 12 REACHABLE-GAPS classes have verdicts.tsv lines:
- 10 are handled or sent already, mostly in code the static scan can't see;
- RemoveNotifications is unreachable;
- UpdateTemporaryAppearance was the one real gap. I built it as the admin `/setmodel` (with persistence for later viewers and re-zones).

The same commit (local/pe2-packets 43987ab, tests green) also:
- gives each session a random CastKeySeed with a logged cast-hash check (L189);
- adds the L177 and L202 citations.
