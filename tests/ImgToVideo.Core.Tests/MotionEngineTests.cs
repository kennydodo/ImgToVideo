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
        var plan = CreateEngine().PlanClip(
            MotionType.PanUp, MotionSource.ExplicitCode, true, 2304, 2160);

        Assert.Equal(MotionType.PanUp, plan.Motion);
        Assert.Equal(new Rect(0, 864, 2304, 1296), plan.Start);
        Assert.Equal(new Rect(0, 0, 2304, 1296), plan.End);
        Assert.True(plan.End.IsInside(new Rect(0, 0, 2304, 2160)));
    }

    [Fact]
    public void Pan_down_spans_full_vertical_travel()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.PanDown, MotionSource.ExplicitCode, true, 2304, 2160);

        Assert.Equal(0, plan.Start.Y);
        Assert.Equal(864, plan.End.Y);
        Assert.True(plan.End.IsInside(new Rect(0, 0, 2304, 2160)));
    }

    [Fact]
    public void Vertical_pan_on_16x9_falls_back_to_push_in()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.PanDown, MotionSource.ExplicitCode, true, 2304, 1296);

        Assert.Equal(MotionType.ZoomIn, plan.Motion);
        Assert.Equal(new Rect(0, 0, 2304, 1296), plan.Start);
        Assert.Contains(plan.Warnings, w => w.Contains("No vertical overscan"));
    }

    [Fact]
    public void Static_shows_full_image()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.Static, MotionSource.AutoSelected, true, 2304, 1296);

        Assert.Equal(new Rect(0, 0, 2304, 1296), plan.Start);
        Assert.Equal(plan.Start, plan.End);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Static_on_16x9_without_oversize_shows_full_image()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.Static, MotionSource.AutoSelected, true, 1920, 1080);

        Assert.Equal(new Rect(0, 0, 1920, 1080), plan.Start);
        Assert.Equal(plan.Start, plan.End);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Static_on_tall_image_fits_whole_image()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.Static, MotionSource.AutoSelected, true, 1024, 1536);

        Assert.Equal(MotionType.Static, plan.Motion);
        Assert.Equal(new Rect(0, 0, 1024, 1536), plan.Start);
        Assert.Equal(plan.Start, plan.End);
    }

    [Fact]
    public void Zoom_in_pushes_into_full_image()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.ZoomIn, MotionSource.AutoSelected, true, 2304, 1296);

        Assert.Equal(new Rect(0, 0, 2304, 1296), plan.Start);
        Assert.True(plan.End.IsInside(new Rect(0, 0, 2304, 1296)));
        Assert.True(plan.End.Width < plan.Start.Width);
        Assert.Equal(2304 * 100.0 / 106.0, plan.End.Width, 3);
    }

    [Fact]
    public void Zoom_on_tall_image_zooms_the_letterbox_canvas()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.ZoomIn, MotionSource.ExplicitCode, true, 1024, 1536);

        Assert.Equal(MotionType.ZoomIn, plan.Motion);
        Assert.Equal(2730, plan.Start.Width);
        Assert.Equal(1536, plan.Start.Height);
        Assert.Equal(853, plan.Start.X);
        Assert.True(plan.End.Width < plan.Start.Width);
        Assert.True(plan.End.IsInside(plan.Start));
    }

    [Fact]
    public void Explicit_pan_right_pans_within_fit_bounds()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.PanRight, MotionSource.ExplicitCode, true, 2880, 1296);

        Assert.Equal(MotionType.PanRight, plan.Motion);
        Assert.Equal(new Rect(0, 0, 2304, 1296), plan.Start);
        Assert.Equal(new Rect(576, 0, 2304, 1296), plan.End);
        Assert.True(plan.End.IsInside(new Rect(0, 0, 2880, 1296)));
    }

    [Fact]
    public void Pan_right_on_16x9_without_overscan_falls_back_to_push_in()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.PanRight, MotionSource.ExplicitCode, true, 1920, 1080);

        Assert.Equal(MotionType.ZoomIn, plan.Motion);
        Assert.Equal(new Rect(0, 0, 1920, 1080), plan.Start);
        Assert.Contains(plan.Warnings, w => w.Contains("No horizontal overscan"));
    }

    [Fact]
    public void Auto_pan_on_16x9_falls_back_to_push_in()
    {
        var plan = CreateEngine().PlanClip(
            MotionType.PanLeft, MotionSource.AutoSelected, true, 2304, 1296);

        Assert.Equal(MotionType.ZoomIn, plan.Motion);
        Assert.Equal(new Rect(0, 0, 2304, 1296), plan.Start);
        Assert.Contains(plan.Warnings, w => w.Contains("No horizontal overscan"));
    }

    [Fact]
    public void Pan_reveal_direction_follows_flag()
    {
        var engine = CreateEngine();

        var leftToRight = engine.PlanClip(
            MotionType.PanReveal, MotionSource.ExplicitCode, true, 3840, 1296);
        Assert.Equal(0, leftToRight.Start.X);
        Assert.Equal(1536, leftToRight.End.X);

        var rightToLeft = engine.PlanClip(
            MotionType.PanReveal, MotionSource.ExplicitCode, false, 3840, 1296);
        Assert.Equal(1536, rightToLeft.Start.X);
        Assert.Equal(0, rightToLeft.End.X);
    }
}
