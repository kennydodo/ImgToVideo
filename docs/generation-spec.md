# Image Generation Spec (ImgToVideo)

Motion codes are decided **at image-generation time**, when the composition is known. The suffix
written into the filename is a binding contract: the pixels only work with that motion.

All sizes assume a 1920×1080 delivery viewport at 16:9. Values below are **minimums** — the
assembler reads the image's real dimensions and derives pan travel from what actually exists, so
slightly different generator output still works (it just warns when below minimum).

| Code | Meaning | Min size (16:9) | Oversize | Subject placement | Engine behavior |
|---|---|---|---|---|---|
| `ST` | Static | 2304×1296 | 120% | Centered | Static shot |
| `ZI` | Zoom in | 2304×1296 | 120% | Centered | 100 → 105–108% push-in |
| `ZO` | Zoom out | 2304×1296 | 120% | Centered, slightly tighter | 106–108 → 100% |
| `PL` | Pan left | 2880×1296 | 150% wide | Subject in right third | Viewport slides **left**, revealing left content |
| `PR` | Pan right | 2880×1296 | 150% wide | Subject in left third | Viewport slides **right**, revealing right content |
| `PV` | Pan/reveal | 3840×1296 | 200% wide | Full-width scene | Long reveal; travel derived from real width |

## Rules for every image

- Height ≥ 1296 keeps a 120% vertical margin so no motion ever reveals a top/bottom edge
- `PL`/`PR`: the useful extra content must genuinely extend in the named direction; the main
  subject sits in the opposite third so the pan has somewhere to go
- `PV`: compose for a full left-to-right (or right-to-left) journey — a beginning detail and an
  end detail worth landing on
- No suffix means "normal composition with 120% safe margin all around"; the assembler may then
  auto-select any restrained motion
- Never write an unknown two-letter code; the assembler treats unknown codes as unsuffixed and
  auto-selects conservatively

## Filename grammar

```
S{scene:2d}_{index:2d}(_{CODE})?.png      case-insensitive
```

Examples:

```
S01_01.png        scene 1, image 1 — motion auto-selected
S01_02_ZI.png     scene 1, image 2 — push-in
S08_02_PR.png     scene 8, image 2 — pan right
S12_03_PV.png     scene 12, image 3 — extra-wide reveal
```

Scenes and image numbers sort naturally (`S01_10` comes after `S01_02`).

## Movement restraint (applies to auto-selected motion)

- Push in: 100% → 105–108%
- Zoom out: 106–108% → 100%
- Pan: small movement only; amplitude always derived from real pixel travel
- Static: 100%
- A static shot at least every 4–6 cuts; no auto-selected motion repeats back-to-back
