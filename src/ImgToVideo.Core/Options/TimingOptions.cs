namespace ImgToVideo.Core.Options;

public sealed class TimingOptions
{
    public double MinImageSeconds { get; set; } = 2.5;
    public double PreferredImageSeconds { get; set; } = 5.0;
    public double MaxImageSeconds { get; set; } = 8.0;
    public double FloorImageSeconds { get; set; } = 2.0;

    /// <summary>Manifest shots holding longer than this emit a SHOT_HOLD_LONG
    /// warning asking for a review or shotlist split. Advisory only — never
    /// auto-split. 0 disables the check.</summary>
    public double WarnHoldSeconds { get; set; } = 30.0;
}
