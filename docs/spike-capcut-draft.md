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
