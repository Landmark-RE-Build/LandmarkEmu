# Props / claims / loot items from the E-leads research — build report (2026-10-17)

Branch **`e-props2`** (worktree `<workspace>\wt-props2`), 7 commits on top of `bd03023`, not pushed.
`dotnet build LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -v q` succeeds. Nothing was run
(no Docker, server, client, DB writes). Sources: `trace/0695/agent-e-leads.md` §1 (L135), §2 (L141), §5 (L146);
`trace/0695/agent-leads-sweep.md` L142, L86, L10. Proof marks: **[P]** client code / data, **[I]** inferred.

| Commit | Lead | What |
|---|---|---|
| 95504a8 | L141 | Ownerless loot claim answered with NotifyOwnerlessLootClaimed 0xFB/4; bag life 120 s |
| 90df0bd | L135 / E.1 | `Game/Props.Platforms.cs`: moving platforms (0x7F for the placed prop) |
| 5cc61f1 | L146 | `Game/ClaimInventory.cs`: site inventory, 0xF9/0x60, 0xF9/0x23 → 0xF9/0x0D; EF migration |
| d7d3018 | L142 | 0xF9/0x12 LostClaimServerConnection when the Environment Server is away |
| 13ea212 | L10, L86 | Locked-out Lost Crystal shows 574500; L86 verified already built (comment only) |
| f1a6dfe | L141 test | dev command `lootfree` |
| ddd7f58 | docs | `docs/protocol.md`, `docs/gameplay.md`, `docs/terrain.md` |

**Restart / data note:** deploying this branch (merge + `up.sh`) restarts the dev world, and the hand-written migration
`20261030090000_ClaimInventory` adds `world_prop.paidByItem`, `world_prop.paidByItemGuid` and `claim.inventory`
(all with defaults; nothing is rewritten). Props placed before it keep 0 there.

---

## 1. L141: ownerless loot claim (`Game/LootPinatas.cs`)

**Client facts re-read** [P]: LootPinataManager::HandlePacket FUN_143125240: case 4 reads with FUN_143121c60
`{u16 0xFB, i8 4, u32 id (+0x18), u64 owner (+0x20)}`, finds the pinata by id and writes the owner to +0x200 and +0x70,
then state 3 (scooping). Cases 2 (RemoveLoot) and 3 (PickupLootResponse) both call FUN_143126f70, which deletes it at once.
The per-pinata update FUN_143127a10 sends 7 RequestClaimOwnerlessLoot on every update while it is ownerless and within
5 m (hash 0x1d59bdcd). Once it has an owner it scoops to that owner entity. The owner's client then sends 5 PickupLoot,
and any other client only sets the destroy flag +0x1E4. If the owner entity is unknown to a client, that client decays
it (state 4).

**Built:**
- `ClaimOwnerless` now handles the first sub-7 claim like this:
  - the claimer must be one of the bag's viewers and within 10 m of the drop. That is 5 m plus 5 m slack [I].
  - the server sets that player as the owner with `ConcurrentDictionary.TryUpdate`, so only one claim can win.
  - it sends **sub 4 `{u32 id, u64 claimer}` to every viewer**, the claimer included.
  - it gives no contents yet. Repeated claims and losing claims get **no reply**.
- The contents go to the claimer through the existing PickupLoot owner branch, which also sends sub 3.
- `Pending` now holds the drop position.
- `BagLinger` = **120 s**, from `LootPinata.DecayAge` (StringHashToValue.txt:374). Its hash 0x65741807 is read nowhere
  in the client, so it is a server value [P]. That it is the bag's lifetime in seconds is [I]. Chunks keep 10 s.
- `SpawnOwnerless` is still the only entry point. **No rule makes loot ownerless** (that is the user's decision).
- Dev command `lootfree <item> [count]` spawns an ownerless bag for testing.

**Interim rule options (for the user to choose; all [I], none built):**
- a. **World-caused drops**: loot with no entitled player. Example: the Fiery Fungus explosion's destroyed-terrain loot
  (P4533). Today `Creatures.cs` gives it to whoever triggered it.
- b. **Abandoned personal bags**: when the owner disconnects or changes zone before collecting, re-spawn the bag
  ownerless for the players still in range. No client path forbids this, and loot would no longer vanish.
- c. **Never** for chest, reward-chest or creature loot with a credited player (P1647 / P6503 keep those personal).
  This applies alongside a and b.
- d. Keep today's behaviour: nothing is ownerless, which is what the server does now.

## 2. L135 / E.1: moving platforms (`Game/Props.Platforms.cs`)

**Wire** [P, re-read]: 0x7F handler FUN_142ce6340. It finds the entity by transient id (FUN_142f85340) and applies the
movement (vfunc +0xD0). If vfunc +0x108 (AsProxiedProp) returns the prop, it sets **ProxiedProp +0x4F00 = 1**.
Each platform update is `Creatures.MovementBody(position, quaternion, velocity)`, the creatures' type-0 body. I added a
quaternion overload; the yaw version now calls it, and its bytes are unchanged. Updates go through `MovementFanout.Queue`.

**Packs and parts** [P, PlacementPackProps / PlacementPacks / PropDefs]:
- 5 Magical: spawner 1207 → platform 1193 + docks 1192 at x −5 (heading 0) and x +5 (heading 180).
- 6 Three-dock: spawner 1288 → 1193 + three 1192, the third at z +5 (heading 90).
- 8 Glass: spawner 1423 → 1421 + docks 1422.

A platform is registered from `BindMover`, which runs on pack expansion and on world load.

**Settings:**
- `moving_platform_speed` (oaat **0x321630f0**): speed = setting × 0.01 m/s. Default 160 → **1.6 m/s**, clamped to
  50-1000 [P factor and limits; I that the unit is m/s].
- `moving_platform_pause_delay` (oaat **0x30725695**): whole seconds. Default **2**, range 0-30. [P: help text 564599
  "The time in seconds that the platform will rest once it reaches a dock."]
- Values are read with `PropSettings.Get` / `TryGetSaved`, keyed by the strings that `KeyHash` (oaat) turns into the
  client's keys. The window already offers rows 7 / 8 for the "platform" kind, and `propset` writes them.
- A dock's own saved value governs the pause at that dock and the leg that leaves it. Otherwise the platform's value
  applies, else the default [I: each waypoint area carries both editables].

**Motion** [I]:
- The motion is a pure function of a running clock, so no per-tick state is kept.
- The platform stops 2.3 m in front of each dock, along the dock's own +X. That is half the platform box (±1.8) plus
  half the dock box depth (1 m), from GlobalAreaShapes.
- At each dock its turn is the dock rotation × inverse(the dock's pack heading). Between docks it slerps (P1537).
- Two docks: there and back. Three: 1 → 2 → 3 → 2 → 1.
- Switched off, the clock waits and the platform stops where it is. "Off" means `Disabled` (prop menu / `propenable`),
  or in Play Mode the Links' state through the new `StoryTools.PlayModeOn` (P2668).
- A platform with any part being placed or moved (`!Placed`, `MovedAt`, `ReplacingBy`) is left alone.
- When parts re-spawn after a move settles, the platform's viewers are reset.

**Bandwidth:**
- Updates go only to players on the landscape within **120 m** who are not mid re-zone.
- One update at each leg start and stop, and every **500 ms** while moving.
- A newcomer is sent the current spot twice (now and +1 s), because the client drops a position for a prop it has not
  spawned yet.
- Nothing is sent when nobody is near. The prop record and the DB keep the placed spot.

**Open: riders.** The client re-registers the prop's collision at most **once per 1000 ms** (FUN_1430ab000, +0x4F08).
Whether a player standing on the platform rides along, or drops through or off, is the runtime test below. If riders
fall, trace next: the ProxiedProp branch of the ProxiedCharacter update FUN_142eb1950 (~line 622). It drives
controller +0x180 per frame.

## 3. L146: claim inventory (`Game/ClaimInventory.cs`, `Game/Props.ClaimItems.cs`)

**Client layouts re-read** [P]:
- **0x60 DebugAddItemClaim** (builder FUN_1433c73a0, writer FUN_143380850): `{u32 item, u32 tint, u32 count, u64 target,
  u64 claim, u32, u32, u8}`.
- **0x23** (from `/item claimItemToInventory`, part_0101 ~33330): `{u64 claim (+0x18), u64 player (+0x20), u64 item
  (+0x28)}`.
- **0x0D** reader FUN_142da0d70 + FUN_142da3c70 / FUN_142d9fe40, dispatcher case 0xC: `{u64 claim, u64, u32 n, n ×
  {u64, u32 ×5, bool, u32, u64, u32, u32}}`. Only the guids are printed ("Claims Inventory for claim: %llu",
  "\tItemGuid: %llu").

**Model:**
- A site's items are every prop inside its box (any owner) with `PaidByItem ≠ 0`.
  - Item guid = `PaidByItemGuid`, or the prop guid when that is 0. Count 1. Prop guid = the spawn guid.
  - A mover pack is listed once: its first part.
- Plus the unplaced items in `claim.inventory` (`guid:item:tint:count;…`). Their guids come from
  `Inventory.NewItemGuid()`.
- `PaidByItem` / `PaidByItemGuid` are now persisted, because they were "this run" only before (`Persist` / `LoadWorld`).
  Pack parts inherit both.

**Handlers (admins only, `Admin.IsAdmin`):**
- **0x60** adds `count` (1-10000) of the item to the unplaced list.
- **0x23** looks up the item:
  - an unplaced item goes to the player (bag, then overflow) with a loot toast.
  - a placed one, matched by item guid or prop guid, is picked up as that player's `Refund.AsPlaced`, the whole pack
    with it (`Props.TakeAsClaimItem`).
  - the player must be online. Player 0 means the sender.
- Both reply **0x0D**: the element fields after the guid are [I] (item, tint, count, index, 0, placed, 0, prop guid,
  0, 0) and the second header u64 is 0. A system line repeats the list, because the client prints to its console.

**Delete (P0079)** — what `Claims.Delete` did before this change:
- It refuses a delete only when the site is unknown, on another landscape, has a game, or the Environment Server is away.
- It never checks room.
- It returns props as **materials** (P4870, 26 Apr 2016, the launch rule) through `Props.ReturnInBox` →
  `Inventory.Add`, which overflows by itself (500 slots).

Now it also returns the unplaced items (bag, then overflow). P0079's 2014 room rule predates the overflow tab (P0862),
so I added no room refusal [I]. That is documented on `Delete`.

## 4. L142: LostClaimServerConnection 0xF9/0x12

- [P] Dispatcher case 0x11 (part_0069 14596) reads the header only (FUN_142da8e50). It calls
  FUN_142db7970("Claim.LostClaimServerConnection") → CodeStringMappings.txt:97 → **562155**, shown as a system message
  and a chat line. The client blocks nothing.
- `Claims.SendLostServerConnection` sends the header-only `0x12`, at most once every 10 s per player [I].
- Where it is sent:
  - a site delete refused because the Environment Server is away;
  - every build refusal that used `ZoneTerrain.SendUnavailable`: Building edits / heal / undo and BuildingClipboard.
    That function now sends 0x12 instead of the invented VoxelConstruction 0x1B ZoneDisplayNotification of the same
    text.
- The server detects the outage through `EnvironmentServer.Instance.Available` (remote link synced and open,
  `RemoteEnvironmentServer`). An in-process Environment Server is always available.

## 5. L86: Build Site Expansion

This was already built, and I checked it:
- `ItemDefinitionBuilder` own id **900001**, template 3711, `NoTrade = false` (bit 0x80 of flag byte 0 cleared).
  `Trades.cs` / `Mail.cs` honour NO_TRADE.
- `ItemUses` → `Claims.UseExpansion` takes one with `TakeItem` and adds a slot (max 8, refusal 571174). It also
  re-sends the Site Management rows: the "unlocks available" count of forum 56259.
- `Store` delivers the item into the inventory (`GiveItem`), which matches the developers' "consume it yourself or
  trade it" (57682-1).
- Only the definition comment changed: it now cites the forum evidence.

## 6. L10: Lost Crystal refusal 574500

- [P] `CaveRewards.TooSoonToUse^574500` is a server table value. Its oaat hash 0x479250ed and the id 574500 appear in no
  client code, Lua or AS3. So the client shows it only when the server sends the id in a text-carrying packet.
- Built: in `Outposts.CompleteChannels`, a locked-out crystal now shows **`SystemMessages.Show(574500)`**
  (PacketShowSystemMessage 0x69 → BaseClient::ShowSystemMessage). It still sends the next-cave offer and still gives
  no reward, per the 4 May 2016 developer post.
- When it is shown is [I]. "Use the Travel menu" could mean live refused at use time without a prompt; that is listed
  below.

---

## Open leads

All of these are still to be settled. None is lost.

- **L135:**
  - whether riders stay on (runtime test below);
  - the exact stop offsets (the model sizes of 1192 / 1193 / 1421 / 1422);
  - the three-dock order (ping-pong or cycle);
  - the three waypoint row fields (+0x50 / +0x54 / +0x58, FUN_143db2e90): next, map the AreaProperties row-struct
    offsets in its loader;
  - the spawn's settings list (+0x340) with the oaat keys is still not sent (L134 part 2). The server moves the
    platform, so the client only needs it for its own data class.
- **L141:**
  - the live trigger rule (options above). Next: developer posts by name in design/forum/all (full text), EQN "loot
    pinata" talks, a live capture;
  - the claim range slack (5 m + 5 m).
- **L146:**
  - the meanings of the ClaimItemDetails fields after the guid;
  - the full-item form 0x37 InventoryListDebugResponse (decode FUN_143404230 / FUN_142c208e0, then send 0x37 for
    `/claim inventory`);
  - unplaced claim items are not yet packed into Build Site Contents when a site **lapses** or is **abandoned** (only
    Delete returns them): next, add them to `SiteContents.Pack`;
  - unplaced item guids come from `NewItemGuid`, which after a restart is seeded only from character_item, so a guid
    can repeat across sites (lookups are per site);
  - props placed before the migration are not listed (their PaidByItem is 0).
- **L142:**
  - whether live also pushed 0x12 by itself when the claim server link dropped. This build sends it only as a request
    refusal: the text "Unable to process the request" reads like a refusal;
  - ZoneDisplayNotification's live texts (L126).
- **L10:**
  - the moment live showed 574500: after the channel, with the offer (built), or at use time instead of the channel;
  - the lockout length (24 h is ours);
  - Next: forum threads after 15 Jun 2016 that quote the text.
- **L86:** live's item id. 900001 is our own id.

## Test steps

**Setup:**
- Write each command as a line in `server-data/worlds/dev/dev_commands.txt`, or use the admin page. `@Name cmd` targets
  one player.
- Use the 0.695 test copy only (`<0.695 test copy>`, `re/dlogin.sh`).
- For the `/item` client commands, the character must be in WorldServer.json `Admins`.

1. **L141, ownerless bag:**
   1. Stand on open ground and run `lootfree 1579 5`. A bag appears 3 m away.
   2. Walk within 5 m. Expected: the bag flies to you, a "Lumens ×5" toast shows, the Lumens count rises by 5, and the
      world log has "<name> claimed ownerless loot <id>."
   3. With two characters in range: the first to come within 5 m wins. On the second client the bag flies to the winner
      and vanishes, and only the winner's Lumens rise.
   4. Unclaimed bags vanish after 120 s.
2. **L135, platform:**
   1. On your own build site: `beginprop 1207`, then `placeat here`. The log shows "… became pack 5: 1193 0x<P> at …,
      1192 0x<D1> …, 1192 0x<D2> …".
   2. Expected: the platform jumps to 2.3 m in front of dock 1. It travels about 5.4 m at 1.6 m/s (about 3.4 s), rests
      2 s, and returns.
   3. `propset <P> moving_platform_speed 400` → 4 m/s. `propset <D1> moving_platform_pause_delay 5` → a 5 s rest at
      dock 1 only.
   4. `propenable <P> 0` stops it where it is; `propenable <P> 1` resumes.
   5. Glass: `beginprop 1423`. Three-dock: `beginprop 1288`.
   6. Rotate a dock with the gizmo: the pad turns as it travels.
   7. **Rider test:** stand on the platform while it runs. Note whether you are carried, slide, or fall through. Then
      repeat slowly:
      1. `propenable <P> 0`;
      2. stand on it;
      3. `propslide <P> 4 0 0 8`, which slides 4 m in 8 s from its **placed** spot.
   8. Optional, test copy only, Frida without timeouts or kills: read ProxiedProp +0x4F00 / +0x4F08 during the slide.
3. **L146, site inventory (admin):**
   1. Get your site id from the log line "Placed site <id>'s Starting Location".
   2. Place a prop from a bag item on the site: `giveprop <prop id>` (any prop that has an item form; the log says
      "Gave … item … for prop …"), then place it from the bag (or `beginprop #<item id>` + `placeat here`).
   3. In the client run `/item claimAddItem 1579 10 0 <id>`. Expected: a system line "Build site <id>: 2 item(s);
      <guid> = 1 x <item> (placed 0x…); <guid2> = 10 x 1579".
   4. Run `/item claimItemToInventory <guid2> <id>` → 10 Lumens arrive (toast).
   5. Run `/item claimItemToInventory <guid> <id>` → the placed prop disappears and its item comes back (a mover
      pack goes whole).
   6. Add an item again, then delete the site in Site Management → the added item returns (log "… 1 inventory item
      stack(s) returned").
4. **L142 (remote Environment Server setup only):**
   1. Stop the Environment Server container.
   2. Try to build, or delete a site. Expected: the system message and chat line "Unable to process the request.
      Connection to the Build SiteServer is unavailable." (once per 10 s), and the log line "site request refused,
      Environment Server unreachable".
   3. Start the Environment Server again.
5. **L10:**
   1. `outpostwake <n>`, then `outpostuse <n> crystal`. Wait 10 s: loot comes.
   2. `outpostuse <n> crystal` again. After 10 s, expected: the system line "You've recently bonded with a Lost Crystal
      in this Chaos Cavern. …", the cave offer, and no loot.
6. **L86:**
   1. `give 900001 2`.
   2. Trade one to another character: it is allowed.
   3. Use one: the slot count in Site Management rises by 1 and the item is gone.
