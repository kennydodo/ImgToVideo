using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class MotionOptions
{
    public bool AutoMotionEnabled { get; set; } = true;
    public EasingMode Easing { get; set; } = EasingMode.Linear;
    public double PushInStartPercent { get; set; } = 100.0;
    public double PushInEndPercent { get; set; } = 106.0;
    public double ZoomOutStartPercent { get; set; } = 107.0;
    public double ZoomOutEndPercent { get; set; } = 100.0;
    public double PanMaxTravelPercent { get; set; } = 4.0;
    public int StaticEveryMinShots { get; set; } = 4;
    public int StaticEveryMaxShots { get; set; } = 6;
}
