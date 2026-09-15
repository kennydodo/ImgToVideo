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
- WPF shell: analyze/build/export UI, diagnostics panel (with COPY), full settings editor, and a scene editor
  (per-clip motion/easing/duration/exclude/order + per-cut transitions + single-clip preview)
  persisted to `overrides.json`
- LLM workflow: the LLM plans from the SRT in one pass — `shotlist.json`
  (image prompts + which filename covers which cues); the batch image app
  generates to those exact names; ANALYZE expands it into the manifest and
  the diagnostics COPY button feeds validation back to the LLM — 201 unit tests
- Transitions: 14 xfade modes, within-scene vs between-scene kinds, centered or late alignment;
  motion easing (linear/ease-in/ease-out/ease-in-out), globally or per clip
- Naming v2 (`S##_##_TYPE_MOTION.png`) supported: image type codes (SCN/CU/INF/CMP/PROC/HYB/OVR),
  vertical pans PU/PD, legacy names still parse; type codes and motion codes are independently
  toggleable in Settings > File naming
- Requires ffmpeg/ffprobe on PATH (or set in Settings); install with `winget install Gyan.FFmpeg`
- Final render: RENDER FINAL produces `out\final\final.mp4` at project resolution
  with Final preset/CRF, plus `captions.srt` — drop both into CapCut (CapCut tier 1).
  Encoder `auto` probes for NVENC/AMF/QSV and falls back to libx264
- Manual spikes pending: phase 0 (import exported XML into Premiere, verify keyframes — fallback
  is `IncludeMotionKeyframes = false`), phase 0b (CapCut draft study)
- Next: CapCut tier 2 draft exporter (spike-gated), headless CLI batch builds, batch image runner

## Build

```
dotnet build ImgToVideo.slnx
dotnet test ImgToVideo.slnx
```

## Batch image generation

Headless runner that turns a project's `shotlist.json` into `images\` in one command
(nano banana / Gemini image API):

```
dotnet run --project src/ImgToVideo.ImageGen -- <projectFolder> [--parallel 2] [--force] [--dry-run]
```

- Reads `images[]` (file → prompt) and the master `style` from `shotlist.json`
- Saves each image under its exact planned filename; existing files are skipped, so
  re-running only fills the gaps (`--force` regenerates everything)
- Retries rate limits/server errors with backoff; billing-exhausted keys fail fast
- Writes `out\image-review.html` — a contact sheet with filename, prompt and status per image
- API key comes from the `GEMINI_API_KEY` environment variable (or `--api-key`)

**Renderly mode** — route generation through a running
[Renderly](../../Renderly) backend instead of calling Gemini directly:

```
dotnet run --project src/ImgToVideo.ImageGen -- <projectFolder> ^
  --renderly http://127.0.0.1:8022 --channel 1 --image-size 1K --upscale 4 --ref-asset 12,13
```

- Generation lands in Renderly's channel history with per-file names; the finished
  image is downloaded into `images\` automatically
- `--image-size 1K` (default, cheap) then `--upscale 4` runs Renderly's local
  Real-ESRGAN GPU upscale — 4K-class images for pennies
- `--ref-asset <ids>` sends channel reference assets with every prompt
  (recurring character / style consistency)
- Uses Renderly's own API key; no `GEMINI_API_KEY` needed in this mode
