# ImgToVideo

Deterministic desktop tool that turns a folder of illustrations + narration audio + SRT into a
timed rough cut with restrained motion, and an editable Premiere timeline.

See [PLAN.md](PLAN.md) for the full architecture, rules, and phased build plan.
See [docs/generation-spec.md](docs/generation-spec.md) for the image generation contract.

## Status

- Phases 1–10 done: core models, timeline JSON round-trip, SRT parser, configurable filename
  parser, natural sort, project loader/inventory, scene inference + scenes.json override, timing
  engine (frame-exact coverage), motion engine (deterministic state machine + viewport math), the
  edit planner that assembles `timeline.json`, the FFmpeg renderer, crossfade transitions, and the
  Premiere FCP7 XML exporter (with motion keyframes + cuts-only fallback) — 129 unit tests
- Requires ffmpeg on PATH (or set in render settings) for preview rendering; install with
  `winget install Gyan.FFmpeg` or download from https://ffmpeg.org
- Manual spikes pending: phase 0 (import the exported XML into Premiere, verify motion keyframes
  survive — fallback: `IncludeMotionKeyframes = false`), phase 0b (CapCut draft study)
- Next: phase 11 (WPF shell + settings pages), CapCut tier 1/2 exporters

## Build

```
dotnet build ImgToVideo.sln
dotnet test ImgToVideo.sln
```
