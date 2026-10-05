using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class TransitionOptions
{
    public bool Enabled { get; set; } = true;
    public TransitionKind Kind { get; set; } = TransitionKind.Crossfade;
    public TransitionKind SceneBoundaryKind { get; set; } = TransitionKind.FadeBlack;
    public TransitionAlignment Alignment { get; set; } = TransitionAlignment.Late;
    public double DurationSeconds { get; set; } = 0.5;

    /// <summary>Fade used when the next item of a reveal shot appears
    /// (0 = hard cut).</summary>
    public double RevealFadeSeconds { get; set; } = 0.25;
}
