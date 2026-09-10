using ImgToVideo.Core.Models;
using ImgToVideo.Core.Parsing;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class SceneMapParserTests
{
    [Fact]
    public void Parses_sorted_windows()
    {
        var result = SceneMapParser.Parse(
            """
            [
              { "scene": "S02", "start": 58.0, "end": 90.0 },
              { "scene": "S01", "start": 0.0, "end": 32.0 }
            ]
            """);

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Windows.Count);
        Assert.Equal("S01", result.Windows[0].SceneId);
        Assert.Equal(0.0, result.Windows[0].StartSeconds);
        Assert.Equal(32.0, result.Windows[0].EndSeconds);
        Assert.Equal("S02", result.Windows[1].SceneId);
    }

    [Fact]
    public void Reports_overlapping_entries_as_errors()
    {
        var result = SceneMapParser.Parse(
            """
            [
              { "scene": "S01", "start": 0.0, "end": 40.0 },
              { "scene": "S02", "start": 32.0, "end": 60.0 }
            ]
            """);

        Assert.Contains(result.Issues, i => i.Code == "SCENE_MAP_OVERLAP" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Reports_end_before_start_as_error()
    {
        var result = SceneMapParser.Parse("""[{ "scene": "S01", "start": 10.0, "end": 5.0 }]""");

        Assert.Contains(result.Issues, i => i.Code == "SCENE_MAP_RANGE");
    }

    [Fact]
    public void Reports_duplicate_scene_ids_as_error()
    {
        var result = SceneMapParser.Parse(
            """
            [
              { "scene": "S01", "start": 0.0, "end": 5.0 },
              { "scene": "S01", "start": 6.0, "end": 9.0 }
            ]
            """);

        Assert.Contains(result.Issues, i => i.Code == "SCENE_MAP_DUPLICATE");
    }

    [Fact]
    public void Reports_non_array_root_as_error()
    {
        var result = SceneMapParser.Parse("""{ "scene": "S01" }""");

        Assert.Contains(result.Issues, i => i.Code == "SCENE_MAP");
    }

    [Fact]
    public void Reports_invalid_json_as_error()
    {
        var result = SceneMapParser.Parse("not json at all");

        Assert.Contains(result.Issues, i => i.Code == "SCENE_MAP");
    }

    [Fact]
    public void Reports_malformed_entries_as_errors()
    {
        var result = SceneMapParser.Parse("""[{ "scene": "S01" }, { "scene": "", "start": 0, "end": 5 }]""");

        Assert.Equal(2, result.Issues.Count(i => i.Code == "SCENE_MAP_ENTRY"));
        Assert.Empty(result.Windows);
    }
}
