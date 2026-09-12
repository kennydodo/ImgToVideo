using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Reporting;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class BuildReportTests
{
    private static Timeline SampleTimeline()
    {
        return new Timeline
        {
            ProjectName = "Video_003",
            Fps = 30,
            Audio = new AudioTrack { FilePath = "audio\\narration.mp3", DurationFrames = 315 },
            Scenes =
            [
                new Scene
                {
                    Id = "S01",
                    StartFrame = 0,
                    EndFrame = 150,
                    Clips =
                    [
                        new VideoClip
                        {
                            FilePath = "images\\S01_01.png",
                            SceneId = "S01",
                            StartFrame = 0,
                            DurationFrames = 150,
                            Motion = MotionType.ZoomIn,
                            MotionSource = MotionSource.AutoSelected,
                            Easing = EasingMode.EaseInOut,
                            ImageType = ImageType.Scene,
                            Transition = new TransitionIn
                            {
                                Kind = TransitionKind.Crossfade,
                                DurationFrames = 15,
                            },
                        },
                    ],
                },
                new Scene
                {
                    Id = "S02",
                    StartFrame = 150,
                    EndFrame = 315,
                    Clips =
                    [
                        new VideoClip
                        {
                            FilePath = "images\\S02_01.png",
                            SceneId = "S02",
                            StartFrame = 150,
                            DurationFrames = 165,
                            Motion = MotionType.PanRight,
                            MotionSource = MotionSource.ExplicitCode,
                            Easing = EasingMode.Linear,
                        },
                    ],
                },
            ],
        };
    }

    private static BuildReport CreateReport() => BuildReportFactory.Create(
        "Video_003", SampleTimeline(), new ProjectOptions(),
        [
            new ValidationIssue(ValidationSeverity.Warning, "TIMING_HOLD", "Scene S02 holds long."),
            new ValidationIssue(ValidationSeverity.Info, "MOTION_EXPLICIT", "S02_01.png uses explicit motion PR."),
        ]);

    [Fact]
    public void ToJson_UsesSnakeCaseAndContainsPlanDecisions()
    {
        var json = BuildReportWriter.ToJson(CreateReport());

        Assert.Contains("\"schema_version\": 1", json);
        Assert.Contains("\"project\": \"Video_003\"", json);
        Assert.Contains("\"start_frame\": 0", json);
        Assert.Contains("\"duration_frames\": 150", json);
        Assert.Contains("\"motion\": \"ZI\"", json);
        Assert.Contains("\"motion_source\": \"ExplicitCode\"", json);
        Assert.Contains("\"transition_in\": \"Crossfade\"", json);
        Assert.Contains("\"scene_boundary_kind\"", json);
        Assert.Contains("\"TIMING_HOLD\"", json);
        Assert.Contains("\"audio_file\": \"audio\\\\narration.mp3\"", json);
    }

    [Fact]
    public void ToJson_IsDeterministic()
    {
        var first = BuildReportWriter.ToJson(CreateReport());
        var second = BuildReportWriter.ToJson(CreateReport());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_SumsClipCoverageIntoTotalFrames()
    {
        var report = CreateReport();

        Assert.Equal(315, report.Timeline.TotalFrames);
        Assert.Equal(2, report.Timeline.Scenes.Count);
        Assert.Equal(2, report.Inventory.ClipCount);
        Assert.Equal(2, report.Issues.Count);
    }

    [Fact]
    public void Save_WritesTheFile()
    {
        using var project = new TempProject();
        var path = System.IO.Path.Combine(project.Path, "build-report.json");

        BuildReportWriter.Save(CreateReport(), path);

        Assert.True(File.Exists(path));
        Assert.Contains("\"schema_version\": 1", File.ReadAllText(path));
    }
}
