using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Ffmpeg;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class PreviewRenderPlanFactoryTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _outputDirectory;

    public PreviewRenderPlanFactoryTests()
    {
        _outputDirectory = System.IO.Path.Combine(_project.Path, "out", "render");
    }

    [Fact]
    public void Builds_one_segment_per_clip_with_encoder_settings()
    {
        var (timeline, images) = SampleTimeline();
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(180, plan.TotalFrames);

        var segment = plan.Segments[0];
        Assert.Equal(1, segment.Index);
        Assert.Equal(90, segment.FrameCount);
        Assert.EndsWith("seg_0001.mp4", segment.OutputPath);
        Assert.Contains("-frames:v", segment.Arguments);
        Assert.Contains("90", segment.Arguments);
        Assert.Contains("-preset", segment.Arguments);
        Assert.Contains("veryfast", segment.Arguments);
        Assert.Contains("-crf", segment.Arguments);
        Assert.Contains("28", segment.Arguments);
        Assert.Contains("-an", segment.Arguments);
        var outputIndex = segment.Arguments.ToList().IndexOf(segment.OutputPath);
        Assert.True(outputIndex > 0);
    }

    [Fact]
    public void Zoom_in_filter_is_exact()
    {
        var (timeline, images) = SampleTimeline();
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var vf = ArgumentAfter(plan.Segments[0].Arguments, "-vf");

        Assert.Equal(
            "scale=4608:2592:flags=lanczos," +
            "zoompan=z='min(1.28,max(1.2,4608/(3840+(-240)*((on+0)/89))))'" +
            ":x='min(max(0,(2304+(0)*((on+0)/89))-iw/zoom/2),iw-iw/zoom)'" +
            ":y='min(max(0,(1296+(0)*((on+0)/89))-ih/zoom/2),ih-ih/zoom)'" +
            ":d=90:s=960x540:fps=30,format=yuv420p",
            vf);
    }

    [Fact]
    public void Pan_right_filter_travels_with_constant_zoom()
    {
        var (timeline, images) = SampleTimeline();
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var vf = ArgumentAfter(plan.Segments[1].Arguments, "-vf");

        Assert.Equal(
            "scale=5760:2592:flags=lanczos," +
            "zoompan=z='min(1.5,max(1.5,5760/(3840+(0)*((on+0)/89))))'" +
            ":x='min(max(0,(1920+(1920)*((on+0)/89))-iw/zoom/2),iw-iw/zoom)'" +
            ":y='min(max(0,(1296+(0)*((on+0)/89))-ih/zoom/2),ih-ih/zoom)'" +
            ":d=90:s=960x540:fps=30,format=yuv420p",
            vf);
    }

    [Fact]
    public void Ease_in_shapes_the_motion_progress()
    {
        var (timeline, images) = SampleTimeline();
        timeline.Scenes[0].Clips[0].Easing = EasingMode.EaseIn;
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var vf = ArgumentAfter(plan.Segments[0].Arguments, "-vf");

        Assert.Contains("pow(min(1,max(0,((on+0)/89))),2)", vf);
        Assert.Contains("4608/(3840+(-240)*pow(min(1,max(0,((on+0)/89))),2))", vf);
    }

    [Fact]
    public void Transitions_produce_trimmed_pieces_and_join_segments()
    {
        var (timeline, images) = SampleTimeline();
        timeline.Scenes[0].Clips[1].Transition = new TransitionIn
        {
            Kind = TransitionKind.Crossfade,
            DurationFrames = 15,
        };
        var previewPath = System.IO.Path.Combine(_project.Path, "out", "preview.mp4");
        var plan = PreviewRenderPlanFactory.Build(timeline, images, Options(), _outputDirectory, previewPath);

        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(180, plan.TotalFrames);

        Assert.EndsWith("seg_0001.mp4", plan.Segments[0].OutputPath);
        Assert.Equal(75, plan.Segments[0].FrameCount);
        Assert.EndsWith("join_0001.mp4", plan.Segments[1].OutputPath);
        Assert.Equal(15, plan.Segments[1].FrameCount);
        Assert.EndsWith("seg_0002.mp4", plan.Segments[2].OutputPath);
        Assert.Equal(90, plan.Segments[2].FrameCount);

        var listIndex1 = plan.ConcatListContent.IndexOf(plan.Segments[0].OutputPath, StringComparison.Ordinal);
        var listIndex2 = plan.ConcatListContent.IndexOf(plan.Segments[1].OutputPath, StringComparison.Ordinal);
        var listIndex3 = plan.ConcatListContent.IndexOf(plan.Segments[2].OutputPath, StringComparison.Ordinal);
        Assert.True(listIndex1 < listIndex2 && listIndex2 < listIndex3);
    }

    [Fact]
    public void Join_arguments_blend_both_sides_with_xfade()
    {
        var (timeline, images) = SampleTimeline();
        timeline.Scenes[0].Clips[1].Transition = new TransitionIn
        {
            Kind = TransitionKind.Crossfade,
            DurationFrames = 15,
        };
        var previewPath = System.IO.Path.Combine(_project.Path, "out", "preview.mp4");
        var plan = PreviewRenderPlanFactory.Build(timeline, images, Options(), _outputDirectory, previewPath);

        var join = plan.Segments[1];
        Assert.Equal(2, join.Arguments.Count(a => a == "-i"));
        Assert.Contains(timeline.Scenes[0].Clips[0].FilePath, join.Arguments);
        Assert.Contains(timeline.Scenes[0].Clips[1].FilePath, join.Arguments);

        var filterComplex = ArgumentAfter(join.Arguments, "-filter_complex");
        Assert.Contains("xfade=transition=fade:duration=0.5:offset=0", filterComplex);
        Assert.Contains("[va]", filterComplex);
        Assert.Contains("[vb]", filterComplex);
        Assert.Contains("((on+75)/89)", filterComplex);
        Assert.Contains("((on+0)/89)", filterComplex);
        Assert.Contains("format=yuv420p", filterComplex);
        Assert.Contains("-frames:v", join.Arguments);
        Assert.Contains("15", join.Arguments);
    }

    [Fact]
    public void Images_at_threshold_are_not_supersampled()
    {
        var imagePath = _project.WriteImage("S01_01.png", 3840, 1296);
        var timeline = SingleClipTimeline(imagePath, 60);
        var images = new List<ImageInfo>
        {
            new(imagePath, new ParsedImageName("S01_01", 1, 1, null, false), 3840, 1296),
        };

        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var vf = ArgumentAfter(plan.Segments[0].Arguments, "-vf");
        Assert.DoesNotContain("scale=", vf);
        Assert.StartsWith("zoompan=", vf);
    }

    [Fact]
    public void Single_frame_clip_uses_constant_expressions()
    {
        var imagePath = _project.WriteImage("S01_01.png", 2304, 1296);
        var timeline = SingleClipTimeline(imagePath, 1);
        var images = new List<ImageInfo>
        {
            new(imagePath, new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
        };

        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var vf = ArgumentAfter(plan.Segments[0].Arguments, "-vf");
        Assert.Contains("z='1.2'", vf);
        Assert.Contains("x='384'", vf);
        Assert.Contains("y='216'", vf);
    }

    [Fact]
    public void Concat_list_lists_segments_in_order()
    {
        var (timeline, images) = SampleTimeline();
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var lines = plan.ConcatListContent.Split('\n');
        Assert.Equal("ffconcat version 1.0", lines[0]);
        Assert.Contains($"file '{plan.Segments[0].OutputPath}'", plan.ConcatListContent);
        Assert.Contains($"file '{plan.Segments[1].OutputPath}'", plan.ConcatListContent);
    }

    [Fact]
    public void Concat_arguments_use_copy_semantics()
    {
        var (timeline, images) = SampleTimeline();
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        Assert.Contains("-f", plan.ConcatArguments);
        Assert.Contains("concat", plan.ConcatArguments);
        Assert.Contains("-safe", plan.ConcatArguments);
        Assert.Contains("0", plan.ConcatArguments);
        Assert.Contains("-c", plan.ConcatArguments);
        Assert.Contains("copy", plan.ConcatArguments);
        Assert.Contains(plan.RoughPath, plan.ConcatArguments);
    }

    [Fact]
    public void Mux_arguments_map_narration_and_use_shortest()
    {
        var (timeline, images) = SampleTimeline();
        var previewPath = System.IO.Path.Combine(_project.Path, "out", "preview.mp4");
        var plan = PreviewRenderPlanFactory.Build(timeline, images, Options(), _outputDirectory, previewPath);

        Assert.Contains(previewPath, plan.MuxArguments);
        Assert.Contains(timeline.Audio.FilePath, plan.MuxArguments);
        Assert.Contains("-shortest", plan.MuxArguments);
        Assert.Contains("aac", plan.MuxArguments);
        var videoIndex = plan.MuxArguments.ToList().IndexOf("0:v:0");
        var audioIndex = plan.MuxArguments.ToList().IndexOf("1:a:0");
        Assert.True(videoIndex >= 0 && audioIndex > videoIndex);
    }

    [Fact]
    public void Missing_dimensions_throw()
    {
        var imagePath = _project.WriteImage("S01_01.png", 2304, 1296);
        var timeline = SingleClipTimeline(imagePath, 60);

        Assert.Throws<InvalidOperationException>(() => PreviewRenderPlanFactory.Build(
            timeline, [], Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4")));
    }

    [Fact]
    public void Decimal_formatting_is_culture_invariant()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var imagePath = _project.WriteImage("S01_01.png", 2304, 1296);
            var timeline = SingleClipTimeline(imagePath, 1, viewport: new Rect(0, 0, 1921, 1081));
            var images = new List<ImageInfo>
            {
                new(imagePath, new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
            };

            var plan = PreviewRenderPlanFactory.Build(
                timeline, images, Options(), _outputDirectory,
                System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

            var vf = ArgumentAfter(plan.Segments[0].Arguments, "-vf");
            Assert.Contains("z='1.199375'", vf);
            Assert.DoesNotContain("1,199375", vf);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static ProjectOptions Options()
    {
        var options = new ProjectOptions();
        options.Render.PreviewWidth = 960;
        options.Render.PreviewHeight = 540;
        options.Render.PreviewPreset = "veryfast";
        options.Render.PreviewCrf = 28;
        return options;
    }

    private static (Timeline, List<ImageInfo>) SampleTimeline()
    {
        var imagePath1 = System.IO.Path.Combine("images", "S01_01.png");
        var imagePath2 = System.IO.Path.Combine("images", "S01_02_PR.png");

        var images = new List<ImageInfo>
        {
            new(imagePath1, new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
            new(imagePath2, new ParsedImageName("S01_02_PR", 1, 2, MotionType.PanRight, false), 2880, 1296),
        };

        var timeline = new Timeline
        {
            ProjectName = "test",
            Fps = 30,
            Audio = new AudioTrack { FilePath = System.IO.Path.Combine("audio", "narration.mp3") },
            Scenes =
            {
                new Scene
                {
                    Id = "S01",
                    StartFrame = 0,
                    EndFrame = 180,
                    Clips =
                    {
                        new VideoClip
                        {
                            FilePath = imagePath1,
                            SceneId = "S01",
                            StartFrame = 0,
                            DurationFrames = 90,
                            Motion = MotionType.ZoomIn,
                            StartViewport = new Rect(192, 108, 1920, 1080),
                            EndViewport = new Rect(252, 141.75, 1800, 1012.5),
                        },
                        new VideoClip
                        {
                            FilePath = imagePath2,
                            SceneId = "S01",
                            StartFrame = 90,
                            DurationFrames = 90,
                            Motion = MotionType.PanRight,
                            MotionSource = MotionSource.ExplicitCode,
                            StartViewport = new Rect(0, 108, 1920, 1080),
                            EndViewport = new Rect(960, 108, 1920, 1080),
                        },
                    },
                },
            },
        };

        return (timeline, images);
    }

    private static Timeline SingleClipTimeline(string imagePath, long durationFrames, Rect? viewport = null)
    {
        var start = viewport ?? new Rect(192, 108, 1920, 1080);
        var end = viewport ?? new Rect(252, 141.75, 1800, 1012.5);

        return new Timeline
        {
            ProjectName = "test",
            Fps = 30,
            Audio = new AudioTrack { FilePath = "audio/narration.mp3" },
            Scenes =
            {
                new Scene
                {
                    Id = "S01",
                    StartFrame = 0,
                    EndFrame = durationFrames,
                    Clips =
                    {
                        new VideoClip
                        {
                            FilePath = imagePath,
                            SceneId = "S01",
                            StartFrame = 0,
                            DurationFrames = durationFrames,
                            Motion = MotionType.ZoomIn,
                            StartViewport = start,
                            EndViewport = end,
                        },
                    },
                },
            },
        };
    }

    private static string ArgumentAfter(IReadOnlyList<string> arguments, string flag)
    {
        var index = arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0 && index + 1 < arguments.Count, $"Flag {flag} not found.");
        return arguments[index + 1];
    }

    public void Dispose() => _project.Dispose();
}
