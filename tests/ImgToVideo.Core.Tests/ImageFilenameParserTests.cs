using ImgToVideo.Core.Models;
using ImgToVideo.Core.Parsing;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ImageFilenameParserTests
{
    [Fact]
    public void Parses_explicit_pan_right_code()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S08_02_PR.png", out var parsed));
        Assert.Equal(8, parsed!.SceneNumber);
        Assert.Equal(2, parsed.ImageNumber);
        Assert.Equal(MotionType.PanRight, parsed.Code);
        Assert.False(parsed.HasUnknownCode);
    }

    [Fact]
    public void Parses_lowercase_code_case_insensitively()
    {
        Assert.True(ImageFilenameParser.Default.TryParse(@"C:\vid\images\s01_03_zi.PNG", out var parsed));
        Assert.Equal(MotionType.ZoomIn, parsed!.Code);
    }

    [Fact]
    public void Parses_unsuffixed_name()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S01_01.png", out var parsed));
        Assert.Null(parsed!.Code);
        Assert.False(parsed.HasUnknownCode);
    }

    [Fact]
    public void Flags_unknown_two_letter_code()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S02_01_XX.png", out var parsed));
        Assert.Null(parsed!.Code);
        Assert.True(parsed.HasUnknownCode);
    }

    [Theory]
    [InlineData("image1.png")]
    [InlineData("S1.png")]
    [InlineData("S01.png")]
    [InlineData("cover.png")]
    [InlineData("S01_02_ZIM.png")]
    [InlineData("S01_02_Z.png")]
    [InlineData("01_02.png")]
    public void Rejects_non_matching_names(string fileName)
    {
        Assert.False(ImageFilenameParser.Default.TryParse(fileName, out _));
    }

    [Fact]
    public void Parses_multi_digit_scene_numbers()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S12_03_PV.png", out var parsed));
        Assert.Equal(12, parsed!.SceneNumber);
        Assert.Equal(3, parsed.ImageNumber);
        Assert.Equal(MotionType.PanReveal, parsed.Code);
    }
}
