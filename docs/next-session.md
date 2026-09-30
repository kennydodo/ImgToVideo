# Next session — status + queue

Shotlist workflow (LLM authors minimal cue→asset decisions), GroupBox scroll fix + COPY diagnostics implemented 2026-09-12. Build clean, 201 tests green.

## 2026-09-30 — TODO: finish this branch (6 failing tests) + make Flow-native 1376×768 the render default

Tested `feat/per-shot-aspect-ratio` (tip 51ed32a) in a worktree:
`dotnet test tests/ImgToVideo.Core.Tests` → **261 pass, 6 FAIL**; all 6 PASS on
master, so the branch introduced them. Two kinds:

1. **REAL render bug — GPU encoder preset.** The new GPU auto-detect picks Intel
   `h264_qsv` but passes `preset ultrafast` (a libx264-only preset QSV rejects):
   `[h264_qsv] Unable to parse "preset" option value "ultrafast" → Invalid argument`
   Fails `PreviewRenderServiceTests.Renders_tiny_preview_end_to_end`. On any
   machine with Intel QSV the branch breaks preview renders.
   **Requirement: GPU preferred, CPU fallback** — map a valid preset per encoder
   (qsv: veryfast/faster…; nvenc: p1..p7; amf: …; else libx264 ultrafast), and if
   the chosen hw encoder fails to initialise, fall back to libx264 (commit 0e13905
   aims at this but the preset mapping is wrong).

2. **Stale test expectations (4)** — the per-shot-canvas / supersampling changes
   altered the ffmpeg plan strings (grid is now `scale=13824:7776`, zoom `z=`,
   encoder args differ) but the tests still assert the OLD values:
   - PreviewRenderPlanFactoryTests.Decimal_formatting_is_culture_invariant
   - PreviewRenderPlanFactoryTests.Join_arguments_blend_both_sides_with_xfade
   - PreviewRenderPlanFactoryTests.Pan_right_filter_travels_with_constant_zoom
   - PreviewRenderPlanFactoryTests.Builds_one_segment_per_clip_with_encoder_settings

## 2026-09-28 — REQUEST: default output resolution = Google Flow's native 1376×768

Requested by Kehinde. Google Flow returns image masters at **1376×768** — that is
the real native size of a generated still, and anything larger is an upscale.
ImgToVideo should default to that rather than 2560×1440, so a project renders at
the resolution the images actually are.

- Today's defaults: `OutputOptions.Width/Height = 2560/1440`
  (`src/ImgToVideo.Core/Options/OutputOptions.cs:5-6`); the Settings preset list
  is HD 1920×1080 / 2K 2560×1440 / 4K 3840×2160
  (`src/ImgToVideo.App/SettingsWindow.xaml.cs:192`); `Timeline.Resolution` falls
  back to 1920×1080 (`src/ImgToVideo.Core/Models/Timeline.cs:9`).
- Wanted: **1376×768 becomes the default** for new projects, and the preset list
  gains it (e.g. `1376 × 768 (Flow native)`). The existing HD/2K/4K presets stay
  selectable for projects that do upscale.
- Ratio caveat: 1376×768 is 1.7917, not exactly 16:9, and Flow's other ratios are
  the same (its "9:16" master is 768×1376). Decide whether "Flow native" keeps
  the exact master size or snaps to the nominal ratio — FlowBatch's upscaler
  already models both as `fit: exact` vs `fit: aspect` (see its README,
  "Upscaling").
- Scope: this is the render/canvas size only. Masters on disk are already
  1376×768 when FlowBatch's upscale tier is off; when the tier is on, the
  upscaled files sit next to them (`<name>_2k.png` etc.), so the default should
  probably key off the master, not the largest file present.

## 2026-09-19 (morning) — DONE: image-batch delta closed, full pipeline rendered

The "fix the delta (22 → 8)" plan below is COMPLETE — with one twist: the 22-card
batch had already been generated overnight (12:33 AM), including the 14 re-renders
under the renamed filenames, so the LLM re-prompt (step 2) and delta-recompute
(step 3) became moot. State now: all 81 shotlist assets on disk, disk ↔ shotlist
exact match (delta 0).

- **Brief edit (step 1) DONE**: docs/manifest-authoring-brief.md §9 now says sub-beat
  numbers are stable identifiers — never rename/renumber; new images take the next
  unused index in their scene (gaps fine). **Uncommitted** — commit with this note.
- **Diagnostics CLEAN**: `plan` → 14 scenes, 81 shots, 81/81 assets used, zero
  missing/unused, zero GENERATE hints, zero SHOT_HOLD_LONG. Only warning is the
  benign tail-pin (last shot extended 15 frames to audio end).
- **Rendered**: preview (960×540, 23:59.39, narration muxed) + final
  (2560×1440, 452 MB, 23:59.39) + captions.srt + premiere.xml + CapCut draft
  (`out\capcut\ImgToVideoTestWhisperRader` — copy into CapCut drafts folder while
  CapCut is CLOSED, then open).
- **Manual checks left for Kehinde**: watch `out\preview.mp4` / `out\final\final.mp4`,
  import `out\premiere.xml` into Premiere, open the CapCut draft. The 14 re-rendered
  images are fresh content under renamed files — spot-check them in the final.

## 2026-09-19 (evening) — refs design settled; docs only, implementation QUEUED

shotlist v2 gains reference images for character/location continuity
(Flow backend confirmed to accept multiple ref images per card):

- **Schema**: top-level `"refs"` registry (name → path, forward slashes;
  names from the supplied character bible only) + optional `"images[].refs"`
  array of registry names — first ref = dominant subject; most images carry
  none; INF/PROC never do. Fully additive, pre-refs shotlists stay valid.
- **Docs done**: brief §6 (registry + attach rules), §7 (JSON shape + sheet
  lines carry `· refs:`), §12 (checklist item 8); manifest-spec.md shotlist
  section (schema + validation split).
- **Validation split (agreed)**: ShotListParser validates NAMES only —
  pure, no disk access — WARNING `SHOTLIST_REF_UNKNOWN` with nearest-name
  suggestion (edit distance ≤ 2). Analyze stays SILENT about ref files.
  `export-batch` resolves names → paths onto image-batch.json cards and
  stamps missing files `MISSING_REF`.
- **TODO (implementation)**: (1) parser name validation, (2) export-batch
  ref resolution + MISSING_REF stamping, (3) Flow Driver multi-image
  pass-through. Planner/timeline untouched — refs are generation-time only.
  Refs are v2/shotlist-only; v1 manifests unaffected.

## 2026-09-20 (morning) — refs authoring VALIDATED end to end

- First real shotlist for PERSONAL FINANCE initially shipped with zero refs
  and a fully faceless plan (root cause: bible never pasted + brief didn't
  force casting). Fixed with: **bible gate** in the brief (LLM's first reply
  must request a missing bible; registry mandatory once supplied — 6234dba),
  selective-use + 10-ref-per-image cap + casting rule (fa8fd98), and a
  **combined per-channel planning prompt** with the bible pre-embedded:
  `E:\YOUTUBE\PERSONAL FINANCE\planning-prompt.txt` (auto-generated from
  master brief + ref-bible.txt — REGENERATE after any brief change).
- Flow: paste planning-prompt.txt → SRT → style.md. Validated working by
  Kehinde 2026-09-20.
- **Still open**: ref-bible.txt has two `<<< FILL IN >>>` placeholders
  (Dana, Conference) — also mirrored inside planning-prompt.txt; fill both
  or regenerate. Implementation TODOs above still queued.

## 2026-09-22 — prompt length budgets (analyzer WARNING) implemented

- Flow's boxes cap text: master prompt ≤ 1500 chars, each card prompt ≤
  2400. Brief now teaches the budgets (§7 shape + images bullet, §12
  item 6 self-check, word-based since LLMs can't count chars).
- **Analyzer check SHIPPED**: `ShotlistOptions` (imgtovideo.json group
  `shotlist`: `master_prompt_max_chars` 1500 / `prompt_max_chars` 2400,
  0 disables; ProjectOptions.Validate rejects negatives).
  `ShotListParser.Parse` takes optional limits and emits WARNING
  `SHOTLIST_MASTER_PROMPT_LONG` (once, with actual count) /
  `SHOTLIST_PROMPT_LONG` (per offending image, with file name + count) in
  deterministic parse order. ProjectLoader passes the configured values —
  the app's ANALYZE and `Cli plan` both surface them. ImageGen keeps
  limits off for now. Tests: 3 new in ShotListTests (master warn with
  counts, per-entry warn naming the file, disabled/under-limit silent).
- **Still queued with the refs TODOs**: export-batch hard stop (ERROR)
  for over-limit entries — the last gate before Flow; plus the refs
  implementation items (parser name validation, ref resolution +
  MISSING_REF stamping, Flow Driver pass-through).
- **export-batch decoupled from the planner (2026-09-22)**: it previously
  ran the full plan first and aborted on `MANIFEST_NO_SHOTS` when a
  brand-new project had zero images on disk — chicken-and-egg (no batch
  without images, no images without the batch). It now reads only
  shotlist.json (+ optional images\), needs no audio/images/planner, runs
  the shotlist parse with the length limits so its output carries the
  new WARNINGs, and writes out\image-batch.json. Verified on PERSONAL
  FINANCE "These 10 Things…": 85/85 missing cards written; LLM had
  self-regulated to style 1499/1500 chars, longest prompt 677/2400.

## 2026-09-19 — START HERE (completed same morning — see section above): fix the image-batch delta (22 → 8)

**State**: TestWhisperRader has the NEW 81-image/81-shot shotlist (the 8 SHOT_HOLD_LONG
splits, done by the LLM) + 59 generated images on disk + 22-card delta in
`out\image-batch.json`. **Problem**: the LLM *renumbered* sub-beats when splitting, so
14 already-generated shots got renamed (their names became orphans in images-retired\)
and the delta ballooned from 8 to 22. Generating the 22 would re-render 14 existing
images under new names.

**Fix, in order:**

1. **Brief edit** (docs/manifest-authoring-brief.md, §9 naming): replace
   "Sequential sub-beat numbering per beat" with —
   > "Sub-beat numbers are stable identifiers: **never rename or renumber an existing
   > entry** — new images allocate the **next unused index in their scene** (gaps are
   > fine; nothing orders by filename, the assembler sequences by cue ranges)."
2. **Re-prompt the LLM** (the split work is done — it only re-emits with stable names):
   > "Re-emit the exact same 81-shot plan with one constraint: keep every existing
   > filename byte-for-byte — never rename or renumber an entry. The 8 new images
   > (the second halves of the 8 splits) take the next UNUSED index in their scene
   > (gaps are fine). Every cue 1–736 stays covered exactly once."
3. Drop the re-issued shotlist.json in → `ImgToVideo.Cli export-batch <projectFolder>`
   → **must report 8 cards** (the recomputed delta; already-generated names drop out).
   If any of the 22 were already generated before the re-issue, they count as present.
4. Generate the 8 into `images\` → ANALYZE → zero GENERATE hints, SHOT_HOLD_LONG gone →
   preview → final → `export-premiere` / `export-capcut`.

Housekeeping from 2026-09-18 (already done): full 736-cue SRT restored from
WhisperRadar (old 114-cue one at narration.srt.old115.bak), 30 stale images retired to
images-retired\, `export-batch` CLI + typo-downgrade committed (572d039, 759086a,
4f958ad). CapCut exports as **cuts** and pans as **static** until the two reference
captures noted in spike-capcut-draft.md.

## 2026-09-18 — planning rules rework + Premiere spike passed (215 tests green)

- **Planning brief reworked** (docs/manifest-authoring-brief.md): no image-count or
  duration caps — pacing is semantic (fragmentation + truncation are the only failures);
  motion assigned WITH composition (motion-by-function table in §5, ST rare: ~1–2-cue
  shots, ≤10%, no code >40%); split triggers added; prompt word cap removed (cards are
  content-only, style lives only in the master).
- **Planner motion precedence fixed**: shotlist motion → asset filename motion code →
  STATIC (was: STATIC with no filename fallback). True MotionSource reporting (shotlist/
  filename = ExplicitCode; editor = Override). Shotlist-driven pans now travel the full
  overscan band.
- **SHOT_HOLD_LONG warning** (`timing.warn_hold_seconds`, default 30, 0 disables): lazy
  long holds surface in diagnostics for the COPY loop.
- **Premiere spike phase 0/10 PASSED** (verified in Premiere by Kehinde): motion keyframes
  + opacity crossfades survive the XML round-trip. Fixes: opacity serialized as Premiere's
  own `opacity` parameter in its own filter (NOT FCP7 `level` — that is audio levels; and
  NOT inside Basic Motion — dropped on import); crossfade overlays trimmed to their fade
  windows (same-track overlap collapse made later crossfades hard cuts); dips sequential
  on V1 (outgoing tail → black, incoming head from black; FadeWhite ≈ dip through dark).
  Wipes/slides remain renderer-only (XML degrades to cuts; `transitionitem` spike optional,
  not queued). New CLI command: `export-premiere <projectFolder>`.
- **CapCut tier-2 spike PASSED + exporter SHIPPED (2026-09-18 evening)**: schema studied
  from a reference draft (µs units, 6 companion materials per segment, KFType* keyframes,
  draft_meta_info.json registration, BOM-less UTF-8 mandatory). `ImgToVideo.CapCut`
  exporter + `ImgToVideo.Cli export-capcut` verified with a real 4-clip draft opened in
  CapCut 9.4.0.4015. Deferred: transform units (pans static), transitions (cuts — needs a
  reference draft with one), captions. See docs/spike-capcut-draft.md.
- **Queue:** (1) reconcile the test project (narration.srt has 115 cues, shotlist.json
  still targets 370 — re-plan with the new brief), (2) full LLM re-plan of the doubled
  script (~100 images expected), optional/minor: global app defaults layer,
  narration-tied PV reveals, transitionitem spike, .gitignore testdata/.

## One-pass LLM flow (2026-09-12 afternoon) — replaces assets.json

- The assets.json round-trip was removed. The LLM plans EVERYTHING from the
  SRT in one pass: shotlist.json now has `images[]` ({file, prompt} — the
  batch image app's input, filenames per generation-spec) + `shots[]`
  ({cues, asset: FILENAME, motion?, transition?, framing?, scene?, shot_id?}).
- Flow: SRT → LLM (prompts + shotlist) → batch image app generates to those
  exact filenames → assembler validates + expands → visual_manifest.json → video.
- Resolution: exact/stem filename match; NOT on disk + no close match →
  GENERATE request (INFO SHOTLIST_GENERATE_AHEAD, prompt becomes
  visual_intent, planner emits the hint); edit distance ≤ 2 to an existing
  filename → SHOTLIST_ASSET_TYPO error with suggestion. .png appended when
  missing.
- ShotListAssets (A001 map + bootstrap + placeholder allocation) deleted.
- Planner v2 default + shotlist expansion for any planner setting except
  explicit "v1" (EditPlanner still falls back to v1 inference when no
  manifest/shotlist exists).

- GENERATE-AHEAD closed the plan gap: shotlist entries may use placeholder
  assets (`NEW1`) with a `generate` {type, motion, description} object — the
  expander allocates the real filename per the naming spec (scene from the
  entry, next free index, type/motion suffixes) + next A-id, persists
  placeholder→id bindings + descriptions in assets.json (new format:
  {assets, descriptions?, placeholders?}, snake_case), and the missing file
  becomes the planner's GENERATE hint. Placeholder bindings make repeat
  analyzes idempotent. Pending (not-on-disk) map entries are kept with
  SHOTLIST_ASSETS_PENDING instead of retired.
## Older planner-default note (kept for context)

- `ProjectOptions.Planner` defaults to "v2". Projects WITH a manifest/shotlist
  plan from it without any imgtovideo.json; projects without one still fall
  back to v1 scene inference (EditPlanner ignores the setting when no manifest
  exists). `"planner": "v1"` opts out.
- ANALYZE bootstraps a default imgtovideo.json when the folder has none.
- OptionsJson is lenient: empty file → defaults; hand-written file without
  schema_version → its fields applied on defaults. Wrong version still errors.
- Settings has a Planning → Planner selector (v1/v2).

## Shotlist workflow (LLM writes 3 fields per shot)

- `assets.json` — assembler-generated short-id map (natural sort → A001…,
  stable; appended images get new ids, removed ones retire with a WARNING).
  Written on every v2 analyze (bootstrap: also before any shotlist exists).
- `shotlist.json` — the LLM format: `{ "shots": [ { "cues": "1-3",
  "asset": "A001", "motion": "ZI", "transition": "CROSSFADE" } ] }`. Cue spec
  accepts "1-3", "4", "1,2", [1,2,3]; asset resolves via assets.json else
  filename/stem from images\; motion/transition/framing accept string
  shorthand (shorthand transition duration = project transition duration) or
  full manifest objects; optional scene + shot_id (auto `shot-###` by entry
  number).
- `ShotListParser` + `ShotListExpander` (Core/Manifest/ShotList.cs): shot
  times = first/last cue SRT times; narration_text/beat_id (c3)/srt_cue_ids
  auto-filled; sorted by narration time (INFO on reorder); overlapping cues
  clamp (partial) or drop (fully covered) with WARNING; unknown asset = ERROR
  naming the entry; duplicate shot_id = ERROR; dropped shots leave gaps that
  the manifest planner's contiguity fix closes (by design).
- Precedence: shotlist wins; expanded manifest is saved to
  visual_manifest.json on every analyze (`VisualManifestLoader.Save`,
  snake_case, nulls omitted) — edit the shotlist, not the manifest. planner
  v1 + shotlist → SHOTLIST_IGNORED info.
- COPY button in the Diagnostics panel copies all issue lines to the clipboard
  for pasting back into the LLM chat (validator loop).
- Docs: manifest-spec.md has the shotlist section;
  docs/manifest-authoring-brief.md is the paste-ready LLM brief.
- Open question carried over: `transition_out` on the LAST shot is ignored
  (outgoing-shot semantics). The brief teaches CROSSFADE-on-outgoing-shot.

## GroupBox scroll fix (2026-09-12)

Diagnostics ListBox could never scroll: the custom GroupBox template wrapped
content in a vertical StackPanel (infinite measure height → ListBox grew,
clipped, no scrollbar). Template now uses a Grid with Auto/*/rows; LstIssues
also sets VerticalScrollBarVisibility explicitly.

## Older: visual manifest pipeline (v2 planner) + hardware encoder option

Visual manifest pipeline (v2 planner) + hardware encoder option implemented
2026-09-11.

## Hardware encoder (GPU encode, CPU fallback)

- `RenderOptions.Encoder`: `auto` (default) probes ffmpeg for h264_nvenc /
  h264_amf / h264_qsv and uses the first available; anything missing → CPU
  libx264. Explicit values force one encoder; `cpu`/`libx264` always CPU.
- Preset/CRF map onto each encoder (nvenc `-rc vbr -cq`, qsv `-global_quality`,
  amf `-rc cqp`); libx264 args unchanged. Filter chain (zoompan/xfade) stays CPU
  — ffmpeg has no GPU zoompan/xfade.
- EncoderProbe caches the encoder list per ffmpeg binary; arg fingerprints
  include the encoder, so switching encoders invalidates the render cache.
- Settings → Render & tools has an Encoder field.

## Visual manifest (planner v2)

- Locked contract with the image generator: `visual_manifest.json` v1.0 with
  `video`, `assets` (id/file/scene_id/beat_ids/type/safe_motion/allow_reuse/
  focal_regions) and a full `timeline` of shots (shot_id, start_ms/end_ms,
  narration_text, visual_intent, change_reason, asset_id, framing {type,
  focal_region}, motion {type, start_scale, end_scale, duration_ms},
  transition_out {type, duration_ms}). The GENERATOR owns the edit; the
  assembler validates, renders, exports and reports.
- Enable: drop `visual_manifest.json` in the project root + `"planner": "v2"`
  in imgtovideo.json. Without a manifest everything stays v1.
- Framing map: wide=full, medium=85% band, close=70%, detail=50%, crop=focal
  region re-framed to the largest 16:9 window inside it (WARNING on re-frame).
  Scale = viewport÷scale centered on the framing rect, clamped in-image.
- Motion completes after `motion.duration_ms` (or `motion_duration_ms`,
  default 4500) and holds — via the new `VideoClip.MotionDurationFrames`
  clamp in the zoompan filter. Pans still ride MotionEngine full-image bands.
- Timing: quantized to frames, contiguity auto-fixed (INFO/WARNING), tail
  pinned to the ffprobe audio duration. Missing image → shot dropped, time
  absorbed by the neighbour, and a GENERATE hint with beat/timecode/narration/
  intent lands in issues + build-report coverage.
- Editor rows are per shot (`shot_id` identity; overrides.json gets a `shot`
  key). Frames boxes + nudge sliders work (per-shot duration overrides, Σ
  preserved or the whole set is ignored with a warning); Exclude works;
  motion/easing/order/cuts stay manifest-owned in v2.
- Coverage block in build-report.json: shots, assets, unique used, avg seconds
  per source change, longest same-source run, longest shot, missing list,
  unused list.
- KNOWN semantic edge: `transition_out` drives the join AFTER its shot; the
  LAST shot's transition_out is ignored. The sample manifest puts the only
  CROSSFADE on the last shot — confirm with Kehinde whether it should become
  transition-IN semantics instead.

## Follow-up fixes from user testing

- **In-place mux bug** ("Output path same as input #0"): clip previews now render
  video-only to `out\clips\parts\{name}.render.mp4` and mux into
  `out\clips\{name}.mp4` (different paths). Narration mux actually works now;
  failures still show the dialog with a video-only fallback.
- **Transition previews**: row ▶ now renders the real cut when the row has a
  transition-in — previous clip's tail + xfade join + this clip — through the
  standard pipeline (mini timeline → PreviewRenderPlanFactory.Build →
  PreviewRenderService, narration muxed at the cut's narration offset). Easing
  was already honored in clip motion. `TailTrimFrames`/`HeadTrimFrames` exposed
  from the factory for the app.
- **Play scene** button: renders all non-excluded clips of the selected scene
  (row motion/easing/duration + transition edits) into
  `out\clips\scene_{id}.mp4` with narration offset to the scene start, then
  plays it.
- **Replay loop removed**: MediaEnded stops playback (position reset, Play
  button) instead of looping forever.

## Done in this batch

1. **Autoplay**: PlayerControl plays from MediaOpened, from its own Loaded event,
   and from Load() — position reset first. No more clicking Restart.
2. **Muted previews (file lock)**: `ReleasePreviewFiles()` stops/closes the docked
   player and closes any floating player before every render (MediaElement file
   handle released via Close() + Source=null). Mux failures now show a dialog
   (video-only preview still opens).
3. **PlayerControl (UserControl)**: MediaElement + Restart / Play-Pause +
   position slider (drag to seek, click to jump) + "0:03 / 0:10" time label on a
   100 ms DispatcherTimer. Load(path, title) / ShowNote / StopAndRelease API.
4. **Dock/undock**: PlayerControl docked at the bottom of SceneEditorWindow below
   the clip list. Undock spawns a modeless ClipPreviewPlayerWindow hosting the
   same control; closing it re-docks (playback resets). Row previews now play in
   the docked player (no more popup dialog).
5. **Play all**: plays out\preview.mp4 in the docked player; missing file →
   status points to BUILD PREVIEW. Subtitle surfaces "saved overrides only".

## Frame nudge — implementation notes + one deviation

- Nudge slider per clip row, ±120 frames, shifts the cut AFTER that clip:
  row duration +δ, next row −δ; Frames boxes update live (ClipRow implements
  INotifyPropertyChanged for Duration).
- Clamp: neighbors keep ≥ 1 frame; transfers compose across multiple sliders.
- Save: when a nudge touches a scene, ALL of that scene's rows are emitted as
  absolute duration overrides (current values, Σ = scene window → the timing
  engine accepts the full set; siblings keep planned values). Scenes without
  nudges emit only rows whose Duration differs from PlannedDuration (engine
  redistributes siblings, unchanged behavior).
- **Deviation from the review doc (since resolved)**: the "across a scene
  boundary" case initially shipped hidden because scene windows are pinned.
  It is now implemented — see the boundary-nudges entry in the queue section
  below (paired full-set deltas shift one boundary; unpaired deltas warn).

## After this batch (queue)

- ~~build-report.json~~ — DONE: `out\build-report.json` written on every plan
  (deterministic, no timestamps; settings + per-scene/clip decisions + issues).
- ~~final-quality render~~ — DONE: RENDER FINAL button → `out\final\final.mp4` +
  `captions.srt` (full output resolution, FinalPreset/FinalCrf in RenderOptions /
  Settings). This IS CapCut tier 1.
- ~~CapCut tier 1~~ — DONE (see RENDER FINAL above).
- CapCut tier 2 (draft exporter) — gated on the phase 0b spike:
  `docs\spike-capcut-draft.md` has the hands-on protocol; needs the installed
  CapCut version and a manual import check.
- Phase 0 spike (Premiere import) — needs a manual Premiere import check of
  `out\premiere.xml` (motion keyframes present? cuts-only fallback is built in).
- Optional: boundary nudges (needs timing engine to accept paired boundary-shift
  full-sets; see above).
- Optional: boundary nudges — DONE (2026-09-11): the timing engine now accepts
  PAIRED full-set overrides that shift one scene boundary (opposite deltas from
  two adjacent scenes; unpaired deltas still warn + ignore). The editor enables
  the slider on every row that has a successor (scene-end rows included — the
  last clip of the video stays fixed), and BuildOverrides pairs deltas in
  timeline order before emitting full-sets. Picture at a shifted boundary drifts
  from narration by up to δ frames — the user's explicit choice.
- Optional next: incremental preview builds — DONE (2026-09-11):
  PreviewRenderService fingerprints each segment (ffmpeg args + input image
  size/mtime) into out\render\render-manifest.json; BUILD PREVIEW and RENDER
  FINAL skip unchanged segments, always re-run concat + narration mux.
