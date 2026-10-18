# Membership and the trial key

How the client keeps a player's membership, what live Landmark did with it, and what this server does
(`LandmarkEmulator.WorldServer/Game/Membership.cs`, `AuthServer/Zone/LoginQueue.cs`). Client addresses are the
**0.695** build (`Landmark64.exe`, image base `0x140000000`); the packets are the same in 0.589. Full trace with
evidence: `research/trace/0695/agent-membership.md`.

## The client's membership state

| Where | What |
|---|---|
| Local player data `+0xfc` (u32) | Membership *type*: 0 none; non-zero = member (Lua `Ui.IsMember`, FUN_1435d2ec0); 1 = trial (`Ui.IsTrialMember`, FUN_1435d2fa0); `Ui.ShouldShowMemberNagScreen` (FUN_1435d36a0) is false for 2 and otherwise returns the SendSelf nag flag (`+0x108`). The debug dump FUN_142f509b0 prints it as "Player membership type". |
| Entity `+0x1452` bit 0 | "Member" bit on any player entity, set by FUN_142ec2ff0. No reader of the bit has been found. |

Nothing in the 0.695 user interface (window scripts, `Resources/Scripts`, SWFs) calls `Ui.IsMember` /
`IsTrialMember` / `ShouldShowMemberNagScreen`. The shared engine reads the type in its store (bundle price for
members, FUN_143c2c370 / FUN_1436fb5c0), activity list (member-only activities, FUN_14371f3b0) and a requirement
check (FUN_143711350); Landmark used none of them. No item in the live item dump has the members-only flag
(definition `+0x0C` bit `0x20`, tooltip `ItemEquipRequirementMembership`).

## Packets

All little-endian; `bool` is one byte.

### SendSelfToClient (0x0F)

The membership type is the u32 after the "dead at login" bool, then the Player Studio flag, then the nag-screen bool
(`SendSelfToClient.MembershipStatus`, `ShowMemberNagScreen`; see sendself.md). The local entity's member bit is set
from it when the player is created (FUN_142cb1f60, no effect).

### PacketMembershipActivation 0x66 (server → client)

```
u16 0x66
u32 type        // 0 none, 1 trial, 2 member
```

Reader FUN_142c4a7c0 (the packet must be consumed exactly); zone dispatcher FUN_142d1ccf0 case 0x2c. The client
stores the type (FUN_142fd3bd0 → `+0xfc`), sets its own entity's member bit with the "show" flag (FUN_142ec2ff0: the
effect id is looked up by the hash 0x782d2049, which the 0.695 `StringHashToValue.txt` does not define, so nothing
plays), calls Lua `MembershipHandler:OnMembershipActivated(type)` (no such handler in the 0.695 scripts) and sends
ActivityListRefreshRequest twice (below).

### PlayerUpdate 0x26 sub 0x35 MemberStatus (server → client)

```
u16 0x26, sub 0x35 (u32 in 0.695, u8 in 0.589)
u64 entity guid
bool member
```

Dispatcher FUN_142cdfa60 case 0x34, reader FUN_142c4f210 → FUN_142c25ad0. Finds the entity (FUN_142f85390) and
sets its member bit (FUN_142ec2ff0 with the effect flag).

### PlayerUpdate 0x26 sub 0x58 AddLightweightPc

The three bools after the scale (reader FUN_142c26e50 `+0x114..+0x116`) are: underage (entity `+0x1453` bit 0x08),
**member** (FUN_142ec2ff0 without the effect), and an unidentified flag (entity `+0x1453` bit 0x80).

### ActivityBase 0xA0 (client → server)

```
u16 0xA0
u8  1
u8  kind        // 4 = ActivityListRefreshRequest (no body), 2 = JoinActivityRequest (u32 activity id, ...)
```

Header writer FUN_142c12c10, sender FUN_142c41640. The refresh comes after every 0x66 and when leaving a mini game
(FUN_14331a850). The server answers it with the activity list it sends at login.

### Login queue status (login server tunnel type 3)

`IsMember`, `MembersInQueue` and `NonMembersInQueue` (layout in `QueueStatus.cs`). Members wait ahead of non-members.

### Not used by this server

| Packet | Why |
|---|---|
| PacketClientMembershipVipInfo 0xB3 {u64 guid, u32, u32, u32} | Loyalty / VIP ranks (entity `+0x14a4..+0x14ac`, Lua `MarketplaceHandler:OnLoyaltyInfo`, not defined in 0.695). Landmark had no VIP ranks. |
| cPacketIdZoneMembershipVipInfo 0xB4 | Only in the client's packet-name table; no reader found. |
| AdminCommand ActivateMembership (0x1E sub 0x452) | Only in the packet-name table; no client command sends it. |
| UramApi Preview/PurchaseMembership | The real-money store; nothing on this server is paid with money. |

## Live Landmark

- Landmark was buy-to-play. Daybreak's All Access membership gave nothing in Landmark (fandom wiki "Daybreak Cash").
- 29 Aug 2016: the update notes gave every player a single-use 10-day trial key to share with a friend, sent by in-game
  mail. The letter is in the client: text 577594 "Invite your friends!" and 577595,
  which gives a code ("LandmarkTrial") to enter on landmarkthegame.com for "a one-time free trial".

## This server

- **State**: the account (auth database `account`: `memberType`, `memberUntil` (UTC), `trialKeyMailed`,
  `trialRedeemed`). A membership counts until `memberUntil`.
- **Sent**: the type in SendSelf; 0x66 whenever it changes (purchase, trial, admin, end — checked every 30 s);
  MemberStatus to the players in the same landscape when a player becomes or stops being a member; the member bool in
  AddLightweightPc.
- **Login queue**: members first.
- **Buying**: `Membership.Purchase(session)` charges `MembershipPriceLumens` Lumens for `MembershipDays` days, added
  after the current membership. Players can use `/membership buy`; `/membership` shows the state. Admins:
  `/membership grant <name> <days>`, `/membership revoke <name>`.
- **Trial key**: every account is mailed one key ("Invite your friends!", item 900501, tradeable) on its first world
  entry while `TrialKeys` is on. Mailing it delivers item 900502 (NO_TRADE), so a key is mailed once; it can't be mailed
  to the sender's own characters. Used from the bag, 900502 gives the account `TrialKeyDays` days of trial membership
  (type 1) unless the account used a trial before or is a member already.

| Setting (WorldServer.json) | Docker | Default |
|---|---|---|
| `MembershipPriceLumens` | `MEMBERSHIP_PRICE_LUMENS` | 1950 (0 = not for sale) |
| `MembershipDays` | `MEMBERSHIP_DAYS` | 30 |
| `TrialKeys` | `TRIAL_KEYS` | true |
| `TrialKeyDays` | `TRIAL_KEY_DAYS` | 10 |
| `MemberMonthlyLumens` | `MEMBER_MONTHLY_LUMENS` | 0 = live (Landmark had no member perks: "Live Landmark" above); e.g. 650 = All Access's 500 Daybreak Cash × 1.3 |
| `MemberStoreDiscountPercent` | `MEMBER_STORE_DISCOUNT` | 0 = live; e.g. 10 (percent off Lumen store prices for members and trials) |
