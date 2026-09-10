namespace ImgToVideo.Core.Models;

public sealed class TransitionIn
{
    public TransitionKind Kind { get; set; } = TransitionKind.None;
    public long DurationFrames { get; set; }
}
