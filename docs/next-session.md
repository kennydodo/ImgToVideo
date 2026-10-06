# Next session — status + queue

Shotlist workflow (LLM authors minimal cue→asset decisions), GroupBox scroll fix + COPY diagnostics implemented 2026-09-12. Build clean, 201 tests green.

## 2026-10-06 - pop↔reveal timing: Late placement was inverted (built, full suite green)

`dotnet test` now actually ran (dotnet 10 SDK): 324/324 green on `fix/reveal-pop-timing`.
- **Root bug:** `TailTrim`/`HeadTrim` for `TransitionAlignment.Late` ("Start at cut") were
  swapped - the whole fade ran at [cue-T, cue], so the incoming image arrived BEFORE its
  narration and pop, against the September design note ("incoming image never appears before
  its narration"). The join now occupies [cue, cue+T]: the outgoing clip holds its final
  framing under it, the incoming head fades in starting exactly at the cue, and the pop (sfx
  lands on the clip's StartFrame) hits at the same instant the motion begins. Measured on a
  synthetic two-item reveal: fade starts 4.000 s, pop onset 4.000 s, fully on 4.267 s.
- The guard loop in `PreviewRenderPlanFactory` had the same swap (prev-cut tail vs head). The
  cut/trim math now lives once in `Core/Planning/TransitionCuts.cs`, shared by the ffmpeg plan
  and the Premiere exporter. `Fcp7XmlExporter` mirrors the new placement (V1: outgoing clip
  extended, incoming shifted to where its fade ends; V2 fade window after the cut; dips fade
  to black and back inside the window), with motion keyframes clamped over the head offset.
  The Transitions/Join/Dip/Crossfade test expectations were retargeted to the new (measured)
  output.
- **Test runner wiring:** `PreviewRenderServiceTests` ran `new FfmpegRunner()` (PATH ffmpeg)
  against a plan built with `ResolveEffectiveFfmpegPath` - on this machine the PATH build
  cannot init nvenc (driver 13.0 vs required 13.1) while a legacy build under C:\tools can, so
  both e2e tests died mid-render with exit -40. The runner is now wired like the app/CLI.
- **Culture-dependent report strings:** MANIFEST_TIMING_FIXED / SHOT_TIMING / FOCAL messages
  are `FormattableString.Invariant` (a comma-decimal locale printed "pause of 0,6s" and broke
  the Narration-pause test).
- The "not built or run" claims of 647aaa9/650d85f/5328b8b are now verified: QSV preset
  mapping, pop-catalog and SoundAndSettingsOptions tests all pass.
- Not verified (no Premiere here): the new dip/crossfade XML keyframe layout on import - the
  geometry mirrors the render plan, but the serialization deserves an eyeball on first import.

## 2026-10-06 - FIXED (unbuilt, untested): the 6 failing tests + QSV preset + Settings save bug

`feat/per-shot-aspect-ratio` was already merged into master, so this is on master. Not run: no dotnet in the authoring sandbox - run `dotnet test` and report.
- **Real bug (QSV):** `QsvPreset` passed libx264's `ultrafast`/`superfast` to `h264_qsv`, which rejects them. They now map to `veryfast`.
  `EncoderProbe.Verify` now probes with the exact `VideoEncoderArgs` a render uses (fastest preset, B-frames), so an
  encoder that opens with defaults but rejects the real options is no longer selected. Theory test: every libx264 preset
  maps to a value each hardware encoder accepts.
- **Stale tests:** `Options()` now pins `Encoder = "libx264"` (the "auto" default made the asserted args depend on the
  machine's GPU). Pan-right is now a `crop` on a 5x grid (a pan keeps the viewport size, so it is no longer `zoompan`),
  the join test expects `n` (crop) for the incoming pan and a `loop=loop=14` stage, and the culture test uses a fractional
  fixed-size viewport (crop width 11521.5) instead of the old zoompan `z=`.
- **Settings save bug:** the dialog rebuilt `ProjectOptions` without the shotlist prompt limits, resetting them on every Save.
  `ProjectOptions.KeepSectionsNotEditedInSettings(previous)` carries them over (tested). `SoundOptions.ApplyPersonalDefault`
  holds the "seed a new project from my saved pop" rule so it is testable (the App project has no test project).
- Still no test for the WPF-only parts (Settings Sound section, Play button, AppSettings.DefaultPop persistence).

## 2026-10-06 - pop sound: 13 variants + selectable default (UNBUILT / UNTESTED - no dotnet in the authoring sandbox)

The unloved built-in `pop` is now a family of 13 synthesized variants (`SoundCatalog.PopVariants`,
recipes in `SoundEffectLibrary`, peaks levelled to about -3 dB): bubble, boop, drop, blip, tap, snap, cork,
thump, pluck, marimba, ping, sparkle, classic (= the old pop).
- New project option `sound.default_pop` (`SoundOptions`, default `pop_bubble` - a blind pick, change it freely).
  A shotlist's `"sfx": "pop"` plays that variant (`SoundEffectLibrary.ResolveAlias`); the brief is unchanged.
  Each variant is also usable by name (`"sfx": "pop_marimba"`). A `pop.wav` in the project's `sfx\` folder still wins.
- Settings > Sound: dropdown + Play (renders the sound with ffmpeg to temp and plays it) + section Reset.
- `SoundEffectResolver.ResolveAsync` gained `defaultPop` (both call sites pass `Options.Sound.DefaultPop`).
- Cache: files are `out\sfx\<variant>.wav`, so switching the default needs no cache clearing.
- TODO: `dotnet build` + `dotnet test` (3 new tests in RevealTests), listen to all 13, maybe a personal
  "Save as my default" like Resolution/Motion. Note: `BtnSave_Click` rebuilds ProjectOptions without
  carrying `Shotlist`, so saving Settings resets the prompt limits to defaults (pre-existing).

## 2026-10-05 — QUEUE: slide-in reveal (agreed, not started) + open items

**Slide-in reveal (requested by Kehinde and his brother).** With 3-4 items to reveal, each item
slides into its own slot, one after another, while the items already placed stay put. Direction is a
**per-channel setting** (right / left / top / bottom, plus "off" = today's cut).
Design agreed on: keep the existing reveal step stills; before each new item appears, a short slide
(~0.4 s) of that item over the previous step's still, then the main still switches to the next step
so the cut is invisible. The new item is its own full-canvas RGBA PNG (only that item, in its slot,
rest transparent) on a layer above the main picture, moving with Position keyframes (eased).
Needed in: `ManifestPlanner.ExpandReveals` (slide overlay clips + shifted switch point), `RevealImageWriter`
(RGBA item PNGs), the preview renderer (ffmpeg overlay with an animated position), Premiere
(`Fcp7XmlExporter`, extra video track), CapCut (extra track + keyframes), the WhisperRadar channel setting
and the shotlist brief wording. Build order: planner + settings, preview, Premiere, CapCut.
**Blocked on:** Premiere ignores Position keyframes today (PU/PD/PL/PR clips are static in Premiere; the
exported `premiere.xml` does contain them, in pixels relative to the frame centre). Kehinde is fixing that on
his laptop - build the slide on whatever keyframe form that fix proves Premiere accepts.

**Open items**
- Reveal "required" level (WhisperRadar channel setting, `2e9d3a2`): check on a real re-plan that the
  list passages now carry a `reveal` field. If a model still skips them, add a post-plan check that flags a
  listed passage planned as an ordinary shot.
- WhisperRadar Section 6B (presentation) says it does not change any other rule, so "No INF and HYB"-style
  rules there may be ignored by Section 5. Make the presentation text override Section 5 (and 8) if INF/HYB
  still appear. This also changes the copy-paste planner prompt - ask first.
- Idea, not built: after a FlowBatch "too many cards failed in a row" stop, run a second gallery recovery
  after the pause (before re-rendering) so cards Flow finished during the pause are not paid for twice.
- PU/PD/PL/PR jitter in the preview is clip-specific (planning and render code treat PU and PD as mirrors);
  left as is. If a pattern shows up, check that clip's image size and planned positions.
- `reveal-effect` is still unmerged in both repos; `master` in WhisperRadar carries the fix loop, the Start over
  fix, the Clear-all/temperature fix and the Flow gallery recovery (none pushed).

## 2026-09-30 — (FIXED 2026-10-06, see above) finish this branch (6 failing tests) + add Flow-native 1376×768 as a selectable preset

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

## 2026-09-28 — DONE: added Flow-native 1376×768 as a selectable preset (default unchanged)

Requested by Kehinde. Google Flow returns image masters at **1376×768** — that is
the real native size of a generated still, and anything larger is an upscale.

**Correction (2026-09-30): 2560×1440 stays the default.** 1376×768 is only
added as a selectable preset for projects sourced from Flow stills that want to
render at native size — not a change to what new projects default to.

- Defaults unchanged: `OutputOptions.Width/Height = 2560/1440`
  (`src/ImgToVideo.Core/Options/OutputOptions.cs:5-6`) — no edit needed here.
  `Timeline.Resolution` fallback (1920×1080,
  `src/ImgToVideo.Core/Models/Timeline.cs:9`) is unrelated and also untouched.
- Settings preset list (`src/ImgToVideo.App/SettingsWindow.xaml.cs`,
  `LoadResolutionPreset()`) now includes `1376 × 768 (Flow native)` alongside
  the existing HD 1920×1080 / 2K 2560×1440 / 4K 3840×2160 presets. 2K stays the
  default selection.
- Ratio note: 1376×768 is 1.7917, not exactly 16:9 (Flow's "9:16" master is
  768×1376). The preset uses the exact master size, not a nominal-ratio snap —
  FlowBatch's upscaler separately models `fit: exact` vs `fit: aspect` for
  anyone who wants to upscale toward a rounder ratio instead.

## 2026-09-30 (afternoon) — DONE: personal default resolution, saveable from Settings

Added a "Save as my default" button next to the Output > Resolution preset in
Settings. It writes Width/Height straight to the per-user `AppSettings` file
(`%AppData%\ImgToVideo\app.json` via `AppSettingsStore` - already used for
`LastProjectFolder`) as `DefaultOutputWidth`/`DefaultOutputHeight`, independent
of the dialog's own Save/Cancel. A status line under the button shows the
saved value, or says none is saved yet.

This is a personal, permanent preference, not a project setting: it only
seeds a BRAND NEW project's `imgtovideo.json` (MainWindow's "first analyze in
this folder" path) when one doesn't exist yet. Any project that already has
its own `imgtovideo.json` - including ones with the plain 2560x1440 default -
is never touched by it. With nothing saved, new projects keep defaulting to
2560x1440 exactly as before.

## 2026-09-30 (evening) — DONE: personal defaults extended to Motion and Transitions

Same pattern as the Output > Resolution "Save as my default" added earlier
today: Motion and Transitions sections in Settings each got their own
"Save as my default" button, writing the section's current values to the
per-user AppSettings file (`DefaultMotion` / `DefaultTransitions`,
MotionOptions/TransitionOptions serialized with the same enum converters
imgtovideo.json uses, so app.json stays human-readable - "EaseInOut", not a
bare int). A status line shows whether a default is saved.

Still a personal preference, not a project setting: only seeds a BRAND NEW
project's imgtovideo.json (MainWindow's first-analyze path); a project with
its own imgtovideo.json is never touched, and with nothing saved a new
project keeps ProjectOptions' own built-in Motion/Transitions defaults.

The Motion/Transitions construction that used to live inline in
BtnSave_Click is now `BuildMotionFromFields` / `BuildTransitionsFromFields`,
shared by the project Save button and the two new default buttons so the
parsing logic can't drift between them.

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

## 2026-09-24 — brief change QUEUED: reference naming convention + per-ref generation prompts

WhisperRadar now consumes shotlist refs in two ways that the brief does not yet
support: it **enforces a naming convention** on registry names, and it **generates
the refs whose file is missing** before running the image batch (so the batch can
attach them by name with `refMode: "assets"` and upload nothing). Both need the
brief to change. Docs only so far — nothing implemented here yet.

**The rule, per ref (agreed with Kehinde 2026-09-24):**

| Registry entry | What happens |
| --- | --- |
| path supplied and the file exists | **use it** — upload to the project gallery (FlowImagesGen `prepare` reports `uploaded`, or `reused` if already there) |
| path supplied but the file is missing on disk | treat as **no path** |
| no path at all | **generate it on the go** from its prompt |
| already in the Flow project gallery under that name | `reused` — nothing uploaded or generated |

The generation set is exactly what FlowImagesGen's `prepare` already reports as
`missing`, so no new signal is needed on that side.

**Naming convention (new, hard requirement):**

- `CH_` character · `BG_` background/environment · `OBJ_` object, then uppercase
  ASCII, `_` separators, zero-padded 2-digit variants:
  `^(CH|BG|OBJ)_[A-Z0-9]+(_[0-9]{2})?$` — e.g. `CH_MAYA`, `BG_BATHROOM_01`,
  `OBJ_ALARM_CLOCK_02`.
- **Mapping from the bible** (the bible stays human-readable): `CH_` + the bible
  name uppercased with every non-alphanumeric run replaced by `_`. "Maya" →
  `CH_MAYA`; "traditional Japanese home" → `BG_TRADITIONAL_JAPANESE_HOME`;
  a second bathroom plate → `BG_BATHROOM_02`.
- Why it matters: the name IS the asset identity in the Flow project. A generated
  ref has to be uploaded under the registry name, or the batch cannot attach it by
  name — and Flow's own asset names are auto-generated, so they never match.

**Schema — keep the registry as `name → path` and add a sibling map.** Do NOT
turn the value into `{path, prompt}`: `src/jobs/load.js` `buildRefMap()` does
`String(value)` and treats it as a path, so an object value silently degrades to
"attach by name" with a `local file not found` warning.

```json
"refs": {
  "CH_MAYA":        "D:/Refs/maya.png",     // provided -> used
  "BG_BATHROOM_01": null                   // no file -> generated from refPrompts
},
"refPrompts": {
  "BG_BATHROOM_01": "small Japanese bathroom, pale wood, shoji light, ..."
}
```

`refPrompts` is only read when a ref has no usable path; a ref with a supplied
file needs no prompt (the field may still carry one, unused).

**Files to change:**

1. `docs/manifest-authoring-brief.md` §6 (~line 88) — replace "names exactly as
   the bible gives them" with the convention + mapping above, and state the
   path/no-path rule. §7 shape (~lines 106-116) — registry example in the new
   names plus the `refPrompts` map; refs bullet (~line 128) and sheet line
   (~147) and checklist item 8 (~181) — same wording, and require a prompt for
   every ref that has no supplied path.
2. `docs/manifest-spec.md` (~130-134 schema, ~146 validation) — document the
   convention and `refPrompts`; keep the "names only, no disk access" validation
   split.
3. `src/jobs/load.js` — add `refPrompts` to `KNOWN_TOP_LEVEL` (~line 16) so it is
   not reported as an unrecognised key, and treat a null/empty registry value as
   "no local file — expected to be generated" instead of warning
   `local file not found at "null"`.
4. This file.

**Coordination:** WhisperRadar's shots gate ALREADY enforces the convention
(`studio.REF_NAME_RE`), so **until this brief change lands, any shotlist carrying
refs fails that gate** — production #6's `hero_kimono_woman` /
`traditional_japanese_home` would be rejected on a re-run. Its
`declared_refs()`/`ref_name()` already tolerate string or object registry values,
so the schema above needs no change on that side. WhisperRadar builds the small
`generate` job for the `missing` refs from `refPrompts`, then runs the real batch
in `refMode: "assets"`.

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

- **Reveal sound:** the built-in `pop` sound effect is not liked; replace or retune it (the other built-ins are unchanged). Raised 2026-10-06, for the next session.
