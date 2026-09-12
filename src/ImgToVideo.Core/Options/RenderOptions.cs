namespace ImgToVideo.Core.Options;

public sealed class RenderOptions
{
    public int PreviewWidth { get; set; } = 960;
    public int PreviewHeight { get; set; } = 540;
    public string PreviewPreset { get; set; } = "veryfast";
    public int PreviewCrf { get; set; } = 28;
    public string FinalPreset { get; set; } = "medium";
    public int FinalCrf { get; set; } = 18;

    /// <summary>"auto" probes for a hardware encoder (NVENC/AMF/QSV) and falls back to CPU libx264.</summary>
    public string Encoder { get; set; } = "auto";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
}
