using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class TransitionOptions
{
    public bool Enabled { get; set; } = true;
    public TransitionKind Kind { get; set; } = TransitionKind.Crossfade;
    public double DurationSeconds { get; set; } = 0.5;
}
