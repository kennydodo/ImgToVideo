# Next session — player rebuild + frame nudge

Feasibility review completed 2026-09-11; all six items approved as possible.
Order and scope below. 161 tests green at commit `c530e81`.

## Bug fixes (do first)

1. **Autoplay**: MediaElement autoplay via MediaOpened is unreliable — also call
   Play() on window Loaded + reset position first. User had to click Restart.
2. **Muted previews (file lock)**: re-previewing the same clip fails the narration
   mux because the player still holds the previous MP4 (Windows lock) and the mux
   silently falls back to video-only. Stop/close any player holding the file
   before rendering, and surface mux failures as a dialog, not a status whisper.
   (Mux command itself verified correct: h264 + aac both present in output.)

## Player rebuild

3. **PlayerControl (UserControl)**: MediaElement + transport (Restart, Play/Pause)
   + **position slider + time label ("0:03 / 0:10")** updated on a ~100 ms
   DispatcherTimer; draggable to seek. Load(path, title) API.
4. **Dock/undock**: PlayerControl docked at the bottom of SceneEditorWindow
   (below the clip list); Undock button spawns ClipPreviewPlayerWindow hosting the
   same control; closing the floating window re-docks (playback resets — fine).
5. **Play all**: button plays `out\preview.mp4` (the BUILD PREVIEW output) in the
   player; if missing, status says to run BUILD PREVIEW. Reflects saved overrides
   only — surface that.

## Frame nudge (per-clip cut adjustment)

6. **Nudge slider per clip row** (± up to ~120 frames): shifts the cut AFTER that
   clip — row duration +δ, next row −δ; Frames boxes update live.
   - Within a scene: two duration overrides, siblings untouched.
   - Across a scene boundary: emit **absolute duration overrides for every clip
     in both scenes** (planned values with δ applied) — the timing engine accepts
     a full-set override when the sum equals the scene total; avoids sibling
     distortion.
   - Clamp: neighbors keep ≥ 1 frame.
   - Save: emit duration overrides for all rows whose Duration differs from
     PlannedDuration; when a nudge touches a scene, emit the whole scene's rows.
   - Implementation note: rows for ALL scenes must exist in the row model (not
     just the selected scene) so cross-scene nudges can update hidden rows.
   - Scene windows stay anchored to narration; nudges move picture cuts within
     the flow.

## After this batch (queue)

- CapCut tier 1/2 exporters, build-report.json, final-quality render option
- Phase 0 spike (Premiere import), phase 0b (CapCut draft study)
