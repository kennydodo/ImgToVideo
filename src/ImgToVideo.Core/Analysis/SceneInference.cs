using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Analysis;

public static class SceneInference
{
    private static readonly char[] TerminalPunctuation = ['.', '!', '?', '\u2026'];

    public static List<SceneWindow> Infer(IReadOnlyList<SubtitleBlock> subtitles, SceneInferenceOptions options)
    {
        var windows = new List<SceneWindow>();
        if (subtitles.Count == 0)
        {
            return windows;
        }

        var groupStart = subtitles[0];
        var groupEnd = subtitles[0];

        for (var i = 1; i < subtitles.Count; i++)
        {
            var block = subtitles[i];
            var gap = block.StartSeconds - groupEnd.EndSeconds;
            var endsSentence = groupEnd.Text.Length > 0 &&
                TerminalPunctuation.Contains(groupEnd.Text.TrimEnd()[^1]);

            var split = gap > options.SentenceGapSeconds ||
                (endsSentence && gap > options.TerminalPunctuationGapSeconds);

            if (split)
            {
                windows.Add(new SceneWindow(string.Empty, groupStart.StartSeconds, groupEnd.EndSeconds));
                groupStart = block;
            }

            groupEnd = block;
        }

        windows.Add(new SceneWindow(string.Empty, groupStart.StartSeconds, groupEnd.EndSeconds));
        return windows;
    }
}
