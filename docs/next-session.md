# Next session — status + queue

Shotlist workflow (LLM authors minimal cue→asset decisions), GroupBox scroll fix + COPY diagnostics implemented 2026-09-12. Build clean, 201 tests green.

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
- **Queue:** (1) reconcile the test project (narration.srt has 115 cues, shotlist.json
  still targets 370 — re-plan with the new brief), (2) full LLM re-plan of the doubled
  script (~100 images expected), (3) CapCut tier-2 spike LAST (docs/spike-capcut-draft.md),
  optional/minor: global app defaults layer, narration-tied PV reveals, transitionitem.

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
