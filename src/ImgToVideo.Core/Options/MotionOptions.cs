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
}
