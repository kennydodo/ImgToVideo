using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class SceneInferenceTests
{
    private static readonly SceneInferenceOptions Options = new();

    [Fact]
    public void Splits_on_large_gap_without_punctuation()
    {
        var windows = SceneInference.Infer(
        [
            new SubtitleBlock(1, 0.0, 2.0, "Line one"),
            new SubtitleBlock(2, 3.5, 6.0, "Line two"),
        ], Options);

        Assert.Equal(2, windows.Count);
        Assert.Equal(0.0, windows[0].StartSeconds);
        Assert.Equal(2.0, windows[0].EndSeconds);
        Assert.Equal(3.5, windows[1].StartSeconds);
        Assert.Equal(6.0, windows[1].EndSeconds);
    }

    [Fact]
    public void Splits_on_terminal_punctuation_with_small_gap()
    {
        var windows = SceneInference.Infer(
        [
            new SubtitleBlock(1, 0.0, 2.0, "Line one ends."),
            new SubtitleBlock(2, 2.5, 5.0, "Line two"),
        ], Options);

        Assert.Equal(2, windows.Count);
    }

    [Fact]
    public void Does_not_split_without_terminal_punctuation_and_small_gap()
    {
        var windows = SceneInference.Infer(
        [
            new SubtitleBlock(1, 0.0, 2.0, "Line one continues,"),
            new SubtitleBlock(2, 2.5, 5.0, "line two"),
        ], Options);

        var window = Assert.Single(windows);
        Assert.Equal(0.0, window.StartSeconds);
        Assert.Equal(5.0, window.EndSeconds);
    }

    [Fact]
    public void Single_block_forms_one_window()
    {
        var windows = SceneInference.Infer(
        [
            new SubtitleBlock(1, 1.0, 4.0, "Only line."),
        ], Options);

        var window = Assert.Single(windows);
        Assert.Equal(1.0, window.StartSeconds);
        Assert.Equal(4.0, window.EndSeconds);
    }

    [Fact]
    public void Empty_input_returns_no_windows()
    {
        Assert.Empty(SceneInference.Infer([], Options));
    }

    [Fact]
    public void Three_sentences_form_three_windows()
    {
        var windows = SceneInference.Infer(
        [
            new SubtitleBlock(1, 0.0, 3.0, "One."),
            new SubtitleBlock(2, 3.5, 6.5, "Two!"),
            new SubtitleBlock(3, 7.0, 10.0, "Three?"),
        ], Options);

        Assert.Equal(3, windows.Count);
    }
}
