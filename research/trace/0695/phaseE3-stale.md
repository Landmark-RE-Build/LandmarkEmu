# Phase E3 — agent "stale": what still rests on 0.589 facts, and the loose ends of part 2 (2026-10-26)

Brief: trace/0695/agent-phaseE3-brief.md (read-only). Server code read at client-0.695 HEAD **8219f7c**. Nothing in the
repository, the installs or any existing file was changed. Scratch (re-runnable, all mine):
`<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\e3-stale\`:

| File | What |
|---|---|
| `cov\` | a copy of re/out/agent_coverage (agent #10's generator). `srvmodels.py` / `srvsubs.py` / `srvsends.py` re-run on HEAD (output in the copy only). `build_table_head.py` = build_table.py with three edits: the server is matched on the **0.695** sub (`sub695`/`s6`, since the server is native-0.695 since 003dc1f), the 10-15 line ranges (`SPECIAL_LINES`) and hand fixes are dropped, and a file naming one family gets that family's case labels. Output `table_head.md`; `cmp.py` diffs its Emulator column against trace/0695/coverage-695.md §10 → `cmp.txt` |
| `fam.py`, `hand.txt` | every FUN_ address in trace/families_695 classified by build |
| `srcscan.py`, `cmpcode.py`, `changed.txt`, `nov.txt`, `dd.py`, `d1-d3.txt` | every 0.589 address cited in LandmarkEmu (*.cs, docs/*.md), compared with its 0.695 partner (rebase695.py's `compare` / `diff`, loaded from its source) |
| `vt.py` | reads a vtable from Landmark64.exe (read-only mmap) |
| `now_list.md` | the coverage-695 corrections below, generated |

## 1. Findings (ranked by player impact)

No real bug was found: in every place I checked where the 0.695 client differs and the server code cites the 0.589
function, the code already does the 0.695 thing and only the comment is old (F4). The findings are research files
that are out of date, and part-2 loose ends.

### F1. trace/0695/coverage-695.md §10 "Emulator" column: 137 rows now wrong — STALE
- **Evidence [P]:** I re-ran agent #10's server scan on HEAD and matched it on the 0.695 sub (scratch `cov\`). 122 rows
  went from not-implemented to implemented. Each row's cited server line was read (`now_list.md`). 4 are false hits of
  the heuristic and must **not** be changed:
  - 0x1E/4 EndDialog, 0x1E/6 PlayerPlaySpeech, 0x1E/7 DialogResponse matched the `case 4/6/7` of `/clienttime` (AdminCommands.cs:456-459).
  - 0x45/0x10 StationCashProductsResponse matched the result code `CouldNotAffordListingFee = 0x10` (Commerce.cs:169, 550).
    "StationCash" occurs nowhere in the server.
- **Missed by the heuristic but implemented at HEAD (checked by hand) [P]:**
  - 0xED/6 PickUpReply and 0xED/8 DeleteReply: Props.cs:~1770 `writer.Write((byte)(delete ? 8 : 6))`. coverage rows 2106 / 2108.
  - 0xED/0x17 AvailableTintsReply: Props.SendTintList, answering 0x18 (PropHandler.cs:67-69). Row 2121.
  - 0xF0/0x13 EnvironmentInstanceDetailsResponse: TemplateInstances.cs:126 / 218, added by HEAD 8219f7c (NL2). Row 2165.
  - 0x10C/8 WorldEditingPlayEffect: VoxelDestruction.cs:252-256. Row 2443.
  - 0x10D/0xD GetConfigurationRequest and 0x10D/0x18 AddOrRemoveRule: InstancedGames.Rework.cs:43, :425. Rows 2457, 2456.
  - 0x10D/0x19 MatchmakingRequest and 0x10D/0x1A MatchmakingUpdate: InstancedGames.Finder.cs:79, :169. Rows 2458, 2459.
  - 0x26/0x80 SetPlayerBio: row 687 still says "stub (MiscHandler.cs:510)". It is a real handler now: MiscHandler.cs:740-741, model PlayerUpdateSetPlayerBio.cs.
  - Runtime "out" in opcode_stats.tsv and a sender at HEAD:
    - 0x1E/0x13 ConfirmFriendRequest: Friends.cs:179. Row 530.
    - 0x1E/0x45 AddWorldCommand and 0x1E/0x46 AddZoneCommand: DynamicCommands.cs:37, 160. Rows 568-569.
    - 0x1E/0x4B ItemDefinitions: ItemDefinitionManager.cs:214-225. Row 573.
    - 0x42/7 CraftWindowEnableResponse: CraftingStations.cs:140. Row 1104.
    - 0x42/8 AddRecipe: RecipeBook.cs:414. Row 1105.
    - 0x42/0x1D ComponentImageSetIdsResponse: CraftingHandler.cs:31-35. Row 1122.
    - 0x45/5 StoreBundleBase: StoreBundles.cs:101, 289. Row 1141.
    - 0x117/2 UndoStackChange: Building.cs:207-221. Row 2555.
- **The 118 real flips** (coverage-695.md line, 0.695 sub, class, new text, HEAD evidence). Where the evidence below is
  a constant declaration, the sending line is: Guilds.cs:1350 (Invitations), 1373 (DataFull), 1535 (RemoveLog);
  InstancedGames.cs:1656 (PlayerRemoval), 1882 (PropState), 1931 (PropInteraction); LootPinatas.cs:138 (0x7);
  Achievements.cs:395 (AddSub); Claims.cs:858 (0x12).
  - **0x1A**: 504 PacketTargetClientNotOnline → sent (TargetNotOnline.cs:70)
  - **0x1E**: 545 0x25 RequestPlayerPositionsReply → sent (PlayerPositions.cs:97). Admin subs → handled (AdminCommands.cs:75-92): 583-590 0x3EC Profile* (8 rows), 597 0x429 GameTime, 598 0x42A ClientTime, 599 0x42E GotoRequest, 604 0x458 HideMe, 606 0x499 AddAmmo, 623 0x49C SpawnVehicle, 627 0x4B5 VerifyAdminTarget, 629 / 630 0x4CF / 0x4D0 ForceAdd / RemoveFriend, 632 0x4D8 Runspeed, 645-649 0x4ED-0x4F1 DeleteAllProps / WeaponSet / PlacedEffect / SetModel / FreeFlyHide
  - **0x24**: 671-673 AutoAttackTarget / AutoAttackOff / SingleAttackTarget → handled (CombatHandler.cs:32-35)
  - **0x26**: 697 0x9 UpdateTemporaryAppearance → sent (TemporaryAppearance.cs:87); 708 0x14 SetDisposition (PlayerUpdateSetDisposition.cs:49); 715 0x1B CustomizationChange (Barbershop.cs:152); 741 0x35 MemberStatus (Membership.cs:350); 763 0x4B InGamePurchaseResult (StoreBundles.cs:277); 764 0x4C SafeEject → handled (PlayerHandler.cs:52); 765 0x4D ManagedObject (Props.Platforms.cs:169); 769 0x51 UpdateCharacterStateDelta (BlockStance.cs:152); 775 0x57 AFK (Afk.cs:50); 780 0x5C LightweightToFullNpc (PlayerUpdateLightweightToFullNpc.cs:37); 789 0x65 RequestAnimationSelection (Creatures.cs:2494)
  - **0x27**: 831 0x8 CastContinue → sent (AbilityMessages.cs:413); 855 / 856 0x23 / 0x24 ClientTargeterEnd / FindTarget → handled (MiscHandler.cs:639-644)
  - **0x2D**: 960 0x1E ShowActivationFailure → sent (ItemUses.cs:118); 964 0x8 EquipByItemRecord, 968 0xD ToggleAutoResupply → handled (InventoryHandler.cs:85, 120)
  - **0x42**: 1124 / 1125 0x1F / 0x20 Hide / UnhideRecipeResponse (RecipeBook.cs:365-368); 1129 0x24 ZoneCraftingWindow (Crafting.cs:99) → sent
  - **0x45**: 1135 0x30 TemplateProductCreate, 1144 0xD ServerStatusRequest, 1167 / 1168 0x35 / 0x36 Follow / UnFollowCreator → handled (Commerce.cs:89, 391-395); 1145 0xE ServerStatusResponse, 1163 0x2E CommerceConfiguration, 1172 / 1173 0x3A / 0x3B Add / RemoveFollowing → sent (Commerce.cs:328, 402, 670, 681)
  - **0x4D** 1224 0x5 FriendUpdatePositions (FriendPositions.cs:49) · **0x4F** 1235 BaseBroadcast (Broadcasts.cs:91) · **0x65** 1314 PendingKickFromServer (Kicks.cs:85) · **0x66** 1320 MembershipActivation (Membership.cs:341) · **0x78** 1413 CommerceSessionResponse (WorldSession.cs:1596) → sent
  - **0x83**: 1436 0x6 InviteRevoke → handled (Guilds.cs:386); 1448 0x12 DataFull, 1450 0x14 Invitations, 1456 0x1A AddLogUpdate, 1457 0x1B RemoveLogUpdate → sent
  - **0x91**: 1510 0x3 RequestInspectPlayer, 1513 0x2 StopInspect → handled; 1512 0x1 StartInspect → sent (Inspect.cs:43-96)
  - **0x92** 1519 0x2 AchievementAdd → sent · **0xB9** 1753 0x6 VoiceRequestCustomChannel → handled (VoiceHandler.cs:47) · **0xD0** 1953 PacketClientInGamePurchase → handled (InteractionHandler.cs:421) · **0xDF** 2001 GuildTag → sent (GuildTags.cs:78) · **0xE0** 2007 LoginQueueStatus → sent (TravelQueue.cs:268)
  - **0xEA**: 2065-2068, 2070 TransferShape / UpdateDefinition / Base / RemoveDefinition / CreateTriggerAreaRequest → handled (AreaDefinitionHandler.cs:13-28); 2069 0x6, 2072 0x8 responses → sent (AreaDefinitions.cs:293, 308)
  - **0xED**: 2098 0x1D DialogResponse, 2109 0x9 RequestList → handled (PropHandler.cs:101, 116) · **0xEE** 2137 0x3 ProjectileDelete → sent (Projectiles.cs:109)
  - **0xF0**: 2151 0x1F, 2152 0x1D, 2164 0x12 → handled (Templates.cs:872-882); 2153 0x1E → sent (DesignMaterials.cs:111) · **0xF4** 2197 0x8 ZoneUnknownPropList → sent (BuildingClipboard.cs:861)
  - **0xF9**: 2255 0x63 PlayerEnteredClaim, 2256 0x45 SetAdventureFlags, 2257 0x46 StartClaimPlacement, 2309 0x60 DebugAddItemClaim → handled (Claims.cs:226-244, 402); 2269 0xD SendInventoryDetails (ClaimInventory.cs:244), 2273 0x12 LostClaimServerConnection → sent
  - **0xFB**: 2318 0x0 Base, 2321 0x4 NotifyOwnerlessLootClaimed → sent (LootPinatas.cs:157, 229); 2324 0x7 RequestClaimOwnerlessLoot → handled
  - **0xFC**: 2335 0x5 PostQueueNotify → sent (TravelQueue.cs:238); 2336 / 2337 / 2339 / 2340 0x6 / 0x7 / 0x9 / 0xA → handled (Travel.cs:80-89)
  - **0x105** 2407-2411 HeroicMovement 1-5 → handled (HeroicMovement.cs:148-161) · **0x109 / 0x10A** 2425 / 2431 ProceduralDataMonitor → handled / sent (AdminMonitor.cs:147, 162)
  - **0x10D**: 2460 RequestJoinTeam (Request type 9) → handled (Rework.cs; phaseE2-families L3); 2478-2480 0x12 / 0x13 / 0x14 PropState / PropInteraction / PlayerRemoval → sent
  - **0x117** 2556 0x1 ToolPacketActivate → handled (Building.Tools.cs:74) · **0x11C** 2599 0x4 EquippedAttachmentReply → sent (Barbershop.cs:130), 2600 0x3 EquippedAttachmentRequest → handled (Barbershop.cs:45), 2602 0x2 AddKnowledgeResponse → sent (Unlockers.cs:222)
- **Also stale in the same file:**
  - The column header "Emulator (0.589 numbering)".
  - The "Change for 0.695" column: "use sub 0x.. for 0.695" / "u32 sub-opcode for 0.695" (all done since 10-17, 003dc1f).
  - §1's table ("new in 0.695: 1 (SetPlayerBio stub)"; "What a 0.695 client breaks on today: 83 rows").
  - §5, the version-gating / translation plan. It is obsolete: ClientWire695 was removed and the server is native 0.695 (project rule "Native 0.695 numbering"; phaseE2-packets L202).
- **Also: my re-run lost 41 rows the 10-15 table had right.** Those were the SPECIAL_LINES / SRV_FIX hand fixes (Command
  subs 9-0x5A, Groups, Target 0xB5, ObjectScripting 0xC-0x16, 0x2D/0x12, 0x2D/0x19). They are still implemented at
  HEAD (spot-checked). So the old text for those rows is right; this is a limit of the heuristic, not a regression.
- **Fix:** replace the Emulator text of the rows above with the new text (or run `build_table_head.py` and merge by
  hand, keeping the 41 old rows and the 4 false hits as they are). Rename the column "Emulator (HEAD, 0.695
  numbering)". Mark §5 as historical. One sentence in §1 pointing to this report. No in-game check needed.

### F2. trace/families_695: one orphan 0.589 file, 0.589 addresses on 34 hand lines, ~70 under-reported server lines — STALE
- **What is already fixed: lead L-R1's example.** trace.py was changed at 07:51 and the files regenerated at 07:52
  (phaseE2-families NL1 parts 1-3). Every "case functions (dispatcher)" line in the 186 current files names a 0.695
  function [P, `fam.py`]. PlayCompositeEffect now lists FUN_142c7ded0 (the 0.695 partner of 0.589 FUN_142be4330; the
  other two 0.589 wrappers have no partner and are dropped). L-R1 looked at the files before that run.
- **(a) Orphan file [P].** `trace/families_695/0084_GuildDisband.md` is dated 07:18 (all others 07:52). It is left over
  from the pre-fix run, in which the 0.589 dispatcher map put the guild family at 0x84. It carries 0.589 numbering
  (e.g. GuildAddMemberPacket `0x84, 0x15`; 0.695 has 0x83/0x15) and 0.589 case functions (FUN_142e4a900 FUN_142e49570
  FUN_142e52500 FUN_142e4c0b0; their 0.695 partners FUN_142ee9690 / FUN_142ee8300 …). FAMILIES_695.md does not link
  it: only `0083_GuildCreate.md` is listed. **Fix:** delete the file (it is a generated file; the coordinator may do
  this, I did not).
- **(b) "resolved by hand" lines quote 0.589 addresses [P].** They come from re/out/manual_opcodes.tsv, which trace.py
  copies unchanged in 0.695 mode. Exact corrections (0.589 → 0.695; file:line):
  - 0042:597 142e9acd0→142f39870
  - 0045:886 1431acc10→143257ae0, 1431adf70→143258f50
  - 0046:9 143298c40→143345ab0
  - 0072:9 and :14 143494180→143543e70, 142f2fb30→142fcf1e0
  - 008F:9 and :17 142d9e8d0→142e3c8a0, 142d9e710→142e3c6e0
  - 0094:32 142e89bd0→142f28770
  - 00A8:9 143be05a0→143ca04d0, 143659070→143714400, 14324f4b0→1432fc360, 14324fc00→1432fcab0
  - 00A8:13 1436596b0→143714a40 (plus the same two as :9)
  - 00AF:44 143122040→1431cd0a0, 143122e20→1431cde80
  - 00B9:9 143029720→1430cfad0
  - 00C0:9 142e42ff0→142ee1d80, 142e42e60→142ee1bf0
  - 00C4:9 and :14 143568b00→14361c300, 142ee70c0→142f85db0
  - 00C4:19 143569530→14361cd30, 143568c10→14361c410
  - 00C4:24 143568e40→14361c640
  - 00CB:9 142da4a00→142e429d0; 00CB:26 142da5580→142e43550
  - 00CC:9 142db30a0→142e51070; 00CC:14 142db31b0→142e51180
  - 00CF:25 142f412c0→142fe1030
  - 00D5:9 142e33330→142ed2090, 142f2fa50→142fcf100
  - 00DC:51 143671890→14372d7a0, 143393ab0→143441100
  - 00DD:46 142f30200→142fcf8b0
  - 00EE:9 1430e4ae0→14318fbe0
  - 00F5:152 142e74800→142f13390
  - 00FC:252 1430c6da0→143171ee0, 1430c74f0→143172630
  - 00FF:218 142dad120→142e4b0f0
  - 0105:355 142c42ab0→142cdc570
  - 0115:28 142e654e0→142f04070
  - 0117:83 1433ba050→143468090
  - 0119:13 and :18 142c42470→142cdbf30

  All partners are from trace/0695/functions.tsv. The opcodes in those lines are unaffected: none of these families
  was renumbered. **Fix:** in trace.py's manual_opcodes loop, map each `FUN_` through `M589` when CLIENT == 695 (as
  is already done for messages.tsv), then regenerate.
- **(c) Header text [P].** Every file says "Server status from re/out/coverage.md". In 0.695 mode, trace.py:41 reads
  `coverage_695.md`. The header string at trace.py:425-426 was never made per-build.
- **(d) "server:" lines under-report.** About 70 classes say NOT SENT / sub NOT handled although HEAD sends or handles
  them. Examples:
  - 0045: AccountStore, UnFollowCreator, GetProductForReview, WalletInfoRequest 0x45/… (all "received 84× / 41× / 518×" and answered).
  - 0083: AddMember, RemoveMember, OnlineStatus, AddLog, RemoveLog.
  - 00F9: 0x45, 0x46, 0x63, 0x12.
  - 010D: StartRequest, EndRequest, ObserverRegistration, PropRecipe, PropState, PlayerRemoval, HUD, Event.
  - 0111: all the ObjectScripting responses.
  - 0112: vendor responses.
  - 00F8: Mail EnableWindow, Compose / AttachItem responses.

  Cause: trace.py's `server_uses` (trace.py:297-333) only sees `ZoneMessageOpcode.X` enum names, and it inherits
  coverage_695.md's statuses, which are older than the native renumbering (phaseE2-families NL1 part 4, still open:
  re/out/coverage_695.md:514 / 516 still say "sub NOT handled" for 0x45/0x37 and 0xF9/0x63). A few hits are not
  sends: ChatRooms result codes and an admin C→S row filed as S→C. **Fix:** as NL1 part 4, plus the
  reachable.py improvements named in phaseE2-packets ("New leads"), i.e. sub constants written after a family base.
  Then regenerate.

### F3. ClaimsPacketSetSupplementalData 0xF9/0x3E: unreachable on the retail 0.695 client; server correct to ignore it — OK-NOTE (closes the L140 addendum)
- **Already settled:** agent-l140.md:105 / :234 closed it as "UNREACHABLE (admin client only)" on 2026-10-18. The
  part-2 addendum ("if it is built, accept index < 0x19") reads as if it were still parked. It should say closed.
- **Senders [P]:**
  - `refscan.py` on Landmark64.exe (0.695): FUN_142dd1e50 and FUN_142dd1f90 have **no** call, LEA or pointer reference
    (only EH-table RVAs). Their 0.589 partners FUN_142d34590 / FUN_142d346d0 were also unreferenced in 0.589
    (agent-l140).
  - The third sender, FUN_142dd0510 (0.589 FUN_142d32d50, "same"), has one call: 0x14311095b in FUN_1431105c0.
    FUN_1431105c0 is called by FUN_143111480 / FUN_143113410, the Templates chain. It builds a "%s^%llu^%d" string
    per prop of the template and sends it with index 0xF.
  - All three send only if `(*DAT_1454311c8 + 0xB0)()` is true. In 0.695 that vtable slot is FUN_142b21000 =
    `return 0` (OPEN-LEADS.md:60). So the retail client never sends 0x3E, whether or not the player is an admin. The
    admin-player flag (+0x7b0d8) is a different check.
- **What it carries [P builder fields, I wire order].** ctor FUN_142dac3e0 sets these object fields:
  - +0x18 u64: the claim guid (+0x348 of the claim object in 1e50 / 1f90; param_3 in 0510)
  - +0x20 u64: 1, or the caller's value in 0510
  - +0x28 string
  - +0x40 u64 = DAT_14543bc28 (default)
  - +0x48 u32 index (< 0x19 in 0.695, < 0x18 in 0.589; 0xF from the template path)

  I did not find the serializer slot (the RTTI COL at 1446c16b0 is not the vtable).
- **Server:** no case for 0x3E. It falls to the Claims default branch (ClaimsHandler.cs:14), which is correct. No live
  use is possible, so nothing to build. If a modified client ever sends it: ignore it, log at debug.
- **What should change:** the L140 addendum text from phaseE2-rebase.md ("if it is built, accept index < 0x19")
  should read: "closed UNREACHABLE: the +0xB0 admin-client check is FUN_142b21000 `return 0`; 1e50 / 1f90
  unreferenced; 0510 only from the template chain FUN_1431105c0, behind the same check (phaseE3-stale.md F3)."

### F4. Server comments and docs that cite 0.589 functions as current — STALE (comments only; no code bug found)
- **Scale [P, `srcscan.py`]:**
  - LandmarkEmu cites 1,798 addresses that are 0.589 function starts and not the same function in 0.695.
  - 1,158 of them are 0.589-only addresses with no "0.589" / "0.695" word within 90 characters. They are in 171 files.
  - The most affected files (citations / distinct functions):
    - Claims.cs 60/50, docs/protocol.md 50/49, Props.cs 40/28, Commerce.cs 37/33, Guilds.cs 33/30
    - docs/terrain.md 33/32, docs/encounters.md 28/21, docs/commerce.md 25/24, Health.cs 24/21
    - docs/instanced-games.md 24/24, InstancedGames.cs 23/20, docs/sendself.md 23/22
    - Groups.cs 22/19, Shared/Game/GroupPackets.cs 21/19, docs/unknowns.md 21/21, Building.cs 20/18
  - The 13 docs/*.md files hold 244 of them. LandmarkEmu/docs was deliberately not given the rebase pointer note
    (phaseE2-rebase.md:16-18), and *.cs files were never in rebase695.py's FILES.
  - A further 363 unlabelled citations are addresses that are a *different* function in each build. A reader using
    re/decomp_all_695 lands on the wrong function.
- **Behaviour check.** For each cited 0.589 function whose 0.695 partner differs (rebase695.py `compare`: self +
  depth-1 callees):
  - 110 are covered by the part-2 verdicts (re/out/rebase695_verdicts.tsv: N / S-match, plus the one S-gap, G1, now on
    HEAD 3d6b1e1).
  - 61 are cited only in code. I read their diffs (`nov.txt`, `d1-d3.txt`). Those that touch the wire or rules:
    - **InstancedGame PropRequest** (Editor.cs:238 cites 0.589 writer FUN_143426a50). 0.695 FUN_1434d62d0 appends a
      u32 (slot, then ScriptPropId) [P diff]. The code already reads it (InstancedGames.Editor.cs:251-257). Only the
      doc comment's layout is 0.589.
    - **PropRecipe item** (Editor.cs:21-23 cites FUN_1434282f0). 0.695 FUN_1434d7f90 reads an extra trailing u32
      (item +0x44, ScriptPropId) [P diff]. The code writes it (Editor.cs:130-133). Only the comment is stale.
    - **AddAdditionalProp / RemoveProp** (Editor.cs:236 cites FUN_14343e9d0). 0.695 FUN_1434f0f00 takes one more
      argument and matches row +0x44 [P]. It is the same ScriptPropId, and it is handled.
    - **CommandPacketBuilderMode** (BuildModeHolster.cs:11, InteractionHandler.cs:113 and MiscHandler.cs:456 cite
      FUN_1433bec40). 0.695 FUN_14346cc90 adds a gate before entering build mode. The gate reads DAT_145438110
      +0x38 / +0x40 / +0x80 [P]; I take these as the instanced-game manager's game / observer fields, from L176's
      FUN_1434e2330 [I]. So the client will not enter build mode while it is in a site game or observing. This is
      client-side; the server only reacts to the report. OK-NOTE.
    - **Barbershop PlayerSet** (Barbershop.cs:16 cites FUN_142ffeec0). The 0.695 sender FUN_1430a2530 sends one more
      u32 (+0x1058) and re-applies the look locally when the race changed. The comment already describes the 0.695
      writer FUN_1430a0530 and its two extra u32s. Fine.
    - **PlayerUpdate readers** cited in Creatures.cs:2381 (0x67), Health.cs:614 (0x39), HitEffects.cs:19 (0xB) and
      InteractionMessages.cs:50 (0x6B): only the u8 → u32 sub changed, and that is built.
    - **The rest** are callee refactors, offset moves or null checks: Inventory RearrangeItemSlots /
      RequestOverflowAbsorb, VeinGenerator FUN_143f17220 (decompiler SUB84 noise), Claims setters (offsets), Groups
      (FUN_142d52130 Lua param refactor), Teleport FUN_142cc0ba0, Templates, WarpNetwork, WorldEditing senders.
- **Wording-only 0.589 statements I checked, all still true for 0.695 [P]:**
  - ClientFixes.cs:6 says "Gaps in the **0.589** client's own UI scripts". The 0.695 UI\ScriptsBase.bin still calls
    `ClaimVendorScreen.BuyItem`, and luabind_695 registers the natives as `LuaClaimVendor` (0x1447b30f0-…). So the
    vendor patch is still needed on 0.695; only the build name is wrong.
  - Health.cs:54 / :484 say "0.589 has no accept prompt". 0.695 likewise: `DoYouAcceptRevive` is only in
    CodeStringMappings.txt:977 and in no 0.695 Lua / ScriptsBase / AS3 / decompile.
  - Encounters.cs:32-33 and Emotes.cs:583 say there is no duel menu. In 0.695, "duel" occurs only in GameOptions.lua
    (auto-refuse) and Gaq.lua (DuelRequest), not in any UI\Default XML.
  - InstancedGames.cs:304 "0.589-style settings window": the 0.695 path is covered (docs/client-to-server-shifts.md row C).
  - Landscapes.cs:431 "0.589 has no fall damage": SafeEject.cs:57 says Landmark had none after 2014.
  - Props.cs:1001 / :2163 "the 0.589 client …": FUN_142f1ab30's 0.695 partner FUN_142fb9ba0 has verdict N.
  - GameSession.cs:51 "Off: the 0.589 client fails to enter the world with it": the default stays right on 0.695 for
    the reason given on the next line (RC4 ciphertext never shrinks).
  - Already noted by part 2: the PlayerUpdateAddLightweightProp.cs header (phaseE2-packets.md:187).
- **Fix (docs / comments, low priority):**
  1. Run rebase695.py's citation pass over `LandmarkEmu/**/*.cs` too (add it to `FILES` with a .cs filter), and give
     every unlabelled 0.589 address in code and docs either a "0.589" label or its 0.695 partner (the address map plus
     `changed.txt` hold all pairs).
  2. Fix the two InstancedGames.Editor.cs doc comments (lines 21-23, 236-238) to the 0.695 layouts the code already
     uses.
  3. "0.589 client" → "the client (0.589 and 0.695)" in ClientFixes.cs:6.

  No in-game check needed.

## 2. Checked and fine
- families_695: every "case functions" line in the 186 current files names a 0.695 function (L-R1 example fixed by the 07:52 regeneration) [P].
- families_695: "built in", "reader" and "code" blocks are taken from re/decomp_all_695 (the 540 both-build addresses in them are 0.695 bodies) [P].
- coverage-695 rows the old text has right: the 41 my re-run lost are still implemented (spot-checked: InteractionHandler.cs Command subs, Groups.cs, StoryTools.cs:2244 family, InventoryHandler 0x12 / 0x19).
- PointOfNeedPrompt 0x45/0x48 and Mail ClearInboxResponse 0xF8/0xE: still not sent at HEAD (row text right).
- ClaimsPacketsIdUpdateClaimBasicInfoAndAOI 0xF9/3: not sent at HEAD (AOIUpdate is); row text right.
- Barbershop PlayerSet 0.695 tail, SetPlayerBio, DisplayPropText: built (rebase findings 1 and 6).
- VoxelOwnership.RequestAllCellsForClaim: 0.589 FUN_14354c980 = 0.695 FUN_1435ff2e0 "same", so the EnvServer comment's "(0.589)" flow holds for 0.695.
- EnvironmentMessages.cs:22 "0.589 numbering" (every sub "same" in 0.695); MailMessages.cs:12 (server-only ids, already 0.695): wording only.
- LoginHandler.cs:316 (any requested server id is accepted): fine for 0.695.
- Gateway refuses 0.589 builds (GatewaySession.cs:290-293), so no 0.589-only code path is reachable.
- local/pe2-rebase G1 (3d6b1e1) and the pe2-packets / combat / social branches are merged into HEAD.

## 3. Open leads: what is settled since

**OPEN-LEADS.md entries with no CLOSED / RESOLVED on their top line** (48), checked against the phaseE2 reports and git
at 8219f7c. "Settled" means only the closure line is missing; the text to add is in the cited report.

| Lead | Settled? | By what |
|---|---|---|
| L95 | yes | phaseE2-combat L95 CLOSED (best evidence); 9226d91 (off-hand models) on HEAD |
| L168 | yes | phaseE2-combat CLOSED; built 38f752b |
| L169 | yes | phaseE2-packets CLOSED (SOEInGame.EvalLUA, store-only hosts) |
| L170 | yes | top line already FOUND + BUILT; phaseE2-combat CLOSED |
| L171, L172 | yes | built 7a12d42 (10-18); phaseE2-packets verified |
| L174 | yes | built da8feb2; verified |
| L175 | yes | built d69050d; CLOSED best evidence |
| L176 | yes | built e0c161c; verified |
| L177 | yes | built 471aa71; Update Notes 02.20.14 found (phaseE2-packets) |
| L180 | mostly | phaseE2-social CLOSED; template / height continue as social N3 |
| L181 | mostly | phaseE2-social CLOSED; self-hosted voice = social N4 |
| L185 | mostly | phaseE2-social CLOSED; live counts = social N2 |
| L188 | yes | phaseE2-combat CLOSED; 813ca1a TravelCooldownSeconds |
| L189, L190 | yes | phaseE2-packets CLOSED; 43987ab merged (9413e7d) |
| L192 | yes | phaseE2-combat RESOLVED / CLOSED; 813ca1a, de3e79e |
| L193 | yes | phaseE2-building CLOSED; d400254 on HEAD |
| L194 | yes | phaseE2-combat CLOSED |
| **L195** | **no** | NEEDS RUNTIME CAPTURE (phaseE2-building NL-1) |
| L196 | mostly | phaseE2-social RESOLVED, b0c86c6 on HEAD; OL-2 needs the two-client check |
| L197 | yes | phaseE2-social RESOLVED / CLOSED |
| L198, L200 | yes | phaseE2-building CLOSED; 8b88c5e docs |
| **L199** | **partly** | The paste-turn half of local/pe2-building **a9f8d95 is not on HEAD**. d400254's message says the org's #12 (b3fccd0, 2026ec4) "fixed the same paste matrix reading". The cube-alignment part (FUN_143c6eb20 grid size+1-s vs cubes size-s) and a9f8d95's 48-turn test `Paste_TurnedLikeThePreview` are not on HEAD; see N-S2. NL-2 (+0x2b0) and NL-3 (turned paste check) are still open |
| L201 | mostly | phaseE2-combat RESOLVED; combat N1 open |
| L5, L6 | yes | FOUND 10-15 ("Coordinator: move to Found"); L5 built 9e59ec8 / 01d1a37, L6 2191f7b |
| L1 (13/14), L2 | yes | in the Found section (TilingTestZZ.bmp and L2 row data remain, as their own text says) |
| L98 | yes | FOUND + built 0cc86a8 (header line says so) |
| L137 | partly | following + digest built (6c2e99a), 0xD0 answered; phaseE2-packets L169: no captures of store.landmarkthegame.com. The 2017-01-06 Showcase snapshots are still unread |
| L140 | yes | triaged 10-18 (agent-l140), built 6513c82; the one sub-lead L170 is closed; SetSupplementalData closed (F3) |
| L142 | yes | FOUND (sweep) + built d7d3018 (sent when the Environment Server is away) |
| L143 | partly | ZoneCraftingWindow → L177 (built); InstancedGamePropState now sent (InstancedGames.cs:1882). LoadingScreenPacket and RewardBasePacket still have no sender at HEAD (grep) and no live trigger |
| L144 | partly | built 5831b55 / 77f0f18 (persistence, levels, lock, friendly-fire awards off); live earning rule still open |
| L145 | yes | FOUND (VoxelPathFindVisualizeCursor slot 83) |
| L146 | yes | FOUND + built 5cc61f1 |
| L147 | no | live DBC prices: no new source |
| L148 | yes | the "store gives designs free" text is stale: cb06bde / fbf760c (creator's price in Lumens, creator share, listing fee). The DBC → Lumen rate belongs to L147 |
| L149 | partly | Regalia found + built (dce93ea); the other founder items are open |
| L150-L154 | no (umbrella) | pointers to agent reports; not re-checked item by item. Graveyard work (0041759, 9a4ee5a) touches L150 |
| L155 | no | dev-only save path, untouched |
| L157 | mostly | built be4ab3f / 53bdcc1 / 785b579; the remaining ScorpionBush / kobold-spear rows have no creature (could be closed as best evidence) |
| L161 | mostly | BUILT 10-17; "whether live restocked other things" open |

**New leads in the phaseE2 reports:**

| Lead | Settled? | By what |
|---|---|---|
| rebase L-R1 | mostly | the 07:52 regeneration; what is left is F2 (a)-(c) |
| rebase L-R2 | no | corrections are in F1 |
| families NL1 | partly | parts 1-3 folded into trace.py / uimap.py at 07:51 (trace.py:20-36, 203-206, 359-362; ui_server_map_695.md regenerated 07:52). Part 4 (coverage_695.md statuses) and part 5 (luabind placeholder) still open |
| families NL2 | yes | HEAD 8219f7c answers 0xF0/0x12 with 0x13 (TemplateInstances.cs:126-218) |
| building NL-1, NL-2 | no | runtime / static work still to do |
| building NL-3 | no | the turned-paste check; the code under test is now the org's #12 |
| combat N1, N2 | no | — |
| social N1 | no | DeadBodies (b0c86c6) still sends no bit-7 delta to the dead client itself |
| social N2, N3, N4 | no | Wayback / Vivox: no change |
| packets | — | no open leads; its reachable.py note is folded into F2 (d) |

## 4. New leads

### N-S1. Where ClaimsPacketSetSupplementalData's 25 indices would be read (low priority; unreachable sender)
- **Need:** the S→C or claim-entry field that a 0-24 supplemental index addresses (the claim record carries only 5
  "supplemental" strings, Claims.cs:1097-1101), to name what the admin tool would have set.
- **Searched:** decompile grep "Supplemental" (only the class's ctor / dtor / three senders and the id table
  FUN_1434bc510); text tables; LandmarkEmu.
- **Try next:** the serializer slot of the ClaimsPacketSetSupplementalData vtable (find the vtable that holds
  FUN_142db2270), then the claim-entry offsets near +0x48..+0xa8 for a 25-element array.

### N-S2. Does the org's paste fix (#12, b3fccd0) cover a9f8d95's cube alignment?
- **Need:** check whether HEAD's Building.PasteBase puts the turned *cubes* on the origin, as the client's
  FUN_143c6eb20 does (grid size+1-s, cubes size-s). If it aligns the grid points instead, flipped axes would sit one
  voxel low, as a9f8d95 described.
- **Searched:** git log / show of a9f8d95, d400254 and b3fccd0; the test `Paste_TurnedLikeThePreview` is not on HEAD.
- **Try next:** port a9f8d95's 48-turn test onto HEAD and run it (no code change), then the in-game check of NL-3.

## Summary
Three of the four loose ends are bookkeeping:
- **coverage-695.md** describes the emulator as of 10-15 in 0.589 numbering. Re-running agent #10's scan on HEAD,
  matched on the 0.695 sub, gives 118 rows that now read sent / handled, plus 19 more I found by hand and 4 false hits
  to leave alone. They are listed with file:line in F1, as are the obsolete summary and gating-plan sections.
- **trace/families_695** was already regenerated with 0.695 case functions, so L-R1's example is gone. What remains:
  an orphan 0.589-numbered 0084_GuildDisband.md to delete, 34 hand-resolved lines quoting 0.589 addresses (all
  partners listed), a wrong header line, and server-status lines that under-report about 70 classes.
- **The supplemental-data message** cannot be sent by the retail 0.695 client. The +0xB0 admin-client check is a
  constant `return 0`; two senders have no reference and the third sits behind the same check on the template path.
  agent-l140 had already closed it; the server is right to ignore it.

On the server, 1,158 unlabelled 0.589-only addresses remain in comments and docs (most in Claims, Props, Commerce,
Guilds and docs/protocol.md). Every case I checked where the 0.695 function really changed — InstancedGame PropRequest
/ recipe item (ScriptPropId), Barbershop PlayerSet, BuilderMode, the PlayerUpdate readers — the code already follows
0.695, so these are stale comments, not bugs.

Of the 48 OPEN-LEADS entries still open at their top line, 29 are settled by builds or part-2 reports and need only
their closure line; 7 more are settled except for a named sub-point. L199 is half-settled: its paste half was replaced by the org's #12, which leaves a cube-alignment
check (N-S2). L195, L147, L155 and the umbrella leads L150-L154 stay open.
