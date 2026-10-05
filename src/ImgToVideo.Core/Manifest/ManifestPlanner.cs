using ImgToVideo.Core.Models;
using ImgToVideo.Core.Motion;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;

namespace ImgToVideo.Core.Manifest;

public sealed record ManifestCoverage(
    double DurationSeconds,
    int ShotCount,
    int AssetCount,
    int UniqueAssetsUsed,
    double AverageSecondsPerSourceChange,
    double LongestSameSourceSeconds,
    double LongestShotSeconds,
    IReadOnlyList<string> MissingAssets,
    IReadOnlyList<string> UnusedAssets);

public sealed record ManifestPlanResult(
    Timeline? Timeline,
    ManifestCoverage? Coverage,
    IReadOnlyList<ValidationIssue> Issues);

public static class ManifestPlanner
{
    private static readonly Dictionary<string, ImageType> AssetTypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["closeup"] = ImageType.CloseUp,
            ["infographic"] = ImageType.Infographic,
            ["comparison"] = ImageType.Comparison,
            ["lifestyle"] = ImageType.Scene,
            ["character"] = ImageType.Scene,
        };

    public static ManifestPlanResult Plan(
        VisualManifest manifest,
        IReadOnlyList<ImageInfo> images,
        ProjectOptions options,
        long audioFrames,
        ProjectOverrides overrides,
        string? audioFilePath = null)
    {
        var issues = new List<ValidationIssue>();
        var fps = options.Output.Fps;

        if (manifest.Video.Fps is { } manifestFps && Math.Abs(manifestFps - fps) > 0.01)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "MANIFEST_FPS_MISMATCH",
                $"Manifest fps {manifestFps} does not match the project output fps {fps}."));
            return new ManifestPlanResult(null, null, issues);
        }

        var assetsByAssetId = new Dictionary<string, VisualAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets ?? [])
        {
            if (string.IsNullOrWhiteSpace(asset.Id))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "MANIFEST_INVALID",
                    "An asset entry is missing its id."));
                continue;
            }

            if (!assetsByAssetId.TryAdd(asset.Id, asset))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "MANIFEST_INVALID",
                    $"Asset id \"{asset.Id}\" is defined more than once."));
            }
        }

        var imagesByFileName = images.ToDictionary(
            i => Path.GetFileName(i.FilePath), i => i, StringComparer.OrdinalIgnoreCase);

        // Resolve shots to images. Missing files become generation hints; their screen
        // time is absorbed by the previous kept shot (or the first one at the head).
        var missingAssets = new List<string>();
        var planned = new List<(VisualShot Shot, VisualAsset Asset, ImageInfo Image)>();
        var revealSources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pendingDropMs = 0L;
        foreach (var shot in manifest.Timeline ?? [])
        {
            var durationMs = Math.Max(0, shot.EndMs - shot.StartMs);
            if (overrides.ForShot(shot.ShotId)?.Exclude == true)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "SHOT_EXCLUDED",
                    $"Shot \"{shot.ShotId}\" is excluded by overrides; its screen time was absorbed by the neighbouring shot."));
                AbsorbDrop(planned, shot, durationMs, ref pendingDropMs);
                continue;
            }

            if (shot.AssetId is not { } assetId)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "MANIFEST_INVALID",
                    $"Shot \"{shot.ShotId}\" references no asset_id; it was dropped."));
                AbsorbDrop(planned, shot, durationMs, ref pendingDropMs);
                continue;
            }

            if (!assetsByAssetId.TryGetValue(assetId, out var asset))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "MANIFEST_INVALID",
                    $"Shot \"{shot.ShotId}\" references unknown asset \"{assetId}\"; it was dropped."));
                AbsorbDrop(planned, shot, durationMs, ref pendingDropMs);
                continue;
            }

            if (!imagesByFileName.TryGetValue(asset.File, out var image) || image.Width <= 0)
            {
                missingAssets.Add(DescribeMissingAsset(shot, asset));
                AbsorbDrop(planned, shot, durationMs, ref pendingDropMs);
                continue;
            }

            if (shot.RevealAssets is { Count: > 0 } extraAssets)
            {
                // Build-up of separate images: every image must exist.
                var sources = new List<string>();
                var anyMissing = false;
                foreach (var extraId in extraAssets)
                {
                    if (assetsByAssetId.TryGetValue(extraId, out var extraAsset) &&
                        imagesByFileName.TryGetValue(extraAsset.File, out var extraImage) && extraImage.Width > 0)
                    {
                        sources.Add(extraImage.FilePath);
                    }
                    else
                    {
                        anyMissing = true;
                        missingAssets.Add(
                            $"Asset \"{extraId}\" (file \"images/{(assetsByAssetId.TryGetValue(extraId, out var known) ? known.File : extraId)}\") " +
                            $"is missing — generate it.\n  Used by reveal shot {shot.ShotId} (images appear one after another)");
                    }
                }

                if (anyMissing)
                {
                    AbsorbDrop(planned, shot, durationMs, ref pendingDropMs);
                    continue;
                }

                revealSources[shot.ShotId] = sources;
            }

            if (shot.EndMs <= shot.StartMs)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "MANIFEST_SHOT_EMPTY",
                    $"Shot \"{shot.ShotId}\" has end_ms <= start_ms; it was dropped."));
                continue;
            }

            var adjusted = pendingDropMs > 0
                ? shot with { StartMs = shot.StartMs - pendingDropMs }
                : shot;
            pendingDropMs = 0;
            planned.Add((adjusted, asset, image));
        }

        if (planned.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "MANIFEST_NO_SHOTS",
                "The manifest timeline has no renderable shots (all assets missing or invalid)."));
            return new ManifestPlanResult(null, null, issues);
        }

        // Quantize to frames, hold narration pauses on the previous shot (tiling, like
        // the v1 scene inference), and pin the tail to the audio length.
        var clips = new List<(VideoClip Clip, VisualAsset Asset)>();
        var reveals = new Dictionary<VideoClip, RevealPlan>();
        long cursor = 0;
        for (var i = 0; i < planned.Count; i++)
        {
            var (shot, asset, image) = planned[i];
            var start = (long)Math.Round(shot.StartMs * fps / 1000.0);
            var end = (long)Math.Round(shot.EndMs * fps / 1000.0);
            var duration = Math.Max(1, end - start);

            if (i == 0 && start > 0)
            {
                // Lead-in silence before the first cue belongs to the first image.
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "MANIFEST_TIMING_FIXED",
                    $"Manifest timeline started at frame {start}; the first shot was extended to cover from 0."));
                duration += start;
                start = 0;
            }

            if (i > 0 && start > cursor)
            {
                // Narration pause between shots: the previous image holds through it
                // and this shot starts exactly when its cue begins.
                var gap = start - cursor;
                if (gap > 2)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Info, "MANIFEST_TIMING_FIXED",
                        $"Narration pause of {gap / (double)fps:0.##}s before shot \"{shot.ShotId}\" " +
                        "is held by the previous shot."));
                }

                clips[^1].Clip.DurationFrames += gap;
                cursor = start;
            }
            else if (i > 0 && start < cursor)
            {
                // Overlapping cue ranges: clamp this shot to the previous shot's end.
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "MANIFEST_TIMING_FIXED",
                    $"Overlap before shot \"{shot.ShotId}\" ({cursor - start:+0;-0} frames) " +
                    "was closed by clamping to the previous shot."));
                duration = Math.Max(1, duration - (cursor - start));
                start = cursor;
            }

            if (i == planned.Count - 1 && start + duration != audioFrames)
            {
                var drift = start + duration - audioFrames;
                issues.Add(new ValidationIssue(
                    Math.Abs(drift) <= 2 ? ValidationSeverity.Info : ValidationSeverity.Warning,
                    "MANIFEST_TIMING_FIXED",
                    $"Manifest timeline ends {drift:+0;-0} frames off the audio duration " +
                    $"({audioFrames / fps:F2}s); the last shot was {(drift > 0 ? "trimmed" : "extended")} to match."));
                duration = Math.Max(1, audioFrames - start);
            }

            var clip = BuildClip(shot, asset, image, start, duration, fps, options, overrides, issues);
            if (shot.RevealAtMs is { Count: >= RevealLimits.MinItems } revealAt)
            {
                if (clip.Motion != MotionType.Static)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "REVEAL_NEEDS_STATIC",
                        $"Shot \"{shot.ShotId}\" asks for a reveal but its motion is {clip.Motion}; " +
                        "a reveal shot must be static (ST). The whole image is shown instead."));
                }
                else
                {
                    reveals[clip] = new RevealPlan(
                        revealAt,
                        RevealLayouts.Normalize(shot.RevealLayout),
                        revealSources.GetValueOrDefault(shot.ShotId));
                }
            }

            clips.Add((clip, asset));
            cursor = start + duration;
        }

        // Transitions: shot N's transition_out becomes the join into shot N+1.
        ApplyTransitions(clips, planned.Select(p => p.Shot).ToList(), fps, options, overrides);

        // Per-shot duration overrides (editor nudges), keyed by shot id.
        var overrideApplied = false;
        foreach (var (clip, _) in clips)
        {
            var shotOverride = overrides.ForShot(clip.ShotId!);
            if (shotOverride?.DurationFrames is { } frames && frames > 0 && frames != clip.DurationFrames)
            {
                clip.DurationFrames = frames;
                overrideApplied = true;
            }
        }

        var plannedSum = clips.Sum(c => c.Clip.DurationFrames);
        if (overrideApplied && plannedSum != audioFrames)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "MANIFEST_OVERRIDE_IGNORED",
                "Per-shot duration edits do not sum to the audio duration; ignoring them. " +
                "Use the nudge sliders (which transfer frames between shots) instead of editing single Frames boxes."));
            foreach (var (clip, _) in clips)
            {
                var shot = planned.First(p => p.Shot.ShotId == clip.ShotId).Shot;
                clip.DurationFrames = Math.Max(1,
                    (long)Math.Round(shot.EndMs * fps / 1000.0) -
                    (long)Math.Round(shot.StartMs * fps / 1000.0));
            }

            var fix = audioFrames - clips.Sum(c => c.Clip.DurationFrames);
            clips[^1].Clip.DurationFrames = Math.Max(1, clips[^1].Clip.DurationFrames + fix);
        }

        // Durations may have changed (editor nudges) — recompute start frames
        // so the timeline stays contiguous for the renderer and VerifyCoverage.
        var cursorFrames = 0L;
        foreach (var (clip, _) in clips)
        {
            clip.StartFrame = cursorFrames;
            cursorFrames += clip.DurationFrames;
        }

        // Per-shot audit: cue coverage and the final on-screen window for every shot.
        var warnHoldSeconds = options.Timing.WarnHoldSeconds;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i].Clip;
            var shot = planned[i].Shot;
            issues.Add(new ValidationIssue(
                ValidationSeverity.Info, "SHOT_TIMING",
                $"Shot \"{shot.ShotId}\" ({clip.SceneId}) · cues {FormatCues(shot.SrtCueIds)} · " +
                $"{FormatMs(clip.StartFrame * 1000.0 / fps)} → {FormatMs((clip.StartFrame + clip.DurationFrames) * 1000.0 / fps)} " +
                $"({clip.DurationFrames / (double)fps:F1} s) · \"{TruncateNarration(shot.NarrationText)}\""));

            // The LLM plans in cues and never computes seconds — long holds are how
            // lazy grouping shows up. Surface it deterministically so the COPY loop
            // can ask for a split. Advisory: the human decides where the boundary is.
            if (warnHoldSeconds > 0 &&
                clip.DurationFrames > (long)Math.Round(warnHoldSeconds * fps))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "SHOT_HOLD_LONG",
                    $"Shot \"{shot.ShotId}\" holds {clip.DurationFrames / (double)fps:0.#}s — verify it is " +
                    "one continuously developing idea, or split it in shotlist.json." +
                    (string.IsNullOrWhiteSpace(shot.NarrationText)
                        ? ""
                        : $"\n  Narration: \"{TruncateNarration(shot.NarrationText)}\"")));
            }
        }

        // Group consecutive shots with the same scene id into timeline scenes.
        var timeline = new Timeline
        {
            ProjectName = manifest.Video.Title ?? manifest.Video.Id ?? "manifest",
            Resolution = new Resolution(options.Output.Width, options.Output.Height),
            Fps = fps,
            Audio = new AudioTrack
            {
                FilePath = audioFilePath ?? string.Empty,
                DurationFrames = audioFrames,
            },
        };
        var finalClips = ExpandReveals(clips.Select(c => c.Clip).ToList(), reveals, fps, options, issues);
        timeline.Sounds.AddRange(BuildSounds(finalClips, planned.Select(p => p.Shot)));
        foreach (var group in finalClips.GroupBy(c => c.SceneId))
        {
            timeline.Scenes.Add(new Scene
            {
                Id = group.Key,
                StartFrame = group.First().StartFrame,
                EndFrame = group.Last().StartFrame + group.Last().DurationFrames,
                Clips = group.ToList(),
            });
        }

        var coverage = BuildCoverage(manifest, assetsByAssetId, clips, audioFrames, fps, missingAssets, issues);
        return new ManifestPlanResult(timeline, coverage, issues);
    }

    /// <summary>Frames a reveal step must hold before the next one may begin.</summary>
    private const long MinRevealStepFrames = 4;

    /// <summary>
    /// A reveal shot becomes consecutive clips: step k shows the first k slices of
    /// the image (the rest black), the last step the whole image. A step whose
    /// cue is too close to its neighbours is skipped; the next step then shows
    /// its slice as well, so the slice layout never changes.
    /// </summary>
    private static List<VideoClip> ExpandReveals(
        List<VideoClip> clips, Dictionary<VideoClip, RevealPlan> reveals,
        double fps, ProjectOptions options, List<ValidationIssue> issues)
    {
        if (reveals.Count == 0)
        {
            return clips;
        }

        var result = new List<VideoClip>();
        foreach (var clip in clips)
        {
            if (!reveals.TryGetValue(clip, out var plan))
            {
                result.Add(clip);
                continue;
            }

            var atMs = plan.AtMs;
            var count = atMs.Count;
            var layout = plan.Layout;
            if (layout == RevealLayouts.Grid && count != 4)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "REVEAL_GRID_NEEDS_FOUR",
                    $"Shot \"{clip.ShotId}\": a grid reveal needs exactly 4 items; using a row instead."));
                layout = RevealLayouts.Row;
            }

            if (plan.Sources is { } given && given.Count != count)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "REVEAL_ASSETS_COUNT",
                    $"Shot \"{clip.ShotId}\": {given.Count} images for {count} reveal cues; the reveal was ignored."));
                result.Add(clip);
                continue;
            }

            var sources = plan.Sources;
            // (offset from the clip start, slices visible)
            var steps = new List<(long Offset, int Visible)> { (0, 1) };
            for (var k = 2; k <= count; k++)
            {
                var offset = (long)Math.Round(atMs[k - 1] * fps / 1000.0) - clip.StartFrame;
                var last = steps[^1].Offset;
                if (offset < last + MinRevealStepFrames || offset > clip.DurationFrames - MinRevealStepFrames)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "REVEAL_STEP_SKIPPED",
                        $"Shot \"{clip.ShotId}\": item {k} appears too close to the previous item or to the " +
                        "end of the shot; it shows together with the next item instead."));
                    continue;
                }

                steps.Add((offset, k));
            }

            if (steps[^1].Visible != count)
            {
                // Trailing items were skipped: the last step shows the whole image.
                steps[^1] = (steps[^1].Offset, count);
            }

            var fadeFrames = (long)Math.Round(options.Transitions.RevealFadeSeconds * fps);
            for (var j = 0; j < steps.Count; j++)
            {
                var offset = steps[j].Offset;
                var end = j + 1 < steps.Count ? steps[j + 1].Offset : clip.DurationFrames;
                var step = new VideoClip
                {
                    FilePath = sources is not null
                        ? RevealPaths.StackPath(sources, steps[j].Visible, layout)
                        : RevealPaths.StepPath(clip.FilePath, steps[j].Visible, count, layout),
                    SceneId = clip.SceneId,
                    ShotId = j == 0 ? clip.ShotId : $"{clip.ShotId}.{steps[j].Visible}",
                    StartFrame = clip.StartFrame + offset,
                    DurationFrames = end - offset,
                    Motion = clip.Motion,
                    MotionSource = clip.MotionSource,
                    ImageType = clip.ImageType,
                    Easing = clip.Easing,
                    StartViewport = clip.StartViewport,
                    EndViewport = clip.EndViewport,
                    MotionDurationFrames = clip.MotionDurationFrames,
                    Transition = clip.Transition,
                    Reveal = new RevealInfo
                    {
                        Layout = layout,
                        Count = count,
                        Visible = steps[j].Visible,
                        BaseShotId = clip.ShotId,
                        SourcePath = sources is null ? clip.FilePath : null,
                        Sources = sources is null ? new List<string>() : sources.ToList(),
                    },
                };
                if (j > 0)
                {
                    var previousFrames = steps[j].Offset - steps[j - 1].Offset;
                    var frames = Math.Min(fadeFrames, Math.Min(previousFrames, step.DurationFrames) - 2);
                    step.Transition = frames >= 2
                        ? new TransitionIn { Kind = TransitionKind.Crossfade, DurationFrames = frames }
                        : null;
                }

                result.Add(step);
            }
        }

        return result;
    }

    private sealed record RevealPlan(IReadOnlyList<long> AtMs, string Layout, List<string>? Sources);

    /// <summary>
    /// Sound effects: a normal shot plays its first sfx when it starts; a reveal shot plays one
    /// per step that appears (a single name plays at every step, a list gives one per item and
    /// repeats its last name).
    /// </summary>
    private static List<SoundEffect> BuildSounds(IReadOnlyList<VideoClip> clips, IEnumerable<VisualShot> shots)
    {
        var byShot = shots
            .Where(s => s.Sfx is { Count: > 0 })
            .GroupBy(s => s.ShotId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Sfx!, StringComparer.Ordinal);
        var sounds = new List<SoundEffect>();
        if (byShot.Count == 0)
        {
            return sounds;
        }

        foreach (var clip in clips)
        {
            var id = clip.Reveal?.BaseShotId ?? clip.ShotId;
            if (id is null || !byShot.TryGetValue(id, out var names))
            {
                continue;
            }

            var index = clip.Reveal is { } reveal ? Math.Min(reveal.Visible - 1, names.Count - 1) : 0;
            sounds.Add(new SoundEffect { Name = names[index], StartFrame = clip.StartFrame });
        }

        return sounds;
    }

    private static void AbsorbDrop(
        List<(VisualShot Shot, VisualAsset Asset, ImageInfo Image)> planned,
        VisualShot dropped, long durationMs, ref long pendingDropMs)
    {
        if (planned.Count > 0)
        {
            var previous = planned[^1];
            planned[^1] = (previous.Shot with { EndMs = previous.Shot.EndMs + durationMs },
                previous.Asset, previous.Image);
        }
        else
        {
            pendingDropMs += durationMs;
        }
        _ = dropped;
    }

    private static string FormatCues(IReadOnlyList<int>? cues)
    {
        if (cues is null || cues.Count == 0)
        {
            return "?";
        }

        var ordered = cues.Distinct().OrderBy(c => c).ToList();
        var parts = new List<string>();
        var runStart = ordered[0];
        var runEnd = runStart;
        for (var i = 1; i <= ordered.Count; i++)
        {
            if (i < ordered.Count && ordered[i] == runEnd + 1)
            {
                runEnd = ordered[i];
                continue;
            }

            parts.Add(runStart == runEnd ? $"{runStart}" : $"{runStart}-{runEnd}");
            if (i < ordered.Count)
            {
                runStart = runEnd = ordered[i];
            }
        }

        return string.Join(", ", parts);
    }

    private static string FormatMs(double ms)
    {
        var total = (long)Math.Round(ms);
        return $"{(int)(total / 60000)}:{total / 1000 % 60:00}.{total % 1000:000}";
    }

    private static string TruncateNarration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        var clean = text.Trim();
        return clean.Length <= 90 ? clean : clean[..90] + "…";
    }

    private static VideoClip BuildClip(
        VisualShot shot, VisualAsset asset, ImageInfo image,
        long start, long duration, double fps, ProjectOptions options,
        ProjectOverrides overrides, List<ValidationIssue> issues)
    {
        var shotOverride = overrides.ForShot(shot.ShotId!);

        // Motion precedence: the shotlist entry first, then the motion code encoded
        // in the asset filename (the image was composed for that motion and overscan),
        // then the documented STATIC default. A Scene Editor override wins over all three.
        MotionType motion;
        var motionText = shot.Motion?.Type ?? "STATIC";
        var motionSource = MotionSource.ExplicitCode;
        if (!TryParseMotion(motionText, out motion))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "MANIFEST_MOTION_UNKNOWN",
                $"Shot \"{shot.ShotId}\" requests unknown motion \"{motionText}\"; using STATIC."));
            motion = MotionType.Static;
            motionText = "STATIC";
            motionSource = MotionSource.AutoSelected;
        }
        else if (shot.Motion is null)
        {
            if (image.Name.Code is { } fromName)
            {
                motion = fromName;
                motionText = fromName.ToString().ToUpperInvariant();
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "MOTION_FROM_FILENAME",
                    $"Shot \"{shot.ShotId}\" specifies no motion; using the filename code {motionText} " +
                    "(the image was composed for it)."));
            }
            else
            {
                motion = MotionType.Static;
                motionText = "STATIC";
                motionSource = MotionSource.AutoSelected;
            }
        }

        // Scene Editor overrides win over the shotlist: the user deliberately
        // changed the motion/easing for this shot.
        if (shotOverride?.Motion is { } overrideMotion)
        {
            motion = overrideMotion;
            motionText = overrideMotion.ToString().ToUpperInvariant();
            motionSource = MotionSource.Override;
        }

        var safeMotion = (asset.SafeMotion ?? [])
            .Select(s => s.ToUpperInvariant())
            .ToHashSet();
        if (shotOverride?.Motion is null && safeMotion.Count > 0 && motion != MotionType.Static &&
            !safeMotion.Contains(motionText.ToUpperInvariant()))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "MANIFEST_UNSAFE_MOTION",
                $"Shot \"{shot.ShotId}\" uses {motionText.ToUpperInvariant()} but asset " +
                $"\"{asset.Id}\" declares safe motion: {string.Join(", ", asset.SafeMotion ?? [])}."));
        }

        long? motionDurationFrames = null;
        var motionMs = shot.Motion?.DurationMs ?? options.Motion.MotionDurationMs;
        if (motionMs > 0 && motion != MotionType.Static)
        {
            var motionFrames = (long)Math.Round(motionMs * fps / 1000.0);
            if (motionFrames < duration)
            {
                motionDurationFrames = Math.Max(2, motionFrames);
            }
        }

        var (startViewport, endViewport) = ResolveViewports(
            shot, asset, motion, motionSource, motionText, image, options, issues);

        // A pan's actual on-screen motion runs for motionDurationFrames when set,
        // otherwise the whole clip (see MotionDurationMs). Long pans need Linear
        // instead of the default Ease-In-Out, or the motion visibly stalls well
        // before the clip ends - see LongPanLinearThresholdSeconds for why.
        var effectiveMotionSeconds = (motionDurationFrames ?? duration) / fps;
        var easing = shotOverride?.Easing ?? ResolveEasing(motion, effectiveMotionSeconds, options.Motion);

        return new VideoClip
        {
            FilePath = image.FilePath,
            ShotId = shot.ShotId,
            SceneId = shot.SceneId ?? "S01",
            StartFrame = start,
            DurationFrames = duration,
            Motion = motion,
            MotionSource = motionSource,
            ImageType = AssetTypeMap.TryGetValue(asset.Type ?? "", out var imageType)
                ? imageType
                : null,
            Easing = easing,
            StartViewport = startViewport,
            EndViewport = endViewport,
            MotionDurationFrames = motionDurationFrames,
        };
    }

    private static EasingMode ResolveEasing(MotionType motion, double motionSeconds, MotionOptions motionOptions)
    {
        if (IsPanMotion(motion) && motionSeconds > motionOptions.LongPanLinearThresholdSeconds)
        {
            return EasingMode.Linear;
        }

        return motionOptions.Easing;
    }

    private static bool IsPanMotion(MotionType motion) =>
        motion is MotionType.PanLeft or MotionType.PanRight or MotionType.PanUp
            or MotionType.PanDown or MotionType.PanReveal;

    private static void ApplyTransitions(
        List<(VideoClip Clip, VisualAsset Asset)> clips,
        IReadOnlyList<VisualShot> shots, double fps, ProjectOptions options,
        ProjectOverrides overrides)
    {
        // The outgoing shot's transition_out drives the join into the next shot; the
        // last shot's transition_out is ignored. Joins without an explicit transition
        // fall back to the project's transition settings — the between-scenes kind
        // when the join crosses a scene boundary, otherwise the within-scenes kind.
        // A Scene Editor cut override on the incoming clip wins over both.
        for (var i = 0; i + 1 < clips.Count; i++)
        {
            var incoming = clips[i + 1].Clip;
            var transition = shots[i].TransitionOut;
            var type = (transition?.Type ?? "CUT").ToUpperInvariant();
            var frames = (long)Math.Round((transition?.DurationMs ?? 0) * fps / 1000.0);

            TransitionIn? incomingTransition;
            var editorCut = overrides.CutFor(incoming.FilePath);
            if (editorCut is { } forced)
            {
                var editorFrames = (long)Math.Round(options.Transitions.DurationSeconds * fps);
                incomingTransition = forced == TransitionKind.None || editorFrames < 2
                    ? null
                    : new TransitionIn { Kind = forced, DurationFrames = editorFrames };
            }
            else if (transition is not null && type != "CUT")
            {
                incomingTransition = transition switch
                {
                    _ when type == "CROSSFADE" && frames >= 2 => new TransitionIn
                    {
                        Kind = TransitionKind.Crossfade,
                        DurationFrames = frames,
                    },
                    _ when type == "DIP" && frames >= 2 => new TransitionIn
                    {
                        Kind = TransitionKind.FadeBlack,
                        DurationFrames = frames,
                    },
                    _ when type == "DIP_WHITE" && frames >= 2 => new TransitionIn
                    {
                        Kind = TransitionKind.FadeWhite,
                        DurationFrames = frames,
                    },
                    _ => null,
                };
            }
            else if (options.Transitions.Enabled)
            {
                var isSceneBoundary = !string.Equals(
                    clips[i].Clip.SceneId, incoming.SceneId, StringComparison.OrdinalIgnoreCase);
                var settingsFrames = (long)Math.Round(options.Transitions.DurationSeconds * fps);
                incomingTransition = settingsFrames >= 2
                    ? new TransitionIn
                    {
                        Kind = isSceneBoundary
                            ? options.Transitions.SceneBoundaryKind
                            : options.Transitions.Kind,
                        DurationFrames = settingsFrames,
                    }
                    : null;
            }
            else
            {
                incomingTransition = null;
            }

            incoming.Transition = incomingTransition;
        }
    }

    private static string DescribeMissingAsset(VisualShot shot, VisualAsset asset)
    {
        var narration = string.IsNullOrWhiteSpace(shot.NarrationText)
            ? ""
            : $"\n  Narration: \"{shot.NarrationText}\"";
        var intent = shot.VisualIntent is null ? "" : $"\n  Intent: {shot.VisualIntent}";
        var change = shot.ChangeReason is null ? "" : $" ({shot.ChangeReason})";
        return $"Asset \"{asset.Id}\" (file \"images/{asset.File}\") is missing — generate it." +
               $"\n  Used by shot {shot.ShotId} for beat {shot.BeatId ?? "?"}{change}{intent}{narration}";
    }

    private static bool TryParseMotion(string text, out MotionType motion)
    {
        var normalized = text.Trim().ToUpperInvariant();
        if (normalized is "STATIC" or "NONE")
        {
            motion = MotionType.Static;
            return true;
        }

        return MotionCodes.TryFromSuffix(normalized, out motion);
    }

    private static (Rect Start, Rect End) ResolveViewports(
        VisualShot shot,
        VisualAsset asset,
        MotionType motion,
        MotionSource motionSource,
        string motionText,
        ImageInfo image,
        ProjectOptions options,
        List<ValidationIssue> issues)
    {
        if (motion is not (MotionType.Static or MotionType.ZoomIn or MotionType.ZoomOut))
        {
            // Pans ride the motion engine's travel bands: explicit codes (shotlist
            // or filename) use the full overscan band, so the pan performs the
            // composition's intent.
            var panPlan = new MotionEngine(options.Motion, options.Output)
                .PlanClip(motion, motionSource, panRight: motion != MotionType.PanLeft,
                    image.Width, image.Height);
            return (panPlan.Start, panPlan.End);
        }

        var baseRect = ResolveBaseRect(shot, asset, image, options, issues);
        var (s0, s1) = ResolveScales(shot, motion, options.Motion);
        return (ScaleRect(baseRect, s0, image), ScaleRect(baseRect, s1, image));
    }

    private static Rect ResolveBaseRect(
        VisualShot shot, VisualAsset asset, ImageInfo image, ProjectOptions options,
        List<ValidationIssue> issues)
    {
        var framing = shot.Framing?.Type?.ToLowerInvariant() ?? "wide";
        var w = (double)image.Width;
        var h = (double)image.Height;

        switch (framing)
        {
            case "wide":
                return new Rect(0, 0, w, h);
            case "medium":
                return CenteredBand(w, h, 0.85);
            case "close":
                return CenteredBand(w, h, 0.70);
            case "detail":
                return CenteredBand(w, h, 0.50);
            case "crop":
            {
                var regionId = shot.Framing?.FocalRegion;
                var region = (asset.FocalRegions ?? []).FirstOrDefault(r =>
                    string.Equals(r.Id, regionId, StringComparison.OrdinalIgnoreCase));
                if (region is null)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "MANIFEST_FOCAL_MISSING",
                        $"Shot \"{shot.ShotId}\" requests crop \"{regionId ?? "?"}\" but asset " +
                        $"\"{asset.Id}\" defines no such focal region; using the full image."));
                    return new Rect(0, 0, w, h);
                }

                var rect = new Rect(region.X * w, region.Y * h, region.Width * w, region.Height * h);
                var target = (double)options.Output.Width / options.Output.Height;
                var aspect = rect.Width / rect.Height;
                if (Math.Abs(aspect - target) > 0.2)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "MANIFEST_FOCAL_REFRAMED",
                        $"Shot \"{shot.ShotId}\": focal region \"{region.Id}\" has aspect " +
                        $"{aspect:F2} vs output {target:F2}; the viewport was re-framed to the largest " +
                        "16:9 window inside the region."));
                }

                double newW, newH;
                if (aspect > target)
                {
                    newW = rect.Height * target;
                    newH = rect.Height;
                }
                else
                {
                    newW = rect.Width;
                    newH = rect.Width / target;
                }

                var newX = rect.X + (rect.Width - newW) / 2;
                var newY = rect.Y + (rect.Height - newH) / 2;
                return new Rect(newX, newY, newW, newH);
            }
            default:
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "MANIFEST_FRAMING_UNKNOWN",
                    $"Shot \"{shot.ShotId}\" requests unknown framing \"{framing}\"; using wide."));
                return new Rect(0, 0, w, h);
        }
    }

    private static Rect CenteredBand(double width, double height, double fraction) =>
        new(0, (height - height * fraction) / 2, width, height * fraction);

    private static (double Start, double End) ResolveScales(
        VisualShot shot, MotionType motion, MotionOptions motionOptions)
    {
        // Explicit shotlist scales win; otherwise the project's zoom percent
        // settings (Settings -> Motion) drive the zoom range.
        var startScale = shot.Motion?.StartScale;
        var endScale = shot.Motion?.EndScale;
        return motion switch
        {
            MotionType.ZoomIn => (
                startScale ?? motionOptions.PushInStartPercent / 100.0,
                endScale ?? motionOptions.PushInEndPercent / 100.0),
            MotionType.ZoomOut => (
                startScale ?? motionOptions.ZoomOutStartPercent / 100.0,
                endScale ?? motionOptions.ZoomOutEndPercent / 100.0),
            _ => (startScale ?? 1.0, endScale ?? startScale ?? 1.0),
        };
    }

    private static Rect ScaleRect(Rect rect, double scale, ImageInfo image)
    {
        var safeScale = Math.Max(1.0, scale);
        var w = rect.Width / safeScale;
        var h = rect.Height / safeScale;
        var cx = rect.X + rect.Width / 2.0;
        var cy = rect.Y + rect.Height / 2.0;
        var x = Math.Clamp(cx - w / 2.0, 0, Math.Max(0, image.Width - w));
        var y = Math.Clamp(cy - h / 2.0, 0, Math.Max(0, image.Height - h));
        return new Rect(x, y, w, h);
    }

    private static ManifestCoverage BuildCoverage(
        VisualManifest manifest,
        Dictionary<string, VisualAsset> assetsByAssetId,
        List<(VideoClip Clip, VisualAsset Asset)> clips,
        long audioFrames, double fps,
        List<string> missingAssets, List<ValidationIssue> issues)
    {
        var usedAssets = clips.Select(c => c.Asset.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unusedAssets = (manifest.Assets ?? [])
            .Where(a => a.Id is not null &&
                        assetsByAssetId.ContainsKey(a.Id) &&
                        !usedAssets.Contains(a.Id))
            .Select(a => $"{a.Id} ({a.File})")
            .ToList();

        foreach (var unused in unusedAssets)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Info, "MANIFEST_ASSET_UNUSED",
                $"Asset {unused} is not referenced by any shot."));
        }

        var longestSameSource = 0L;
        var sameSourceRun = 0L;
        string? previousAsset = null;
        var sourceChanges = 0;
        foreach (var (clip, asset) in clips)
        {
            if (asset.Id == previousAsset)
            {
                sameSourceRun += clip.DurationFrames;
            }
            else
            {
                if (previousAsset is not null)
                {
                    longestSameSource = Math.Max(longestSameSource, sameSourceRun);
                    sourceChanges++;
                }

                sameSourceRun = clip.DurationFrames;
            }

            previousAsset = asset.Id;
        }

        longestSameSource = Math.Max(longestSameSource, sameSourceRun);

        return new ManifestCoverage(
            audioFrames / fps,
            clips.Count,
            assetsByAssetId.Count,
            usedAssets.Count,
            sourceChanges == 0 ? 0 : audioFrames / fps / Math.Max(1, sourceChanges),
            longestSameSource / fps,
            clips.Count == 0 ? 0 : clips.Max(c => c.Clip.DurationFrames) / fps,
            missingAssets,
            unusedAssets);
    }
}
