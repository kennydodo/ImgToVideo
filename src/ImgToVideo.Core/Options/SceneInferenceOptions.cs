namespace ImgToVideo.Core.Options;

public sealed class SceneInferenceOptions
{
    public double SentenceGapSeconds { get; set; } = 0.8;
    public double TerminalPunctuationGapSeconds { get; set; } = 0.4;
}
