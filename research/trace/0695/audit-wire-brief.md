# Wire audit (2026-10-17) — shared brief

After the native 0.695 renumbering (003dc1f) and the 0.589 body removal (4e7e9ed), four read-only agents check every
packet the server WRITES (S→C) and every packet it READS (C→S) against the 0.695 client, field by field.

## Rules
- READ-ONLY. Write only your own file `trace/0695/audit-wire-<group>.md`. No code, git, game, Frida, Docker, web,
  outreach. Mark [P] (client function + offset) / [I].
- Server code: `<workspace>\LandmarkEmu` (LandmarkEmulator.WorldServer/Network/Message/Model/*, the Game/*.cs files
  that build bodies by hand with GamePacketWriter, SocialServer / CommerceServer / Gateway client packets).
  Opcode enum: LandmarkEmulator.WorldServer/Network/Message/ZoneMessageOpcode.cs; wire numbering: docs/protocol.md §1.6
  (PlayerUpdate sub is u32 on the wire; families use their real 0.695 sub numbers).
- Client: `<workspace>\re\decomp_all_695`. Tools (run with `DECOMP=decomp_all_695`): `re/tools/pkt.py <reader>`
  prints a reader's wire layout with offsets; `re/tools/fn.py`; packet ids: `re/out/packet_ids_695.tsv`; existing
  traces: trace/0695/packets-695.md, coverage-695.md, trace/families/*.md.
- For each packet in your group: find every place the server writes (or reads) it; find the client's reader (S→C) or
  writer (C→S) for 0.695; compare field order, widths, counts, string encoding, bools, trailing fields, and the
  sub-opcode number on the wire. Also check the client accepts trailing bytes or rejects them (many readers require the
  body to be consumed exactly — a mismatch silently drops the packet).
- Output: a table Packet | server writer (file:line) | client reader | verdict (OK / MISMATCH / UNKNOWN) | detail; then a
  FIX list (exact code changes, ranked by player impact). Don't spend time on packets the server never sends/reads.
- Final message: the MISMATCH list with fixes.
