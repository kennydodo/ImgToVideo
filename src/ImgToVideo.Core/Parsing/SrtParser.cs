using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Parsing;

public static partial class SrtParser
{
    [GeneratedRegex(@"^(\d+):(\d{1,2}):(\d{1,2})[,.](\d{1,3})$")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static List<SubtitleBlock> Parse(string content)
    {
        content = content.Replace("\r\n", "\n").Replace('\r', '\n');
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        var lines = content.Split('\n');
        var blocks = new List<SubtitleBlock>();
        int i = 0;

        while (i < lines.Length)
        {
            while (i < lines.Length && lines[i].Trim().Length == 0)
            {
                i++;
            }

            if (i >= lines.Length)
            {
                break;
            }

            var index = 0;
            var first = lines[i].Trim();
            if (int.TryParse(first, out var parsedIndex))
            {
                index = parsedIndex;
                i++;
            }

            if (i >= lines.Length)
            {
                throw new FormatException($"SRT block {index}: missing timecode line.");
            }

            var (start, end) = ParseTimeLine(lines[i].Trim(), index);
            i++;

            var textLines = new List<string>();
            while (i < lines.Length && lines[i].Trim().Length > 0)
            {
                textLines.Add(lines[i].TrimEnd());
                i++;
            }

            blocks.Add(new SubtitleBlock(index, start, end, string.Join("\n", textLines)));
        }

        return blocks;
    }

    /// <summary>The cue key used wherever cues are referenced: the SRT index
    /// when present, else the block's 1-based position. Shared by the shotlist
    /// expander and the compact-narration export so the two cannot drift.</summary>
    public static int CueIndex(SubtitleBlock block, int position) =>
        block.Index > 0 ? block.Index : position + 1;

    /// <summary>Renders an SRT as cue-index + text only — timestamp lines and
    /// blank separators removed — for pasting into the LLM planner. Cue numbers
    /// match the ones a shotlist must reference (the SRT index, or the position
    /// for unnumbered blocks), so a plan authored from this text still resolves
    /// against the original SRT on disk. The assembler keeps using the real
    /// timestamps; the LLM never needs them.</summary>
    public static string ToCompactNarration(string content)
    {
        var blocks = Parse(content);
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var text = WhitespaceRegex().Replace(block.Text, " ").Trim();
            builder.Append('[').Append(CueIndex(block, i)).Append("] ").Append(text).Append('\n');
        }

        return builder.ToString();
    }

    private static (double Start, double End) ParseTimeLine(string line, int blockIndex)
    {
        var separator = line.IndexOf("-->", StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new FormatException($"SRT block {blockIndex}: invalid timecode line \"{line}\".");
        }

        var start = ParseTimestamp(line[..separator].Trim(), blockIndex);
        var end = ParseTimestamp(line[(separator + 3)..].Trim(), blockIndex);
        return (start, end);
    }

    private static double ParseTimestamp(string value, int blockIndex)
    {
        var match = TimestampRegex().Match(value);
        if (!match.Success)
        {
            throw new FormatException($"SRT block {blockIndex}: invalid timestamp \"{value}\".");
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var millis = int.Parse(match.Groups[4].Value.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return hours * 3600 + minutes * 60 + seconds + millis / 1000.0;
    }
}
