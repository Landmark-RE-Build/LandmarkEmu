# Bookkeeping pass 2026-10-17 (late): FEATURES.md / TASKS.md against the code

Sources: client-0.695 HEAD 936f360 (git log read-only), MODLOG.md entries of 2026-10-17, OPEN-LEADS.md lead states,
TESTING-NEEDED.md, greps of the named files. Only FEATURES.md and TASKS.md were edited.

## Counts

| | before | after |
|---|---|---|
| FEATURES rows ✅ / 🟡 / ❌ / — | 98 / 6 / 0 / 2 | 99 / 5 / 0 / 2 |
| TASKS [x] / [~] / [ ] | 200 / 42 / 2 | 230 / 13 / 1 |

TASKS rule used: [x] when everything in the item is built and only an in-game check is left (noted "untested in game"),
as the E.x items already do; [~] when a named gap remains. 6.61 is [~] because its own note says "tick after the
TESTING-NEEDED check".

## FEATURES rows changed

- Date line updated (second pass, HEAD 936f360).
- Status changes: Roll Wraps 🟡 → ✅ (L26 closed: never released, admin-only as live).
- Text only: toggle props (movers, E.1), Story Node (🟡 kept; exact gap = weapon model, L117 closed; broken text fixed),
  Reset Node (L22 closed), Combat Node (ability rows built, 70f7d05), IF Gate (If / Is labels, L19 closed), Creature
  spawners (🟡 kept; weapon model gap, site pathing), Game Table settings (L37 closed), Score / Victory Node (L21 closed),
  site permissions (936f360), Graveyards (template stamps 0fb9cc4, trips land on graveyards 9a4ee5a), weapon attacks
  (three-attack kits 80a386c), creatures (pathing 3d5094a), trees (min / max respawn 3c8f9c4), worlds / Spire (Pioneer
  464-468, Spire collision), Underground Adventure (🟡 kept; only L131 left), Parlor (🟡 kept; L34 closed, outfit kept),
  titles (16a789f), Inspect (full data fix, two-client check), grappling (HeroicMovement relay 158ff76), login queue
  (travel queue built, L60 closed), GM tools (/run, not /runspeed).

## TASKS items changed

- [~] → [x]: §1 PropertyTextLine, 2.12, 2.16, 2.17, 2.19, 2.29, 3.3, 3.5, 3.9, 3.10, 3.11, 3.12, 3.17, 3.19, §4 Outfits,
  4.7, 4.11, 4.12, 4.15, 4.16, 4.17, 5.2, 5.4, 5.6, 5.10, 6.59, 6.60, Cross-world services, E.10, E.12.
- [ ] → [~]: 6.61 (built 3d5094a, waits for its TESTING-NEEDED check).
- [~] kept, text updated with the exact gap: 2.23 / E.11 (weapon model), 3.14 / E.15 (L131 other worlds; F2 test),
  3.15 (F1 ice burst, L90; L91 closed), 4.9 (in-client Marketplace product), 5.12 (Colonist's Elegant Robe Green only),
  E.1 (rider on a moving platform L135; pads / flinger / launcher untested).

## Left unchanged (unsure; for the coordinator)

- 3.8 Seasonal drops [~]: the user's decision on hat drops all year vs seasonal is still asked in TESTING-NEEDED.
- 3.16 Goals [~]: L92 / L93 still open (pass 3 unchanged).
- 4.10 Hat unlockers [~]: no open part is named; the Cowboy Hat 4521 is not in the drops by design (RareDrops.cs
  comment). Unclear what keeps it [~] beyond an in-game check.
- B.5 [~]: trade broker and duels with two players not recorded as run; whether dev moved to the split layout is not
  in today's MODLOG.
- B.6 [ ]: only if one process can't hold a world.
- FEATURES "Founder's outfits" 🟡: L149 still open.
- Conflict noticed: E.15's old text said L131 "= landscapes, already working", but OPEN-LEADS L131 (pass 2) reads P5734
  as other worlds (servers) and the lead is still open; both files now say L131 is open.
- 936f360 (site permissions) has no MODLOG entry yet.
