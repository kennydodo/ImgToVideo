using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Parsing;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ImageFilenameParserOptionsTests
{
    [Fact]
    public void Default_parser_matches_standard_grammar()
    {
        var parser = new ImageFilenameParser();

        Assert.Equal("S08_02_PR", parser.FormatExample());
        Assert.True(parser.TryParse("S08_02_PR.png", out var parsed));
        Assert.Equal(MotionType.PanRight, parsed!.Code);
    }

    [Fact]
    public void Custom_prefix_separator_and_padding_are_honored()
    {
        var options = new NamingOptions
        {
            ScenePrefix = "SC",
            Separator = "-",
            NumberPadding = 3,
        };
        var parser = new ImageFilenameParser(options);

        Assert.Equal("SC008-002-PR", parser.FormatExample());
        Assert.True(parser.TryParse("SC008-002-PR.png", out var parsed));
        Assert.Equal(8, parsed!.SceneNumber);
        Assert.Equal(2, parsed.ImageNumber);
        Assert.Equal(MotionType.PanRight, parsed.Code);
        Assert.Equal("SC008", options.SceneId(8));
    }

    [Fact]
    public void Disabled_motion_codes_reject_suffixed_files()
    {
        var options = new NamingOptions { MotionCodesEnabled = false };
        var parser = new ImageFilenameParser(options);

        Assert.False(parser.TryParse("S01_02_ZI.png", out _));
        Assert.True(parser.TryParse("S01_02.png", out var parsed));
        Assert.Null(parsed!.Code);
    }

    [Fact]
    public void Padding_affects_formatting_not_matching()
    {
        var options = new NamingOptions { NumberPadding = 3 };
        var parser = new ImageFilenameParser(options);

        Assert.True(parser.TryParse("S1_2.png", out var parsed));
        Assert.Equal(1, parsed!.SceneNumber);
        Assert.Equal(2, parsed.ImageNumber);
    }

    [Fact]
    public void Regex_metacharacters_in_naming_are_escaped()
    {
        var options = new NamingOptions { ScenePrefix = "S.", Separator = "_" };
        var parser = new ImageFilenameParser(options);

        Assert.True(parser.TryParse("S.08_02.png", out var parsed));
        Assert.Equal(8, parsed!.SceneNumber);
        Assert.False(parser.TryParse("SX08_02.png", out _));
    }

    [Fact]
    public void Static_convenience_uses_default_naming()
    {
        Assert.True(ImageFilenameParser.Default.TryParse("S01_01.png", out var parsed));
        Assert.Null(parsed!.Code);
    }
}
