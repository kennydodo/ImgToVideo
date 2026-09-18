# Phase 0b spike — CapCut draft import study

Goal: decide whether the tier-2 CapCut draft exporter (cuts + media refs + motion
keyframes written into a `draft_content.json`) is worth building, or whether tier 1
(RENDER FINAL → `out\final\final.mp4` + `captions.srt`) stays the committed CapCut path.

CapCut desktop stores each project as a folder of JSON files; the timeline lives in
`draft_content.json`. There is no official interchange — this is reverse-engineered
territory (see the pyJianYingDraft project for prior art). Version-fragile by nature.

## Protocol (~1 hour, hands-on)

1. Install/open CapCut desktop, create a throwaway project, add two images to the
   timeline, and save. Note the CapCut version number.
2. Locate the draft folder:
   `%LOCALAPPDATA%\CapCut\User Data\Projects\com.lveditor.draft\<project>\`
   (JianYing variants use a similar path). Open `draft_content.json`.
3. Record the schema surface needed for an exporter:
   - canvas: width/height/fps fields
   - duration/timebase units (CapCut uses microseconds in most fields — verify)
   - materials → videos: how media files are referenced (absolute path? `file_id`?)
   - tracks/segments: how clip start/duration/target_timerange map to the timeline
   - keyframes: where scale/position/opacity keyframe arrays live per segment
     (`common_keyframes` with `keyframe_list`), and the property tag names
   - transitions between segments
4. Hand-edit a copy: change one clip's target_timerange and one scale keyframe, put
   the file back (keep a backup), reopen the draft in CapCut.
5. Verdict:
   - PASS → CapCut shows the two clips with the edited timing/scale → build
     `ImgToVideo.CapCut` exporter as a pure consumer of `timeline.json`
     (maps VideoClip viewports → scale/position keyframes, transitions → xfade
     equivalents, narration → audio segment).
   - FAIL → record what broke; tier 1 remains the path (already implemented via
     RENDER FINAL).

## Exit criteria

A dated note in this file: CapCut version, schema observations (or a link to notes),
PASS/FAIL, and if PASS the minimum stable subset the exporter will target.

## RESULT — PASS (2026-09-18)

CapCut desktop **9.4.0.4015** (draft `new_version 185.0.0`). Reference draft created by
hand (two images, no transition/keyframes), then hand-edited per protocol: clip 1
extended 5s→7s, clip 2 shifted, and `KFTypeScaleX`/`KFTypeScaleY` keyframes (1.0→1.3)
added — **CapCut reopened the draft and played the zoom. Verified by Kehinde.**

### Schema observations

- **Units: microseconds** everywhere (`duration 10000000` = 10 s). Stills get a nominal
  3-hour material duration (`10800000000`); `source_timerange` slices it.
- **Media**: `materials.videos[]` with absolute forward-slash `path`, `width`, `height`,
  `material_name`, `has_audio`.
- **Segment**: `{material_id, target_timerange {start,duration}, source_timerange,
  extra_material_refs (6 GUIDs), clip {scale{x,y}, rotation, transform{x,y}, flip, alpha},
  common_keyframes, speed}`. Segment order = timeline order.
- **Companion materials per segment (all 6 refs)**: `canvases` (canvas_color),
  `speeds` (speed 1.0), `sound_channel_mappings`, `material_colors`,
  `placeholder_infos`, `vocal_separations` — an exporter must emit these.
- **Keyframes**: `segment.common_keyframes[]` = `{id, keyframe_list[], material_id:"",
  property_type}`; keyframe = `{id, left_control/right_control {x:0.5,y:0.5},
  time_offset (µs, clip-relative), values[1]}`. Property tags: `KFTypeScaleX`,
  `KFTypeScaleY`, `KFTypePositionX`, `KFTypePositionY`, `KFTypeRotation`, `KFTypeAlpha`…
- **Canvas**: top-level `canvas_config {ratio:"original", width, height, background}` —
  `original` follows the first clip's aspect; an exporter pins `ratio:"16:9"` explicitly.
- **Registration**: the draft folder needs `draft_content.json` **and**
  `draft_meta_info.json` (draft_id, draft_name, draft_fold_path, tm_draft_create/modified,
  tm_duration, draft_materials…). `draft_content.json.bak` is CapCut's own backup.
- **CRITICAL GOTCHA**: `draft_content.json` must be **BOM-less UTF-8**. PowerShell 5.1's
  `UTF8` encoding writes a BOM and CapCut's parser then refuses to open the draft
  (silently — the project just fails to open). Write with `UTF8Encoding(false)`.

### Minimum stable subset for the exporter (v1) — SHIPPED & VERIFIED (2026-09-18)

- `src/ImgToVideo.CapCut` (`CapCutDraftExporter`) + `ImgToVideo.Cli export-capcut
  <projectFolder>` → writes `out\capcut\<name>\` (draft_content.json +
  draft_meta_info.json, BOM-less); copy the folder into the CapCut draft root
  and open. Verified by Kehinde: a real 4-clip + narration draft (26 s) opened
  in CapCut 9.4.0.4015 with correct timing and scale keyframes.
- Video track: one segment per VideoClip (µs timeranges, stills slice the 3-h nominal),
  six companion materials per segment (exact reference field shapes — material_colors
  has no `type` field), `canvas_config` pinned to the project resolution.
- Scale keyframes for ZI/ZO (`KFTypeScaleX/Y`, cover-fit = scale 1.0 baseline); ST static.
- Narration as an audio material (`extract_music`) + audio track segment.
- `draft_meta_info.json` emitted alongside (draft_id/name/path/times/duration/materials).
- **Deferred**: transform/position keyframes (CapCut transform units need one calibration
  capture — move a clip in CapCut, read `transform` values), transitions (need a reference
  draft with one), text/captions. Until then: PL/PR/PU/PD/PV export as static, all joins
  export as cuts.
