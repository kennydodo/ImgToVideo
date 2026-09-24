namespace ImgToVideo.Core.Options;

public sealed class RenderOptions
{
    public int PreviewWidth { get; set; } = 960;
    public int PreviewHeight { get; set; } = 540;
    public string PreviewPreset { get; set; } = "veryfast";
    public int PreviewCrf { get; set; } = 28;

    /// <summary>
    /// B-frames for the preview draft. 0 disables them on purpose: at the
    /// preview's low resolution the encoder's B-frame quality pattern shows up
    /// as a period-(bframes+1) sharpness shimmer that reads as shaky motion.
    /// The final render keeps B-frames (invisible at full resolution, and they
    /// shrink the file).
    /// </summary>
    public int PreviewBframes { get; set; } = 0;

    public string FinalPreset { get; set; } = "medium";
    public int FinalCrf { get; set; } = 18;
    public int FinalBframes { get; set; } = 3;

    /// <summary>"auto" probes for a hardware encoder (NVENC/AMF/QSV) and falls back to CPU libx264.</summary>
    public string Encoder { get; set; } = "auto";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
}
