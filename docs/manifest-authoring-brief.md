# Manifest authoring brief — give this to the LLM that plans the video

The LLM plans the whole visual layer in ONE pass, directly from the SRT. It
outputs a single `shotlist.json` that contains BOTH the image prompts (for the
batch image app) AND the edit (which image covers which subtitle lines). No
round-trips: LLM → generate images → assembler → video.

## What to paste alongside this brief

1. The full SRT file (verbatim).
2. If images already exist: the list of their filenames (exact names from
   `images\`). If starting from scratch, nothing else is needed.

## The prompt

> You are the visual edit planner for a deterministic video assembler. From the
> SRT below, return a single `shotlist.json` and nothing else. It has two parts:
>
> **`images`** — every image the video needs, with the EXACT filename it must
> be saved as and the generation prompt:
>
> ```json
> { "file": "S04_03_HYB_ST.png", "prompt": "what to draw, one detailed sentence" }
> ```
>
> Filename rules (HARD — the assembler and the batch app both key on them):
> `S##_##_TYPE_MOTION.png` — scene number, image index inside the scene, then:
> - TYPE: SCN (scene/character) | CU (close-up) | INF (infographic) |
>   CMP (comparison) | PROC (process) | HYB (hybrid scene+graphic) |
>   OVR (overview)
> - MOTION: ST | ZI | ZO | PL | PR | PU | PD | PV — this also defines the
>   required canvas: ST/ZI/ZO 2304×1296, PL/PR 2880×1296 (subject left third
>   for PR, right third for PL), PU/PD 2304×2160, PV 3840×1296.
> Sequential numbering per scene, uppercase codes, .png lowercase. If an image
> already exists (in the provided inventory), reuse its exact filename — never
> rename or duplicate it.
>
> **`shots`** — the edit, in narration order:
>
> ```json
> { "cues": "12-15", "asset": "S04_03_HYB_ST.png", "motion": "ST" }
> ```
>
> - `cues`: a range `"1-3"`, single `"4"`, or list `[1,2,3]`. Cover EVERY cue
>   from 1 to N exactly once — no gaps, no overlaps. The last shot ends at cue N.
> - `asset`: the filename from the images list. Reuse an image only when the
>   narration genuinely returns to it; keep a source change every 3–6 s and
>   never hold one image beyond ~12 s.
> - `motion` (optional, default STATIC): ST | ZI | ZO | PL | PR | PU | PD | PV.
>   Zooms default 1.0→1.1. PU/PD suit tall infographics, PL/PR suit wide scenes.
> - `transition` (optional, default CUT): CROSSFADE for flow, DIP/DIP_WHITE
>   sparingly for scene breaks.
> - `framing` (optional, default wide): wide | medium | close | detail.
> - `scene` (optional): a scene id like "S04" to group shots for the editor.

## Self-check before returning

1. Every cue 1..N is covered exactly once, in order.
2. Every `asset` exists in the provided inventory OR has an entry in `images`
   with a prompt.
3. Every `images[].file` follows `S##_##_TYPE_MOTION.png` and has a prompt.
4. All motion/transition/framing values from the lists above.

## What the assembler does next (no LLM involvement)

- Expands the shotlist into the full manifest: shot times = first/last cue's
  SRT times, narration text auto-filled, frame quantization, gap/overlap fixes,
  audio tail pinning.
- Filenames not on disk yet become GENERATE requests — `out\build-report.json`
  → `missing_assets[]` lists file + beat + timecode + narration + prompt, which
  is exactly what the batch image app needs.
- Only come back to the LLM if Diagnostics shows errors (COPY button → paste
  into chat): e.g. `SHOTLIST_CUE_UNKNOWN`, `SHOTLIST_ASSET_TYPO`
  (with the suggested correct name), `SHOTLIST_CUES_OVERLAP`.
