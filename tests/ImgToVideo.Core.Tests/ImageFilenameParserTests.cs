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
        Assert.False(parsed.HasUnknownSuffix);
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
        Assert.False(parsed.HasUnknownSuffix);
    }

    [Fact]
    public void Flags_unknown_two_letter_code()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S02_01_XX.png", out var parsed));
        Assert.Null(parsed!.Code);
        Assert.True(parsed.HasUnknownSuffix);
    }

    [Theory]
    [InlineData("image1.png")]
    [InlineData("S1.png")]
    [InlineData("S01.png")]
    [InlineData("cover.png")]
    [InlineData("S01_02_Z.png")]
    [InlineData("01_02.png")]
    public void Rejects_non_matching_names(string fileName)
    {
        Assert.False(ImageFilenameParser.Default.TryParse(fileName, out _));
    }

    [Fact]
    public void Parses_new_format_with_type_and_motion()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S08_02_SCN_PR.png", out var parsed));
        Assert.Equal(ImageType.Scene, parsed!.Type);
        Assert.Equal(MotionType.PanRight, parsed.Code);

        Assert.True(ImageFilenameParser.Default.TryParse("S01_03_INF_ST.png", out var infographic));
        Assert.Equal(ImageType.Infographic, infographic!.Type);
        Assert.Equal(MotionType.Static, infographic.Code);
    }

    [Fact]
    public void Parses_four_letter_type_code()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S01_04_PROC_ST.png", out var parsed));
        Assert.Equal(ImageType.Process, parsed!.Type);
        Assert.Equal(MotionType.Static, parsed.Code);
    }

    [Fact]
    public void Parses_type_only_suffix()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S01_02_CU.png", out var parsed));
        Assert.Equal(ImageType.CloseUp, parsed!.Type);
        Assert.Null(parsed.Code);
    }

    [Fact]
    public void Unknown_three_letter_suffix_is_flagged_not_rejected()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S01_02_ZIM.png", out var parsed));
        Assert.Null(parsed!.Code);
        Assert.Null(parsed.Type);
        Assert.True(parsed.HasUnknownSuffix);
    }

    [Fact]
    public void Multi_segment_name_with_unknown_type_is_flagged()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S01_02_XXX_ST.png", out var parsed));
        Assert.Equal(MotionType.Static, parsed!.Code);
        Assert.Null(parsed.Type);
        Assert.True(parsed.HasUnknownSuffix);
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
