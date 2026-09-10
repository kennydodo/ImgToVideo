using ImgToVideo.Core.Parsing;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class NaturalSortComparerTests
{
    [Fact]
    public void Sorts_numeric_runs_numerically()
    {
        string[] input = ["S01_10.png", "S01_02.png", "S01_01.png"];
        string[] expected = ["S01_01.png", "S01_02.png", "S01_10.png"];

        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void Sorts_scene_numbers_before_image_numbers()
    {
        string[] input = ["S02_01.png", "S10_01.png", "S02_03.png", "S02_02.png"];
        string[] expected = ["S02_01.png", "S02_02.png", "S02_03.png", "S10_01.png"];

        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void Is_case_insensitive()
    {
        Assert.Equal(0, NaturalSortComparer.Instance.Compare("s01_01.png", "S01_01.PNG"));
    }

    [Fact]
    public void Multi_digit_scenes_sort_numerically()
    {
        string[] input = ["S10_01.png", "S09_01.png", "S100_01.png", "S09_02.png"];
        string[] expected = ["S09_01.png", "S09_02.png", "S10_01.png", "S100_01.png"];

        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(expected, sorted);
    }
}
