namespace ImgToVideo.Core.Options;

public sealed class TimingOptions
{
    public double MinImageSeconds { get; set; } = 2.5;
    public double PreferredImageSeconds { get; set; } = 5.0;
    public double MaxImageSeconds { get; set; } = 8.0;
    public double FloorImageSeconds { get; set; } = 2.0;
}
