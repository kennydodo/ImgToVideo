using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Motion;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Timing;

namespace ImgToVideo.Core.Planning;

public sealed record PlanningResult(
    bool Success,
    Timeline? Timeline,
    IReadOnlyList<ValidationIssue> Issues,
    ManifestCoverage? Coverage = null);

public static class EditPlanner
{
    public static PlanningResult Plan(ProjectInventory inventory, double audioDurationSeconds, ProjectOptions options)
    {
        var issues = new List<ValidationIssue>(inventory.Issues);

        if (ValidationIssue.HasErrors(issues))
        {
            return new PlanningResult(false, null, issues);
        }

        if (inventory.AudioFilePath is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "PLANNER_NO_AUDIO", "No audio file; cannot build."));
            return new PlanningResult(false, null, issues);
        }

        if (inventory.Manifest is { } manifest)
        {
            if (!string.Equals(options.Planner, "v2", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "MANIFEST_IGNORED",
                    "visual_manifest.json found but \"planner\" is \"v1\" — set \"planner\": \"v2\" in " +
                    "imgtovideo.json to plan from the manifest."));
            }
            else
            {
                var manifestResult = ManifestPlanner.Plan(
                    manifest,
                    inventory.AllImages,
                    options,
                    (long)Math.Round(audioDurationSeconds * options.Output.Fps),
                    inventory.Overrides);
                issues.AddRange(manifestResult.Issues);
                if (manifestResult.Timeline is null || ValidationIssue.HasErrors(issues))
                {
                    return new PlanningResult(false, null, issues);
                }

                if (!VerifyCoverage(manifestResult.Timeline, audioDurationSeconds, issues))
                {
                    return new PlanningResult(false, null, issues);
                }

                return new PlanningResult(true, manifestResult.Timeline, issues, manifestResult.Coverage);
            }
        }

        var sceneInputs = BuildSceneInputs(inventory, audioDurationSeconds, options, issues);
        if (ValidationIssue.HasErrors(issues))
        {
            return new PlanningResult(false, null, issues);
        }

        var timing = TimingEngine.Plan(
            sceneInputs,
            audioDurationSeconds,
            options,
            DurationOverrideLookup(inventory));
        issues.AddRange(timing.Issues);
        if (ValidationIssue.HasErrors(issues))
        {
            return new PlanningResult(false, null, issues);
        }

        var timeline = AssembleTimeline(inventory, timing, audioDurationSeconds, options, issues);
        if (!VerifyCoverage(timeline, audioDurationSeconds, issues))
        {
            return new PlanningResult(false, null, issues);
        }

        return new PlanningResult(true, timeline, issues);
    }

    private static List<ScenePlanInput> BuildSceneInputs(
        ProjectInventory inventory,
        double audioDurationSeconds,
        ProjectOptions options,
        List<ValidationIssue> issues)
    {
        var groups = inventory.SceneGroups;
        if (groups.Count == 0)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "PLANNER_NO_SCENES", "No image scenes found."));
            return [];
        }

        if (inventory.SceneMapWindows is { } mapWindows)
        {
            if (mapWindows.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SCENE_MAP_EMPTY",
                    "scenes.json exists but defines no usable scenes."));
                return [];
            }

            return AlignBySceneId(mapWindows, groups, audioDurationSeconds, issues);
        }

        if (inventory.Subtitles.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "PLANNER_NO_SUBTITLES",
                "No subtitle blocks available for scene inference."));
            return [];
        }

        var inferred = SceneInference.Infer(inventory.Subtitles, options.SceneInference);
        return AlignByOrder(inferred, groups, audioDurationSeconds, issues);
    }

    private static List<ScenePlanInput> AlignBySceneId(
        IReadOnlyList<SceneWindow> windows,
        IReadOnlyList<SceneImageGroup> groups,
        double audioDurationSeconds,
        List<ValidationIssue> issues)
    {
        var tiled = TileWindows(windows, audioDurationSeconds, issues, warnOnAdjustment: true);
        var groupById = groups.ToDictionary(g => g.SceneId, StringComparer.OrdinalIgnoreCase);
        var inputs = new List<ScenePlanInput>();

        foreach (var window in tiled)
        {
            if (groupById.TryGetValue(window.SceneId, out var group))
            {
                inputs.Add(new ScenePlanInput(group.SceneId, window.StartSeconds, window.EndSeconds, group.Images));
                groupById.Remove(window.SceneId);
            }
            else
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SCENE_MAP_NO_IMAGES",
                    $"Scene map defines {window.SceneId} but no images match it."));
            }
        }

        foreach (var orphan in groupById.Values)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SCENE_MAP_UNCOVERED",
                $"Scene {orphan.SceneId} has {orphan.Images.Count} images but no entry in scenes.json."));
        }

        return inputs;
    }

    private static List<ScenePlanInput> AlignByOrder(
        IReadOnlyList<SceneWindow> windows,
        IReadOnlyList<SceneImageGroup> groups,
        double audioDurationSeconds,
        List<ValidationIssue> issues)
    {
        if (windows.Count != groups.Count)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "SCENE_COUNT_MISMATCH",
                $"Narration suggests {windows.Count} scenes but images define {groups.Count}; " +
                "images were distributed evenly across the audio duration (boundaries snapped to " +
                "sentence pauses). Provide scenes.json to control which narration range each scene covers."));
            return DistributeEvenly(groups, audioDurationSeconds, windows, issues);
        }

        var tiled = TileWindows(windows, audioDurationSeconds, issues, warnOnAdjustment: false);
        var inputs = new List<ScenePlanInput>(tiled.Count);
        for (var i = 0; i < tiled.Count; i++)
        {
            inputs.Add(new ScenePlanInput(
                groups[i].SceneId, tiled[i].StartSeconds, tiled[i].EndSeconds, groups[i].Images));
        }

        return inputs;
    }

    private static List<ScenePlanInput> DistributeEvenly(
        IReadOnlyList<SceneImageGroup> groups,
        double audioDurationSeconds,
        IReadOnlyList<SceneWindow> sentenceWindows,
        List<ValidationIssue> issues)
    {
        var count = groups.Count;
        var inputs = new List<ScenePlanInput>(count);
        double previousBoundary = 0;
        var snappedCount = 0;

        for (var i = 0; i < count; i++)
        {
            double end;
            if (i == count - 1)
            {
                end = audioDurationSeconds;
            }
            else
            {
                var raw = audioDurationSeconds * (i + 1) / count;
                var min = previousBoundary + Pacing.FloorImageSeconds;
                var max = audioDurationSeconds - (count - 1 - i) * Pacing.FloorImageSeconds;
                end = SnapBoundary(raw, min, max, sentenceWindows, ref snappedCount);
            }

            inputs.Add(new ScenePlanInput(groups[i].SceneId, previousBoundary, end, groups[i].Images));
            previousBoundary = end;
        }

        if (snappedCount > 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Info, "SCENE_BOUNDARIES_SNAPPED",
                $"{snappedCount} of {count - 1} scene boundaries snapped to sentence pauses."));
        }

        return inputs;
    }

    private static double SnapBoundary(
        double raw,
        double min,
        double max,
        IReadOnlyList<SceneWindow> sentenceWindows,
        ref int snappedCount)
    {
        const double MaxSnapSeconds = 2.5;

        var best = raw;
        var bestDistance = MaxSnapSeconds;
        foreach (var window in sentenceWindows)
        {
            var candidate = window.EndSeconds;
            if (candidate <= min || candidate >= max)
            {
                continue;
            }

            var distance = Math.Abs(candidate - raw);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (Math.Abs(best - raw) > 0.001)
        {
            snappedCount++;
        }

        return best;
    }

    private static List<SceneWindow> TileWindows(
        IReadOnlyList<SceneWindow> windows,
        double audioDurationSeconds,
        List<ValidationIssue> issues,
        bool warnOnAdjustment)
    {
        var tiled = windows
            .OrderBy(w => w.StartSeconds)
            .Select(w => new SceneWindow(w.SceneId, w.StartSeconds, w.EndSeconds))
            .ToList();

        if (tiled.Count == 0)
        {
            return tiled;
        }

        if (tiled[0].StartSeconds > 0.001)
        {
            if (warnOnAdjustment)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "SCENE_MAP_LEAD_GAP",
                    $"First scene starts at {tiled[0].StartSeconds:F2}s; extended to 0s to cover the audio start."));
            }

            tiled[0] = tiled[0] with { StartSeconds = 0 };
        }

        for (var i = 1; i < tiled.Count; i++)
        {
            if (tiled[i].StartSeconds > tiled[i - 1].EndSeconds + 0.001)
            {
                if (warnOnAdjustment)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "SCENE_MAP_GAP",
                        $"Gap between {tiled[i - 1].SceneId} and {tiled[i].SceneId}; " +
                        $"{tiled[i - 1].SceneId} extended to cover it."));
                }

                tiled[i - 1] = tiled[i - 1] with { EndSeconds = tiled[i].StartSeconds };
            }
        }

        if (tiled[^1].EndSeconds < audioDurationSeconds - 0.001)
        {
            if (warnOnAdjustment)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "SCENE_MAP_TRAIL_GAP",
                    $"Last scene ends at {tiled[^1].EndSeconds:F2}s; extended to audio end " +
                    $"{audioDurationSeconds:F2}s."));
            }

            tiled[^1] = tiled[^1] with { EndSeconds = audioDurationSeconds };
        }

        return tiled;
    }

    private static IReadOnlyDictionary<string, long>? DurationOverrideLookup(ProjectInventory inventory) =>
        inventory.Overrides.Clips
            .Where(c => c.DurationFrames is not null)
            .GroupBy(c => c.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DurationFrames!.Value, StringComparer.OrdinalIgnoreCase);

    private static Timeline AssembleTimeline(
        ProjectInventory inventory,
        TimingResult timing,
        double audioDurationSeconds,
        ProjectOptions options,
        List<ValidationIssue> issues)
    {
        var motionEngine = new MotionEngine(options.Motion, options.Output);
        var fps = options.Output.Fps;
        var transitionFrames = (long)Math.Round(options.Transitions.DurationSeconds * fps);

        var timeline = new Timeline
        {
            ProjectName = Path.GetFileName(inventory.ProjectFolder),
            Resolution = new Resolution(options.Output.Width, options.Output.Height),
            Fps = fps,
            Audio = new AudioTrack
            {
                FilePath = inventory.AudioFilePath!,
                DurationFrames = (long)Math.Round(audioDurationSeconds * fps),
            },
        };

        MotionType? previous = null;
        var previousSceneId = string.Empty;
        var shotsSinceStatic = 0;

        foreach (var scene in timing.Scenes)
        {
            var timedClips = scene.Clips;
            var sceneClips = new List<VideoClip>(timedClips.Count);

            for (var i = 0; i < timedClips.Count; i++)
            {
                var timed = timedClips[i];
                var image = inventory.AllImages.First(img => img.FilePath == timed.FilePath);

                var ctx = new MotionContext(
                    timed.ExplicitMotion,
                    IsVideoStart: scene == timing.Scenes[0] && i == 0,
                    IsVideoEnd: scene == timing.Scenes[^1] && i == timedClips.Count - 1,
                    IsSceneStart: i == 0,
                    previous,
                    shotsSinceStatic);

                var decision = motionEngine.SelectMotion(ctx);
                var clipOverride = inventory.Overrides.ForClip(timed.FilePath);
                var requestedMotion = clipOverride?.Motion ?? decision.Motion;
                var requestedSource = clipOverride?.Motion is not null
                    ? MotionSource.Override
                    : decision.Source;

                if (requestedSource == MotionSource.ExplicitCode)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Info, "MOTION_EXPLICIT",
                        $"{Path.GetFileName(timed.FilePath)} uses explicit motion {requestedMotion}."));
                }

                var panRight = ctx.Previous != MotionType.PanRight;
                var plan = motionEngine.PlanClip(
                    requestedMotion, requestedSource, panRight, image.Width, image.Height);
                var effectiveMotion = plan.Motion;
                var effectiveSource = plan.Motion == requestedMotion
                    ? requestedSource
                    : MotionSource.AutoSelected;

                foreach (var warning in plan.Warnings)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "VIEWPORT", $"{Path.GetFileName(timed.FilePath)}: {warning}"));
                }

                if (image.Width <= 0 || image.Height <= 0)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "IMAGE_NO_DIMENSIONS",
                        $"{Path.GetFileName(timed.FilePath)} has unknown dimensions; cannot compute viewports."));
                }

                var easing = clipOverride?.Easing ?? options.Motion.Easing;
                var finalDecision = new MotionDecision(effectiveMotion, effectiveSource);
                VideoClip? clip;
                if (options.Transitions.Enabled && (scene != timing.Scenes[0] || i > 0))
                {
                    var isSceneBoundary = i == 0 && previousSceneId.Length > 0 && previousSceneId != scene.SceneId;
                    var cutKind = inventory.Overrides.CutFor(timed.FilePath)
                        ?? (isSceneBoundary
                            ? options.Transitions.SceneBoundaryKind
                            : options.Transitions.Kind);

                    clip = MakeClip(timed, image.Name.Type,
                        finalDecision, easing,
                        plan.Start, plan.End, new TransitionIn
                        {
                            Kind = cutKind,
                            DurationFrames = transitionFrames,
                        });
                }
                else
                {
                    clip = MakeClip(timed, image.Name.Type,
                        finalDecision, easing,
                        plan.Start, plan.End, null);
                }

                sceneClips.Add(clip);
                previous = effectiveMotion;
                previousSceneId = scene.SceneId;
                shotsSinceStatic = effectiveMotion == MotionType.Static ? 0 : shotsSinceStatic + 1;
            }

            timeline.Scenes.Add(new Scene
            {
                Id = scene.SceneId,
                StartFrame = scene.StartFrame,
                EndFrame = scene.EndFrame,
                Clips = sceneClips,
            });
        }

        return timeline;
    }

    private static VideoClip MakeClip(
        TimedClip timed,
        ImageType? imageType,
        MotionDecision decision,
        EasingMode easing,
        Rect startViewport,
        Rect endViewport,
        TransitionIn? transition) =>
        new()
        {
            FilePath = timed.FilePath,
            SceneId = timed.SceneId,
            StartFrame = timed.StartFrame,
            DurationFrames = timed.DurationFrames,
            Motion = decision.Motion,
            MotionSource = decision.Source,
            ImageType = imageType,
            Easing = easing,
            StartViewport = startViewport,
            EndViewport = endViewport,
            Transition = transition,
        };

    private static bool VerifyCoverage(Timeline timeline, double audioDurationSeconds, List<ValidationIssue> issues)
    {
        var audioFrames = (long)Math.Round(audioDurationSeconds * timeline.Fps);
        long cursor = 0;

        foreach (var scene in timeline.Scenes)
        {
            foreach (var clip in scene.Clips)
            {
                if (clip.StartFrame != cursor)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "COVERAGE_BROKEN",
                        $"Coverage invariant violated at {clip.FilePath}: expected start {cursor}, found {clip.StartFrame}."));
                    return false;
                }

                cursor += clip.DurationFrames;
            }
        }

        if (cursor != audioFrames)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "COVERAGE_AUDIO_MISMATCH",
                $"Timeline ends at frame {cursor} but audio lasts {audioFrames} frames."));
            return false;
        }

        return true;
    }
}
