namespace ImgToVideo.Core.Models;

public sealed record SubtitleBlock(int Index, double StartSeconds, double EndSeconds, string Text)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
}
