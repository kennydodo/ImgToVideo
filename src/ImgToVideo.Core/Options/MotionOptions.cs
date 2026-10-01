using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class MotionOptions
{
    public bool AutoMotionEnabled { get; set; } = true;
    public EasingMode Easing { get; set; } = EasingMode.EaseInOut;
    public double PushInStartPercent { get; set; } = 100.0;
    public double PushInEndPercent { get; set; } = 106.0;
    public double ZoomOutStartPercent { get; set; } = 107.0;
    public double ZoomOutEndPercent { get; set; } = 100.0;
    public double PanMaxTravelPercent { get; set; } = 4.0;
    public int StaticEveryMinShots { get; set; } = 4;
    public int StaticEveryMaxShots { get; set; } = 6;

    /// <summary>
    /// Milliseconds after which clip motion completes and the framing holds.
    /// 0 means motion runs for the whole clip - the shot's actual hold length,
    /// whatever that is - instead of completing early and freezing on the end
    /// frame for the remainder. This is the default: a fixed sub-clip duration
    /// (the old default was 4500ms) truncates pans/zooms on any shot held
    /// longer than that, which is never what "motion for this shot" means.
    /// A per-shot override (Shot.Motion.DurationMs) still takes precedence
    /// when a shot explicitly wants a shorter, timed motion.
    /// </summary>
    public long MotionDurationMs { get; set; } = 0;

    /// <summary>
    /// Above this many seconds of actual motion duration, a pan (PL/PR/PU/PD/PV)
    /// switches from the default Ease-In-Out curve to Linear.
    ///
    /// Why: the Ease-In-Out curve used for motion (a smootherstep,
    /// 6t^5-15t^4+10t^3) has zero velocity AND zero acceleration at both ends -
    /// by 80% of the way through a clip it has already covered ~94% of the
    /// travel distance, and by 90% it has covered ~99%. For a short clip (4-8s)
    /// that flat tail is a fraction of a second and reads as a smooth, cut-
    /// friendly ease. For a long hold (confirmed on a real render: a 30s pan
    /// and a 20s pan both visually stopped moving well before the clip ended -
    /// around the two-thirds mark - because the image's available overscan
    /// doesn't grow with duration, so the same small travel distance spread
    /// ease-in-out across a long clip spends most of the back half essentially
    /// motionless), that flat tail is many real seconds of apparent stillness -
    /// exactly the "motion stops partway through" complaint this exists to fix.
    /// Linear keeps velocity constant for the whole clip instead, so a long pan
    /// keeps drifting right up to the cut. Short pans are unaffected.
    /// </summary>
    public double LongPanLinearThresholdSeconds { get; set; } = 10.0;
}
