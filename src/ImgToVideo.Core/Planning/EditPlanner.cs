using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Motion;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Timing;

namespace ImgToVideo.Core.Planning;

public sealed record PlanningResult(
    bool Success,
    Timeline? Timeline,
    IReadOnlyList<ValidationIssue> Issues);

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

        var sceneInputs = BuildSceneInputs(inventory, audioDurationSeconds, options, issues);
        if (ValidationIssue.HasErrors(issues))
        {
            return new PlanningResult(false, null, issues);
        }

        var timing = TimingEngine.Plan(sceneInputs, audioDurationSeconds, options);
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
                $"Scene inference found {windows.Count} scenes but images define {groups.Count}; " +
                $"pairing by order — consider a scenes.json override."));
        }

        var tiled = TileWindows(windows, audioDurationSeconds, issues, warnOnAdjustment: false);
        var count = Math.Min(tiled.Count, groups.Count);

        for (var i = count; i < tiled.Count; i++)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SCENE_NO_IMAGES",
                $"Inferred scene {i + 1} has no matching images (images define {groups.Count} scenes)."));
        }

        for (var i = count; i < groups.Count; i++)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "IMAGES_NO_SCENE",
                $"Scene {groups[i].SceneId} has {groups[i].Images.Count} images but narration has no matching scene."));
        }

        var inputs = new List<ScenePlanInput>(count);
        for (var i = 0; i < count; i++)
        {
            inputs.Add(new ScenePlanInput(
                groups[i].SceneId, tiled[i].StartSeconds, tiled[i].EndSeconds, groups[i].Images));
        }

        return inputs;
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

        var allClips = timing.Scenes.SelectMany(s => s.Clips).ToList();
        MotionType? previous = null;
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

                if (decision.Source == MotionSource.ExplicitCode)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Info, "MOTION_EXPLICIT",
                        $"{Path.GetFileName(timed.FilePath)} uses explicit motion {decision.Motion}."));
                }

                var panRight = ctx.Previous != MotionType.PanRight;
                var viewports = motionEngine.ComputeViewports(
                    decision.Motion, decision.Source, panRight, image.Width, image.Height);

                foreach (var warning in viewports.Warnings)
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

                VideoClip? clip;
                if (options.Transitions.Enabled && i > 0 && options.Transitions.Kind == TransitionKind.Crossfade)
                {
                    clip = MakeClip(timed, decision, viewports, new TransitionIn
                    {
                        Kind = TransitionKind.Crossfade,
                        DurationFrames = transitionFrames,
                    });
                }
                else
                {
                    clip = MakeClip(timed, decision, viewports, null);
                }

                sceneClips.Add(clip);
                previous = decision.Motion;
                shotsSinceStatic = decision.Motion == MotionType.Static ? 0 : shotsSinceStatic + 1;
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
        MotionDecision decision,
        ViewportResult viewports,
        TransitionIn? transition) =>
        new()
        {
            FilePath = timed.FilePath,
            SceneId = timed.SceneId,
            StartFrame = timed.StartFrame,
            DurationFrames = timed.DurationFrames,
            Motion = decision.Motion,
            MotionSource = decision.Source,
            StartViewport = viewports.Start,
            EndViewport = viewports.End,
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
