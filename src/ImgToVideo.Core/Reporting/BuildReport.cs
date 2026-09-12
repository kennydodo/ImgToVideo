using System.Text.Json;
using System.Text.Json.Serialization;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Reporting;

public sealed record BuildReport(
    int SchemaVersion,
    string Project,
    BuildReportSettings Settings,
    BuildReportInventory Inventory,
    BuildReportTimeline Timeline,
    IReadOnlyList<BuildReportIssue> Issues,
    ManifestCoverage? Coverage = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record BuildReportSettings(
    double Fps,
    int Width,
    int Height,
    bool AutoMotionEnabled,
    string Easing,
    BuildReportTiming Timing,
    BuildReportTransitions Transitions);

public sealed record BuildReportTiming(
    double FloorImageSeconds,
    double MinImageSeconds,
    double PreferredImageSeconds,
    double MaxImageSeconds);

public sealed record BuildReportTransitions(
    bool Enabled,
    string Kind,
    string SceneBoundaryKind,
    string Alignment,
    long DurationFrames);

public sealed record BuildReportInventory(
    int SceneCount,
    int ClipCount,
    string? AudioFile);

public sealed record BuildReportTimeline(
    long TotalFrames,
    IReadOnlyList<BuildReportScene> Scenes);

public sealed record BuildReportScene(
    string Id,
    long StartFrame,
    long EndFrame,
    IReadOnlyList<BuildReportClip> Clips);

public sealed record BuildReportClip(
    string File,
    long StartFrame,
    long DurationFrames,
    string Motion,
    string MotionSource,
    string Easing,
    string? ImageType,
    string? TransitionIn);

public sealed record BuildReportIssue(string Severity, string Code, string Message);

public static class BuildReportFactory
{
    public static BuildReport Create(
        string projectName,
        Timeline timeline,
        ProjectOptions options,
        IReadOnlyList<ValidationIssue> issues,
        ManifestCoverage? coverage = null)
    {
        var fps = options.Output.Fps;
        var scenes = timeline.Scenes
            .Select(s => new BuildReportScene(
                s.Id,
                s.StartFrame,
                s.EndFrame,
                s.Clips.Select(ClipReport).ToList()))
            .ToList();

        return new BuildReport(
            BuildReport.CurrentSchemaVersion,
            projectName,
            new BuildReportSettings(
                fps,
                options.Output.Width,
                options.Output.Height,
                options.Motion.AutoMotionEnabled,
                EasingModes.NameOf(options.Motion.Easing) ?? options.Motion.Easing.ToString(),
                new BuildReportTiming(
                    options.Timing.FloorImageSeconds,
                    options.Timing.MinImageSeconds,
                    options.Timing.PreferredImageSeconds,
                    options.Timing.MaxImageSeconds),
                new BuildReportTransitions(
                    options.Transitions.Enabled,
                    TransitionCatalog.NameOf(options.Transitions.Kind) ??
                        options.Transitions.Kind.ToString(),
                    TransitionCatalog.NameOf(options.Transitions.SceneBoundaryKind) ??
                        options.Transitions.SceneBoundaryKind.ToString(),
                    options.Transitions.Alignment.ToString(),
                    (long)Math.Round(options.Transitions.DurationSeconds * fps))),
            new BuildReportInventory(
                timeline.Scenes.Count,
                timeline.Scenes.Sum(s => s.Clips.Count),
                string.IsNullOrEmpty(timeline.Audio.FilePath) ? null : timeline.Audio.FilePath),
            new BuildReportTimeline(
                timeline.Scenes.Sum(s => s.Clips.Sum(c => c.DurationFrames)),
                scenes),
            issues
                .Select(i => new BuildReportIssue(i.Severity.ToString(), i.Code, i.Message))
                .ToList(),
            coverage);
    }

    private static BuildReportClip ClipReport(VideoClip clip) =>
        new(
            clip.FilePath,
            clip.StartFrame,
            clip.DurationFrames,
            MotionCodes.All.FirstOrDefault(m => m.Motion == clip.Motion).Suffix ??
                clip.Motion.ToString(),
            clip.MotionSource.ToString(),
            EasingModes.NameOf(clip.Easing) ?? clip.Easing.ToString(),
            clip.ImageType?.ToString(),
            clip.Transition is null
                ? null
                : TransitionCatalog.NameOf(clip.Transition.Kind) ?? clip.Transition.Kind.ToString());
}

public static class BuildReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(BuildReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static void Save(BuildReport report, string path) =>
        File.WriteAllText(path, ToJson(report));
}
