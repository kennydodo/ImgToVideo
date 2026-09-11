using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Timing;

public sealed record TimedClip(
    string FilePath,
    string SceneId,
    long StartFrame,
    long DurationFrames,
    MotionType? ExplicitMotion);

public sealed record TimedScene(string SceneId, long StartFrame, long EndFrame, IReadOnlyList<TimedClip> Clips);

public sealed record TimingResult(IReadOnlyList<TimedScene> Scenes, IReadOnlyList<ValidationIssue> Issues);

public static class TimingEngine
{
    public static TimingResult Plan(
        IReadOnlyList<ScenePlanInput> scenes,
        double audioDurationSeconds,
        ProjectOptions options,
        IReadOnlyDictionary<string, long>? durationOverrides = null)
    {
        var issues = new List<ValidationIssue>();
        var fps = options.Output.Fps;
        var audioFrames = (long)Math.Round(audioDurationSeconds * fps);

        var sceneFrames = ComputeSceneFrameRanges(scenes, audioFrames, fps, issues);

        var timedScenes = new List<TimedScene>();
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var (startFrame, endFrame) = sceneFrames[i];
            if (endFrame <= startFrame)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "TIMING_SCENE_EMPTY",
                    $"Scene {scene.SceneId} has a non-positive duration after quantization."));
                continue;
            }

            timedScenes.Add(SplitScene(scene, startFrame, endFrame, fps, options, durationOverrides, issues));
        }

        return new TimingResult(timedScenes, issues);
    }

    private static List<(long Start, long End)> ComputeSceneFrameRanges(
        IReadOnlyList<ScenePlanInput> scenes,
        long audioFrames,
        double fps,
        List<ValidationIssue> issues)
    {
        var ranges = new List<(long Start, long End)>(scenes.Count);
        long previousEnd = 0;

        for (var i = 0; i < scenes.Count; i++)
        {
            var start = previousEnd;
            var isLast = i == scenes.Count - 1;
            long end = isLast
                ? audioFrames
                : (long)Math.Round(scenes[i].EndSeconds * fps);

            if (end < start)
            {
                end = start;
            }

            ranges.Add((start, end));
            previousEnd = end;
        }

        return ranges;
    }

    private static TimedScene SplitScene(
        ScenePlanInput scene,
        long startFrame,
        long endFrame,
        double fps,
        ProjectOptions options,
        IReadOnlyDictionary<string, long>? durationOverrides,
        List<ValidationIssue> issues)
    {
        var total = endFrame - startFrame;
        var images = scene.Images;
        var count = images.Count;
        var timing = options.Timing;

        if (count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "TIMING_NO_IMAGES",
                $"Scene {scene.SceneId} has no images."));
            return new TimedScene(scene.SceneId, startFrame, endFrame, []);
        }

        var baseSeconds = total / fps / count;

        if (baseSeconds < timing.FloorImageSeconds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "TIMING_OVERPACKED",
                $"Scene {scene.SceneId} contains {count} images in {total / fps:F1}s; even the " +
                $"{timing.FloorImageSeconds:F1}s floor cannot fit. Remove images or extend the scene."));
        }
        else if (baseSeconds > timing.MaxImageSeconds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "TIMING_HOLD",
                $"Scene {scene.SceneId} contains {count} images in {total / fps:F1}s; the last image " +
                $"holds past the {timing.MaxImageSeconds:F1}s maximum."));
        }
        else if (baseSeconds < timing.MinImageSeconds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "TIMING_DENSITY",
                $"Scene {scene.SceneId} contains {count} images in {total / fps:F1}s; images show " +
                $"~{baseSeconds:F1}s each, below the {timing.MinImageSeconds:F1}s minimum."));
        }

        var durations = new long[count];
        if (baseSeconds > timing.MaxImageSeconds)
        {
            var maxFrames = (long)Math.Round(timing.MaxImageSeconds * fps);
            long assigned = 0;
            for (var i = 0; i < count - 1; i++)
            {
                durations[i] = maxFrames;
                assigned += maxFrames;
            }

            durations[count - 1] = Math.Max(1, total - assigned);
        }
        else
        {
            var per = Math.Max(1, total / count);
            var extra = total - per * count;
            for (var i = 0; i < count; i++)
            {
                durations[i] = per + (i < extra ? 1 : 0);
            }
        }

        if (durationOverrides is not null)
        {
            ApplyDurationOverrides(scene, images, durations, total, durationOverrides, issues);
        }

        var clips = new List<TimedClip>(count);
        long cursor = startFrame;
        for (var i = 0; i < count; i++)
        {
            clips.Add(new TimedClip(
                images[i].FilePath,
                scene.SceneId,
                cursor,
                durations[i],
                images[i].Name.Code));
            cursor += durations[i];
        }

        return new TimedScene(scene.SceneId, startFrame, endFrame, clips);
    }

    private static void ApplyDurationOverrides(
        ScenePlanInput scene,
        IReadOnlyList<ImageInfo> images,
        long[] durations,
        long totalFrames,
        IReadOnlyDictionary<string, long> durationOverrides,
        List<ValidationIssue> issues)
    {
        var applied = new List<(int Index, long Frames)>();
        for (var i = 0; i < images.Count; i++)
        {
            if (durationOverrides.TryGetValue(images[i].FilePath, out var frames) && frames > 0)
            {
                applied.Add((i, frames));
            }
        }

        if (applied.Count == 0)
        {
            return;
        }

        var others = images.Count - applied.Count;
        var remaining = totalFrames - applied.Sum(a => a.Frames);
        if (remaining < Math.Max(1, others) && others > 0 ||
            others == 0 && remaining != 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "TIMING_OVERRIDE_IGNORED",
                $"Scene {scene.SceneId}: duration overrides do not fit the scene window; ignoring them."));
            return;
        }

        foreach (var (index, frames) in applied)
        {
            durations[index] = frames;
        }

        if (others > 0)
        {
            var perOther = remaining / others;
            var extra = (int)(remaining - perOther * others);
            var otherIndex = 0;
            for (var i = 0; i < images.Count; i++)
            {
                if (applied.Any(a => a.Index == i))
                {
                    continue;
                }

                durations[i] = perOther + (otherIndex < extra ? 1 : 0);
                otherIndex++;
            }
        }
    }
}
