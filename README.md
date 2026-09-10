# ImgToVideo

Deterministic desktop tool that turns a folder of illustrations + narration audio + SRT into a
timed rough cut with restrained motion, and an editable Premiere timeline.

See [PLAN.md](PLAN.md) for the full architecture, rules, and phased build plan.
See [docs/generation-spec.md](docs/generation-spec.md) for the image generation contract.

## Status

- Phases 1–11 done — the app is end-to-end usable: `dotnet run --project src/ImgToVideo.App`,
  pick a project folder, Analyze → Build Preview → Export to Premiere
- Core: models, timeline JSON round-trip, SRT parser, configurable filename parser, natural sort,
  project loader/inventory, scene inference + scenes.json override, timing engine (frame-exact
  coverage), deterministic motion engine (viewport rects), edit planner, validated options
- Ffmpeg: preview render plan (supersampled zoompan, crossfade joins), runner with
  progress/cancel, ffprobe audio duration
- Premiere: FCP7 XML exporter with motion keyframes + cuts-only fallback
- WPF shell: analyze/build/export UI, diagnostics panel, full settings editor persisted to
  `imgtovideo.json` — 143 unit tests
- Naming v2 (`S##_##_TYPE_MOTION.png`) supported: image type codes (SCN/CU/INF/CMP/PROC/HYB/OVR),
  vertical pans PU/PD, legacy names still parse; type codes and motion codes are independently
  toggleable in Settings > File naming
- Requires ffmpeg/ffprobe on PATH (or set in Settings); install with `winget install Gyan.FFmpeg`
- Manual spikes pending: phase 0 (import exported XML into Premiere, verify keyframes — fallback
  is `IncludeMotionKeyframes = false`), phase 0b (CapCut draft study)
- Next: CapCut tier 1/2 exporters, build-report.json, final-quality render option

## Build

```
dotnet build ImgToVideo.sln
dotnet test ImgToVideo.sln
```
