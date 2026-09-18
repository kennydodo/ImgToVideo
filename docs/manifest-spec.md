# visual_manifest.json — specification (locked 2026-09-11)

The manifest is the **edit decision list** for a project. The generator owns it;
the assembler validates it, builds the timeline from it, renders, exports, and
reports. The assembler never invents shots.

- File location: `<project folder>\visual_manifest.json`
- Default: projects with a `visual_manifest.json` (or `shotlist.json`) plan
  from it. Set `"planner": "v1"` in `imgtovideo.json` to opt out and use the
  assembler's own v1 scene-inference planner. Projects without a manifest
  always plan via v1 inference.
- Unknown extra fields are ignored (e.g. `asset_index`, `summary`,
  `recommended_motion` are accepted and currently unused).
- Times are milliseconds; everything is quantized to frames
  (`frame = round(ms × fps / 1000)`).

## Top level

```json
{
  "schema_version": "1.0",
  "video": { "id": "interval-walking", "title": "Interval Walking",
             "duration_ms": 530000, "fps": 30 },
  "assets": [ ... ],
  "timeline": [ ... ]
}
```

- `schema_version` must be `"1.0"`.
- `video.fps` must match the project output fps (mismatch = ERROR).

## assets[]

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | unique, referenced by shots (`asset_id`) |
| `file` | yes | image filename inside `images\` (must exist) |
| `scene_id` | no | grouping/metadata |
| `beat_ids` | no | narration beat ids this asset illustrates |
| `type` | no | free-form (`closeup`, `infographic`, `comparison`, `lifestyle`, `character`, …). `closeup`/`infographic`/`comparison`/`lifestyle`/`character` are carried into the timeline as image-type metadata |
| `safe_motion` | no | motion values allowed for this image; shots using others get a WARNING |
| `allow_reuse` | no | informational today |
| `focal_regions` | no | normalized rects `{ id, x, y, width, height }` used by `crop` framing |

## timeline[] (shots, in order)

| Field | Required | Meaning |
|---|---|---|
| `shot_id` | yes | unique clip identity |
| `start_ms`, `end_ms` | yes | shot window on the narration clock |
| `asset_id` | yes | asset to show |
| `scene_id`, `beat_id`, `srt_cue_ids` | no | grouping / diagnostics |
| `narration_text` | no | the SRT text this shot covers (echoed in diagnostics) |
| `visual_intent`, `change_reason` | no | free-form editorial metadata (echoed in diagnostics) |
| `framing` | no | `{ "type": "wide" \| "medium" \| "close" \| "detail" \| "crop", "focal_region": "<id>" }` (default `wide`) |
| `motion` | no | `{ "type": "STATIC" \| "ZI" \| "ZO" \| "PL" \| "PR" \| "PU" \| "PD" \| "PV", "start_scale": 1.0, "end_scale": 1.1, "duration_ms": 4500 }` (default STATIC) |
| `transition_out` | no | `{ "type": "CUT" \| "CROSSFADE" \| "DIP" \| "DIP_WHITE", "duration_ms": 180 }` — the join **after** this shot. The last shot's `transition_out` is ignored. Fewer than 2 frames of duration = CUT |

### Framing map (base viewport in source pixels)

| `type` | base viewport |
|---|---|
| `wide` | full image |
| `medium` | centered band, 85% of image height, full width |
| `close` | centered band, 70% of image height |
| `detail` | centered band, 50% of image height |
| `crop` | the referenced `focal_region` rect scaled to pixels |

Scale semantics: `viewport = baseRect ÷ scale`, centered on the base rect's
center, clamped inside the image. `ZI 1.0→1.1` therefore starts on the framing
rect and ends 10% tighter. A `crop` region whose aspect differs from the output
aspect by more than 0.2 is re-framed to the largest 16:9 window inside the
region (WARNING). Non-16:9 viewports letterbox through the existing render modes.

### Motion rules

- `duration_ms` per shot, else project `motion_duration_ms` (default 4500),
  else the whole shot. `0` = move spans the entire shot.
- The camera move completes after that duration and the framing **holds**.
- `PL/PR/PU/PD/PV` ride the motion engine's full-image travel bands; framing is
  ignored for pans.
- Unknown motion type → STATIC + WARNING. Motion outside `safe_motion` → WARNING.

## Timing rules (validated, auto-fixed with reports)

1. First shot starts before 0 → extended to 0 (WARNING).
2. Contiguity: a start drifting ≤ 2 frames from the previous end is snapped (INFO);
   larger gaps/overlaps close by adjusting the previous shot (WARNING).
3. The last shot is pinned to the ffprobe audio duration (INFO if ≤ 2 frames of
   drift, else WARNING).
4. A shot holding longer than `timing.warn_hold_seconds` (default 30 s; 0
   disables) emits WARNING `SHOT_HOLD_LONG` — verify it is one continuously
   developing idea, or split it in shotlist.json.
5. A shot whose asset is missing does not block the build: it is dropped, its
   screen time is absorbed by the neighbouring shot, and a GENERATE hint is
   emitted (asset id, file, beat, timecode, `narration_text`, `visual_intent`).
6. Shots referencing an unknown `asset_id` are ERRORS and dropped the same way.

## shotlist.json — the LLM plan (one-pass, updated 2026-09-12)

The LLM plans everything from the SRT in ONE pass: `shotlist.json` contains
both the image prompts (input for the batch image app) and the edit. No
intermediate files, no round-trips. Filename references that are not on disk
yet are kept as GENERATE requests.

```json
{
  "images": [
    { "file": "S04_03_HYB_ST.png", "prompt": "what to draw (batch app input)" }
  ],
  "shots": [
    { "cues": "12-15", "asset": "S04_03_HYB_ST.png", "motion": "ST" }
  ]
}
```

Per shot: `cues` (`"1-3"`, `"4"`, `"1,2"` or `[1,2,3]` — SRT cue indices),
`asset` (filename per `docs/generation-spec.md`; stem match works, and
not-yet-generated files are fine), optional `motion` (string shorthand or the
full manifest motion object), optional `transition` (`CUT|CROSSFADE|DIP|
DIP_WHITE` — shorthand uses the project transition duration), optional
`framing` (`wide|medium|close|detail`), optional `scene`, optional `shot_id`.

Resolution rules:
- Existing image: exact filename (case-insensitive) or stem match.
- Not on disk + no close existing match: kept as a GENERATE request (INFO
  `SHOTLIST_GENERATE_AHEAD`); the planner emits the hint with narration and the
  entry's prompt from `images[]` as `visual_intent`. `.png` is appended when
  the extension is missing.
- Close match to an existing filename (edit distance ≤ 2): ERROR
  `SHOTLIST_ASSET_TYPO` with the suggested correct name.
- Unknown cue numbers: ERROR `SHOTLIST_CUE_UNKNOWN`.

Precedence: when present, `shotlist.json` wins; every ANALYZE expands it and
regenerates `visual_manifest.json` on disk (edit the shotlist, not the
manifest). Any planner setting except explicit `"v1"` expands it.

Expansion: shot start/end = first/last cue's SRT times; `narration_text`,
`beat_id` (`c3`) and `srt_cue_ids` auto-filled; entries sorted by narration
time (INFO when reordered); overlapping cue ranges clamp (partial) or drop
(fully covered) with WARNING. Quantization, contiguity, tail pinning and
coverage are the v1.0 manifest pipeline.

## Per-shot overrides (from overrides.json, written by the Scene Editor)

Keyed by `shot`: `duration_frames` (Frames boxes / nudge sliders — must keep the
shot-total equal to the audio length, else all are ignored with a WARNING),
`exclude` (shot dropped, time absorbed), and motion/easing fields (stored but
manifest-owned in v2 — the manifest wins for framing/motion/cuts).

## Coverage report (build-report.json → `coverage`)

`duration_seconds, shot_count, asset_count, unique_assets_used,
average_seconds_per_source_change, longest_same_source_seconds,
longest_shot_seconds, missing_assets[] (generation hints), unused_assets[]`.
