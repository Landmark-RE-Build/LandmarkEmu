# Landmarks of Landmark (TASKS 3.20)

Contest-winning player builds that the developers placed beside the Spire of every landscape, on every live world.
Built in `LandmarkEmu/LandmarkEmulator.WorldServer/Game/LandmarksOfLandmark.cs` (+ `LandmarksOfLandmark.Table.cs`);
format notes in `LandmarkEmu/docs/terrain.md` "Landmarks of Landmark". Tags: [P] proven from the client / a dev post,
[W] wiki, [I] inferred (ours).

## 1. Sources

- **Dev list, 3 Aug 2016** (Domino, forum 57799, `design/forum/all/2016-08-03-57799-landmarks-of-landmark.md`) [P]:
  "Landmarks of Landmark are now placed on each island, as follows"; "We tried to place all the 2016 landmark winners,
  and then filled in any empty islands with 2014 landmarks". Lists, for the worlds Courage, Determination, Liberation,
  Rebellion and Serenity, the build on each of the 29 landscapes (24 islands + 5 Pioneer lands). Note at the end: Kuer's
  Elemental Sanctum has an incomplete template (the client ships it: `...elemental_sanctum.otd`, 81 cells, no props; not
  in the list, not placed).
- **Later posts in the thread:** #3 the Serenity / Burning Frostlands claim was *labelled* "100% Prop Free Tower by
  Candela" instead of "Dark Elf Tower of Darkness by Yarnila" (a label error; the build itself was right), so a site's
  name is "<title> by <creator>" [P]; #6 Domino: "when a build site is flagged as a landmark claim (with the fancy blue
  header banner), it becomes un-editable by anyone except internal staff using dev powers", they "will be placed by
  hubs", builders were told to build for "placement on a slope" [P]; #7 "We were instructed to have a 25-voxel base for
  leveling, yet some of the landmarks have been placed on a flat area with the base sticking up" [P]; #2 a prop scaled
  smaller than submitted (the template's own scale is used here).
- **Week End Wrap Up, 5 Aug 2016** (forum 57814): "if you've stopped by a spire lately ... the Landmarks of Landmarks
  placement has been completed" [P].
- **Patch notes 10 Jul 2014** (P1385-P1388): the winners "enshrined in their very own Claim, for all players to see";
  later "contributors will be listed individually ... Click their name, taking you to the Showcase ... Send them
  Feedback" [P].
- **Wiki** "Landmarks of Landmark" / "... A-Z (List)" (design/wiki/pages): placed "typically in close proximity to the
  spires"; clicking the site's title shows no rating and "Landmarks of Landmark" below the title and artist [W].

## 2. The client side (traced 2026-10-16)

- **Templates** (the 0.695 copy and the Steam install ship the same files): 122 `landmarks_of_landmark_2016_winner_-_*.otd`
  (versions 5 / 6, 61 MB) and 99 `lm_<creator>_<title>.otd` (2014, version 4) in `CommonData/Voxel/templates`.
  - A 2016 template is the whole contest box, 175 x 250 x 175 voxels (70 x 100 x 70 m), and its changed mask covers the
    whole box: air everywhere the build isn't. A 2014 template writes only the built voxels.
  - Voxel order inside a template cell is `y + 40*(x + 40*z)` like every other cell (measured: only that order puts
    the builds' solids inside x / z 0-175). `VoxelTemplate.StampAt` used x-first order (`BlockData.Index`); fixed.
  - `...aetherion_tower.otd` (Courage / Lonely Glacier) is damaged in the shipped files: written in text mode, every LF
    became CR LF (1,383 CR LF, no lone LF; 398,961 bytes). Repaired in memory: per cell the stored length is exact and the
    zlib stream has an Adler-32, so the 3 genuine CR LF pairs are found by trial (397,581 bytes, 84 cells, 99 props;
    the C# repair gives the same bytes as the Python prototype).
  - The lowest solid layer, e.g.: The Pinnacle 5, Seacrest Inn 74, Overland Express 142 (the train floats 57 m up in its
    box), Arachna-Mech 1.
- **Award model:** `CommonData/Collision/Theme_Awards_Showcases_LandmarkOfLandmarks01.cdt`, Models.txt 1369, PropDefs
  1735 (name 566223 "Landmarks of Landmark Winner"); item 1836 (description 566688) is the creators' trophy. Whether live
  stood one beside each landmark is not found yet (L111).
- **Admin claim / banner** (0.695):
  - Claim record reader `FUN_142da1a50`; claim entry built by `FUN_142db5ca0`: record +0x158 (the u64 after the u32 that
    follows the named access list) -> entry +0x138; the five strings at record +0x168..+0x1c8 -> entry +0x48 / +0x60 /
    +0x78 / +0x90 / +0xa8 (clamped to 0x80 / 0x80 / 0x100 / 0x20 / 0x400); record +0x1e0 -> entry +0xc0.
  - Walk-on data `FUN_142dc7540`: `AdminTemplateGuid` = entry +0x138; the AS3 (`ClaimWalkOnTextWindow`,
    `getIsAdminClaim`) shows `PANEL_STATE_CONTEST` (`ContestWinnerContent`) when it is not 0. For such a site the same
    function fills `ClaimDialogs.WinnerWalkOnText`: WinnerClaimName = entry +0x68 (string 2), WinnerCompetitionName =
    +0x50 (string 1), WinnerCompetitionDate = +0x98 (string 4), WinnerCompetitionDescription = +0x80 (string 3),
    WinnerFeedbackRecipientGuid = +0xc0, and `FUN_142db82e0(entry + 0xa8)` parses string 5 into
    `ClaimDialogs.AdminClaimContributors`: space-separated, first token a format number; format 0 = one row per
    following name; format 1 = a total, then `name^guid^voxelcount` rows.
  - The panel shows the name, the description (`WinnerCompetitionDescription`), the competition (`CompetitionInfoLabel`
    = WinnerCompetitionName), "Click the contributors to see more of their designs!" and the contributor links.
  - `ClaimsPacketSetAsAdminClaim` (0xF9 / 0x3D, "Claim %llu set as admin claim with template %llu") is how live's
    staff set it (admin-gated in the client). 0.589 has the same entry +0x138 test (re/decomp_all part_0068 line 37265)
    and the same AS3.

## 3. What the server does

- One world's list (setting `LandmarksOfLandmarkWorld` / Docker `LANDMARKS_WORLD`; auto = the live world of
  `WorldNameId`, Liberation when that is 0 (the teleport window's world), any other name Courage; the Docker dev world,
  "Dev Server" 62147, gets Courage).
- Stamped once per landscape right after its Spire is built; marker `TerrainEdits/landmarks_of_landmark.txt`.
- Spot [I]: nearest to the Spire (rings 10 m, up to 1 km) with dry ground at most 6 m uneven under the footprint
  (9 samples), 20 m clear of every site and of the Spire's platform. Height [I]: the build's lowest solid layer on the
  lowest ground under it (live put bases on flat ground "sticking up", #7); template voxels under that layer are not
  written.
- Multi-template entries [I]: in a row along +x on one site.
- Site: in-memory claim (owner 0x4C4F4C0000000000, Default Access Play Around), admin template = its id; strings
  "Landmarks of Landmark", "<title> by <creators>", "A winner of the <year> Landmarks of Landmark building competition,
  enshrined here by the developers." [I], "<year>" [I], "0 <creators>".
- Props: template props (spawners left out) + the award 3 m outside the site on the Spire's side [I]; streamed within
  320 m (removed beyond 420 m).

## 4. Placement table (from the 3 Aug 2016 post; matched by title and creator)

Matching notes: 117 entries match a 2016 file by title; 28 are 2014 files (`lm_*`). Ones that needed a judgement:
Courage / Singed Timbers "Opera House by DexMontoya" -> `lm_dexmontoya_opera_house` (the 2016 "shoehouse" is Maqea's);
Determination / Forgotten Hills "Elemental Encounter by Sideshow and Metallical" -> `lm_sideshow_metallical_elemental`
(not Kuer's `elemental_sanctum`); Determination / Twisted Shoreline "Firiona in Landmark by Spore" ->
`lm_spore_firiona_resurgent`; Determination / Pioneer Tundra "Refreshing Drink Oasis by Icefire" -> `ice's_drinks_for_fun.`;
Liberation / Lonely Glacier "Dragon by Ogresnot" -> `ogresnot's_dragon`; Liberation / Pioneer Old Growth "Soul Skydance
by Shardi" -> `shardi's_claim_1`; Liberation / Timeworn Dunes "Landmark Trophy by Raz" -> `landmark_of_landmark_too_trophy`;
Liberation / Endless River "Willows End Castle by Nevrai" -> `willows_end_castle` (the client also has
`nevrai_willows_end`, 175 x 208 x 144, used by no entry: which one live placed is L111); Rebellion / Whistling Tangle
"Vyvern Tower by Verjigorm" -> `verjigorm's_vyvern_tower`. Unused 2016 files: `elemental_sanctum` (Kuer, incomplete),
`nevrai_willows_end`.

| World | Landscape (zone) | Landmark | Creator(s) | Year | Template(s) |
|---|---|---|---|---|---|
| Courage | Ancient Crag (398) | Halfling Mountain Manor | Bryde | 2014 | lm_bryde_halfling_mountain |
| Courage | Arid Thicket (391) | Silent Bell Tower | Aloysius | 2016 | (2016) silent_bell_tower |
| Courage | Burning Frostlands (403) | Takish Observation Platform | Andelos | 2016 | (2016) takish_observation_platform |
| Courage | Emerald Vale (405) | Stepford Cultural Center | Arcturius, Zhael | 2016 | (2016) zhael_stepford_cultural_center |
| Courage | Endless River (408) | Guardian Oak | Ansgar | 2016 | (2016) guardian_oak |
| Courage | Forgotten Hills (395) | Watch Tower | Fugazied | 2016 | (2016) watch_tower |
| Courage | Frigid Steppes (412) | Local Island Airport | ArjunaNightFox | 2016 | (2016) local_island_airport |
| Courage | Golden Sands (415) | Biblockteca | Risenstrike | 2016 | (2016) biblockteca |
| Courage | Grand Fjord (416) | Summerwinds Point | Arywinn | 2016 | (2016) summerwinds_point |
| Courage | Hidden Bluffs (399) | Stepford Embassy | Arcturius, Zhael | 2016 | (2016) landmark_competition__stepford_embassy |
| Courage | Lonely Glacier (401) | Aetherion Tower | Andelos | 2016 | (2016) aetherion_tower |
| Courage | Lost Caldera (400) | Tree of Life | Bomdiggety | 2016 | (2016) tree_of_life |
| Courage | Pioneer Deciduous (421) | Landmark Overland Express | Elkay | 2016 | (2016) landmark_overland_express<br>(2016) landmark_overland_express_-_passenger_car<br>(2016) landmark_overland_express_-_sleeper_car |
| Courage | Pioneer Desert (418) | Terrorarium | Fazz | 2016 | (2016) the_terrorarium |
| Courage | Pioneer Jungle (417) | Bio-Industrial Enclosed World | Draconious | 2016 | (2016) bio-industrial_enclosed_world |
| Courage | Pioneer Old Growth (420) | Forgotten Shrine | Butchywuff | 2016 | (2016) forgotten_shrine |
| Courage | Pioneer Tundra (419) | Gazebo | Fugazied | 2016 | (2016) gazebo |
| Courage | Roaring Scars (396) | Crimson Cosmic Candles | Darkk | 2016 | (2016) crimson_cosmic_candles |
| Courage | Shattered Cliffs (411) | Sternwarte Observatory | Belana | 2016 | (2016) belana_sternwerte_observatory |
| Courage | Silver Shallows (402) | Izsharee's Rest | Curamach | 2016 | (2016) izsharee's_rest |
| Courage | Singed Timbers (404) | Opera House | DexMontoya | 2014 | lm_dexmontoya_opera_house |
| Courage | Singing Mesa (414) | Fantasy Bath House | Demystify | 2016 | (2016) fantasy_bath_house! |
| Courage | Sleeping Ridge (406) | Last Tree of the Gargantuans | Eleven | 2016 | (2016) last_tree_of_the_gargantuans |
| Courage | Snowcapped Wilds (393) | Holiday House | Fugazied | 2016 | (2016) holiday_house |
| Courage | Timeworn Dunes (413) | Landmark Aeronautics and Space Administration | Fantal | 2016 | (2016) landmark_aeronautics_and_space_administration |
| Courage | Twisted Shoreline (407) | Mage Prison | Koda | 2014 | lm_koda_mage_prison |
| Courage | Whistling Tangle (394) | Secret Garden | Ellieshine | 2016 | (2016) ellieshine's_secret_garden |
| Courage | Winding Creek (410) | Island of Wishful Thinking | Falcone | 2016 | (2016) the_island_of_wishful_thinking |
| Courage | Windy Barrens (397) | Dreams of Insanity | Voidlust | 2014 | lm_voidlust_dreams_of_insanity |
| Determination | Ancient Crag (398) | Road to Gamma | Genbrua | 2016 | (2016) road_to_gamma |
| Determination | Arid Thicket (391) | Landmark Inn | Graysilk | 2016 | (2016) graysilk_landmark_inn |
| Determination | Burning Frostlands (403) | Fountain | Helie | 2016 | (2016) helie's_fountain |
| Determination | Emerald Vale (405) | Hermit's Hideout | Helline | 2016 | (2016) helline_hermit_hideout |
| Determination | Endless River (408) | Elden Light | Icefire | 2016 | (2016) elden_light |
| Determination | Grand Fjord (416) | Shoehouse | Maqea | 2016 | (2016) shoehouse |
| Determination | Frigid Steppes (412) | Landmark's Claim | Metallical | 2016 | (2016) landmark's_claim |
| Determination | Golden Sands (415) | Sands of Time | Icefire | 2016 | (2016) icefire_sands_of_time |
| Determination | Forgotten Hills (395) | Elemental Encounter | Sideshow, Metallical | 2014 | lm_sideshow_metallical_elemental |
| Determination | Hidden Bluffs (399) | Hadder's Mill | Hadder | 2014 | lm_hadder_mill |
| Determination | Lonely Glacier (401) | Remnants of Laputa | Icefire | 2016 | (2016) remnants_of_laputa |
| Determination | Lost Caldera (400) | Spirit of the Scrying Pool | Sairyss | 2016 | (2016) sairyss_spirit_of_the_scrying_pool |
| Determination | Pioneer Deciduous (421) | Remnants of the Sundering | Lucitie | 2016 | (2016) remnants_of_the_sundering |
| Determination | Pioneer Desert (418) | Merchant Tower Crossroads | Jadam | 2016 | (2016) the_merchant_tower_crossroads |
| Determination | Pioneer Jungle (417) | Bastion Reliquary Tower | Kalen | 2016 | (2016) bastion_reliquary_tower |
| Determination | Pioneer Old Growth (420) | Rustic Windmill | Merma | 2016 | (2016) rustic_windmill |
| Determination | Pioneer Tundra (419) | Refreshing Drink Oasis | Icefire | 2016 | (2016) ice's_drinks_for_fun. |
| Determination | Roaring Scars (396) | Shrine of Life | Jadam | 2016 | (2016) shrine_of_life |
| Determination | Shattered Cliffs (411) | Shrine of the Divine Voxel | Jinkys | 2016 | (2016) shrine_of_the_divine_voxel |
| Determination | Silver Shallows (402) | Garden of the Sun | Juliard | 2016 | (2016) garden_of_the_sun |
| Determination | Singed Timbers (404) | Shrine of the Moon | Juliard | 2016 | (2016) shrine_of_the_moon |
| Determination | Singing Mesa (414) | Bastion Archway | Kalen | 2016 | (2016) bastion_archway |
| Determination | Sleeping Ridge (406) | Statue of Gregor | Gyson, Shylo | 2014 | lm_gyson_shylo_statue_of_gregor |
| Determination | Snowcapped Wilds (393) | Saucer Incident | Keroya | 2016 | (2016) saucer_incident |
| Determination | Timeworn Dunes (413) | Little Garden Tower | Kranimal | 2016 | (2016) little_garden_tower |
| Determination | Twisted Shoreline (407) | Firiona in Landmark | Spore | 2014 | lm_spore_firiona_resurgent |
| Determination | Whistling Tangle (394) | Unicorn Glue Company Clock Tower | Kylen | 2016 | (2016) the_unicorn_glue_company_clock_tower |
| Determination | Winding Creek (410) | Unicorn Glue Company Giant Stalker | Kylen | 2016 | (2016) the_unicorn_glue_company_giant_stalker |
| Determination | Windy Barrens (397) | Old Lookout Post | Lordunderneath | 2016 | (2016) old_lookout_post |
| Liberation | Ancient Crag (398) | The Pinnacle | Milieu | 2016 | (2016) the_pinnacle |
| Liberation | Arid Thicket (391) | Seacrest Inn Bed and Breakfast | Mirasha | 2016 | (2016) seacrest_inn_bed_and_breakfast |
| Liberation | Burning Frostlands (403) | Birthday Monument | Murren | 2016 | (2016) birthday_monument |
| Liberation | Emerald Vale (405) | Highborn Gardens | Murren | 2016 | (2016) highborn_gardens |
| Liberation | Endless River (408) | Willows End Castle | Nevrai | 2016 | (2016) willows_end_castle |
| Liberation | Forgotten Hills (395) | The Pagoda | Nobwinkle | 2016 | (2016) the_pagoda |
| Liberation | Frigid Steppes (412) | Sanctuary | Nymitz | 2016 | (2016) sanctuary |
| Liberation | Golden Sands (415) | Emperor's Estate | Nyakris | 2016 | (2016) emperors_estate |
| Liberation | Grand Fjord (416) | Arachna-Mech (AM MPM 52) | Hanna | 2014 | lm_hanna_arachna-mech |
| Liberation | Hidden Bluffs (399) | Landmark Welcome Sign | Gingerella | 2014 | lm_gingerella_welcome_sign |
| Liberation | Lonely Glacier (401) | Dragon | Ogresnot | 2016 | (2016) ogresnot's_dragon |
| Liberation | Lost Caldera (400) | Sword of a Thousand Stars | Onethousandstars | 2016 | (2016) the_sword_of_a_thousand_stars |
| Liberation | Pioneer Deciduous (421) | Eden's Gate | Odessya | 2016 | (2016) eden's_gate |
| Liberation | Pioneer Desert (418) | Cyborg | Puch | 2016 | (2016) statue_cyborg |
| Liberation | Pioneer Jungle (417) | Summoning Mirror | Sairyss | 2016 | (2016) sairyss_summoning_mirror |
| Liberation | Pioneer Old Growth (420) | Soul Skydance | Shardi | 2016 | (2016) shardi's_claim_1 |
| Liberation | Pioneer Tundra (419) | Secret Agency Gnomish 7 Hangars | Jadam | 2016 | (2016) s.a.g.7__hangars |
| Liberation | Roaring Scars (396) | Ruins of Dustrock Tower | Osmotar | 2014 | lm_osmotar_ruins_of_dustrock |
| Liberation | Shattered Cliffs (411) | Ashen Vaults | Osmotar | 2016 | (2016) the_ashen_vaults |
| Liberation | Silver Shallows (402) | Elephant Landmark with Tree | Spootyman | 2014 | lm_spootyman_elephant_with_tree |
| Liberation | Singed Timbers (404) | Golden Throne | MsQuiltsalot | 2014 | lm_msquiltsalot_golden_throne |
| Liberation | Singing Mesa (414) | Stargazer | Portavar | 2016 | (2016) stargazer |
| Liberation | Sleeping Ridge (406) | Tree Lab | Risenstrike | 2016 | (2016) tree_lab |
| Liberation | Snowcapped Wilds (393) | Meditation | Puch | 2016 | (2016) voila_meditation |
| Liberation | Timeworn Dunes (413) | Landmark Trophy | Raz | 2016 | (2016) landmark_of_landmark_too_trophy |
| Liberation | Twisted Shoreline (407) | Architectural Sculpture | Rengerel | 2016 | (2016) rengerel_architectural_sculpture |
| Liberation | Whistling Tangle (394) | Can't Fit a Square Peg in a Round Hole | Rhaina | 2016 | (2016) can't_fit_a_square_peg_in_a_round_hole |
| Liberation | Winding Creek (410) | Little Lost Isle | Arywinn | 2016 | (2016) little_lost_isle |
| Liberation | Windy Barrens (397) | Peaceful Solace | Risenstrike | 2016 | (2016) peaceful_solace |
| Rebellion | Ancient Crag (398) | Ancient Lighthouse | Shattari | 2016 | (2016) shattari_ancient_lighthouse |
| Rebellion | Arid Thicket (391) | Wayward Wanderer's Rest | Finnigan | 2014 | lm_finnigan_wanderer's_rest |
| Rebellion | Burning Frostlands (403) | Innoruuks Gate | SirDigsby | 2014 | lm_sirdigsby_innoruuk's_gate |
| Rebellion | Emerald Vale (405) | Party Paradise | Shizaku | 2016 | (2016) _party_paradise |
| Rebellion | Endless River (408) | Redemption Beacon | Shizaku | 2016 | (2016) redemption_beacon |
| Rebellion | Forgotten Hills (395) | Tomb of Reflection | Shizaku | 2016 | (2016) tomb_of_reflection |
| Rebellion | Frigid Steppes (412) | Temple of the Elements | Shyshadow | 2016 | (2016) shyshadow_temple_of_the_elements |
| Rebellion | Golden Sands (415) | Dwarven Forge | SLuGMaster | 2016 | (2016) slugmaster's_dwarven_forge |
| Rebellion | Grand Fjord (416) | Snowglobe Playground | Sophiana | 2016 | (2016) snowglobe_playground |
| Rebellion | Hidden Bluffs (399) | Spider's Hot Rod Garage | SpiderOz | 2016 | (2016) spider's_hot_rod_garage |
| Rebellion | Lonely Glacier (401) | Surface to Orbit Defense Leyline Cannon | Shattari | 2016 | (2016) shattari_leyline_cannon |
| Rebellion | Lost Caldera (400) | Abstract Sculpture | Eileithia | 2014 | lm_eileithia_abstract_sculpture |
| Rebellion | Pioneer Deciduous (421) | Reactor Core - Beta 7 | Visarys | 2016 | (2016) reactor_core_-_beta7 |
| Rebellion | Pioneer Desert (418) | Landmark Boy & Landmark Girl | Voila | 2016 | (2016) statue_landmark_boy_by_voila<br>(2016) landmark_girl_by_voila |
| Rebellion | Pioneer Jungle (417) | Temple of the Gods | Shimii | 2016 | (2016) temple_of_the_gods |
| Rebellion | Pioneer Old Growth (420) | Into the Sky | Vorhasse | 2016 | (2016) into_the_sky |
| Rebellion | Pioneer Tundra (419) | Temple of the Moon | Waymaker | 2016 | (2016) temple_of_the_moon |
| Rebellion | Roaring Scars (396) | The Giant Carriage Clock | Fazz | 2014 | lm_fazz_giant_carriage_clock |
| Rebellion | Shattered Cliffs (411) | Hasai Tower | Thelandir | 2016 | (2016) hasai_tower |
| Rebellion | Silver Shallows (402) | Temple of Aleht | Thelandir | 2016 | (2016) temple_of_aleht |
| Rebellion | Singed Timbers (404) | The Sacred Stone | Thelandir | 2016 | (2016) the_sacred_stone |
| Rebellion | Singing Mesa (414) | Remember These | Rikki | 2014 | lm_rikki_remember_these |
| Rebellion | Sleeping Ridge (406) | Frozen Golem Wizard Tower | Logun | 2014 | lm_logun_frozen_golem_wizard_towe |
| Rebellion | Snowcapped Wilds (393) | Club Ember | Vanya | 2016 | (2016) club_ember |
| Rebellion | Timeworn Dunes (413) | Morbourne Town Square | Vanya | 2016 | (2016) morbourne_town_square |
| Rebellion | Twisted Shoreline (407) | Nexus Pointe | Vanya | 2016 | (2016) nexus_pointe |
| Rebellion | Whistling Tangle (394) | Vyvern Tower | Verjigorm | 2016 | (2016) verjigorm's_vyvern_tower |
| Rebellion | Winding Creek (410) | Doctor Cogsprocket's Transducing Prism Permeator | Verlin | 2016 | (2016) doctor_cogsprocket's_transducing_prism_permeater |
| Rebellion | Windy Barrens (397) | The Pumpernickel | Verlin | 2016 | (2016) the_pumpernickel |
| Serenity | Ancient Crag (398) | The Claim Destroyer | Ogresnot | 2016 | (2016) snot_the_claim_destroyer |
| Serenity | Arid Thicket (391) | Garden of the Makers | Lucitie | 2016 | (2016) garden_of_the_makers |
| Serenity | Burning Frostlands (403) | Dark Elf Tower of Darkness | Yarnila | 2016 | (2016) yarnila's_dark_elf_tower_of_darkness |
| Serenity | Emerald Vale (405) | Shrine of Sand | Yarnila | 2016 | (2016) yarnilas_shrine_of_sand |
| Serenity | Endless River (408) | 100% Prop Free Tower | Candela | 2016 | (2016) propfree_tower |
| Serenity | Forgotten Hills (395) | Wave Industries Canal Plant | Seriel | 2014 | lm_seriel_wave_industries |
| Serenity | Frigid Steppes (412) | Naga Statue | Zrom | 2016 | (2016) zrom_naga_statue |
| Serenity | Golden Sands (415) | Aquarius | Silke | 2014 | lm_silke_aquarius |
| Serenity | Grand Fjord (416) | The Frozen Throne Landmark | Tamrya | 2014 | lm_tamrya_frozen_throne |
| Serenity | Hidden Bluffs (399) | A.I. Hyperspace Uplink | Shattari | 2016 | (2016) a.i._hyperspace_uplink |
| Serenity | Lonely Glacier (401) | Nights Edge Beacon | Nissi | 2014 | lm_nissi_nights_edge_beacon |
| Serenity | Lost Caldera (400) | Well of Souls | Icefire | 2016 | (2016) _well_of_souls |
| Serenity | Pioneer Deciduous (421) | Dr. Blockson's Landmark Casebook | Thrift | 2016 | (2016) dr._blockson's_landmark_casebook |
| Serenity | Pioneer Desert (418) | The Navigator | Valontinian | 2016 | (2016) valontinian_the_navigator |
| Serenity | Pioneer Jungle (417) | Watch Towers of the Stars | Onethousandstars | 2016 | (2016) watch_towers_of_the_stars |
| Serenity | Pioneer Old Growth (420) | Proxel Bonsai | SpiderOz | 2016 | (2016) spider's_proxel_bonzai |
| Serenity | Pioneer Tundra (419) | Velium Shard Tower | Alderon | 2016 | (2016) velium_shard_tower |
| Serenity | Roaring Scars (396) | The Weeping | Jarindar | 2014 | lm_jarindar_the_weeping |
| Serenity | Shattered Cliffs (411) | Ancora Imparo Quantum Generator | Zee | 2014 | lm_zee_ancora_imparo_quantum_gen |
| Serenity | Silver Shallows (402) | Anchor Point Lighthouse | Taelaa | 2014 | lm_taelaa_anchor_point_lighthouse |
| Serenity | Singed Timbers (404) | The Well | Ingux | 2016 | (2016) the_well |
| Serenity | Singing Mesa (414) | Lunar Lookout VI | Portavar | 2016 | (2016) lunar_lookout_vi |
| Serenity | Sleeping Ridge (406) | Specimen 36 | Squishimus | 2016 | (2016) specimen_36 |
| Serenity | Snowcapped Wilds (393) | Messenger's Outpost | Eleven | 2016 | (2016) messenger's_outpost |
| Serenity | Timeworn Dunes (413) | Tower | Fugazied | 2016 | (2016) tower |
| Serenity | Twisted Shoreline (407) | Egyptian Temple | Skomag | 2014 | lm_skomag_egyptian_temple |
| Serenity | Whistling Tangle (394) | Dragon Lord's Shrine | Spikes | 2016 | (2016) dragon_lord's_shrine |
| Serenity | Winding Creek (410) | Chess Game Last Move | Syndic | 2014 | lm_syndic_chess_game_last_move |
| Serenity | Windy Barrens (397) | Tower of Ilithine | Osmotar | 2016 | (2016) the_tower_of_ilithine |
