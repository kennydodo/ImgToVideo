using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Planning;
using ImgToVideo.Core.Serialization;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class EditPlannerTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Plans_complete_project_end_to_end()
    {
        WriteStandardProject();

        var result = Plan();

        Assert.True(result.Success);
        Assert.NotNull(result.Timeline);
        var timeline = result.Timeline!;

        Assert.Equal(2, timeline.Scenes.Count);
        Assert.Equal(600, timeline.Audio.DurationFrames);

        var clips = timeline.Scenes.SelectMany(s => s.Clips).ToList();
        Assert.Equal(4, clips.Count);

        var clip1 = clips[0];
        Assert.Equal(MotionType.ZoomIn, clip1.Motion);
        Assert.Equal(MotionSource.AutoSelected, clip1.MotionSource);
        Assert.Equal(0, clip1.StartFrame);
        Assert.Equal(153, clip1.DurationFrames);
        Assert.Null(clip1.Transition);

        var clip2 = clips[1];
        Assert.Equal(MotionType.ZoomIn, clip2.Motion);
        Assert.Equal(MotionSource.ExplicitCode, clip2.MotionSource);
        Assert.Equal(153, clip2.StartFrame);
        Assert.Equal(15, clip2.Transition!.DurationFrames);

        var clip3 = clips[2];
        Assert.Equal(MotionType.Static, clip3.Motion);
        Assert.Equal(306, clip3.StartFrame);
        Assert.Equal(147, clip3.DurationFrames);

        var clip4 = clips[3];
        Assert.Equal(MotionType.PanRight, clip4.Motion);
        Assert.Equal(MotionSource.ExplicitCode, clip4.MotionSource);
        Assert.Equal(453, clip4.StartFrame);
        Assert.Equal(0, clip4.StartViewport.X);
        Assert.Equal(960, clip4.EndViewport.X);

        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Warning);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
        Assert.Equal(2, result.Issues.Count(i => i.Code == "MOTION_EXPLICIT"));
    }

    [Fact]
    public void Zoom_in_end_viewport_is_magnified()
    {
        WriteStandardProject();

        var result = Plan();

        var clip1 = result.Timeline!.Scenes[0].Clips[0];
        Assert.Equal(1920 * 100.0 / 106.0, clip1.EndViewport.Width, 3);
        Assert.True(clip1.EndViewport.IsInside(new Rect(0, 0, 2304, 1296)));
    }

    [Fact]
    public void Scene_map_override_wins_over_inference()
    {
        WriteStandardProject();
        _project.WriteFile(
            "scenes.json",
            """
            [
              { "scene": "S01", "start": 0.0, "end": 8.0 },
              { "scene": "S02", "start": 8.0, "end": 20.0 }
            ]
            """);

        var result = Plan();

        Assert.True(result.Success);
        var clips = result.Timeline!.Scenes.SelectMany(s => s.Clips).ToList();
        Assert.Equal(120, clips[0].DurationFrames);
        Assert.Equal(120, clips[1].DurationFrames);
        Assert.Equal(180, clips[2].DurationFrames);
        Assert.Equal(180, clips[3].DurationFrames);
    }

    [Fact]
    public void Mismatched_scene_counts_distribute_images_evenly()
    {
        _project.WriteImage("S01_01_SCN_ST.png", 2304, 1296);
        _project.WriteImage("S02_01_SCN_ZI.png", 2304, 1296);
        _project.WriteImage("S03_01_SCN_ST.png", 2304, 1296);
        _project.WriteSrt();
        _project.WriteAudio();

        var result = Plan();

        Assert.True(result.Success);
        var timeline = result.Timeline!;
        Assert.Equal(3, timeline.Scenes.Count);

        Assert.Equal(0, timeline.Scenes[0].StartFrame);
        Assert.Equal(200, timeline.Scenes[0].EndFrame);
        Assert.Equal(200, timeline.Scenes[1].StartFrame);
        Assert.Equal(400, timeline.Scenes[1].EndFrame);
        Assert.Equal(400, timeline.Scenes[2].StartFrame);
        Assert.Equal(600, timeline.Scenes[2].EndFrame);

        Assert.Contains(result.Issues, i => i.Code == "SCENE_COUNT_MISMATCH");
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Missing_audio_blocks_planning()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteSrt();

        var result = Plan();

        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Code == "AUDIO_MISSING");
    }

    [Fact]
    public void Planning_is_deterministic()
    {
        WriteStandardProject();

        var first = Plan();
        var second = Plan();

        var json1 = System.Text.Json.JsonSerializer.Serialize(first.Timeline, TimelineJson.Options);
        var json2 = System.Text.Json.JsonSerializer.Serialize(second.Timeline, TimelineJson.Options);
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Coverage_invariant_holds()
    {
        WriteStandardProject();

        var result = Plan();

        var clips = result.Timeline!.Scenes.SelectMany(s => s.Clips).ToList();
        long cursor = 0;
        foreach (var clip in clips)
        {
            Assert.Equal(cursor, clip.StartFrame);
            cursor += clip.DurationFrames;
        }

        Assert.Equal(600, cursor);
    }

    private void WriteStandardProject()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteImage("S01_02_ZI.png", 2304, 1296);
        _project.WriteImage("S02_01.png", 2304, 1296);
        _project.WriteImage("S02_02_PR.png", 2880, 1296);
        _project.WriteSrt();
        _project.WriteAudio();
    }

    private PlanningResult Plan()
    {
        var inventory = ProjectLoader.Load(_project.Path);
        return EditPlanner.Plan(inventory, 20.0, new ProjectOptions());
    }

    public void Dispose() => _project.Dispose();
}
