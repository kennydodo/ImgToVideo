using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Timing;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class TimingEngineTests
{
    private static readonly ProjectOptions Options = new();

    private static ScenePlanInput Scene(string id, double start, double end, int imageCount) =>
        new(
            id,
            start,
            end,
            Enumerable.Range(1, imageCount)
                .Select(i => new ImageInfo(
                    $"images/{id}_{i:00}.png",
                    new ParsedImageName($"{id}_{i:00}", 1, i, null, false),
                    2304,
                    1296))
                .ToList());

    [Fact]
    public void Even_split_when_base_in_range()
    {
        var result = TimingEngine.Plan([Scene("S01", 0, 10, 2)], 10.0, Options);

        var scene = Assert.Single(result.Scenes);
        Assert.Equal(150, scene.Clips[0].DurationFrames);
        Assert.Equal(150, scene.Clips[1].DurationFrames);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Warning);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Sparse_scene_caps_clips_and_holds_the_last()
    {
        var result = TimingEngine.Plan([Scene("S01", 0, 20, 2)], 20.0, Options);

        var scene = Assert.Single(result.Scenes);
        Assert.Equal(240, scene.Clips[0].DurationFrames);
        Assert.Equal(360, scene.Clips[1].DurationFrames);
        Assert.Contains(result.Issues, i => i.Code == "TIMING_HOLD");
    }

    [Fact]
    public void Dense_scene_below_min_warns()
    {
        var result = TimingEngine.Plan([Scene("S01", 0, 6, 3)], 6.0, Options);

        var scene = Assert.Single(result.Scenes);
        Assert.All(scene.Clips, c => Assert.Equal(60, c.DurationFrames));
        Assert.Contains(result.Issues, i => i.Code == "TIMING_DENSITY");
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Scene_below_floor_errors()
    {
        var result = TimingEngine.Plan([Scene("S01", 0, 6, 4)], 6.0, Options);

        Assert.Contains(result.Issues, i => i.Code == "TIMING_OVERPACKED" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Scene_without_images_errors()
    {
        var empty = new ScenePlanInput("S01", 0, 10, []);
        var result = TimingEngine.Plan([empty], 10.0, Options);

        Assert.Contains(result.Issues, i => i.Code == "TIMING_NO_IMAGES" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Scene_boundaries_are_contiguous_and_cover_audio()
    {
        var result = TimingEngine.Plan(
            [Scene("S01", 0, 10.05, 2), Scene("S02", 10.05, 20.05, 2)],
            20.05,
            Options);

        Assert.Equal(2, result.Scenes.Count);
        Assert.Equal(0, result.Scenes[0].StartFrame);
        Assert.Equal(result.Scenes[0].EndFrame, result.Scenes[1].StartFrame);
        Assert.Equal((long)Math.Round(20.05 * 30), result.Scenes[1].EndFrame);

        var allFrames = result.Scenes.SelectMany(s => s.Clips).ToList();
        long cursor = 0;
        foreach (var clip in allFrames)
        {
            Assert.Equal(cursor, clip.StartFrame);
            cursor += clip.DurationFrames;
        }

        Assert.Equal((long)Math.Round(20.05 * 30), cursor);
    }

    [Fact]
    public void Remainder_frames_are_distributed_without_gaps()
    {
        var result = TimingEngine.Plan([Scene("S01", 0, 7, 3)], 7.0, Options);

        var scene = Assert.Single(result.Scenes);
        Assert.Equal(210, scene.Clips.Sum(c => c.DurationFrames));
        Assert.Equal(70, scene.Clips[0].DurationFrames);
        Assert.Equal(70, scene.Clips[1].DurationFrames);
        Assert.Equal(70, scene.Clips[2].DurationFrames);
    }

    [Fact]
    public void Explicit_motion_codes_are_carried_through()
    {
        var scene = new ScenePlanInput(
            "S01",
            0,
            10,
            new List<ImageInfo>
            {
                new("images/S01_01.png", new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
                new("images/S01_02_PR.png", new ParsedImageName("S01_02_PR", 1, 2, MotionType.PanRight, false), 2304, 1296),
            });

        var result = TimingEngine.Plan([scene], 10.0, Options);

        Assert.Null(result.Scenes[0].Clips[0].ExplicitMotion);
        Assert.Equal(MotionType.PanRight, result.Scenes[0].Clips[1].ExplicitMotion);
    }
}
