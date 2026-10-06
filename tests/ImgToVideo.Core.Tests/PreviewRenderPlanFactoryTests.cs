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
            "scale=13824:7776:flags=lanczos," +
            "zoompan=z='min(1.28,max(1.2,13824/(11520+(-720)*((on+0)/89))))'" +
            ":x='min(max(0,(6912+(0)*((on+0)/89))-iw/zoom/2),iw-iw/zoom)'" +
            ":y='min(max(0,(3888+(0)*((on+0)/89))-ih/zoom/2),ih-ih/zoom)'" +
            ":d=90:s=960x540:fps=30,format=yuv420p",
            vf);
    }

    [Fact]
    public void Pan_right_filter_moves_a_fixed_size_crop_over_a_supersampled_still()
    {
        var (timeline, images) = SampleTimeline();
        var plan = PreviewRenderPlanFactory.Build(
            timeline, images, Options(), _outputDirectory,
            System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

        var vf = ArgumentAfter(plan.Segments[1].Arguments, "-vf");

        // A pan keeps the viewport size, so it is a crop (not zoompan) on a 5x grid: 2880 -> 14400.
        Assert.Equal(
            "scale=14400:6480:flags=lanczos," +
            "loop=loop=89:size=1:start=0," +
            "crop=w='9600':h='5400'" +
            ":x='min(iw-ow,max(0,0+(4800)*((n+0)/89)))'" +
            ":y='min(ih-oh,max(0,540+(0)*((n+0)/89)))'," +
            "scale=960:540,format=yuv420p",
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
        Assert.Contains("13824/(11520+(-720)*pow(min(1,max(0,((on+0)/89))),2))", vf);
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
        // "Start at cut": the outgoing clip keeps its full duration (it holds its
        // final framing across the join afterwards), the second clip's head plays
        // inside the join and its own segment resumes at frame 15 of its motion.
        Assert.Equal(90, plan.Segments[0].FrameCount);
        Assert.EndsWith("join_0001.mp4", plan.Segments[1].OutputPath);
        Assert.Equal(15, plan.Segments[1].FrameCount);
        Assert.EndsWith("seg_0002.mp4", plan.Segments[2].OutputPath);
        Assert.Equal(75, plan.Segments[2].FrameCount);

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
        // Outgoing clip is a zoom (zoompan, "on") held at its final framing from
        // frame 90 on; the incoming one is a pan (crop, "n") clamped at its first
        // framing while it fades in over the whole window after the cut.
        Assert.Contains("((on+90)/89)", filterComplex);
        Assert.Contains("((n-15)/89)", filterComplex);
        Assert.Contains("loop=loop=14:size=1:start=0", filterComplex);
        Assert.Contains("format=yuv420p", filterComplex);
        Assert.Contains("-frames:v", join.Arguments);
        Assert.Contains("15", join.Arguments);
    }

    [Fact]
    public void Images_at_the_old_preview_threshold_are_still_supersampled()
    {
        // 4K sources used to skip supersampling entirely (0.67 px wobble on a
        // 2K screen); they now ride the same grid target as everything else.
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
        Assert.Contains("scale=15360:5184:flags=lanczos", vf);
        Assert.StartsWith("scale=", vf);
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
        Assert.Contains("x='1152'", vf);
        Assert.Contains("y='648'", vf);
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
            // Fixed-size viewport -> crop path; 6x grid makes the crop width 6 * 1920.25 = 11521.5.
            var timeline = SingleClipTimeline(imagePath, 1, viewport: new Rect(0, 0, 1920.25, 1080.1875));
            var images = new List<ImageInfo>
            {
                new(imagePath, new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
            };

            var plan = PreviewRenderPlanFactory.Build(
                timeline, images, Options(), _outputDirectory,
                System.IO.Path.Combine(_project.Path, "out", "preview.mp4"));

            var vf = ArgumentAfter(plan.Segments[0].Arguments, "-vf");
            Assert.Contains("crop=w='11521.5'", vf);
            Assert.Contains("h='6481.125'", vf);
            Assert.DoesNotContain("11521,5", vf);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("ultrafast")]
    [InlineData("superfast")]
    [InlineData("veryfast")]
    [InlineData("faster")]
    [InlineData("fast")]
    [InlineData("medium")]
    [InlineData("slow")]
    [InlineData("slower")]
    [InlineData("veryslow")]
    [InlineData("nonsense")]
    public void Every_libx264_preset_maps_to_a_value_each_hardware_encoder_accepts(string preset)
    {
        // h264_qsv rejects libx264's ultrafast/superfast ("Unable to parse preset option value"),
        // which failed every preview render on Intel QSV machines.
        var qsv = ArgumentAfter(PreviewRenderPlanFactory.VideoEncoderArgs("h264_qsv", preset, 28, 0), "-preset");
        Assert.Contains(qsv, new[] { "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" });

        var nvenc = ArgumentAfter(PreviewRenderPlanFactory.VideoEncoderArgs("h264_nvenc", preset, 28, 0), "-preset");
        Assert.Matches("^p[1-7]$", nvenc);

        var amf = ArgumentAfter(PreviewRenderPlanFactory.VideoEncoderArgs("h264_amf", preset, 28, 0), "-quality");
        Assert.Contains(amf, new[] { "speed", "balanced", "quality" });
    }

    [Fact]
    public void Libx264_keeps_the_preset_and_crf_as_given()
    {
        var args = PreviewRenderPlanFactory.VideoEncoderArgs("libx264", "ultrafast", 23, 0);

        Assert.Equal("ultrafast", ArgumentAfter(args, "-preset"));
        Assert.Equal("23", ArgumentAfter(args, "-crf"));
        Assert.Equal("0", ArgumentAfter(args, "-bf"));
    }

    [Fact]
    public void Explicit_cpu_encoder_resolves_without_probing_hardware()
    {
        var options = Options();
        options.Render.Encoder = "cpu";

        Assert.Equal("libx264", PreviewRenderPlanFactory.ResolveEncoder(options.Render));
    }

    private static ProjectOptions Options()
    {
        var options = new ProjectOptions();
        options.Render.PreviewWidth = 960;
        options.Render.PreviewHeight = 540;
        options.Render.PreviewPreset = "veryfast";
        options.Render.PreviewCrf = 28;
        // "auto" probes this machine's GPU, so the asserted arguments would differ per machine.
        options.Render.Encoder = "libx264";
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
