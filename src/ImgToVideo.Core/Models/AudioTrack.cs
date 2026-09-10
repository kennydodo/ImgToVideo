namespace ImgToVideo.Core.Models;

public sealed class AudioTrack
{
    public string FilePath { get; set; } = string.Empty;
    public long DurationFrames { get; set; }
}
