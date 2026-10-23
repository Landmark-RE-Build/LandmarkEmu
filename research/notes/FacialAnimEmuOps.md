# FacialAnim emu ops: detecting `0xF1` and when `0xF2` is required

Operational notes for WorldServer today. Codec / wire layout:
[`FacialAnimDeltaContract.md`](FacialAnimDeltaContract.md). Channel names:
[`FacialAnimChannelMap.md`](FacialAnimChannelMap.md). Probe path: PuppetPc +
`docs/agent-mcp.md`.

---

## Detecting inbound `0xF1` (ClientFacialAnim)

**There is no dedicated C→S handler.** Opcode `ClientFacialAnim = 0xF100` is
named in `ZoneMessageOpcode`, but there is no `IReadable` model and no
`[ZoneMessageHandler]`. Trace: `research/trace/families/00F1_PacketClientFacialAnim.md`
— server **NOT HANDLED**.

When a client does send `0xF1`, `WorldSession` hits the unreadable path and
logs at **Warn** (default NLog levels already show it):

```text
Received Zone packet that was unreadable ClientFacialAnim(0xF100) : <hex dump>
```

### Practical check

1. Run WorldServer with a client that actually emits face frames (SOEmote /
   LiveDriver send path). Vanilla Landmark without `LiveDriverSDK.dll` may
   never send `0xF1`.
2. Watch WorldServer console / NLog for the Warn line above.
3. Confirm opcode name / `0xF100` and that the hex dump length looks like a
   mode-3 payload (keyframe body ~40 B, or shorter deltas) after C→S framing.

Absence of that Warn usually means the **client never sent F1**, not that a
handler swallowed it.

### What does *not* prove inbound F1

| Signal | Why not |
|--------|---------|
| `FACIAL_ANIM_RECEIVER` / `FacialAnimReceiver` | SendSelf flag so the **client applies inbound `0xF2`**, not an F1 ingest switch |
| PuppetPc / MCP `puppet_*` / `probe_self` | Server-synthesized **F2** only |
| OpcodeStats alone | May count bytes; Warn + hex dump is the clearest signal |

---

## Is `0xF2` required?

**Yes — for other clients** to apply your face over the network. Remote apply
goes through `FacialAnim_ApplyReceivedFrame` on **`0xF2`**, with per-character
delta prev state. Correct multiplayer design is opaque relay of peer `0xF1`
payload into `0xF2` (same order, including keyframes) — see Delta Contract
emu obligations.

**Do not echo `0xF2` back to the originator.** The sending client already has
local LiveDriver / send-side preview; F1→F2 relay is peer fan-out only.

| Goal | Need F2? |
|------|----------|
| Other clients see your face | **Yes** — fan out F2 tagged with your **TransientId**; payload = F1 body |
| Originator (the F1 sender) | **No** — do not echo F2 to self |
| Local LiveDriver preview only | F2 not required |
| Peers actually apply F2 | SendSelf `FacialAnimReceiver` must be true (`FACIAL_ANIM_RECEIVER`, default true) |

`probe_self` (F2 tagged with your own TransientId) remains a **separate probe
tool** for testing apply, not part of the F1 relay design.

### Current emu behavior

- **No** F1→F2 relay (parked; needs LiveDriver / shim for real client F1).
- Outbound F2 today: **PuppetPc** / agent probe only.
- Trace family `00F2_…` “NOT SENT” is **stale** relative to PuppetPc.
- When relay exists: fan out to proximity peers only; never to the F1 sender.
- Do not send raw uncompressed 39 B while receivers are on
  `livedriver_compression_type == 3` — use mode-3 keyframe `[1]+39` or proper
  deltas.

---

## Confidence

**High** for current dispatch / Warn path and F2 apply gate (code + prior
`probe_self` / PuppetPc probes). F1 emission from stock clients remains
**unreachable** without LiveDriver / shim.
