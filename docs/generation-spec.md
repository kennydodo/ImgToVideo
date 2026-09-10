# Image Generation Spec (ImgToVideo)

## Official naming convention

```
S##_##_TYPE_MOTION.png
```

| Part | Meaning | Example |
|---|---|---|
| `S01` | Scene number | Scene 1 |
| `01` | Image/shot order inside scene | First image |
| `SCN` | Image type (see table) | Normal scene |
| `ST` | Intended motion (see table) | Pan right |
| `.png` | File format | Always lowercase |

Rules: uppercase codes, two digits for scene and image numbers, underscores only, `.png` lowercase.
Names are case-insensitive to the assembler, but generate them uppercase as above.

### Image type codes

| Code | Meaning |
|---|---|
| `SCN` | Normal scene / environment / character scene |
| `CU` | Close-up / detail |
| `INF` | Infographic / diagram |
| `CMP` | Comparison |
| `PROC` | Process / step-by-step |
| `HYB` | Hybrid — scene + explanatory graphic |
| `OVR` | Overview / concept image |

### Motion codes

| Code | Meaning | Min size (16:9) | Oversize | Composition rule |
|---|---|---|---|---|
| `ST` | Static | 2304×1296 | 120% | Centered |
| `ZI` | Zoom in | 2304×1296 | 120% | Centered |
| `ZO` | Zoom out | 2304×1296 | 120% | Centered, slightly tighter |
| `PL` | Pan left | 2880×1296 | 150% wide | Subject in right third, content extends left |
| `PR` | Pan right | 2880×1296 | 150% wide | Subject in left third, content extends right |
| `PU` | Pan up | 2304×2160 | 200% tall | Content extends upward |
| `PD` | Pan down | 2304×2160 | 200% tall | Content extends downward |
| `PV` | Extra-wide reveal (extension) | 3840×1296 | 200% wide | Full-width scene |

## Rules for every image

- The fixed dimension keeps a 120% margin so no motion ever reveals an edge; the travel dimension
  carries the extra overscan
- `PL`/`PR`/`PU`/`PD` names are the assembler's signal that the image needs that overscan —
  generate the larger size whenever you use one of those codes
- `PV` is an ImgToVideo extension (extra-wide reveal); the batch generator may also emit
  `S07_02_CU_PV` style names and they will render as a long horizontal reveal
- Do **not** encode editing instructions (duration, scale, easing) into filenames — only
  scene + order + type + motion
- No suffix at all (`S01_01.png`) is still valid: the assembler auto-selects restrained motion
  for a normal 120%-margin composition

## Filename examples

```
S01_01.png          scene 1, image 1 — motion auto-selected
S01_02_ZI.png       legacy format — still supported
S01_01_SCN_ST.png   official format — static scene
S01_02_CU_ZI.png    close-up with push-in
S01_03_HYB_PR.png   hybrid with pan right
S08_02_SCN_PR.png   scene 8, pan right
```

Scenes and image numbers sort naturally (`S01_10` comes after `S01_02`).

## Movement restraint (applies to auto-selected motion)

- Push in: 100% → ~106%
- Zoom out: ~107% → 100%
- Pan: small movement only; amplitude always derived from real pixel travel
- Static: 100%
- A static shot at least every 4–6 cuts; no auto-selected motion repeats back-to-back
- Vertical pans (`PU`/`PD`) and reveals (`PV`) are explicit-code only — never auto-selected
