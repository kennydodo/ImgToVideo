using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Ffmpeg;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class PreviewRenderServiceTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public async Task Runs_ffmpeg_version_when_available()
    {
        if (!FFmpegProbe.IsAvailable)
        {
            return;
        }

        var runner = new FfmpegRunner();
        var result = await runner.RunAsync(["-version"]);

        Assert.True(result.Success);
        Assert.Contains("ffmpeg", result.StandardOutput);
    }

    [Fact]
    public async Task Throws_actionable_error_when_ffmpeg_missing()
    {
        if (FFmpegProbe.IsAvailable)
        {
            return;
        }

        var runner = new FfmpegRunner("ffmpeg-definitely-not-on-path-xyz");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(["-version"]));

        Assert.Contains("Install ffmpeg", exception.Message);
    }

    [Fact]
    public async Task Renders_tiny_preview_end_to_end()
    {
        if (!FFmpegProbe.IsAvailable)
        {
            return;
        }

        var image1 = _project.WriteImage("S01_01.png", 2304, 1296);
        var image2 = _project.WriteImage("S01_02_PR.png", 2880, 1296);
        var audio = _project.WriteSilenceWav("narration.wav", 2.0);
        var images = new List<ImageInfo>
        {
            new(image1, new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
            new(image2, new ParsedImageName("S01_02_PR", 1, 2, MotionType.PanRight, false), 2880, 1296),
        };

        var timeline = new Timeline
        {
            ProjectName = "integration",
            Fps = 30,
            Audio = new AudioTrack { FilePath = audio, DurationFrames = 60 },
            Scenes =
            {
                new Scene
                {
                    Id = "S01",
                    StartFrame = 0,
                    EndFrame = 60,
                    Clips =
                    {
                        new VideoClip
                        {
                            FilePath = image1,
                            SceneId = "S01",
                            StartFrame = 0,
                            DurationFrames = 30,
                            Motion = MotionType.ZoomIn,
                            StartViewport = new Rect(192, 108, 1920, 1080),
                            EndViewport = new Rect(246.34, 138.57, 1811.32, 1018.87),
                        },
                        new VideoClip
                        {
                            FilePath = image2,
                            SceneId = "S01",
                            StartFrame = 30,
                            DurationFrames = 30,
                            Motion = MotionType.PanRight,
                            MotionSource = MotionSource.ExplicitCode,
                            StartViewport = new Rect(0, 108, 1920, 1080),
                            EndViewport = new Rect(960, 108, 1920, 1080),
                            Transition = new TransitionIn
                            {
                                Kind = TransitionKind.Crossfade,
                                DurationFrames = 8,
                            },
                        },
                    },
                },
            },
        };

        var options = new ProjectOptions();
        options.Render.PreviewWidth = 320;
        options.Render.PreviewHeight = 180;
        options.Render.PreviewPreset = "ultrafast";
        options.Render.PreviewCrf = 40;

        var renderDirectory = System.IO.Path.Combine(_project.Path, "out", "render");
        var previewPath = System.IO.Path.Combine(_project.Path, "out", "preview.mp4");
        var plan = PreviewRenderPlanFactory.Build(timeline, images, options, renderDirectory, previewPath);

        var debugDir = @"C:\Users\Kehinde\AppData\Local\Temp\kilo\itv-debug";
        Directory.CreateDirectory(debugDir);
        foreach (var segment in plan.Segments)
        {
            File.WriteAllLines(
                Path.Combine(debugDir, Path.GetFileName(segment.OutputPath) + ".args.txt"),
                segment.Arguments);
        }

        var service = new PreviewRenderService(new FfmpegRunner());
        var result = await service.RenderAsync(plan, maxParallelism: 2);

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.True(File.Exists(previewPath));
        Assert.True(new FileInfo(previewPath).Length > 1000);
    }

    [Fact]
    public async Task Probes_wav_duration_when_ffmpeg_available()
    {
        if (!FFmpegProbe.IsAvailable)
        {
            return;
        }

        var wav = _project.WriteSilenceWav("probe.wav", 2.0);
        var seconds = await new Ffprobe().GetDurationSecondsAsync(wav);

        Assert.Equal(2.0, seconds, 2);
    }

    public void Dispose() => _project.Dispose();
}
