using ImgToVideo.Core.Models;
using ImgToVideo.Core.Motion;
using ImgToVideo.Core.Options;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class MotionEngineTests
{
    private static readonly MotionOptions MotionOptions = new();
    private static readonly OutputOptions Output = new();

    private static MotionEngine CreateEngine() => new(MotionOptions, Output);

    private static MotionDecision Select(MotionContext ctx) => CreateEngine().SelectMotion(ctx);

    [Fact]
    public void First_shot_of_video_is_zoom_in()
    {
        var decision = Select(new MotionContext(null, true, false, true, null, 0));

        Assert.Equal(MotionType.ZoomIn, decision.Motion);
        Assert.Equal(MotionSource.AutoSelected, decision.Source);
    }

    [Fact]
    public void Explicit_code_wins_even_at_video_start()
    {
        var decision = Select(new MotionContext(MotionType.PanLeft, true, false, true, null, 0));

        Assert.Equal(MotionType.PanLeft, decision.Motion);
        Assert.Equal(MotionSource.ExplicitCode, decision.Source);
    }

    [Fact]
    public void Closing_shot_is_static_or_zoom_out()
    {
        var a = Select(new MotionContext(null, false, true, false, MotionType.PanLeft, 1));
        Assert.Equal(MotionType.Static, a.Motion);

        var b = Select(new MotionContext(null, false, true, false, MotionType.Static, 1));
        Assert.Equal(MotionType.ZoomOut, b.Motion);
    }

    [Fact]
    public void Scene_start_prefer_zoom_in_else_static()
    {
        var a = Select(new MotionContext(null, false, false, true, MotionType.PanLeft, 1));
        Assert.Equal(MotionType.ZoomIn, a.Motion);

        var b = Select(new MotionContext(null, false, false, true, MotionType.ZoomIn, 1));
        Assert.Equal(MotionType.Static, b.Motion);
    }

    [Fact]
    public void After_pan_left_prefers_static()
    {
        var decision = Select(new MotionContext(null, false, false, false, MotionType.PanLeft, 1));

        Assert.Equal(MotionType.Static, decision.Motion);
    }

    [Fact]
    public void After_zoom_in_prefers_static()
    {
        var decision = Select(new MotionContext(null, false, false, false, MotionType.ZoomIn, 1));

        Assert.Equal(MotionType.Static, decision.Motion);
    }

    [Fact]
    public void Static_cadence_forces_static_at_max_shots()
    {
        var decision = Select(new MotionContext(null, false, false, false, MotionType.ZoomOut, 6));

        Assert.Equal(MotionType.Static, decision.Motion);
    }

    [Fact]
    public void Static_cadence_forces_static_after_pan_runs()
    {
        var decision = Select(new MotionContext(null, false, false, false, MotionType.PanRight, 4));

        Assert.Equal(MotionType.Static, decision.Motion);
    }

    [Fact]
    public void After_static_prefers_zoom_in()
    {
        var decision = Select(new MotionContext(null, false, false, false, MotionType.Static, 1));

        Assert.Equal(MotionType.ZoomIn, decision.Motion);
    }

    [Fact]
    public void Auto_selection_never_repeats_previous_motion()
    {
        var motions = Enum.GetValues<MotionType>().Where(m => m != MotionType.PanReveal);

        foreach (var previous in motions)
        {
            var decision = Select(new MotionContext(null, false, false, false, previous, 1));
            Assert.NotEqual(previous, decision.Motion);
        }
    }

    [Fact]
    public void Auto_selection_never_picks_pan_reveal_or_vertical_pans()
    {
        var engine = CreateEngine();
        var previousPool = Enum.GetValues<MotionType>()
            .Where(m => m is not (MotionType.PanReveal or MotionType.PanUp or MotionType.PanDown));

        foreach (var previous in previousPool)
        {
            for (var shots = 0; shots <= 8; shots++)
            {
                var decision = engine.SelectMotion(
                    new MotionContext(null, false, false, false, previous, shots));
                Assert.NotEqual(MotionType.PanReveal, decision.Motion);
                Assert.NotEqual(MotionType.PanUp, decision.Motion);
                Assert.NotEqual(MotionType.PanDown, decision.Motion);
            }
        }
    }

    [Fact]
    public void Pan_up_spans_full_vertical_travel()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.PanUp, MotionSource.ExplicitCode, true, 2304, 2160);

        Assert.Equal(new Rect(192, 1080, 1920, 1080), result.Start);
        Assert.Equal(new Rect(192, 0, 1920, 1080), result.End);
        Assert.True(result.End.IsInside(new Rect(0, 0, 2304, 2160)));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("below"));
    }

    [Fact]
    public void Pan_down_spans_full_vertical_travel()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.PanDown, MotionSource.ExplicitCode, true, 2304, 2160);

        Assert.Equal(0, result.Start.Y);
        Assert.Equal(1080, result.End.Y);
        Assert.Equal(192, result.Start.X);
        Assert.True(result.End.IsInside(new Rect(0, 0, 2304, 2160)));
    }

    [Fact]
    public void Vertical_pan_on_undersized_image_warns_and_stays_inside()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.PanDown, MotionSource.ExplicitCode, true, 2304, 1296);

        Assert.Contains(result.Warnings, w => w.Contains("below the 2304x2160 spec"));
        Assert.True(result.Start.IsInside(new Rect(0, 0, 2304, 1296)));
        Assert.True(result.End.IsInside(new Rect(0, 0, 2304, 1296)));
    }

    [Fact]
    public void Static_viewports_are_centered_at_100_percent()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.Static, MotionSource.AutoSelected, true, 2304, 1296);

        Assert.Equal(new Rect(192, 108, 1920, 1080), result.Start);
        Assert.Equal(result.Start, result.End);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Zoom_in_shrinks_viewport_and_stays_inside_image()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.ZoomIn, MotionSource.AutoSelected, true, 2304, 1296);

        Assert.True(result.Start.IsInside(new Rect(0, 0, 2304, 1296)));
        Assert.True(result.End.IsInside(new Rect(0, 0, 2304, 1296)));
        Assert.True(result.End.Width < result.Start.Width);
        Assert.Equal(1920 * 100.0 / 106.0, result.End.Width, 3);
    }

    [Fact]
    public void Explicit_pan_right_spans_full_available_travel()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.PanRight, MotionSource.ExplicitCode, true, 2880, 1296);

        Assert.Equal(0, result.Start.X);
        Assert.Equal(960, result.End.X);
        Assert.Equal(1080, result.Start.Height);
        Assert.True(result.End.IsInside(new Rect(0, 0, 2880, 1296)));
    }

    [Fact]
    public void Auto_pan_travel_is_capped_by_options()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.PanLeft, MotionSource.AutoSelected, true, 2304, 1296);

        var expectedTravel = 0.04 * 1920;
        Assert.Equal((2304.0 - 1920) / 2 + expectedTravel / 2, result.Start.X, 3);
        Assert.Equal((2304.0 - 1920) / 2 - expectedTravel / 2, result.End.X, 3);
    }

    [Fact]
    public void Pan_reveal_direction_follows_flag()
    {
        var engine = CreateEngine();

        var leftToRight = engine.ComputeViewports(
            MotionType.PanReveal, MotionSource.ExplicitCode, true, 3840, 1296);
        Assert.Equal(0, leftToRight.Start.X);
        Assert.Equal(1920, leftToRight.End.X);

        var rightToLeft = engine.ComputeViewports(
            MotionType.PanReveal, MotionSource.ExplicitCode, false, 3840, 1296);
        Assert.Equal(1920, rightToLeft.Start.X);
        Assert.Equal(0, rightToLeft.End.X);
    }

    [Fact]
    public void Undersized_pan_image_produces_warning()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.PanRight, MotionSource.ExplicitCode, true, 2304, 1296);

        Assert.Contains(result.Warnings, w => w.Contains("below the 2880x1296 spec"));
        Assert.True(result.Start.IsInside(new Rect(0, 0, 2304, 1296)));
        Assert.True(result.End.IsInside(new Rect(0, 0, 2304, 1296)));
    }

    [Fact]
    public void Viewports_are_clamped_for_oversized_height()
    {
        var result = CreateEngine().ComputeViewports(
            MotionType.Static, MotionSource.AutoSelected, true, 1000, 4000);

        Assert.True(result.Start.IsInside(new Rect(0, 0, 1000, 4000)));
        Assert.NotEmpty(result.Warnings);
    }
}
