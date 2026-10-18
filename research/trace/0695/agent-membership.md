# TASKS 3.12 — membership and the trial key (agent report, 2026-10-17)

Branch **`e-membership`** (worktree `<workspace>\wt-member`), commits `66a02b2` (code), `64e1d85` (docs), `5a699d0` (comment references). It is
not merged or pushed. `dotnet build LandmarkEmulator.sln -v q` passes. Nothing was started, stopped or written to a
database. Repo doc: `LandmarkEmu/docs/membership.md`. Test probe: `re/test/member_probe.js` / `.py` (read-only).

User decision (2026-10-17): "the trial key is an item for membership: keep it so players can mail it once to a friend,
and make sure membership can be purchased in the store with Lumens."

Tags: **[P]** proven from the client (decompile, data, texts) or a developer post; **[I]** inferred, with the reason
next to it; **[W]** from the wiki or forum players.

---

## 1. Summary

- **The client has a complete membership state**, and it is the shared engine's (PlanetSide 2 / EQN). A membership
  *type* sits in the local player data (+0xfc): 0 none, 1 trial, 2 member. Each player entity has a member bit
  (+0x1452 bit 0). Three server packets feed it: SendSelf, **PacketMembershipActivation 0x66**, and
  **PlayerUpdate 0x35 MemberStatus**, plus a bool in **AddLightweightPc**. All the layouts are proven.
- **Landmark itself used none of it.** No 0.695 window or script calls `Ui.IsMember`. No live item has the
  members-only flag. The membership benefits text is PlanetSide 2's. The wiki says All Access gave nothing in
  Landmark. The client's membership screens and the UramApi purchase flow belong to the real-money store.
- **Live's trial key was a website code.** It came in an in-game letter that is still in the client (texts 577594 /
  577595). The code ("LandmarkTrial") was entered on landmarkthegame.com to get "a one-time free trial". The client
  has no key item.
- **What I built:**
  - Membership is an account state in the auth database.
  - The server sends it the way the client reads it.
  - Members get priority in the login queue (a real client concept: "Member priority in queue").
  - `Membership.Purchase` buys it with Lumens. This is the hook for the store branch, and `/membership buy` uses it
    too.
  - The trial key is an item in the "Invite your friends!" letter, mailed once per account. Mailing it to a friend
    turns it into a NO_TRADE copy, so a key travels by mail only once. Using that copy gives the friend 10 days of
    trial membership, once per account.
  - Four settings control price, period and the trial key.

## 2. Live: what membership and the trial key were

| Fact | Evidence |
|---|---|
| Landmark was buy-to-play; Daybreak Cash (DBC) bought Lumens and store items. | wiki `Daybreak Cash.wiki`, `Lumens.wiki` [W] |
| All Access (Daybreak's cross-game membership) gave **no** benefits in Landmark. | wiki `Daybreak Cash.wiki`: "while Landmark is still in a beta phase, it does not have any of the normal All Access benefits" [W]; SOE 2014 "The Business of Landmark": the benefits "have yet to be determined" [P dev]; a player, Apr 2015: "Landmark is not part of All Access" (`2015-04-16-54307-upcoming-crafting-harvesting-changes.md`) [W] |
| All Access elsewhere: a monthly plan with a 500 SC stipend. | forum (2014 "The Business of Landmark" thread, a player) "the monthly 500 SC allowance … all access premium accounts" [W]; client text 557395 (PS2 benefits: "500 SC every month", cert / XP / resource bonuses, "Login queue priority") [P text, other game] |
| 26 Aug 2016: "A Landmark trial key to send to your friends is coming soon! You should receive details via in-game mail". | P6398, `design/forum/all/2016-08-26-57889-…` [P dev] |
| 29 Aug 2016: "A single-use, 10-day trial key is now available for you to share with your friends, enemies, complete strangers … You should receive an in-game mail with details when you log in". | P6417, `design/forum/all/2016-08-29-57902-…` [P dev] |
| 2 Sep 2016: "Pass them this information and they can give it a try for free and see if they like it." | `2016-09-02-57925-week-end-wrap-up…` [P dev] |
| The letter: subject 577594 "Invite your friends!"; body 577595 "Greetings, Luminary! … free 10-day trial! Enter "LandmarkTrial" at landmarkthegame.com in Account Management for a one-time free trial! …". | `re/out/text_695.tsv` [P]; 0.695-only (CLIENT-DIFF.md) |
| No key item exists in the client. | `gameData695/items.json` (2,885 items) / `items_summary.tsv`, `re/out/item_names.tsv`, text search for trial / key / token / voucher / pass / invite [P: not found] |
| What a trial account could and couldn't do on live. | not found yet (lead M7) |

So on live, "membership" in Landmark meant nothing beyond the account owning the game. The trial key gave a
non-owner's new account 10 days of access. On this server everyone plays free, so the user's design gives the trial
key's 10 days as *membership* instead.

## 3. The client's membership system (0.695 decompile)

### 3.1 State

| Where | Meaning | Evidence |
|---|---|---|
| Player data `+0xfc` u32 (object at `DAT_145431a20`, = zone client `[0x629e]+0xf18`) | Membership type. Lua `Ui.IsMember` = `!= 0` (FUN_1435d2ec0); `Ui.IsTrialMember` = `== 1` (FUN_1435d2fa0); `Ui.ShouldShowMemberNagScreen` = `type != 2 && +0x108` (FUN_1435d36a0). Debug dump FUN_142f509b0 prints "Player has any membership" / "Player membership type=%d". The crash report FUN_143119140 sends it as "IsMember". | [P] (`re/out/luabind_695.tsv` rows 223/224/228) |
| Player data `+0x108` u8 | Nag-screen flag (SendSelf `ShowMemberNagScreen`). | [P] |
| Entity `+0x1452` bit 0 | Member bit on any player entity. Set by FUN_142ec2ff0(entity, member, showEffect), which then calls the nameplate colour refresh FUN_142ecc900 (it never reads the bit). With showEffect, a newly set bit would play the effect whose id is the StringHashToValue entry hashed 0x782d2049. That hash is not in the 0.695 `StringHashToValue.txt` (`re/tools/strhash.py` → "?"), so the id is 0 and nothing plays. | [P]; nothing found yet that reads the bit (lead M3) |
| Type meaning beyond 0/1/2 | "Player membership type" is an int. 2 is the one type that never nags. Whether 2 = paid and other values (e.g. lifetime, `stationCashGrantedForLifetimeMembershipUpsell`) existed is not known. | [I] 2 = member (lead M6) |

Callers of FUN_142ec2ff0 (all four found):
- 0x66 handler (local player, show = 1).
- MemberStatus handler (any entity, show = 1).
- AddLightweightPc (other players, show = 0).
- Local player creation FUN_142cb1f60: `+0xfc != 0`, show = 0.

### 3.2 Packets

| Packet | Dir | Layout | Client | Status |
|---|---|---|---|---|
| **SendSelfToClient** 0x0F | S→C | …, bool IsUnderage, bool, bool IsDead, **u32 membership type**, bool PlayerStudioRegistered, **bool nag**, … | already decoded (`SendSelfToClient.MembershipStatus` / `ShowMemberNagScreen`) [P] | now **sent** with the account's type |
| **PacketMembershipActivation** 0x66 | S→C | `u16 0x66, u32 type` [P] | zone dispatcher FUN_142d1ccf0 case 0x2c, reader FUN_142c4a7c0 (u16 +8, u32 +0x10; rejects leftover bytes). Effects: FUN_142fd3bd0 stores `+0xfc`; FUN_142ec2ff0(local entity, type != 0, 1); Lua `MembershipHandler:OnMembershipActivated(type)` (no handler in 0.695 `Resources/Scripts` or `UI/Default/*.lua` → only a Lua error log); ActivityListRefreshRequest sent twice (channels 0 and 1). | **sent** on every change |
| **PlayerUpdate MemberStatus** 0x26 sub 0x35 | S→C | `u16 0x26, u32 0x35 (0.695; u8 in 0.589), u64 guid, bool member` [P] | PlayerUpdate dispatcher FUN_142cdfa60 case 0x34, reader FUN_142c4f210 → FUN_142c25ad0 (u16 +8, u32 +0x10, u64 +0x18, bool +0x20; rejects leftover bytes); FUN_142f85390 finds the entity; FUN_142ec2ff0(entity, member, 1). | **sent** to the landscape when a player's member state flips (`PlayerUpdateMemberStatusWire` 0x260035; the gateway widens the sub for 0.695) |
| **AddLightweightPc** 0x26 sub 0x58 | S→C | after f32 scale: `bool underage (+0x114), bool member (+0x115), bool ? (+0x116)` [P] | reader FUN_142c26e50; dispatcher FUN_142cdfa60 case 0x57: +0x114 → entity +0x1453 bit 8, +0x115 → FUN_142ec2ff0(entity, b, 0), +0x116 → entity +0x1453 bit 0x80 | member bool now **set**; the other two stay 0 (lead M2 for +0x116) |
| **ActivityListRefreshRequest** 0xA0 | C→S | `u16 0xA0, u8 1, u8 4` [P] (header writer FUN_142c12c10 + sender FUN_142c41640 byte of +0x18) | sent after 0x66 and when leaving a mini game (FUN_14331a850) | now **answered** with the login activity list (`ActivityHandler`) [I: live's answer not captured, lead M8] |
| Login queue status (tunnel type 3) | S→C | i32 place, i32 members, i32 non-members, i32, i32, u8 IsMember [P, `QueueStatus.cs`] | 0.695 data source FUN_14368b240 ("IsMember", "MembersInQueue", "NonMembersInQueue", EstimatedMemberWaitTime / EstimatedWaitTime) | members first, real counts |
| LoginReply (login server) | S→C | `… u32 result, bool IsMember (+0x1c), bool IsInternal …` [P, FUN_143a57cb0] | where the client uses it: not traced (lead M1) | set to the account's membership [I] |
| PacketClientMembershipVipInfo 0xB3 | S→C | `u16, u64 guid, u32 a, u32 b, u32 c` [P] (reader FUN_142c49fb0 → FUN_142c23900) | case 0x4e: entity +0x14a4 = a (VIP rank [I]: the local player's +0x14a4 goes to FUN_14370e530, part_0118), +0x14a8 = b, +0x14ac = c, nameplate refresh; for the local player Lua `MarketplaceHandler:OnLoyaltyInfo(a, …)` (not defined in 0.695) | **not sent**: loyalty / VIP ranks, none in Landmark [I: no VIP rank in any Landmark text, patch note or the wiki] |
| cPacketIdZoneMembershipVipInfo 0xB4 | ? | – | only in the packet-name table (part_0107 FUN_1434c7940 0xb400); no reader found yet | lead M5 |
| AdminCommand ActivateMembership 0x1E / sub 0x452 | C→S? | – | id table only (`0x452001e00` CommandBase, `0x452001f00` AdminBase); no slash command or writer sends sub 0x452 (searched `0x452` in decomp_all_695) | not handled (lead M4); our admin path is `/membership grant` |
| UramApi Preview/PurchaseMembership, OnMembershipScreenOpened/Closed | – | – | the real-money store (FUN_143648370 "Successfully previewed membership purchase…", FUN_143a23540 `stationCashGrantedForLifetimeMembershipUpsell`, MarketplacePortalScreen log-only FUN_1435ecc90 / cb0) | not used: nothing is paid with money (verdicts 6.59 / 6.60) |

### 3.3 Other readers of the type (shared engine, unused by Landmark)

| Reader | What it does | Why Landmark never reached it |
|---|---|---|
| FUN_1436fb5c0 / FUN_1436fb610 → FUN_143c2c370 | Sorts store bundles by price; members get the member-sale price (+0x1dc) when the bundle has the member-sale flag (+0x1ec) [P] | The old in-client Marketplace SWFs (MarketPlaceHome / DefaultListView / MyHome) ask for the columns IsMemberOnly, IsMemberOnlyItemOfTheDay and MemberSalePrice (FUN_1436fcd40 names them; FUN_143c2c730 `flag_is_member_only`). Landmark's Showcase was a browser page; no bundles reach the client |
| FUN_14371f3b0, FUN_14331c580 | Activity list: activity flag +0x2178 & 0x40 or +0x21a8 and not a member → state 6 [P] ("member-only" [I]) | Landmark sends an empty activity list |
| FUN_143711350 | Requirement check: `+0x2c` 1 = is member, 2 = is not member [P]; vtables 0x144740ff0 / 0x144743b28 (no RTTI name) | no data table found that uses it (lead M3) |
| FUN_142e452b0 | Item tooltip: "ItemEquipRequirementMembership" when definition +0x0C bit 0x20 [P] | no item in the live dump has the bit (flags[0] values: 0 / 8 / 128 / 136 / 192 / 200) [P] |
| FUN_142f509b0 | "OfflineSecMaxMember / NonMember", "Skill point rate tier" | PS2 cert points; no Landmark skill points |
| Texts | 560799 "Requires Membership" (MembershipRequired), 560757 CoinStoreTransactionResultFail_NotAMember, 560800/560801 quest member limits, 62067/62068/62071 queue texts, 6378 / 557342 / 557372 / 557382 / 557395 "Premium Member" texts | shared engine |

**UI paths:** none in 0.695 reach the type.
- `Resources/Scripts/*.lua`: only `soundhandler.lua` names `UI_Marketplace_Membership_Grant` (a sound id).
- `UI/Default/*.lua`: nothing.
- `UI/ScriptsBase.bin`: only the same two sound names.
- The SWFs in `UI/Default`: only the three Marketplace views' columns listed above.
- 0.589 AS3 export: `Hud` PlayerInfoRow.isMember (data source "IsMember", unused) and LoginQueueRow / ZoneQueueStatusView
  (PS2 zone queue).
- The login queue dialog (LoginQueueDialog.swf) shows QueuePosition and EstimatedWaitTime only.

## 4. What was built (branch e-membership)

| File | Change |
|---|---|
| `LandmarkEmulator.Database.Auth/Model/AccountModel.cs`, `AuthContext.cs` | `MemberType` (tinyint unsigned), `MemberUntil` (datetime, UTC, null), `TrialKeyMailed`, `TrialRedeemed` (tinyint(1)); `MemberTypeAt(utcNow)` |
| `Migrations/20261030100000_AccountMembership.cs` + snapshot | hand-written migration (4 columns on `account`) |
| `AuthDatabase.cs` | `UpdateMembership`, `MarkTrialKeyMailed` (conditional update: true only for the call that set it), `GetMembership` |
| `WorldServer/Game/Membership.cs` (new) | the system: types, `Purchase`, `Offer`, `TypeOf` / `IsMember`, SendSelf value, world entry (trial key letter), 30 s end check, 0x66 + MemberStatus, trial key rules, `/membership` command |
| `Network/Handlers/ActivityHandler.cs`, `Network/Message/Model/ActivityRequest.cs` (new) | ActivityBase 0xA0 C→S: refresh answered, join logged |
| `Network/WorldSession.cs` | SendSelf `MembershipStatus`; AddLightweightPc `IsMember` |
| `Network/Message/Model/PlayerUpdateAddLightweightPc.cs` | the three bools after the scale named (underage, member, unknown) |
| `Network/Message/ZoneMessageOpcode.cs` | `PlayerUpdateMemberStatusWire = 0x260035` |
| `Game/Inventory/ItemDefinitionBuilder.cs` | trial key items 900501 / 900502 |
| `Game/ItemUses.cs` | using a trial key → `Membership.UseTrialKey` |
| `Game/Mail.cs`, `Game/Social/RemoteMail.cs` | compose: a trial key may not go to the sender's own account; a mailed key is stored as 900502 |
| `Game/PlayerManager.cs`, `HostedService.cs` | `Membership.Enter` at world entry; `Membership.Initialise` (command) at start; `Membership.Update` in the tick |
| `AuthServer/Zone/LoginQueue.cs` | members first (DB read when joining the queue); real member / non-member counts; per-player IsMember |
| `AuthServer/Network/Handlers/LoginHandler.cs` | LoginReply `IsMember` = account membership [I] |
| `WorldServerConfiguration.cs`, `docker/world/docker-compose.yml`, `.env.example` | settings below |
| `docs/membership.md`, `docs/README.md` | documentation |

### Settings

| WorldServer.json | Docker env | Default | Source |
|---|---|---|---|
| `MembershipPriceLumens` | `MEMBERSHIP_PRICE_LUMENS` | **1950** (0 = not for sale) | [I] All Access was US$14.99 a month. That price is not in the archive (it comes from outside knowledge; lead M9). 1499 DBC × 1.3 Lumens per DBC (the e-store branch's `LumensPerDaybreakCash`: the Lumen Box's 650 Lumens for 500 DBC), rounded up to 5 Lumens |
| `MembershipDays` | `MEMBERSHIP_DAYS` | **30** | [I] All Access was monthly |
| `TrialKeys` | `TRIAL_KEYS` | **true** | live sent everyone a key (P6417) |
| `TrialKeyDays` | `TRIAL_KEY_DAYS` | **10** | [P] "single-use, 10-day trial key" |

### Rules (what the code does)

- **Type in force:** `MemberType` while `MemberUntil > now`, else 0. A purchase or grant adds days after the
  current end; a trial that is bought over becomes type 2 and keeps its remaining days [I].
- **Client told:**
  - SendSelf at login, with the account's type.
  - 0x66 when the type changes: purchase, trial, `/membership grant` / `revoke`, or the end (checked every 30 s, with
    "Your membership has ended." in chat).
  - Text 557382 "Thank you for becoming a Premium Member!" (ShowSystemMessage) when a membership starts [I use].
  - MemberStatus to everyone in the same landscape when the member bit flips.
  - AddLightweightPc's member bool on introduction.
- **Login queue:** members wait ahead of non-members, each group in arrival order. The status packet carries the
  real member / non-member counts and the player's own IsMember.
- **Trial key** (items: name 577594 "Invite your friends!", description 557342 "Become a Premium Member!" [I: no
  client text describes a key], icon image set 10783 Giftbox02 [I], template 3711, stack 1):
  1. On an account's first world entry while `TrialKeys` is on, one character gets a letter from "Landmark Team" [I]
     with subject 577594, our body text (live's wording, minus the website), and key **900501** (tradeable).
     `account.trialKeyMailed` is set atomically, so it happens once per account across worlds.
  2. 900501 used from the bag: a chat line says it is for a friend; the key stays.
  3. Mailing 900501:
     - Refused with mail result 2 (`SelfWithAttachment`) when a recipient is one of the sender's own characters.
       The remote mail path can only check names this world's character DB knows.
     - Otherwise it arrives as **900502** (NO_TRADE). `Mail.CheckBag` / `Attach` already refuse NO_TRADE items, so it
       can't be mailed, traded or put in a claim chest again.
  4. 900502 used: if trial keys are on, the account has never used a trial and has no membership, the key is used up
     and the account gets type 1 for `TrialKeyDays` days (`trialRedeemed` set). Otherwise a chat line explains why
     and the key stays.
  5. Not blocked: 900501 can change hands in person (trade window, a claim chest with permission, a player vendor)
     before it is mailed. It only becomes usable once mailed, so each key still gives at most one trial.

## 5. Coordination: how membership should appear in the store (branch e-store)

I did not touch `Game/Commerce.cs`, `LandmarkEmulator.CommerceServer`, `LocalCommerceService.cs`, `Store.cs` or
`WebPages.cs`. The e-store branch (read-only look) prices store items in Lumens through `Store.Take` /
`session.ChangeCoins`. To sell membership it needs one catalogue row that is not an item, wired like this:

```csharp
// Listing (show the row only when PriceLumens > 0 and Days > 0):
(uint price, uint days) = Game.Membership.Offer;             // MembershipPriceLumens / MembershipDays
// Name / text: e.g. $"Membership ({days} days)"; icon: image set 1643 icon_currency_sc [I] or 10783.
// Show the buyer's state: Game.Membership.TypeOf(session) (0 none, 1 trial, 2 member),
// session.Account.MemberUntil (UTC).

// Buying (world thread, as Store.Take):
(bool ok, string message) = Game.Membership.Purchase(session);
// Charges the Lumens itself (session.Coins / ChangeCoins), stores the account, tells the client (0x66),
// and returns the text to show. Do not charge separately.
```

- No store price-table entry is needed. If the store wants owner overrides in one place, `MembershipPriceLumens` is
  the setting to point to.
- Membership is not an item, so it must not go through `Store.Take`'s give-item path.
- If the user later wants member store prices (the client's own MemberSalePrice idea), `Membership.IsMember(session)`
  is the check.

## 6. Open leads (nothing here is written off)

| # | Need | Searched | Try next |
|---|---|---|---|
| M1 | What the 0.695 client does with LoginReply IsMember (+0x1c) | FUN_143a57cb0 (XML logger only) | the login reply handler that copies the fields (callers of the LoginReply reader; Frida on the login reply) |
| M2 | AddLightweightPc bool +0x116 → entity +0x1453 bit 0x80 | dispatcher case 0x57 | readers of `+0x1453) & 0x80` in decomp_all_695 |
| M3 | Any reader of entity +0x1452 bit 0 (member bit); the data that uses requirement check FUN_143711350 | grep for `0x1452) & 1`, whole-byte reads (part_0103 use bit 1), `0x1450` dword masks | Ghidra xrefs on the bit; the requirement factory id for vtable 0x144740ff0; RequirementExpressions / Requirements tables |
| M4 | A client sender for AdminCommand ActivateMembership (0x1E sub 0x452) | `0x452` constants in decomp_all_695 | Ghidra scalar search for 0x452 in instruction operands (the decompile can hide it); the 0.589 client |
| M5 | cPacketIdZoneMembershipVipInfo 0xB4: direction, reader | id table only | zone dispatcher case for 0xb4; the 0.589 decompile; RTTI for `PacketZoneMembershipVipInfo` |
| M6 | Membership types other than 0/1/2 (lifetime?) and which live used for paid All Access | Lua bindings, debug dump | the UramApi membership SKU parsing (UMembershipSkuData), FUN_143648370 |
| M7 | What a live trial account could / couldn't do (B2P trial limits); live's letter sender; the forum thread "Announcing the Landmark (10-day) Trial Key" (threads 57888 / 57904) | design/forum (not archived), wiki | archive.org snapshots of the two thread ids (browser, by the main session) |
| M8 | What live answered to ActivityListRefreshRequest | the login capture (one ListOfActivities) | a live capture of a mini-game leave, if any exists in the PacketDumps |
| M9 | Evidence for All Access's price / a DBC price for membership (used for the 1950 default) | forum archive ("monthly 500 SC allowance" only), wiki | archive.org daybreakgames.com/allaccess (2016) |
| M10 | The member effect: the name hashed 0x782d2049 (and 0x8dea741b in FUN_142ecc900) | `re/tools/strhash.py` over StringHashToValue.txt (not found) | other hashed tables in DAT_145431198 (+0x278 / +0x2108 lists), the 0.589 data, PS2 string lists |

## 7. Decisions for the user

1. **Benefits.** Live Landmark gave members nothing, so this branch gives only what the client itself shows: member
   status and login queue priority. Other possible benefits (each is a user decision, none is live):
   - member store prices (the client's MemberSalePrice idea);
   - a Lumen stipend (All Access' 500 SC a month). With a Lumen price this makes a loop unless stipend < price;
   - a longer build site upkeep (players asked for it in 2015).
2. **Price and period**: 1950 Lumens / 30 days are [I] defaults (§4). Change them with
   `MEMBERSHIP_PRICE_LUMENS` / `MEMBERSHIP_DAYS`.
3. **Trial key while already a member**: refused for now, and the key stays. Alternative: allow it and add the 10 days.
4. **Dev world**: trial-key letters write mail and account rows on first login. The project rule says data-writing
   features default off on dev, so set `TRIAL_KEYS=false` in the dev env until it has been tested.

## 8. In-game test steps (main session)

Preparation:
- Back up the auth and character databases.
- Build the dev images from a clean export of `e-membership` (or after merging). The auth migration
  `AccountMembership` applies at start; this **restarts the dev world**.
- Two test accounts, A and B (`test-account.txt` / `test-account2.txt`), each with a character, the 0.695 test copy
  (<0.695 test copy>), `re/dlogin.sh`. Optionally attach `python re/test/member_probe.py` (read-only poll of `+0xfc`,
  `+0x108` and the self entity's member bit → `re/test/member_probe.log`).

1. **Letter.** Log in A. The Inbox shows a new letter from "Landmark Team": subject "Invite your friends!", our text,
   and one "Invite your friends!" gift-box item.
   - Take it: it is in the bag; its tooltip has no "No Trade".
   - World log: "trial key letter sent".
   - Relog A: no second letter.
2. **Sender's copy.** Right-click → Use on the key. A chat line says it is for a friend, and the key stays.
3. **Own account.** If A has a second character, compose to it with the key attached. The compose is refused (the
   client's result 2 text) with the chat line "can't be mailed to your own characters"; the key stays in the bag.
4. **Mail to a friend.** Compose to B's character with the key. Sent; the key leaves A's bag.
5. **Recipient.** Log in B.
   - B gets its own letter (its own key) and A's mail.
   - Take A's key: its tooltip shows **No Trade**.
   - Try to attach it to a new mail: refused (No Trade). The trade window refuses it too.
6. **Trial.** B uses A's mailed key.
   - "Thank you for becoming a Premium Member!" appears, plus the chat line "Trial key used: you are a member until
     …".
   - Probe: `type` 1, `bit` 1.
   - `/membership` (the server adds the command at zone-in) answers "Trial membership until …".
   - World log: "used a trial key".
   - Check the client's own reaction: the probe line, or the client log for the `MembershipHandler` Lua error, which
     proves 0x66 was read.
   - No "Unhandled Zone packet ActivityBase" warning (the refresh is answered).
7. **Second trial refused.** B uses its own letter's key: the sender's copy, so the "for a friend" line. Have A mail B
   a second key, or reuse step 4 with another account: "already used a trial key", and the key stays.
8. **Purchase.**
   - On A, with ≥ 1950 Lumens (`coins` dev command if needed), run `/membership buy`. Lumens drop by 1950; the chat
     says until when; the probe shows `type` 2.
   - With fewer Lumens: "Membership costs 1,950 Lumens; you have …", and nothing is charged.
   - B buys while on trial: the type becomes 2 and the end is the trial end + 30 days.
9. **Other players.** With A and B in the same landscape, `/membership revoke B` as an admin (WorldServer Admins).
   - B: chat "has ended" line; probe `type` 0, `bit` 0.
   - A's client gets MemberStatus (bit only; nothing visible is expected).
   - Relog B: `/membership` says "no membership".
10. **End by time.** Optional: `dbq.sh` set B's `memberUntil` to one minute ahead (a DB write: main session's call).
    Within 30 s of the end: "Your membership has ended." and probe `type` 0.
11. **Login queue.** `MAX_PLAYERS=1` (or the dev "maxplayers full" command). Queue a non-member first, then a member:
    the member's dialog shows place 1 and the non-member's place 2, and the member gets in first.
12. **Switch off.** `TRIAL_KEYS=false` (restart): a new account gets no letter; using a mailed key says "Trial keys are
    switched off". `MEMBERSHIP_PRICE_LUMENS=0`: `/membership buy` says "not for sale".

Not verified in game: everything above (no client run in this task).
