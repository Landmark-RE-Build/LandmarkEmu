# Live rules: what the live game did, and what this server does

A table of the game rules that the live game's own sources state, each checked against the server. Every rule has its
source, the server's behaviour and a status. Where the server did something else although the source was already
known, it was changed to the live rule, behind a setting whose default is live (the settings page and
`.env.example` list them). This page was written in the audit of 2026-10-21, which covered:

- the fandom wiki dump (2,340 pages): every page that states a rule (numbers, limits, durations, who may do what,
  what happens when);
- the official update and hotfix notes from June 2015 to February 2017 (the developers' posts at the top of each
  thread, not the player replies);
- every developer post in the archived forum threads (Q&A threads, feedback threads, weekly wrap-ups).

The earlier passes are not repeated here. They checked the 1,625 update-note items of March to June 2016 (511 of them
state a rule), the wiki's 282 system pages and 1,575 content pages, and a list of 17 wiki gaps. Their open items were
built before this pass; this page names the ones they settled only where a rule touches a fix below.

**How sources are weighed.** The 0.695 client is the January 2017 build. When sources disagree, the latest source
before the 2017 shutdown wins. The client's own data (item descriptions, tables, texts) beats the wiki, which players
wrote and which often kept beta numbers. A developer post beats a player's wiki line of the same period.

**Status:**
- **matches**: the server does what the source says (where it does it is named);
- **fixed**: it did something else and now follows the source (the setting, default = live);
- **missing**: no server code for it yet;
- **ours**: no source gives the number; the server's choice is named so a later find can replace it.

Sources are cited as *wiki Page*, *notes D Mon YYYY* (official update or hotfix notes), *dev D Mon YYYY, thread N*
(a developer's forum post, thread id of the archived forum). Quotes are short; the rest is paraphrased.

## Summary

| | Rules |
|---|---|
| Checked in this pass | 97 |
| Matched already | 82 |
| Fixed in this pass | 4 |
| Missing (no code yet) | 7 |
| Ours (no source for the number) | 4 |

The 97 are the 91 rows of the tables below plus the six missing features listed at the end (competition winners are
both a row and a missing feature). Nine conflicts between sources are listed with the source that was taken.

## Fixed in this pass

| Rule (live) | Source | The server did | Fix (setting, default = live) |
|---|---|---|---|
| Lumens can't be traded between players | dev 14 Mar 2016, thread 56195 (Landmark Wipe Q&A: Lumens "cannot be traded"); players still asking for Lumen trading on 29 Aug 2016, thread 57902 | the client's coin panes are Lumens: a trade's coin offer, and mail's send money / cash on delivery, moved Lumens between players | `LUMEN_TRADING=false`: a coin offer is "your offer is invalid" and stays 0, a trade still holding one fails; a mail with coins or cash on delivery is refused with "can't send NO_TRADE item" |
| Designs bought on Player Studio, and designs on Player Studio, take no design storage | wiki Designs and Build Mode (2016: bought designs "do not count toward the limit"); notes 17 Oct 2014, thread 49423 (submitted designs no longer count against Voxel Storage); the Designs window's "free" column, notes 21 Mar 2016 and dev 6 May 2016, thread 57063 | counted every design but Daybreak's; a purchase was refused when the storage was full | `PLAYER_STUDIO_DESIGNS_FREE_STORAGE=true`: bought copies and designs pending or listed are free (the "free" column shows it), a purchase no longer needs room |
| Designs belong to the account, not the character | dev 14 Mar 2016, thread 56195, and 13 Mar 2015, thread 53904 (designs "are tied to your account, not your character"; a new character gets the account's designs) | designs were per character: a deleted character's designs, and those made of its build sites, were lost | `DESIGNS_FOLLOW_ACCOUNT=true`: at world entry the designs of the account's deleted characters move to the character, which becomes their author where the deleted one was |
| An upkeep login day runs midnight to midnight Pacific time | dev 17 Mar 2015, thread 53943 (each day "12:00:00AM to 11:59:59 PM Pacific Time"; the launch notes of 21 Mar 2016 keep that upkeep) | counted UTC days, so a California evening and the next morning were two login days | `UPKEEP_TIME_ZONE=America/Los_Angeles` (any IANA / Windows zone; unknown = UTC) |

Fixed shortly before this pass, same bug class (listed for the record): mutual friend lists (wiki Friends List; setting
`FRIEND_REQUESTS_MUTUAL`), the 12-hour store trade wait (dev 26 Mar 2014; `STORE_TRADE_WAIT_HOURS`), no Teleport to
Friend cooldown (notes 5 May 2015; `FRIEND_TELEPORT_COOLDOWN_SECONDS`).

## The rules, by system

### Trading, mail, currency

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| Lumens can't be traded | dev 14 Mar 2016, thread 56195 | `LUMEN_TRADING` (above) | fixed |
| A store purchase can be traded only after a wait | dev 26 Mar 2014 (12 h); wiki Trading (12-24 h, unsure) | `STORE_TRADE_WAIT_HOURS=12`, the item tooltip counts down | matches |
| Bought designs and soulbound items are never traded | wiki Trading, Soulbound, Player Studio | NO_TRADE items refused in trade, mail and others' chests; a Player Studio copy can't be made into an item | matches |
| A mail carries at most four attachments | wiki Spire | compose limit 4 | matches |
| At most 9,000 of a gathered material per attachment | wiki Trading | `MAIL_MAX_STACK=9000` | matches |
| Overflow items are lost 24 hours after they arrive | wiki Inventory, Pioneer Landscape | `OVERFLOW_EXPIRY_HOURS=24`, the client's countdown | matches |
| "Retrieve all" puts overflow resources in the resources tab | notes 7 Jun 2016 | Inventory "Retrieve all" | matches |
| Soulbound items go only on your own site and into your own chests | wiki Soulbound | prop placement result 7, chest result 0x20 | matches |
| A non-tradeable item in a chest comes out only for whoever put it in | client text 561999 | chest withdraw result 0x21 | matches |
| Lumen store items and Marketplace purchases are lost with a deleted character | notes 21 Mar 2016; hotfix 13 May 2015 | character deletion keeps no items | matches |
| Item wear survives trade, mail and chests | the server's own wear model (no live repair-free path) | wear carried through all three | matches |

### Friends, party, guild, chat

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| Accepting a friend request makes both players friends | wiki Friends List | `FRIEND_REQUESTS_MUTUAL=true` | matches |
| Teleport to Friend has no cooldown | notes 5 May 2015 (cooldown removed) | `FRIEND_TELEPORT_COOLDOWN_SECONDS=0` | matches |
| A party holds four players | wiki Party | 4, in the world and the social service | matches |
| When the leader leaves, the eldest member leads | notes 2014 (leadership passes to the eldest member) | members kept in joining order, the first takes over | matches |
| Party members within 200 m share harvested materials | wiki Party, Gathering | `PartyShareRange` 200 m; the client's in-range icon is 200 m too | matches |
| Everyone within 50 m of a chest when it opens gets loot, grouped or not | wiki Chests, Party, Caves, Exploring | chest share range 50 m | matches |
| A guild holds up to 250 members | notes 2014; the client's Guild.MaxMembers | 250 in the world and the social service | matches |
| Guild invitations go to online players | wiki Guild | invite needs the target online | matches |
| Officer chat for ranks that may join it | wiki Chat | rank permission JoinOfficerChat | matches |
| General chat is world-wide, across all worlds and landscapes | wiki Chat, User Interface | the social service routes General to every world | matches |
| Custom chat channels survive a change of world | notes 16 Jun 2016 | chat rooms live in the social service | matches |
| Friends don't seem to log off and on when they change world | notes 2014 | presence grace (`SOCIAL_PRESENCE_GRACE_SECONDS`) | matches |
| Friends lists hold 200 | no source | 200 | ours |
| An inbox holds 100 mails; 20 sends per 10 minutes | no source (the client only has the "full inbox" / "sent too many" texts) | 100 / 20 | ours |

### Build sites and upkeep

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| New players have one site; up to eight with expansions | wiki Build Sites, Build Site Expansion (June 2016) | 1, at most 8 | matches |
| A Build Site Expansion can be traded or given away | notes 14 Jul 2016 | the expansion item is tradeable | matches |
| Sites are placed from Site Management, no flags needed | notes 14 Jul 2016 | Site Management placement | matches |
| Sites can be placed on Pioneer landscapes | notes 29 Sep 2016 (replaces the launch rule that only new players' sites went there) | Pioneer landscapes allowed | matches |
| Upkeep: 7 days, +1 for every 2 login days, at most 90 | dev 17 Mar 2015, thread 53943; notes 21 Mar 2016; wiki Build Site Management | `SiteUpkeep` | matches |
| A login day is a Pacific-time calendar day | dev 17 Mar 2015, thread 53943 | `UPKEEP_TIME_ZONE` (above) | fixed |
| A reminder three days before the sites expire | wiki Build Site Management; notes 7 Aug 2014 | reminder letter, once per absence | matches |
| An expired site becomes a design; its resources and props come back | wiki Build Sites, Designs; dev 2014 | site design + Build Site Contents letter | matches |
| A site is deleted only from its own landscape | wiki Build Site Management | refused elsewhere ("must be on the same Landscape") | matches |
| 50 storage chests per site | notes 21 Mar 2016 | 50 per site; `CHEST_ACCOUNT_CAP=0` | matches |
| 125 creature spawners per site | notes 25 May 2016 | 125, shown in Build Mode | matches |
| Graveyards can't be claimed | hotfix 13 May 2015 | placement refused near a graveyard | matches |
| No building inside another site's buffer zone | wiki Build Sites; hotfix 13 May 2015 | buffer refusal (26) | matches |
| One Starting Location per site, free, placed in the north-west corner of a new site | wiki Starting Location | `EnsureStartingLocation` | matches |
| Landmarks of Landmark sites can be edited only by staff | dev 5 Aug 2016, thread 57799 | landmark claims are not editable | matches |
| Only the owner may change the visitors' site time | notes 28 Jul 2016 | owner only | matches |
| Play Mode Rules: grappling hook, boot abilities and voxel destruction on by default, only the character camera | notes 30 Jun 2016 | Play Mode Rules bits, default allowed | matches |
| Dynamite destruction on sites heals after 100 seconds | dev 1 Jul 2016, thread 57596 | `DynamiteHeal` 100 s | matches |
| A Linking Area's "destroy voxels" heals after 10,000 seconds | dev 28 Jul 2016, thread 57751 | 10,000 s | matches |
| No mining or building next to a Portal Spire | client text 560944; notes 1 Apr 2016 | refused with that text | matches |
| No ore nodes around the Spires | notes 7 Jun 2016 | suppressed | matches |
| Gathering only off build sites; dug land grows back after about 20 minutes | wiki Gathering | 20-minute regeneration; no gathering on sites | matches |

### Designs and Player Studio

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| Design storage 6,000,000 voxels, +6,000,000 per expansion, at most 48,000,000 | wiki Designs | expansions and the client's own cap | matches |
| Daybreak's designs (starter castle, design items) take no storage | dev 6 May 2016, thread 57063; wiki Designs | designer designs free | matches |
| Bought and listed Player Studio designs take no storage | wiki Designs; notes 17 Oct 2014 | `PLAYER_STUDIO_DESIGNS_FREE_STORAGE` (above) | fixed |
| Competition-winning designs take no storage | notes 17 Oct 2014 | the server has no winner flag yet (sends ContestWinner false) | missing |
| Designs made by the game (site deleted or expired) may go over the limit | wiki Designs; dev 14 Mar 2016 | site designs are saved without a room check | matches |
| Designs belong to the account | dev 14 Mar 2016 and 13 Mar 2015 | `DESIGNS_FOLLOW_ACCOUNT` (above) | fixed |
| Placing a design needs its materials; props without materials are left out | wiki Designs | paste checks materials; props skipped | matches |
| Lumens can pay for the missing materials of a design, in Lumen Station bundles | notes 14 Jul 2016 | Design Details purchase | matches |
| Only the owner may place, copy or save designs over bought designs on a site | client text 563373; wiki Player Studio | template instances | matches |
| Player Studio: minimum price 25, a listing fee | wiki Player Studio | `StudioRules.MinPrice` 25; `PLAYER_STUDIO_LISTING_FEE` | matches |
| Reviewers are players who spent at least 1,000 Daybreak Cash; two reviews a day; rejected designs go to staff | wiki Player Studio; notes 18 Jun 2014 | `PLAYER_STUDIO_REVIEWER_MIN_SPEND`, `_REVIEWS_PER_DAY`, `_STAFF_REVIEW` | matches |

### Death, combat, creatures

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| Revive Here costs 15 Lumens; Flee is free | wiki Death, Respawn Point | `ReviveCost` 15 | matches |
| Flee goes to the nearest enabled Respawn Point or Starting Location on a site, else a graveyard | wiki Respawn Point; notes 28 Jul 2016 | nearest respawn site | matches |
| After a revive, creatures leave you alone until you fight | notes 26 Apr and 16 Jun 2016 | revive grace, ends on combat | matches |
| Non-elite outpost creatures respawn in 8-10 minutes, elites in 15 | dev 4 May 2016, thread 57065 | 480-600 s, 15 min | matches |
| Elites and very large creatures ignore crowd control | notes 11 May 2016 | statuses skipped, effects still shown | matches |
| A creature killed by a Damage Node drops no loot | notes 7 Jun 2016 | killed with no killer | matches |
| A creature whose health or damage was changed may drop no loot; health can only go up, damage up or down | notes 29 Aug 2016 | spawner rows 100-500 % / 25-300 %; changed creatures drop nothing | matches |
| Fiery Fungus and Proximity Mines can damage terrain on sites | notes 29 Aug 2016 | spawner row "Damages Terrain" | matches |
| The Lop Ear Bunny pet drops rarely from outpost elites; one pet out at a time | notes 28 Jul 2016 | `RareDrops` 2 % (rate ours); one pet | matches |
| Cozy Campfire heals to full at once out of combat, nothing in combat | notes 21 Mar 2016; wiki Cozy Campfires | campfire tick | matches |
| Halloween drops ended on 6 Dec 2016; holiday flight suits and sweaters dropped instead | notes 6 Dec 2016 | `LANDMARK_SEASON` | matches |

### Caverns and outposts

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| The Lost Crystal works only once the outpost's Elite is dead, never in combat; a hit cancels the channel | wiki Lost Crystals | 10 s channel, combat check | matches |
| The crystal's loot lockout is per crystal; a locked crystal only offers the next trip | dev 4 May 2016, thread 57065 | per player and crystal | matches |
| The lockout was made much longer | notes 16 Jun 2016 (no number) | 24 h | ours |
| One Lost Chest per outpost | wiki Lost Chests, Outposts; notes 30 Jun 2016 | one per outpost | matches |
| Underground Adventure from a Pioneer landscape goes to a cavern of another landscape; trips may go to other worlds | notes 30 Jun and 14 Jul 2016, 7 Jun 2016 | trips skip Pioneer lands; `CAVERN_TRIPS_OTHER_WORLDS=true` | matches |
| Chaos Caverns are open world, not instances | dev 4 Mar 2016, thread 56109 | caverns under each landscape | matches |

### Story tools and site games

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| Timer 1-600 s; Counting Node and Scoreboard 0-99; Server Time Node up to six hours | wiki Timer Filter, Counting Node, Scoreboard, Server Time Node | the props' setting rows | matches |
| Story props are seen only in Edit Mode (Scoreboard and Starting Location excepted) | wiki story tool pages | client side; the server sends nothing for it | matches |
| Moods and stances on creatures persist | notes 7 Jun 2016 | `STORY_POSE_PERSISTS=true` | matches |
| Reward chests: per-player cooldown, a count of loots, only in Play Mode, never the owner, the owner mailed with bonus resources when used up, loot for the whole group | notes 29 Aug and 29 Sep 2016 | `RewardChests` | matches |
| Game tables: NPCs count toward the minimum players, not the maximum | dev 30 Sep 2016, thread 57990 | `InstancedGames` | matches |

### Character, items, time

| Rule (live) | Source | Server | Status |
|---|---|---|---|
| Bag 40 slots, +20 per expansion to 100; vault 60, to 120 | notes 21 Mar 2016 (founders' Mega Pocket added 20 / 20 more; see Conflicts) | 40 / 100, 60 / 120 | matches |
| Lightstone: Light Radius +15 for 30 minutes | the 0.695 client's item description (see Conflicts) | +15, 30 min | matches |
| Harvesting potions and similar last 15 minutes | wiki potion pages; client descriptions | 15 min | matches |
| Crimson Parlor changes cost Lumens per option; gender and size can change since 28 Jul 2016 | notes 7 Jun and 28 Jul 2016 | Barbershop | matches |
| Fantasy Parlor Pack 499 Daybreak Cash; Build Site Expansion 799 | wiki Fantasy Parlor Pack; wrap-up 1 Apr 2016 | store prices | matches |
| Trial key: single use, 10 days | notes 29 Aug 2016 | `TRIAL_KEY_DAYS=10` | matches |
| Daily goals: 30-45 Lumens, reset every 24 h, progress kept | wiki Daily Goals | `DailyGoals` | matches |
| A day and night take three real hours (eight times real time) | wiki Astronomy | `DAY_CYCLE_SPEED=8` | matches |
| Servers restart Monday and Saturday at 3 a.m. Pacific | dev 24 Jun 2016, thread 57554 | `RESTART_SCHEDULE` (the owner's choice; live's value given) | matches |
| Likes on banners and in the gallery never decay | notes 7 Jun 2016 | no decay | matches |
| The daily reset hour of goals and loot pacing | no source | UTC midnight | ours |

## Conflicts between sources

| Topic | Sources | Taken |
|---|---|---|
| Pioneer landscapes | wiki Pioneer Landscape: you can never place a site there again; notes 29 Sep 2016: sites can be placed on Pioneer islands like any other | the later notes |
| Friend teleport cooldown | wiki Chests: a cooldown may block it; notes 5 May 2015: cooldown removed | the notes (the wiki line is from 2014) |
| Chest cap | notes 21 Mar 2016: 50 per site; wiki Chests: 50 per account | the notes; the account cap is a setting, off |
| Store trade wait | dev 26 Mar 2014: 12 h; wiki Trading: "12-24 hours" | the developer's 12 h |
| Bag and vault size | notes 21 Mar 2016: base 40 / 60; wiki Inventory, Vault (June 2016): 60 / 80 | the notes: the wiki's numbers are a founder's (the Mega Pocket's 20 / 20 were added to founders' bags at the wipe) |
| Lightstone | wiki Lightstone, Light Radius: +20 for 60 minutes, 30 s cooldown; the 0.695 client's own description: +15 for 30 minutes | the client |
| Resources per material | wiki Inventory: 500,000 per material, then bag stacks; the 0.695 client's InventoryDefinition: 24,000,000 per resource | the client |
| Build site size | notes Mar 2016: 525 × 525 × 450 voxels; the client's placement preview | the client |
| Characters per account | a player's recollection (May 2015) that one slot is all anyone needs; nothing official | the server allows several; open lead (account designs are now shared, so nothing is lost either way) |

## Missing (no server code yet)

- **Competition winners**: no winner flag on designs (the record's ContestWinner is always false), so winning designs
  also don't get the free storage of notes 17 Oct 2014.
- **Voice chat** (wiki Chat; notes 2014: proximity, party and guild channels through Vivox): the client's voice packets
  are only logged.
- **Chest locking and unlocking** (notes 25 May 2016).
- **Player Studio royalties split by voxel ownership** between the contributors of a design (wiki Designs, Player
  Studio): the whole share goes to the product's owner.
- **Player Studio reviewer opt-out and the fee difference on a later price increase** (notes 2014).
- **Showcase follow e-mail** (the in-game mail digest is built; the e-mail is not).
- **Rendered character portraits** (live had a render service; the server keeps the client's uploads).
