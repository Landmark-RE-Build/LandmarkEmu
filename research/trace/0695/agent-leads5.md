# Agent leads 5: L168, L170, L137 (research only, 2026-10-18)

Scope: OPEN-LEADS L168 (movement-effect numbers), L170 (melee targeters), L137 (live's My Store follow page and digest
mail). No code, git, game, Frida or Docker. Sources read: design/forum/all + patches (203 threads), design/wiki/pages
(2337 pages), re/out/forum_raw, re/out/text_695.tsv, gameData695 + gameData (0.589) tables, items.json, the 0.695
decompile (re/decomp_all_695 through re/tools/fn.py), and our code (MovementEffects.cs, BootAbilities.cs, Abilities.cs,
AbilityMessages.cs, WeaponCombat.cs).

Marks: [P] = proven (source id / address / row, quote of 15 words or fewer). [I] = inferred (reason given).

**Web status.** WebFetch refuses every web.archive.org URL ("unable to fetch from web.archive.org"). The archive.org
availability API (archive.org/wayback/available) does answer, but it returned no snapshots even for `landmarkthegame.com`
itself. That answer is not reliable, so it proves nothing either way. So nothing in this report comes from archive.org.
The local forum archive was itself built from web.archive.org 2017 snapshots (its "Source:" lines), so the browser route
named in the project notes ("use browser for archive.org") works. That route was outside this task's rules (WebFetch only).

---

## Summary

| Lead | Verdict | One line |
|---|---|---|
| **L170** | **FOUND (mechanism) [P] / live use [I strong]** | Targeters are **client-started from the Abilities CasterInfoReply**: there is a second list after the effects list, `List<ClientExecutedTargeterData>`, read at reply +0x118 and built at cast start by FUN_142e257b0. No S→C 0x27/9 is needed. Our reply writes that list as 0. 2016 melee texts ("all opponents in front of you", "blocking stance") fit the two types the client has, Sweep and Block. |
| **L168** | ADVANCED [P semantics, 2014 numbers; I for 2016 numbers] | Hold vs tap is in InputActionGroupEntries.TRIGGER_ON [P]. Sprint, Messenger's and Breeze are **channelled** (the 0.695 ability texts say "Channel…") [P]. The 2014 costs and cooldowns are the only numbers on record. Row 242 changed 1.4375 → 1.417 between 0.589 and 0.695 [P]. Dash Soles has no movement effect type [P]. |
| **L137** | UNCHANGED for archive.org (blocked) | Locally, only the 27 Aug 2014 note and the 2016 wiki line exist. No forum post quotes a digest mail (subject, sender, hour) or a follow limit [P negative, grep]. |

---

## L170: melee targeters. FOUND: client-started from the CasterInfoReply

### What the client does [P, 0.695 decompile]

1. **The reply carries targeter data.** The CasterInfoReply reader FUN_142e016b0 reads two lists:
   - FUN_142e04680 → reply +0xf8: the ClientExecutedEffectData list (L160);
   - **FUN_142e04720 → reply +0x118** (record +0x100): elements read by FUN_142e03c60. Each element holds a
     `List<Abilities::ShapeDefinition>` (allocator FUN_142e1dab0 sets `SoeUtil::List<Abilities::ShapeDefinition>` vftable).
   The RTTI has `List<ClientExecutedTargeterData>` (rtti_695 1446c91b8).
2. **The client builds them at cast start.** The cast-start function FUN_142e0f810 (the same function that starts the
   movement effects) calls `FUN_142e257b0(manager, record, &castEvent, aimPos)` just after "send cast to zone".
   FUN_142e257b0 walks record +0x100 (next = +0x50). It looks up `elem+4 ^ 0xa142e048` in the targeter ObjectFactory
   (ClientAbilityManager +0x408, hash +0x438/+0x448), creates the targeter, and calls its init (vtable +0x18). Live
   targeters are linked at manager +0x478/+0x480, with the count at +0x488.
3. **Type hashes** (registered in the ClientAbilityManager constructor FUN_142e081c0):
   - **Block = 0xcc82431f** (FUN_142dff8f0 → ClientExecutedTargeterBlock);
   - **Sweep = 0xdeecf7d8** (FUN_142dff9b0 → ClientExecutedTargeterSweep).
   Both classes are built by FUN_14354cba0 / FUN_14354cc20 on the base FUN_14354caf0, which has a per-character
   HitTracker map.
4. **Init** is Sweep FUN_14354eca0 → base FUN_14354ebb0. It stores:
   - targeter +0x38 = the cast event u64 and +0x40 = a u32, both from FUN_142e0f810's local_1f0 / local_1e8;
   - +0x50 = the data element;
   - +0x70.. = the aim position.
   It then schedules itself at the cast time plus **`*(float*)(elem+0x14)`**. That field is a delay or duration in
   seconds [P use, I name].
5. **FindTarget is sent once per new opponent hit.** FUN_14354e800 checks `elem+0x10` (the maximum number of targets;
   0 = no limit) and the HitTracker. It then sends AbilityPacketClientTargeterFindTarget **0x27/0x24** with the hit
   character's guid, the targeter's +0x38 u64 and +0x40 u32, and **`elem[0]`** (an id). ClientTargeterEnd 0x27/0x23
   comes from the end slots (FUN_14354d570 / FUN_14354d640).
6. **The S→C TargeterStart 0x27/9 handler is visual only.** FUN_142e1c440 plays a composite effect (FUN_142dfc010) and
   sets an entity state (vtable +0x250, 2). It creates no ClientExecutedTargeter. **This corrects agent-l140.md**, which
   says targeters are created only from 0x27/9.

**Wire layout of one ClientExecutedTargeterData element** (FUN_142e03c60, in wire order):
- u32 → +0x00 (the id sent back in FindTarget);
- u32 → +0x04 (the type hash);
- u32 → +0x08;
- u32 → +0x0c;
- u32 → **+0x14** (delay or duration, read as a float);
- u32 → **+0x10** (maximum targets);
- u32 ×5 → +0x18, +0x1c, +0x20, +0x24, +0x28;
- then `List<ShapeDefinition>` (u32 count; FUN_142e047c0 / element FUN_142e03e60).

Each **ShapeDefinition** element, in wire order: u32 +8, u32 +0x10, u32 +0xc, u32 +0x18, bool +0x1c, u32 +0x14, then
nine u32 +0x20..+0x40 [P layout]. The meaning of each shape field (radius, arc, length, offsets) is open.

### Did live fill it for melee? [I strong]

- The client has exactly two client-executed targeter kinds, **Sweep** and **Block** [P RTTI].
- The 2016 kit texts describe swept and blocking attacks:
  - 25 May 2016 notes (design/forum/all/2016-05-24-57170): Fiery Cleave "Swing your axe at all opponents in front of
    you"; Decimate "Slam your mace down to damage any opponents in front of you" [P].
  - Corrupting Revenge: "Channel a blocking stance that prevents all damage from the next attack" [P].
  - 11 May 2016 hotfix (57105): Molten Rebuke "continues to block but does not counter projectiles" [P].
  - 26 Apr 2016 hotfix (56999): "the block effect wasn't always correctly blocking damage" [P].
- Text 563159 "Test Roll -> Cone Ability Weapon" (text_695) [P] shows that cone abilities were authored in data.
- So live most likely sent Sweep targeter data in melee CasterInfoReplies, with the client reporting hits through
  FindTarget, and Block for the blocking stances [I]. **No capture proves it.** The two live captures (lightstone 221,
  1098) are not melee [P, agent-l138-c2.md].

### What a build agent should do

- `LandmarkEmulator.WorldServer/Network/Message/Model/AbilityMessages.cs:151` writes `0u // executed targeters`. Make it
  a list written in the layout above (it mirrors how `ExecutedEffects` was added for L160).
- Handle C→S **0x27/0x24 FindTarget** and **0x27/0x23 ClientTargeterEnd** (ZoneMessageOpcode.cs:258-259 already names
  them). On FindTarget, validate it on the server (the target is in range and inside the WeaponCombat cone of
  `Game/Combat/WeaponCombat.cs`), then apply the damage. Keep the server cone as the fallback and anti-cheat check
  (live-default toggle).
- First read the FindTarget writer (vtable 14479a1b8, `Abilities::AbilityPacketClientTargeterFindTarget`, slot →
  FUN_14354cce0 / FUN_14349cc60) with pkt.py to fix the wire field order. Then send one Sweep element with one shape on a
  test swing, and check in the client log that FindTarget arrives.

### Still open (L170)

- ShapeDefinition field meanings: read the Sweep update FUN_14354da40 / FUN_14354d880 (shape walk) and the Block slots
  FUN_14354da00 / FUN_14354ec30.
- What elem +0x08 / +0x0c / +0x18..+0x28 are.
- Whether the server answered FindTarget with anything.

---

## L168: movement-effect numbers

### Hold vs tap [P]: InputActionGroupEntries (gameData695), the boots' input groups

| Group | Boot (item) | Ability | TRIGGER_ID | TRIGGER_ON |
|---|---|---|---|---|
| 81 | Nimbus Boots (1120) | 320 Cloud Burst | 3 (jump) | **1** (press) |
| 82 | Dash Soles (235) | 432 + 807-813 by direction modifier | 17 (sprint key) | **1** |
| 84 | Breeze Boots (2851) | 542 Endless Breeze | 3 | **2** (hold) |
| 85 | Boots of Bounding (243) | 321 Charged Leap | 17 | **2** |
| 86 | Leaper's Launchers (244) | 322 Explosive Leap | 17 | **2** |
| 87 | Neon Blitzing Boots (233) | 327 Electric Charge | 17 | **1** |
| 88 | Adventurer's Sprint Boots (2178) | 326 Adventurer's Sprint | 17 | **2** |
| 89 | Messenger's Sandals (234) | 328 Immediate Delivery | 17 | **2** |
| 153 | Roll Wraps (4406) | 1207-1214 | 17 | 1 |
| 83 | (no 2016 item) | 313 | 3 | 2 |

TRIGGER_ON 2 is used by the two leaps, which are proven charged casts released on key-up (MovementEffects.cs). So
**2 = press-and-hold, ended on release** [I strong], and 1 = a single press.

### 0.695 ability texts [P, text_695]

- 565085/565086 Adventurer's Sprint: "Channel power into your legs to increase your run speed."
- 2016/2017 Immediate Delivery: the same "Channel power into your legs…" text.
- 571704/571705 Endless Breeze: "Channel the endless breeze while in the air to glide at high speeds."
- 565069/565070 Cloud Burst: "Perform a double jump while in the air and temporarily gain increased damage."
- 565071/565072 Charged Leap: "Charge up energy in your legs, performing a powerful leap forward when released."
- 565077/565078 Explosive Leap: "…when released that deals damage to all opponents near you."
- 565094/565095 Electric Charge: "Leap forward a short distance and temporarily increase your run speed."
- 567463/567465 Dash: "Execute a sudden dash in any direction."

### Numbers on record [P, Update Notes 15 Oct 2014, design/forum/all/2014-10-14-49225, Felgon]

| Ability | Cost | Cooldown | Cast | Effect |
|---|---|---|---|---|
| Cloud Burst (Nimbus) | 50 energy | 5 s | instant | double jump, +10 % damage for 5 s |
| Infinite Glide (Zephyr; now Breeze) | 0 energy / s | 0 s | instant | glide while holding space, +5 armour / s |
| Electric Charge (Neon) | 75 energy | 10 s | **0.5 s** | jump forward, then +25 % movement for 3 s |
| Immediate Delivery (Messenger's) | **40 energy / s** | 10 s | instant | +37.5 % movement "while channeling" |
| Charged Leap (Bounding) | 100 energy | 5 s | 0-1.25 s, auto release at 4 s | longer charge = farther |
| Explosive Leap (Leaper's) | 80 energy | 10 s | 0-1.50 s, auto release at 4 s | take-off damage, grows with charge |
| Outrider's Charge (Soles of the Outrider) | 60 energy / s | 10 s | 0 s | +25 % speed, +1 armour / s while channelled |

- Run speed: base 10, Shift sprint 13. After the change, base 13 out of combat (sprint folded in) and 8 inside PvP
  claims (same post; 17 Oct 2014 hotfix 49423: "Run Speed is now correctly set to 13").
- 17 Oct 2014 hotfix: "Increased the angle at which you jump" for Bounding and Leaper's [P].
- 2016 player feedback (2016-05-04-57065): "we are slowed to half speed in combat" [P, player].
- Breeze is used "infinitely" (57064 line 1658, 57065 line 131: "infinitely floating and double jump") [P, player].
- 7 Jun 2016 (57252): "Dash Soles can no longer drain energy without actually activating", so Dash used energy [P].
- 26 Apr 2016 (56999): "Boot crafting costs have been adjusted"; no movement numbers changed [P].
- **No 2016 source gives a sprint percentage, jump height, leap distance or glide speed** [P negative]. Searched: all
  forum threads, forum_raw, and the wiki pages Boot, each boot, Run Speed, Jump Bonus and Controls.

### Table clue [P]

- AbilityParameterDefinitions **row 242** is `Constant 1.4375` in 0.589 (gameData) and **`1.417` in 0.695**
  (gameData695). It is one of only five rows that changed between the builds (the others are 620, 733, 734 and the new
  881). A value tuned in late 2016 points to a live gameplay number in active use.
- 1.417 sits close to the +37.5 % of Immediate Delivery. That fits our current use for 328, but nothing names the row's
  user [I weak].
- Row 429 is `ChargedStatMultiplier` stat 10 (Offense) × 1.3 + 500 × charge. No text references it. Its neighbours 426
  and 428 belong to 2015 weapon texts (565039 Ambush, 565027 Crackling Blade). So it remains only a weak candidate
  for Explosive Leap's take-off damage [I weak].
- Messenger's Sandals: the wiki page says "Item removed from the game during closed beta". Item 234 is still in the 2016
  login item list (items.json) [P], and it is missing from the 2016 wiki Boot list [P].

### Values a build agent should use (Game/MovementEffects.cs, Game/Abilities.cs)

1. **326 / 328 / 542 become channelled casts** [P semantics].
   - Answer with the channelled kind (0x216b5dcb), so the client swaps Sprint → ChanneledSprint and Glide →
     ChanneledGlide (FUN_142e255e0).
   - Do **not** send the CastSuccess that ends the effect (FUN_142e18340) until the client releases the key. Before
     building, confirm which packet the client sends on release of a TRIGGER_ON 2 action (CancelCast vs another sub) by
     reading the input-action release path.
   - This replaces `SprintHoldSeconds = 5` [I], the timed sprint.
   - Sprint multiplier: keep row 36 (1.25) [I]. Nothing better is on record; the old 10 → 13 sprint was +30 %.
   - Breeze: Glide factor row 714 with duration 0 (until landing / release) is fine [I]. 0 energy, 0 cooldown, +5
     armour / s (2014) [P 2014, I that 2016 kept it].
   - 328: keep row 242 [I]; 40 energy / s, 10 s cooldown [P 2014].
2. **320 Cloud Burst:** 50 energy, 5 s cooldown, instant. Add the +10 % damage buff for 5 s, which the 0.695 text still
   promises [P text; 2014 numbers].
3. **321:** 100 energy, 5 s cooldown, charge 0-1.25 s, auto release at 4 s (already built). **322:** 80 energy, 10 s
   cooldown, charge 0-1.5 s.
   - Take-off damage to opponents near the caster when released, growing with charge [P text].
   - Amount: row 429 [I weak], or the WeaponCombat damage model scaled by charge [I]. Make it a server setting with live
     as the default.
4. **327 Electric Charge:** 75 energy, 10 s cooldown, **0.5 s cast time** (ours is instant) [P 2014]. Keep the jump
   forward and +25 % for 3 s.
5. **Energy and cooldowns:** the server can answer them in the CasterInfoReply (cost and recovery fields); the client runs
   Energy itself (MiscHandler 326 note). Put the 2014 values above into the reply and mark them [P 2014, I 2016].

### Still open (L168)

- 2016 magnitudes: sprint %, jump height, leap distance, glide speed. Next: a video measurement (2016 Landmark Live
  streams on Twitch / YouTube, as linked in design/patchnotes-after-0589.md), or a capture.
- **Dash Soles / Roll Wraps movement:** none of the seven ClientExecutedEffect types is a dash [P]. The DesignBased
  states 75 / 149 only carry the input group [P]. Next: the 432 / 807-813 caster-info AnimationId (root motion) and the
  client's ability animation path. Also check whether a weapon "dash" (Trick Shot, Battle Rush) moves the same way.
- Which packet ends a channel on key-up (see point 1).
- Ability 313 (group 83, jump key, hold): no 2016 item carries it. It may be Lunar Treads or Astral Ascension [I].

---

## L137: live's My Store follow page and digest mail

### Local findings

- **27 Aug 2014 Update Notes** (design/forum/all/2014-08-27-46891, lines 58-68) [P]:
  - "On the My Store tab, click "Start Following."";
  - tags can be grouped (both tags required);
  - "Every 24 hours, we'll search…"; an in-game mail plus an e-mail if one is on file.
  - The thread capture has 77 of 137 posts (pages 2-4 not archived); none of the captured replies quotes a digest mail.
- **Showcase.wiki:29** (2016): to follow creators or tags, "using the search feature in My Store" [P].
- **No digest mail text anywhere locally** [P negative]. Searched all 203 forum threads, forum_raw and the wiki for:
  digest, daily mail, "start following", followed creator / tag, new items from, newly approved. No client text names one
  either (text_695 "follow" hits are only 564762 / 564766 camera follow and pet texts). So the subject, sender, hour and
  layout stay [I] as built (SHOWCASE_FOLLOW_DIGEST, hour 10 UTC, sender 563379).
- **Per-player limits:** none mentioned in any source [P negative].
- **Natives** (unchanged, luabind_695.tsv) [P]:
  - FollowCreator FUN_1435e9df0 → PacketInGamePurchaseCommerceFollowCreator;
  - UnFollowCreator FUN_1435e9e70;
  - both over TunnelPacketFromExternalConnection;
  - no tag-follow native, so tag follows were on Daybreak's web side [I, as already recorded].
- About the "202 local threads": those are threads containing "follow" in any sense. The store-follow mentions are only
  the 27 Aug 2014 thread (its post plus quotes) and the wiki line above.

### Still open (L137): next path

- Read the archived store pages with the Browser pane (memory: "use browser for archive.org"). That needs the
  parent's or the user's go-ahead, because this task allowed WebFetch only.
- Targets (from agent-leads2-d2.md):
  - `store.landmarkthegame.com`, `store-main.landmarkthegame.com`, `store.eqnlandmark.com`;
  - `www.landmarkthegame.com/showcase*`;
  - the CDX listing `web.archive.org/cdx/search/cdx?url=store.landmarkthegame.com*&collapse=urlkey`.
- Look for: a My Store "Start Following" form, its JS (FollowCreator / tag endpoints), any follow limit, and the
  2014 forum thread pages 2-4 of 46891, which are missing locally and may quote the first digest mail.

---

## Files

- This report: `<workspace>\trace\0695\agent-leads5.md`.
- Code referenced (not edited):
  - `LandmarkEmu\LandmarkEmulator.WorldServer\Game\MovementEffects.cs`
  - `…\Game\BootAbilities.cs`
  - `…\Game\Abilities.cs` (lines 176-194)
  - `…\Network\Message\Model\AbilityMessages.cs:151`
  - `…\Game\Combat\WeaponCombat.cs`
- Correction for `trace/0695/agent-l140.md` (line 98): the "targeters are created only from S→C 0x27/9" verdict is wrong.
  They are created from the CasterInfoReply's targeter list (above).
