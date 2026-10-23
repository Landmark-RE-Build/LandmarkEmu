# FacialAnim channel map (uncompressed leaf bytes 0…32)

Observed on PuppetPc / slider UI after mode-3 keyframe encode
(`research/notes/FacialAnimDeltaContract.md`). Wire: each channel is `u8 = round(float * 255)`.
Names match in-client face motion (SoE-style labels from probe 2026-10-22).

Source JSON (working copy): `local/future/facialanim-channel-map.json`.

| Index | Name |
|------:|------|
| 0 | Eyes_Rotate_Right |
| 1 | Eyes_Rotate_Left |
| 2 | Eyes_Rotate_Down |
| 3 | Eyes_Rotate_Up |
| 4 | Right_Blink |
| 5 | Left_Blink |
| 6 | Right_Eye_Wide |
| 7 | Left_Eye_Wide |
| 8 | Left_Brow_Up |
| 9 | Left_Brow_Down |
| 10 | Right_Brow_Up |
| 11 | Right_Brow_Down |
| 12 | Mid_Brows_Up |
| 13 | Mid_Brows_Down |
| 14 | Scrunch_Right |
| 15 | Scrunch_Left |
| 16 | Jaw_Rotate_Y_Min |
| 17 | Jaw_Rotate_Y_Max |
| 18 | Left_Frown |
| 19 | Right_Frown |
| 20 | Mbp |
| 21 | NormalFV |
| 22 | Ch |
| 23 | Upper_Lip_Left_Up |
| 24 | Upper_Lip_Right_Up |
| 25 | Lower_Lip_Left_Down |
| 26 | Lower_Lip_Right_Down |
| 27 | Mouth_Down |
| 28 | Mouth_Up |
| 29 | Smile_Big_Left |
| 30 | Smile_Big_Right |
| 31 | Mouth_Open |
| 32 | Oo_Tight |

Head rot (leaf bytes 33…38): three LE `u16`, `q = round((deg + 90) * 360)`, not face-param channels.

## Confidence

**High** for ordering under continuous mode-3 keyframe streams on a default PC head.
Hashes / `SetFaceParamByHash` table not re-verified in this pass (see Frida stub under
`tools/facialanim-probe/`).
