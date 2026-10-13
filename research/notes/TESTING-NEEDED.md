# Things to test in the real client

Newest first. Each item says what was built, what I already verified with the test harness, and what still needs a
person playing (real mouse/keyboard, two players, or judging how it looks). Tick items off or note problems next to them.

## Deleting a site keeps your build — (2026-10-12)
- [ ] Build something on a site, then Site Management → Delete: your materials come back, and Designs has a new design
      named after the site; placing it on another site puts your build back.

## Site screenshots — (2026-10-12)
- [ ] Site Management → Screenshots → take one: the window finishes (close button back), the shot shows in the list;
      "make primary" and delete work; the gallery shows the primary shot.

## Party frames — (2026-10-12)
- [ ] In a party, the Party window shows each member's health and energy bars moving as they fight, their distance,
      and "knocked out" when they die.

## Vaults and stack splitting — (2026-10-12)
- [ ] Place a Rustic Stone Vault on your site and click it: the Vault window opens; drag items between bag and vault;
      walk away and it closes. Your vault holds the same items from any vault prop.
- [ ] Shift-drag a stack (e.g. Lightstones): pick an amount — a new stack appears where you drop it; Ctrl-drag moves
      one; dropping onto the same item adds to it.

## Bag sort, guild ranks / description / tag — (2026-10-12)
- [ ] Inventory (I) → the sort dropdown (e.g. by name): the bag reorders and stays that way after a relog.
- [ ] Guild window (N), as leader: add a rank, rename one, tick / untick its permissions, remove it; set the guild's
      description and tag. A guildmate sees the changes; a member of a removed rank becomes a Member.

## Weapon combat: named attacks, affinity states, armour effects — (2026-10-12)
Every weapon now uses its own two attacks from the game's texts (e.g. Staff of Frozen Sorrow: Shatter / Blizzard) with
its affinity's properties (Fire Burn/Frenzy/Erupt, Water Flow/Surge/Freeze, …) and the armour / accessory effects.
Verified by harness: states land on creatures and on you (frozen ogre, Flow / Supercharge icons on the buff bar).
- [ ] Hold a weapon and Alt-hover it / the attack buttons: names like "Basic Attack: Shatter" with their descriptions.
- [ ] Fight a cavern monster with a few weapons: frozen / burning / knocked-down effects show on it and it stops
      attacking while Frozen, Burning, Electrocuted or Crushed; Crippled ones move slowly.
- [ ] Your buff bar shows Flow / Frenzy / Supercharge / Exalt … while they last.
- [ ] A shield weapon's Power Attack (Flame Bound Bastion, Blight-Warden's Pain): block a hit from the front, the
      monster gets bashed (Burn / Rot).
- [ ] Charged attacks (e.g. Frost Lord's Fury right-click, Staff of Frozen Sorrow left-click): hold the button — the
      charge builds; let go — the attack fires, and a full charge adds its extra (Freeze, Condemn, Surge …).
- [ ] Do attacks feel right (reach, cone width, damage)? The numbers are ours — tell me what feels off.

## Cave Seeker — (2026-10-12)
Right-click the Cave Seeker (or the Automatic Chaos Cavern Sounder) now switches on the game's own cave view: nearby
caves show through the ground in green until you switch it off (it was a chat message before). Verified by harness
(shots re/test/shots/caveseeker_on.png / _off.png).
- [ ] Use a Cave Seeker on the surface: caves glow green through the terrain; use it again: they fade out.

## Daily goals, loot toasts, harvest messages — (2026-10-12)
- [ ] Journal (J) → Daily: three daily goals; doing one (e.g. kill 50 enemies, harvest wood) counts up and pays 30-45
      Lumens; the next day you get new ones (an unfinished one with progress stays).
- [ ] Picking up loot (bags, harvest) shows "Copper Ore x12"-style toasts top-left.
- [ ] Swinging an axe at a rock / a pick in a build site: the game's own centred message ("It is not possible to
      harvest that." / "You cannot harvest here.").

## Outposts, Lost Chests / Crystals, treasure chests — (2026-10-12)
23 Outposts (player-built cavern ruins from the client) now stand in large caverns, each with a faction garrison and
an Elite, a Lost Chest and a Lost Crystal. Glowing treasure chests sit in surface caves and caverns.
Please try:
- [ ] Find an Outpost (Travel → An Underground Adventure, explore): the outpost name shows in chat as you come in.
- [ ] Kill the Elite: does the Lost Crystal light up? Click it out of combat: channel, loot, then the "Eureka!" dialog —
      OK takes you to another Outpost.
- [ ] Open a Lost Chest / treasure chest with a friend within 50 m: both get loot bags.

## Monsters where launch had them — (module audit, 2026-10-12)
The surface now has only Fiery Fungus ("boom shrooms"); the kobold camps are gone. The underground caverns (Travel →
An Underground Adventure) have Chompers, Slaugs, Abominations and Fiery Fungus in the cave's biome colours.
Verified by harness: a random cavern shows Putrid Chompers, Rotten Slaugs, an Abomination and Fiery Fungus; a Slaug
attacked; a fungus next to the player blew up.
Please try:
- [ ] Walk near a Fiery Fungus on the surface: it swells, then explodes and hurts you; shoot one from range instead —
      it blows up without hurting you and leaves dirt/stone.
- [ ] In a cavern: Chompers don't move but spray when you're close; the Abomination spits from a distance and backs
      away; Slaugs run at you. Do they stand on the cave floor (not floating, not sunk)?
- [ ] Standing on the surface above a cavern: nothing underground attacks you.

## Placed Mailbox opens your mail — (module audit, 2026-10-12)
- [ ] Place a Mailbox prop on your site and click it: your inbox opens (as at the Spire's mailbox).

## Spire Local Travel (underground portal crystals) — (UI audit, 2026-10-12)
The Spire's Local Travel now works. Clicking the Spire's Portal Crystal opens the travel window. Underground
there are 81 Portal Crystals in the caves. Clicking one opens the Local Travel map, where you click the Spire or another
crystal to warp there (1 Lumen to the Spire, 2 to a crystal — the client's own price).
Verified by harness: the map shows the Spire icon on the surface layer and the crystals on the cave layer; a warp
through the client's own travel call takes the Lumens and lands at the crystal and back at the Spire.
Please try:
- [ ] At the Spire, click the big crystal: does the travel menu open? Is there a "Local Travel" choice, and does it
      open the map?
- [ ] On the Local Travel map, switch to the cave layer and click a crystal: confirm window with the cost, then you
      arrive at that crystal underground. Do you land in open cave (not inside rock)?
- [ ] Underground, walk to a Portal Crystal, click it: the map opens; travel back to the Spire.

## Site Management: Special Access list — (UI audit, 2026-10-12)
Players you give Special Access now appear by name in Site Management (they were stored but never listed).
Verified by harness: granting MrLawKing access on LawTest's site adds one row to the dialog's list; revoking removes it.
Please try:
- [ ] Site Management → your site → permissions: add a player by name with a level: listed with that level? Change
      the level, remove them: the list follows.

## Voxel Vision (builders on a site) — (UI audit, 2026-10-12)
The site information window's Voxel Vision button opens the Builder List; the server now records who built every voxel
and answers the client's ownership request.
Verified by harness: after building a small cube, opening Voxel Vision lists one builder (LawTest) in the client's
data and the window opens with the Voxel Vision tint.
Please try:
- [ ] Build something on your site, open the site info window (top centre) → Voxel Vision: expand Builders — are
      you listed with a voxel count and colour? Does ticking your name highlight what you built?
- [ ] With a second player allowed to build on your site: both names listed, each highlighting their own blocks.
- [ ] Heal or delete your blocks, reopen Voxel Vision: the count drops.

## Combat feedback: hit marker, damage-direction arrows — (UI audit, 2026-10-12)
The Combat HUD reticle has two pieces of feedback the server never fed: a hit marker flash when your attack lands and
up to three arrows round the reticle pointing at whoever just hurt you. Both come from one combat message the server
now sends with every hit (to the victim with the attacker, to the attacker with themselves).
Verified by harness (scenario hitindicator, 3/3): a kobold's hits reach the client's reticle code with the kobold's
guid; LawTest's hit fires the client's "show reticle hit marker" event.
Please try:
- [ ] Hit a creature: does the reticle flash a hit marker for a moment?
- [ ] Let a creature hit you from the side or behind: does an arrow appear by the reticle pointing at it and fade
      within about a second? (UserOptions HitIndicator=1 is on in the test client.)

## Death and respawn, chat rooms, gathering drops — (audit rounds, 2026-10-12)
Verified by harness: death window Revive Here takes 15 Lumens and revives in place; Flee respawns at your own site's
Starting Location (deathrespawn 3/3); /join room password gives Success and the "entered" line; leaving no longer
gives error 22.
Please try:
- [ ] Die on your own site and choose the free respawn: do you come back at your site rather than a far Spire?
- [ ] Two players: /join a room with a password, chat in it, /leave: messages only reach room members.
- [ ] Mine and chop for a while: occasional Lumens / Adventure Essence / recipe drops; mining inside a build site is
      refused with the client's own text; Harvestron and Goliath Axe give more.

## Combat: power attack Energy, hit impact effects — (Combat round 1, 2026-10-12)
The "right click stops working" report: the client does regenerate Energy (about 20 per second), but our weapon
abilities didn't tell it the attack's Energy cost, so it kept attacking at low Energy and the server answered with an
empty success (no animation). Now the client knows the cost (power 60, combo 70, special 80) and refuses itself.
Every creature hit now plays an impact effect on the target (slash / arrow / burst by weapon, coloured by affinity).
Verified by harness: at low Energy the client's own check refuses (NoMana) and every sent attack is paid; the impact
packet is parsed and a long test effect is visible on the character.
Please try:
- [ ] Hold right-click with a sword: power attacks until the blue bar is low, then roughly one every 3 s as it
      refills. Does the bar visibly drop and refill? Any "not enough energy" feedback?
- [ ] Hit a creature (Chaos Caverns / Underground Adventure) with sword, bow and staff: do you see impacts on it?
- [ ] Floating damage numbers: they need floating combat text on (console: togglefloatingcombattext) — do they show?

## Site access levels, prop rules, upkeep timer, Starting Location — (Claims / Props rounds, 2026-10-12)
Verified by harness: placing a prop on a site in Play Mode is refused with "You cannot place props while the Build Site
is in Play Mode"; logging in on a new day extends the site timer by a day (7 → 8) and mails "Your Build Site active
time has changed!"; a site without a Starting Location gets one in its north-west corner.
Please try (two players for the access ones):
- [ ] Site Management > Upkeep tab: the days shown match the timer (7 on day one, +1 per day you log in, max 90);
      "Your Max Possible Build Sites: 1", "Build Site Unlocks Available: 7".
- [ ] Other player on your site with No Touching: they can't use your Replicator / chests; with Play Around they can
      use the Replicator but not take from chests; Pillage & Plunder: chests too; Let's Decorate: they can move and pick
      up props.
- [ ] Press F5 (Play Mode), try to place or pick up a prop: refused with the Play Mode text.
- [ ] Teleport to your own site from the Travel menu: you arrive at the Starting Location (NW corner; move it if you
      like).

## Build sites: launch size, buffer, refusal texts, delete reply — (Claims rebuild round 1, 2026-10-12)
From the full trace (trace/systems/claims.md): sites are now the launch size 210 × 180 × 210 m (the client's own
constant; existing sites 9 and 20 grew around their centres), the buffer is the client's rule (a new site may not come
within 285 m of another site's centre), refusals use the client's own texts, and Site Management delete no longer
shows "Unable to recover build site" after a successful delete.
Verified by harness: the client's placement cursor is 210 × 180 × 210; after SendAllClaimBasicInfo the client's own
pre-check refuses spots within 285 m of a site centre (measured 380-400 m for a whole site); LawTest using a flag
while owning a site now gets "You have reached your maximum number of Build Sites!" and stays out of placement mode.
Please try:
- [ ] Walk around your site: is the build area now much bigger (about 210 m wide, 180 m tall)? Can you build and
      place props near the old edges?
- [ ] Right-click a Build Site Flag while you already own a site: the max-sites message, no placement mode.
- [ ] Map (M) "Show Claimable Land": do sites show? (the client now gets the site list it asks for)
- [ ] (Needs a character without a site) place a site close to another one: the client refuses with "too close to
      another build site"; far enough away: the confirm dialog.
- [ ] Site Management > Delete on your own landscape: no error text; from another landscape: "must be on the same
      Landscape".

## Player Studio: submit a design, peer review, result mail — commits 656c993, 4ea5527
Verified by harness: product created (status 1, stored); the other client is offered it, gets the "items waiting for
review" popup, the review window data (name, description, creator) parses; approve lists it, reject turns it down;
the owner's client shows the result as Player Studio mail (accepted / rejected text). docs/commerce.md.
Note: the dev server's WorldServer.json has PlayerStudioApprovals / PlayerStudioRejections = 1 so two accounts can
finish a review (default 3).
Please try (two players):
- [ ] Designs window > pick one of your designs > Submit to Player Studio: fill name, description, price (25+), tags
      and submit. Does it say it was submitted?
- [ ] Log the OTHER character in: a popup about items waiting for review appears. Accept it: does the review window
      show the name, description, created date and your creator name? Thumbs up.
- [ ] Back on the first character: open mail. Is there a Player Studio "accepted" mail with the design's name?
- [ ] Repeat with thumbs down (and the rejection box): "rejected" mail.
Not built yet: the listing/search/buy side of the marketplace, screenshots on products, the "my products" list.

## Death, revive, duels, trees — commits 0ce255a, c3c627c, 1d8cfc8, (this)
Please try:
- [ ] Die near another player: does their kill feed show it? (KnockedOut is now sent to everyone.)
- [ ] Right-click a dead player > Revive Player: in 0.589 this probably does nothing (the client can't mark other
      players as down; docs/gameplay.md). Tell me if anything happens at all.
- [ ] /duel <name> next to another player, they type /duelaccept; hit each other: hits land only between you, the loser
      ends at 1 health, "You win!" / "You lost the duel." Walk >150 m apart: forfeit.
- [ ] Trees and plants now get their full prop form when you see them: chop a tree and pick a plant as usual; do trees
      block you when you walk into them?
- [ ] Use a food buff, travel to another landscape: is the buff icon still shown with the right time left?

## Build site voting; guild invites after a relog — commits 5ce7dee, a5f81d8
Found by a new check of how the client reads each packet: the "can I vote for this site?" answer was missing a field,
so the client threw every answer away (now read exactly, 11/11).
Please try:
- [ ] Walk onto another player's build site: does the walk-on window offer voting (and not after you voted)?
- [ ] Get a guild invite and log out without answering; log back in within 5 minutes: does the invite popup come back?

## Build site vendors (player shops) — commit 74826bc
Verified by harness + screenshot: the client's Vendor window opens with the stock; buying 2 took 20 coins and the
seller got a mail "Sold: 2 × Lightstone" with 20 coins; taking items back works.
Please try (two players):
- [ ] Get a "Build Site Vendor" from the Showcase store (Decorations › Build Site Vendor) and place it on your site.
- [ ] Showcase › My vendors: list something from your bag (count and price; 10% fee in coins).
- [ ] The other player clicks the vendor: does the Vendor window list it? Add to Cart, Confirm: coins go down, the
      item arrives, the seller gets a mail with the coins.
- [ ] Try to pick up the vendor while it still has stock: refused ("vendor inventory not empty").
Optional: in ClientConfig.ini set PetitionUri=http://127.0.0.1:19001/#!/help so the Escape menu's Help opens this
server's help page (it opens in your normal web browser).

## Showcase: free store + build site gallery; winner banner — commit 429f110
Verified by harness and screenshots: the Showcase window shows the server's store signed in as the right player, with
icons; Get puts the item in that player's bag; the gallery lists build sites and Visit travels there; the end-of-round
banner says "<name> Wins!" instead of "Team 1".
Setup for your own client: ClientConfig.ini [Marketplace] MarketplaceUrl=http://127.0.0.1:19001 (same for
MarketplaceAssets), and run once: python re/tools/export_icons.py (icons into gameData/icons).
Please try:
- [ ] Click the HUD Showcase button: does "Signed in as <your name>" show? Browse Outfits / Decorations, search.
- [ ] Get an outfit and a decoration (try 10 of one): they appear in your bag; equip the outfit.
- [ ] Gallery tab: your site is listed; Visit a site (with two players, visit the other's).
- [ ] Play a Deathmatch to the end: the banner reads "<winner> Wins!" (Team Deathmatch: "Blue team Wins!" / "Draw!").

## Group invite popup Accept / Decline + creatures after a teleport — next commit
Verified by harness: the invite popup's Decline sends "decline" (no group), Accept forms the group. Before this, the
client never sent an answer (the server's invite was missing two fields) and Accept only worked by accident.
Please try:
- [ ] With two players: /invite Name. The other gets the popup. Decline: the inviter reads "X declined your group
      invitation" and no party window opens. Invite again and Accept: the Party window opens for both.
- [ ] Teleport (Landscapes window, your own site) and then stand still: creatures and trees nearby show up within a
      few seconds. Before, standing still could hide them for up to a minute.

## Guild invitation popup + roster promote / demote / kick — commits 1f49853 and next
Verified by trace: the invited client now builds its "GAQGuildInvite" queue entry (it never did before).
- [ ] Two players: one in a guild invites the other (/guildinvite Name or the Guild window): does the invited player
      get an invitation popup / queue entry with Accept? Accept joins the guild.
- [ ] Guild window (N) roster, right-click a member: Promote makes them Officer, Demote back to Member, Kick removes
      them (leader / officers only; officers can't touch the leader or other officers).

## Boss display for BOSS spawners (crash-safe version) — next commit
Verified by harness: a "Spawner: Ruins: ... BOSS" creature gets the client's boss flag, and travelling / Play Mode off
with it in view no longer crashes (the first version did).
- [ ] Place a BOSS spawner (e.g. "Spawner: Ruins: Barren BOSS (1 Limit)"), enter Play Mode and fight it: does a boss
      health bar / boss marker appear? Travel away mid-fight: no crash.

## Crash fix: travelling while placing a prop — next commit
Verified by harness: travelling / teleporting while a prop placement was still open crashed the client; the server now
cancels the placement first and moves you a second later.
- [ ] Start placing a prop (palette or bag), and without placing it travel (Travel window, death window respawn, or the
      game editor's Go To): no crash; the placement is simply cancelled.

## Bug reports and build site reports — commit b83fb4b
Verified by harness: a bug report from the client's own bug window code is saved as Reports/bugs/<time>_<name>.xml
next to the server (with your location, equipment, client and graphics info as the client sends them).
- [ ] Open the in-game bug report window, submit a report: check the new file in the server's Reports/bugs folder.
- [ ] Inside someone's build site, use "report this build site": a file appears in Reports/sites.
- [ ] Game editor Add on an empty spawn point row now starts placing it for free (no materials needed).

## Site game queues — commit 86b1048
Verified by harness (two test characters): a full game queues the next player; when a place frees they can join.
- [ ] Two players: set a Game Table's Max Players to 1 (game editor), one joins and starts; the other presses Join:
      a queue entry should appear in the action queue (position, wait); the walk-on window shows 1 queued.
- [ ] The first player leaves: the queued player's entry turns into "Join Now"; pressing it joins the game.
- [ ] "Leave Queue" on the entry takes you out of the queue.

## Story creatures: click + walk route + area events — commits 582a14a, ff7e21b
Verified by harness (server side): clicking a story creature runs its spawner's "When a Luminary clicks this
creature..." Links; "Have this creature walk along their route" runs "When this creature is told to walk along
their route..." (link it to a Travel Point's "walk here").
- [ ] In Play Mode, hover a spawned story creature: does the interaction cursor show? Use (click / E) it: do the
      Links from its spawner's "clicked" event run?
- [ ] Linking Area: link its "When a creature enters this area..." to something visible, let a story creature walk
      into it (Travel Point inside the area): does it fire? (Areas now count as a cylinder-like volume, 4 m × scale
      across, 6 m × scale up/down — players entering should still work as before.)

## PvP damage numbers in site games — commit 7e409a0
Not harness-tested (needs two players hitting each other).
- [ ] With floating combat text on (console: togglefloatingcombattext), two players in a Deathmatch hit each other:
      both see the damage number over the one that was hit.

## Fell out of the world → safe point — commit ba7b0cc
Verified by harness (the client's own request): you're moved to the nearest respawn site.
- [ ] If you ever fall through the terrain / out of the world, you should be put back at the nearest respawn site
      within a few seconds instead of falling forever.

## Adventure end → Chaos Cavern; weapon tooltip names — commit 4313057
Verified by harness: completing an adventure sends you to a random underground cavern after the scoreboard.
Please try:
- [ ] Complete an adventure (link a switch to the Adventure table's "Give reward and teleport to a Chaos Cavern"):
      after the scoreboard you should land in a cavern without a hang.
- [ ] Hover a weapon in your bag: its attacks should read Basic Attack / Power Attack (not "Ability Name Not Found").

## Game editor (Game Table) + visible spawn points — commits 4313057, 99c4b0a, 9816440, 27b6fd8
Verified by harness and screenshots: the client's own game editor opens titled with the game ("Deathmatch") with
General Settings / Game Type Settings; Apply, Disable Game / Enable Game, and the prop list's Remove / Go To work;
spawn point props are now visible (they used a model this client doesn't have).
Please try (as the site owner, standing on a site with a Game Table):
- [ ] Open the editor in game: in build mode select the Game Table and use its Settings (the same button that opens
      other props' settings). The game editor should open (dev fallback: `gameedit`, `gameedit off`).
- [ ] Game Settings tab: expand General Settings and Game Type Settings. Do the rows show sensible names and values
      (e.g. Time Limit 600, Point Limit 20, Max Join Time 30)?
- [ ] Press Disable Game, change a value (e.g. Time Limit), Apply, Enable Game, then start a round: does the round use
      the new value? Does the walk-on window refuse joining while the game is disabled?
- [ ] Prop Settings tab: does it list Neutral Spawn Point (required) / Team Spawn Point - team? Place a spawn point:
      does its row get Go To / Remove buttons? Go To teleports you next to it; Remove picks it up into your bag.
- [ ] Add on an empty row: starts placing that spawn point if you have one in your bag (or its materials and recipe).
- [ ] Spawn points (blue/yellow/neutral markers) are now visible on sites: check they look right.
- [ ] Random Node, Timer Node and Counter Node props (story widgets) are now visible when placed (Counter Node borrows
      the Logic Node look: this client has no model of its own for it).

## Hardening (limits) — commit 4b83046
Verified by harness: normal Links / prop settings / story scenarios still pass with the new limits.
Please try:
- [ ] Drag a prop setting slider (e.g. a Timer's length) back and forth quickly, close and reopen the Settings window:
      is the last value kept? (Values are now saved in batches once a second.)
- [ ] Nothing else should feel different. If a Links window ever answers "failed" during normal editing, tell me.

## Site game observers + contest voting — commit after a08d050
Built (not verified with the real UI): ObserverRegistration answers (flag 1 = watching: the client's "game in progress"
dialog, HUD and scoreboard follow the game; flag 0 stops); CanVote answers for contest sites (may vote = not your own
site and not voted yet).
Please try:
- [ ] Walk onto a site with a running game and press the walk-on window's "Observe" button (if shown): does the game
      HUD/scoreboard appear? Leave observer mode again.
- [ ] Walk onto a contest-entered site of another player: does the Recommend button appear and work once?

## Build contests — commit after 236f53c
Verified by harness: the client's contest request now gets a list (one open contest, "Community Showcase", defined in
the server's Contests/contests.json); entering a site via dev saves the entry (Contests/entries.json). The success
message wasn't visible in a screenshot.
Please try:
- [ ] Build Site Management → Contest (enter competition): does the drop-down list "Community Showcase"? Enter your
      site with a description and screenshots: do you get a success message? (Server log: "entered site … ok".)
To add or close contests: edit Contests/contests.json next to the server (State 1 = open, 2 voting, 3 judging, 4 closed).

## Held mining / tool swing rate (still open)
Synthetic input can't hold the mouse in the game, so this needs your real mouse:
- [ ] Hold the left mouse with the pick for ~5 s: does it keep swinging at about one dig a second? (The client sends
      30-60 attempts a second while held; the server paces them. If it feels laggy, tell me.)

## Digging: cleaner hole rims — commit after f0f6cce
Verified by screenshot: on a flat beach a dug hole's rim is now a clean edge instead of a notched ring.
Please try:
- [ ] Dig with the pick on flat ground and on a mountain top: do the hole edges look smooth? Any floating teeth?

## Game Table settings — commit after a953832
Verified by harness: a Deathmatch table's settings window shows Time Limit / Point Limit / Points Per Kill / Penalty
Per Death / Penalty Per Self Kill / Respawn Delay (seen on screen); changing Time Limit and Point Limit through the
client's own setter applied to the next round (120 s, limit 3).
Please try:
- [ ] Open a Game Table's settings in build mode, move the sliders, close; start a round: does the timer start at the
      new time limit? Does reaching the new point limit end the round?

## Zone control — commit after 08f1ae1
Verified by harness: standing on a Control Point takes it for your team in 5 s, it then scores 10 every 10 s; the HUD
shows a control indicator under the timer (filled in the team colour once held).
Please try:
- [ ] Team Zone Control table + 2-3 Control Points; two players on different teams: contest a point (nobody gains while
      both stand on it), take it, watch the team score and the indicator colours change.

## Adventure games — commit after f378b52
Verified by harness: an Adventure Game Table hosts an Adventure; the round puts the site in Play Mode (story runs,
spawner creature appears), a creature kill scores 1, and a switch linked to the table's "Give reward and teleport to a
Chaos Cavern" ends the round as completed; Play Mode ends with it.
Please try:
- [ ] Build a small story on your site with an Adventure Game Table; walk on: the window shows "Adventure – Difficulty
      Tier 1" and Join. Join, wait for the countdown, play through, finish it with the "Give reward" link.
- [ ] Two players: both join, they cannot hurt each other, both score for kills.

## Prop settings window — next commit after 5c51ddd
Verified by harness: opening a Timer Filter's settings sends the request and the client accepts the reply (2 settings);
changing the timer length through the client's own setter is saved; in Play Mode the timer then uses it.
Please try:
- [ ] In build mode open a Timer Filter's / Dice Roll / Damage Node / creature spawner's settings (its menu): do the
      sliders and checkboxes show with names (e.g. "Timer Duration") and the right ranges?
- [ ] Move a slider, close the window, open it again: is the value kept? Relog: still kept?
- [ ] Story Node: set Emote to "wave", type a line in its Text window and Save; link a switch to "Activate
      everything on this node" and the node's "When everything activates" to a creature spawner's "Have this creature
      perform everything"; Play Mode, click the switch: the creature waves and the line appears in chat under its name.
Seen working in the harness: the window shows Mood/Stance/Emote drop-downs, a Text Duration slider, the
"Announce Emotes in Chat" checkbox and the Text editor; the creature's say line reached chat.

## Story tools (Links) and Play Mode — commits c859069, 5c51ddd
Verified by harness: the link editor accepts the server's event/action lists; links save; Play Mode starts the site's
story props; a switch click put out a linked torch, which started a linked timer; a creature spawner's Slaug appeared.
Please try:
- [ ] In build mode, select a placed prop (torch, door, switch, timer, dice, logic filter, spawner), open its menu and
      choose Links. Do the "When this…" events show as readable sentences? Pick one, click a second prop, pick an action.
- [ ] Remove a link from the list; "remove all"; does the list refresh?
- [ ] Press F5 (Play Mode) on your own site: creatures from spawners appear; press F5 again: they go away.
- [ ] In Play Mode click a Metal Switch: linked lights turn on/off.
- [ ] Timer Filter: link something to "Start this timer" and the timer's "completes" to a light; wait 60 s.
- [ ] Kill a spawned creature: does it come back after ~30 s? Do its "dies" links fire?
- [ ] Travel Points: link a spawner's "When this creature spawns" to a Travel Point's "The creature walks to this
      location", and that point's "When a creature arrives here" to a second point: the creature walks point to point.
- [ ] Faction: link a switch to a spawner's "Set this creature's faction to Neutral": it stops attacking you.
- [ ] Starting Location: place one on your site, travel to "My Sites": you arrive on it. In Play Mode, link its "When a
      Luminary teleports to this Build Site" to a light: arriving switches the light.
- [ ] Doors / gates / portcullises / shutters: link a switch's "When this switch turns on" to "Open this door"; in Play
      Mode click the switch: the lever flips and the door swings open (seen in the harness only as client state: door
      animation set 1 at index 0). Does it look right, and is the swing direction sensible for left/right doors?
Note: switches now start OFF in Play Mode (lever down); the first click turns them on.

## Site games (instanced games) — commit 12d2cd0
Verified by harness: game appears on the site, join/leave, HUD title + "Time Left" timer counting, end-of-game
scoreboard opens, respawns at spawn points, 4-round stress without hangs.
Please try:
- [ ] Place a Deathmatch Game Table on your site; walk off and back on: does the walk-on window show the game and a
      Join button? Join, then Leave.
- [ ] With two players: both join; the 30 s countdown starts; hit each other with weapons; kills count on the
      scoreboard (Tab / scoreboard key) and the kill feed; the dead respawn at a Spawn Location after 6 s.
- [ ] Team Deathmatch table: two players end up on different teams; same-team hits do nothing.
Known gaps: winner banner says "Team 1" in free-for-all; no game settings editor; no queues/observers; zone control
points and Adventure story games not yet.

## Teleports / respawns (re-zone hang fixes) — commit 12d2cd0
Verified by harness: 5 round starts + 5 respawns, 3 death-window respawns, cave/Spire teleports: no hangs.
Please try:
- [ ] Die in the world and use the free respawn on the death window: loading screen, then alive at the respawn site.
- [ ] Travel (Landscapes window, Spire, caves) a few times in a row quickly: no freeze.

## Tells, group invites, cast refusals, prop limits (2026-10-12)
Verified by harness: tells 3/3, castfail, proplimits 3/3, groupinvite.
Please try:
- [ ] /whisper (or /tell) someone: only they see "[You] whispers: …"; you see "To [them]: …". Whisper a made-up name:
      "User '…' not found."
- [ ] Invite someone already in a party, or invite twice: the client's own lines ("… is already in a party.",
      "… already has a pending party invitation."). Leave an invite unanswered for 2 minutes: both sides see "expired".
- [ ] Drink a healing potion at full health: refused, and the hotbar slot does NOT start its cooldown.
- [ ] Build Mode on your site: the prop limit line shows "n/50 Chests"; place / pick up a chest and it updates.
- [ ] Let a site lapse (or use Delete): the letter reads with line breaks, no "<br>".

## Build cursors, moving props, revive (2026-10-12)
Not yet run in the harness (waiting for the REA analysis to finish; scenarios propaccess, revivechannel).
Please try:
- [ ] With a friend nearby in Build Mode: you see their tool cursor (box / sphere / selection) move as they build, and
      it disappears when they stop or walk away.
- [ ] Click one of your placed props with the prop tool: the move gizmo opens and you can move / rotate it and save.
      A visitor without "Let's Decorate" gets the refusal instead of a stuck tool.
- [ ] Hold a power attack (charge) for a few seconds, several times in a row: every release lands, no
      "sending packets too fast" in the server log.
- [ ] Tints (2026-10-13 fix): log in fresh, open build mode → Tints, pick a colour and click your stool: it changes
      colour and keeps it after relogging. Character / armour colours should also look right now.
- [ ] Right-click one of your props outside build mode: no menu. Press Tab into build mode and right-click it: the
      prop menu (Reposition, Adjust, Duplicate, Pick up, Delete) appears.
- [ ] Missing voxels: build something on your site, walk far away (out of sight), come back: everything you built is
      still there. Also check whether the voxels that went missing before came back after the relog.
- [ ] Build costs: every material with ∞ in the Materials tray (e.g. Halloween Black, terrain textures) builds without
      using anything; a material with a number builds while you hold that item and the number goes down.
- [ ] Removed / built terrain looks the same from far away and while flying around (coarse copies refresh ~20 s after
      you stop editing).
- [ ] Spam the add / remove tool, including the biggest brush: no stretch where clicks do nothing.
- [ ] Terrain v2 (layers): build, remove, sphere tool on your site; walk/fly away and back; relog. No old shapes
      flickering in, no blocky spheres, no floating chunks; walls visible from far away (coarse copies refresh ~20 s
      after you stop editing). Mine outside your site: the hole shows and heals after ~20 min.

## Build effects seen by other players (2026-10-13)
- With two players near each other, one builds (add, remove, smooth, paint, heal). The other should see the tool's
  effect (sparkle/dust) at the edited spot. Checked only with one client (the server sending it to the builder via a
  dev command); the two-player case needs a check.
- Undo / redo (Ctrl+Z / Ctrl+Y) still works and the Voxel Vision window (if open) refreshes after it.
- Saving a design: the Designs window's voxel bar (CurrentVoxels / MaxVoxels) and the server agree; a design that
  would go over 6,000,000 voxels is refused with the "exceeds max template voxels" message.

## Props (2026-10-13)
- Move a placed prop with the gizmo, then Ctrl+Z: it goes back; Ctrl+Y moves it again. The undo list shows "Move Prop".
- Copy (Select tool) a box with props in it and paste it: the props you have items for are placed with it (turned
  with the paste — check a turned paste looks right), the rest are listed in chat ("Skipped props" + count : name).
  Ctrl+Z removes the pasted props and gives the items back.
- Save a design with props inside the box: the Designs window shows its prop count; placing it places the props.
- Place a Rustic Sign, give it text, open its settings (Text Location / Text Duration), link a switch's "clicked"
  to "...Have this sign display the text." and click the switch in Play Mode: the text shows as a bubble / on
  screen / in chat as chosen.

## Items (2026-10-13)
- Try to attach Lumens or a Build Site Flag to a mail: "can't send NO_TRADE item". Try to trade one: it doesn't go in.
- Put a NO_TRADE item into a chest on someone else's site: the client's "can't place no-trade" message; without
  permission on a chest: "access denied". Moving items in your own bag / equipping still works.
