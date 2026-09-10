namespace ImgToVideo.Core.Options;

public sealed class RenderOptions
{
    public int PreviewWidth { get; set; } = 960;
    public int PreviewHeight { get; set; } = 540;
    public string PreviewPreset { get; set; } = "veryfast";
    public int PreviewCrf { get; set; } = 28;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
}
