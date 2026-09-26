using ImgToVideo.Core.Parsing;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class SrtParserTests
{
    [Fact]
    public void Parses_standard_block()
    {
        var blocks = SrtParser.Parse(
            "12\n00:00:32,000 --> 00:00:38,500\nAnd the first idea starts before you enter the house.\n");

        var block = Assert.Single(blocks);
        Assert.Equal(12, block.Index);
        Assert.Equal(32.0, block.StartSeconds);
        Assert.Equal(38.5, block.EndSeconds);
        Assert.Equal("And the first idea starts before you enter the house.", block.Text);
    }

    [Fact]
    public void Parses_multiple_blocks()
    {
        var blocks = SrtParser.Parse(
            "1\n00:00:00,000 --> 00:00:02,000\nFirst.\n\n2\n00:00:02,500 --> 00:00:05,000\nSecond.\n");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(2.5, blocks[1].StartSeconds);
        Assert.Equal("Second.", blocks[1].Text);
    }

    [Fact]
    public void Handles_crlf_and_bom()
    {
        var blocks = SrtParser.Parse(
            "\uFEFF1\r\n00:00:00,000 --> 00:00:01,000\r\nHello.\r\n");

        var block = Assert.Single(blocks);
        Assert.Equal("Hello.", block.Text);
    }

    [Fact]
    public void Handles_multiline_text()
    {
        var blocks = SrtParser.Parse(
            "1\n00:00:00,000 --> 00:00:01,000\nLine one\nLine two\n");

        var block = Assert.Single(blocks);
        Assert.Equal("Line one\nLine two", block.Text);
    }

    [Fact]
    public void Handles_hours_and_dot_milliseconds()
    {
        var blocks = SrtParser.Parse(
            "1\n01:02:03.250 --> 01:02:04.000\nLate.\n");

        var block = Assert.Single(blocks);
        Assert.Equal(3723.25, block.StartSeconds, 3);
        Assert.Equal(3724.0, block.EndSeconds, 3);
    }

    [Fact]
    public void Allows_blank_text()
    {
        var blocks = SrtParser.Parse("1\n00:00:00,000 --> 00:00:01,000\n\n");

        var block = Assert.Single(blocks);
        Assert.Equal(string.Empty, block.Text);
    }

    [Fact]
    public void Throws_on_missing_timecode_line()
    {
        Assert.Throws<FormatException>(() => SrtParser.Parse("1\nJust text.\n"));
    }

    [Fact]
    public void Throws_on_bad_timestamp()
    {
        Assert.Throws<FormatException>(
            () => SrtParser.Parse("1\n00:00:00 --> 00:00:01,000\nText.\n"));
    }

    [Fact]
    public void Throws_on_garbage_without_timecode()
    {
        Assert.Throws<FormatException>(() => SrtParser.Parse("not an srt file at all\n"));
    }

    [Fact]
    public void Returns_empty_list_for_blank_input()
    {
        Assert.Empty(SrtParser.Parse("   \n\n  \n"));
    }

    [Fact]
    public void Compact_narration_drops_timestamps_but_keeps_cue_numbers_and_text()
    {
        var compact = SrtParser.ToCompactNarration(
            "1\n00:00:00,000 --> 00:00:02,000\nFirst.\n\n" +
            "2\n00:00:02,500 --> 00:00:05,000\nSecond line\ncontinues here.\n");

        Assert.Equal("[1] First.\n[2] Second line continues here.\n", compact);
    }

    [Fact]
    public void Compact_narration_uses_position_for_unnumbered_blocks()
    {
        // An unnumbered SRT still yields the same effective cue keys the
        // shotlist expander uses (Index > 0 ? Index : position + 1).
        var compact = SrtParser.ToCompactNarration(
            "00:00:00,000 --> 00:00:02,000\nFirst.\n\n00:00:02,500 --> 00:00:05,000\nSecond.\n");

        Assert.Equal("[1] First.\n[2] Second.\n", compact);
    }

    [Fact]
    public void Compact_narration_preserves_non_sequential_cue_numbers()
    {
        // Cue references resolve by the SRT's own numbers, so they must survive.
        var compact = SrtParser.ToCompactNarration(
            "7\n00:00:00,000 --> 00:00:02,000\nSeven.\n\n12\n00:00:02,500 --> 00:00:05,000\nTwelve.\n");

        Assert.Equal("[7] Seven.\n[12] Twelve.\n", compact);
    }

    [Fact]
    public void Compact_narration_is_empty_for_blank_input()
    {
        Assert.Equal(string.Empty, SrtParser.ToCompactNarration("   \n\n  \n"));
    }
}
