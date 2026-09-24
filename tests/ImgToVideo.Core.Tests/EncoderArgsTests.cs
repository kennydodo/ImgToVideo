using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Ffmpeg;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class EncoderArgsTests
{
    private static VideoClip Clip() => new()
    {
        FilePath = "images/S01_01.png",
        SceneId = "S01",
        DurationFrames = 60,
        Motion = MotionType.ZoomIn,
        StartViewport = new Rect(192, 108, 1920, 1080),
        EndViewport = new Rect(246, 138, 1811, 1019),
    };

    private static IReadOnlyList<string> Args(RenderOptions render)
    {
        var options = new ProjectOptions { Render = render };
        return PreviewRenderPlanFactory.BuildClipPreviewArguments(Clip(), 2304, 1296, options, "out.mp4");
    }

    [Fact]
    public void Libx264_args_unchanged_by_default()
    {
        var args = Args(new RenderOptions { Encoder = "libx264", PreviewPreset = "veryfast", PreviewCrf = 28 });

        var index = args.ToList().IndexOf("-c:v");
        Assert.Equal("libx264", args[index + 1]);
        Assert.Contains("veryfast", args);
        Assert.Contains("28", args);
        Assert.DoesNotContain("h264_nvenc", args);
    }

    [Fact]
    public void Nvenc_args_use_cq_quality_mode()
    {
        var args = Args(new RenderOptions { Encoder = "h264_nvenc", PreviewPreset = "veryfast", PreviewCrf = 28 });

        var index = args.ToList().IndexOf("-c:v");
        Assert.Equal("h264_nvenc", args[index + 1]);
        Assert.Equal("vbr", args[args.ToList().IndexOf("-rc") + 1]);
        Assert.Equal("28", args[args.ToList().IndexOf("-cq") + 1]);
        Assert.Equal("p3", args[args.ToList().IndexOf("-preset") + 1]);
    }

    [Fact]
    public void Qsv_and_amf_args_map_presets_and_quality()
    {
        var qsv = Args(new RenderOptions { Encoder = "h264_qsv", PreviewPreset = "slow", PreviewCrf = 20 });
        Assert.Contains("h264_qsv", qsv);
        Assert.Equal("slow", qsv[qsv.ToList().IndexOf("-preset") + 1]);
        Assert.Equal("20", qsv[qsv.ToList().IndexOf("-global_quality") + 1]);

        var amf = Args(new RenderOptions { Encoder = "h264_amf", PreviewPreset = "slow", PreviewCrf = 20 });
        Assert.Contains("h264_amf", amf);
        Assert.Equal("quality", amf[amf.ToList().IndexOf("-quality") + 1]);
        Assert.Equal("20", amf[amf.ToList().IndexOf("-qp_i") + 1]);
    }

    [Fact]
    public void Preview_disables_b_frames_to_avoid_low_res_shimmer()
    {
        // B-frames at the preview's low resolution pump the quality every
        // (bframes+1) frames, which reads as shaky motion. The preview default
        // is 0.
        var args = Args(new RenderOptions { Encoder = "libx264" });
        Assert.Equal("0", args[args.ToList().IndexOf("-bf") + 1]);
    }

    [Fact]
    public void Final_render_can_keep_b_frames()
    {
        var args = Args(new RenderOptions { Encoder = "libx264", PreviewBframes = 3 });
        Assert.Equal("3", args[args.ToList().IndexOf("-bf") + 1]);
    }

    [Fact]
    public void Hardware_encoders_emit_b_frames_too()
    {
        foreach (var encoder in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
        {
            var args = Args(new RenderOptions { Encoder = encoder, PreviewBframes = 0 });
            Assert.Equal("0", args[args.ToList().IndexOf("-bf") + 1]);
        }
    }

    [Theory]
    [InlineData(1376, 4)]   // 720p -> 5504 grid
    [InlineData(1920, 3)]   // 1080p -> 5760 grid
    [InlineData(2304, 2)]   // unchanged (4608 grid)
    [InlineData(2880, 2)]   // unchanged (5760 grid)
    [InlineData(3840, 1)]   // at the threshold -> never supersampled
    [InlineData(5504, 1)]
    public void Supersample_reaches_the_target_grid(long width, int expected)
    {
        Assert.Equal(expected, PreviewRenderPlanFactory.SupersampleFor(width, 4608));
    }

    [Fact]
    public void Supersample_can_be_disabled()
    {
        Assert.Equal(1, PreviewRenderPlanFactory.SupersampleFor(1376, 0));
    }

    private static IReadOnlyList<string> ArgsFor(int srcWidth, int srcHeight, RenderOptions render)
    {
        var clip = new VideoClip
        {
            FilePath = "images/x.png",
            SceneId = "S01",
            DurationFrames = 60,
            Motion = MotionType.ZoomIn,
            StartViewport = new Rect(0, 0, srcWidth, srcHeight),
            EndViewport = new Rect(srcWidth * 0.02, srcHeight * 0.02,
                                   srcWidth * 0.96, srcHeight * 0.96),
        };
        var options = new ProjectOptions { Render = render };
        return PreviewRenderPlanFactory.BuildClipPreviewArguments(
            clip, srcWidth, srcHeight, options, "out.mp4");
    }

    [Theory]
    [InlineData(1376, 768, "scale=5504:3072")]   // 720p -> x4
    [InlineData(1920, 1080, "scale=5760:3240")]  // 1080p -> x3
    [InlineData(2304, 1296, "scale=4608:2592")]  // unchanged -> x2
    public void Low_resolution_sources_are_supersampled_to_the_grid(
        int width, int height, string expected)
    {
        var args = ArgsFor(width, height, new RenderOptions());
        var vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.Contains(expected, vf);
    }

    [Fact]
    public void Large_sources_are_not_supersampled()
    {
        var args = ArgsFor(5504, 3072, new RenderOptions());
        var vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.DoesNotContain("scale=", vf);
    }

    [Fact]
    public void ResolveEncoder_falls_back_to_cpu_when_hardware_is_missing()
    {
        // No ffmpeg on the probe path (or no hw encoders) must resolve to libx264
        // rather than emitting hardware args the machine cannot run.
        var render = new RenderOptions { Encoder = "auto", FfmpegPath = "ffmpeg-definitely-missing-xyz" };

        Assert.Equal("libx264", PreviewRenderPlanFactory.ResolveEncoder(render));
    }

    [Fact]
    public void ResolveEncoder_honours_explicit_cpu()
    {
        var render = new RenderOptions { Encoder = "cpu" };

        Assert.Equal("libx264", PreviewRenderPlanFactory.ResolveEncoder(render));
    }

    [Fact]
    public void Final_render_substitution_flows_through_encoder_args()
    {
        var options = new ProjectOptions { };
        options.Render.Encoder = "h264_nvenc";
        options.Render.FinalPreset = "slow";
        options.Render.FinalCrf = 18;

        var final = new ProjectOptions
        {
            Output = new OutputOptions { Width = 1920, Height = 1080, Fps = 30 },
            Render = new RenderOptions
            {
                Encoder = options.Render.Encoder,
                PreviewWidth = options.Output.Width,
                PreviewHeight = options.Output.Height,
                PreviewPreset = options.Render.FinalPreset,
                PreviewCrf = options.Render.FinalCrf,
            },
        };

        var args = Args(final.Render);
        Assert.Equal("p6", args[args.ToList().IndexOf("-preset") + 1]);
        Assert.Equal("18", args[args.ToList().IndexOf("-cq") + 1]);
    }
}
