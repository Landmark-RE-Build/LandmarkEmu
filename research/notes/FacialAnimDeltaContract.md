# FacialAnim Delta Contract (`livedriver_compression_type == 3`)

**Normative** wire + client obligations for the delta codec used inside `0xF1` / `0xF2` **payload** bytes (not the packet headers). Image default cvar value is **3**.

**Related:** [`FacialAnimChannelMap.md`](FacialAnimChannelMap.md) (33 channel names),
[`FacialAnimEmuOps.md`](FacialAnimEmuOps.md) (detect inbound `0xF1`; when `0xF2` is required),
[`SOEmote.md`](SOEmote.md) (packets + uncompressed layout + apply map), [`ClientMap.md`](ClientMap.md) § SOEmote.

| Role | Name | VA | RVA |
|------|------|----|-----|
| Mode cvar | `livedriver_compression_type` → `DAT_14525c28c` | `0x14525C28C` | — |
| Encode | `FacialAnim_EncodeDelta` | `0x1434A8AF0` | `0x34A8AF0` |
| Decode | `FacialAnim_DecodeDelta` | `0x1434A9420` | `0x34A9420` |
| Uncompressed leaf | `FacialAnim_EncodeUncompressed` / `DecodeUncompressed` | `0x1434A85C0` / `0x1434A9010` | — |
| Send select | `FacialAnim_SendClientFrames` | `0x1434A9C00` | `0x34A9C00` |
| Recv apply | `FacialAnim_ApplyReceivedFrame` | `0x1434ADAE0` | `0x34ADAE0` |
| Send prev (local) | `SoeUtil::Array<u8>` @ `DAT_145469250` | `0x145469250` | — |
| Recv prev (per char) | proximity queue entry `+0x28` | — | — |

---

## Mode gate (receive)

When `DAT_14525c28c == 3`, `FacialAnim_ApplyReceivedFrame(charId, wirePayload, prevUncompressed)`:

1. Call `FacialAnim_DecodeDelta(headRotOut, floatChannels, wirePayload, prevUncompressed)`.
2. **Only if decode returns true** → `FacialAnim_ApplyDecodedChannels`.
3. Decode false → **silent drop** (no fallthrough to uncompressed).

The mode is **not** on the wire. Sender encode and receiver decode each use their **local** cvar. Mismatch breaks the stream.

---

## Uncompressed frame (delta base)

Always **39 = `0x27` bytes** (same as mode-default codec):

| Off | Type | Meaning |
|-----|------|---------|
| `0x00`…`0x20` | 33×`u8` | channel = `float * 255` |
| `0x21`…`0x22` | `u16` LE | head rot X: `(deg+90)*360` clamp `[0,65535]` |
| `0x23`…`0x24` | `u16` LE | head rot Y |
| `0x25`…`0x26` | `u16` LE | head rot Z |

Delta never ships a different channel map — it only compresses this 39-byte blob.

---

## Wire payload forms

Discriminant = **`payload[0] & 1`**.

### Keyframe (`payload[0] & 1 != 0`)

| Off | Type | Meaning |
|-----|------|---------|
| `0` | `u8` | client writes **`1`** |
| `1`…`39` | `u8[39]` | full uncompressed frame |

`payloadLen` = **40**. Decoder: ensure `prev.len >= 39` (set to 39), `memcpy(prev, payload+1, payloadLen-1)`, then `DecodeUncompressed(prev)`.

### Delta (`payload[0] & 1 == 0`)

| Off | Type | Meaning |
|-----|------|---------|
| `0`…`4` | `u8[5]` | change mask (see bit rules) |
| `5`… | `u8[N]` | changed bytes in index order `0…38` |

Client keyframe/delta byte0 is **`0`** for delta (so bit0 clear). Decoder requires **`prev.len == 39`** else **fail**.

---

## Mask bit rules (encode + decode)

39 frame bytes ↔ 39 change bits packed into the **5-byte** header, with **bit0 of byte0 reserved** for the keyframe flag:

- Walk frame index `i = 0 … 38`.
- Bit cursor starts at **`bit = 2`** (not 1) on **mask byte 0**.
- After each index: `bit <<= 1`; if `bit > 0xFF` → `bit = 1`, advance to next mask byte.
- Coverage: byte0 bits **1–7** (7) + bytes1–4 bits **0–7** (32) = **39**.

**Encode (delta):** for each `i`, if `cur[i] != prev[i]`, set that bit and append `cur[i]` after the 5-byte header.

**Decode (delta):** for each `i`, if bit set, read next data byte into `prev[i]`; else leave `prev[i]`. Then `DecodeUncompressed(prev)`.

---

## Encoder policy (`FacialAnim_EncodeDelta`)

Inputs: current floats/head → temp uncompressed `cur[39]`; `prev` array; `forceKeyframe` bool.

| Condition | Output |
|-----------|--------|
| `forceKeyframe` | keyframe |
| `prev.len != 39` | keyframe |
| else build delta; if `deltaLen > 40` | keyframe (delta not smaller than keyframe) |
| else | delta |

After either form: **`prev ← cur`** (full 39 B).

**Send-side `forceKeyframe`** (`FacialAnim_SendClientFrames`): true when no prior send timestamp, or elapsed **> 1000 ms** since last delta-path timestamp (`DAT_145469240`). Caps desync windows on the wire.

---

## Receiver prev state

| Path | Prev storage |
|------|----------------|
| Local send (`0xF1`) | global array `DAT_145469250` |
| Remote apply (`0xF2`) | **per `characterId`** `SoeUtil::Array<u8>` at proximity queue entry **`+0x28`** (passed as 3rd arg to `ApplyReceivedFrame`) |

First applied frame for a character **must** be a keyframe (or decode fails until one arrives). Drops / mid-stream join without keyframe → silent apply skip until keyframe.

Queue drain: `FUN_1434abd10` → `ApplyReceivedFrame(&charId, frameNode, queueEntry+5)`.

---

## Emu obligations

1. **Opaque relay** of peer `0xF1` payload into `0xF2` is correct when both clients stay on cvar `3` and the per-character stream is forwarded in order (incl. keyframes). **Fan out to other clients only — do not echo `0xF2` to the F1 originator.**
2. **Do not** inject raw 39 B uncompressed bodies while receivers are on mode `3` — decode fails / no apply.
3. Synthesizing frames: emit keyframe `[1]+39` to (re)seed, then deltas with the mask rules above; keep a per-`characterId` prev on the server if you encode yourself.
4. Mode `2` / uncompressed are **out of scope** of this contract — see [`SOEmote.md`](SOEmote.md).
5. **Ops:** inbound `0xF1` is unhandled today (Warn + hex dump only); peers need outbound `0xF2` — see [`FacialAnimEmuOps.md`](FacialAnimEmuOps.md).

---

## Confidence

**High** — encode/decode bit walk and keyframe branch matched in Ghidra; recv prev pointer confirmed at apply call site; cvar default `3` in image `.data`.
